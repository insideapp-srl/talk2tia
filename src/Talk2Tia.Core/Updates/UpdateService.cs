using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Talk2Tia.Core.Configuration;
using Talk2Tia.Core.Openness;

namespace Talk2Tia.Core.Updates;

/// <summary>A newer release found on GitHub.</summary>
public sealed record UpdateInfo(Version Version, string Tag, string Page, string MsiUrl, string MsiName, string? Sha256)
{
    /// <summary>Local MSI, set once downloaded and verified (SHA-256 and InsideApp signature).</summary>
    public string? LocalMsi { get; init; }
}

/// <summary>
/// Checks GitHub for newer Talk2Tia releases (at most once a day), downloads the MSI, verifies it and installs it on
/// request once every Talk2Tia process has exited. Nothing runs when updates are "off".
/// </summary>
public sealed class UpdateService : IDisposable
{
    private const string ReleasesApi = "https://api.github.com/repos/insideapp-srl/talk2tia/releases?per_page=20";
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(24);

    private readonly Talk2TiaConfig _config;
    private readonly HttpClient _http;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private UpdateInfo? _available;
    private string? _lastError;
    private bool _installScheduled;

    public UpdateService(Talk2TiaConfig config)
    {
        _config = config;
        _http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("talk2tia-updater");
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    }

    public static Version Current { get; } = ParseVersion(Tools.FeedbackReport.Talk2TiaVersion) ?? new Version(0, 0, 0);

    public bool Enabled => _config.Updates != Talk2TiaConfig.UpdatesOff;

    public UpdateInfo? Available => _available;

    public bool InstallScheduled => _installScheduled;

    /// <summary>Raised (on a worker thread) when an update becomes ready or is scheduled.</summary>
    public event Action? Changed;

    /// <summary>Folder for downloaded installers, next to the work folder so the Windows Installer service can read it.</summary>
    public static string DownloadDirectory => Path.Combine(Path.GetDirectoryName(Workspace.Root.TrimEnd('\\'))!, "updates");

    private static string StateFile => Path.Combine(DownloadDirectory, "update-check.json");

    /// <summary>Summary for portal_status; null when there is nothing to report.</summary>
    public object? Status => !Enabled
        ? null
        : _available is { } u
            ? new
            {
                current = Current.ToString(3),
                latest = u.Version.ToString(3),
                ready = u.LocalMsi is not null,
                installScheduled = _installScheduled,
                howToInstall = _installScheduled
                    ? "The update installs when every Talk2Tia session ends (close and reopen the MCP client)."
                    : "Talk2Tia icon in the Windows notification area > Install update, or download it from " + u.Page,
            }
            : _lastError is not null ? new { current = Current.ToString(3), checkError = _lastError } : null;

    /// <summary>Checks for an update in the background; errors are logged, never thrown.</summary>
    public void CheckInBackground(bool force = false)
    {
        if (!Enabled)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await CheckAsync(force).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException or JsonException or TaskCanceledException or InvalidOperationException)
            {
                _lastError = ex.Message;
                Console.Error.WriteLine($"[talk2tia] update check failed: {ex.Message}");
            }
        });
    }

    public async Task CheckAsync(bool force)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(DownloadDirectory);
            var info = !force && ReadState() is { } cached && DateTime.UtcNow - cached.CheckedUtc < CheckInterval
                ? cached.Latest
                : await FetchLatestAsync().ConfigureAwait(false);
            _lastError = null;
            if (info is null || info.Version <= Current)
            {
                return;
            }

            var local = Path.Combine(DownloadDirectory, info.MsiName);
            if (!File.Exists(local) || !Verify(local, info.Sha256))
            {
                var partial = local + ".part";
                using (var response = await _http.GetAsync(info.MsiUrl, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();
                    using var file = File.Create(partial);
                    await response.Content.CopyToAsync(file).ConfigureAwait(false);
                }

                if (File.Exists(local))
                {
                    File.Delete(local);
                }

                File.Move(partial, local);
                if (!Verify(local, info.Sha256))
                {
                    File.Delete(local);
                    throw new InvalidOperationException($"{info.MsiName} failed the checksum or signature verification and was deleted.");
                }
            }

            _available = info with { LocalMsi = local };
            Changed?.Invoke();
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<UpdateInfo?> FetchLatestAsync()
    {
        var json = await _http.GetStringAsync(ReleasesApi).ConfigureAwait(false);
        var latest = PickLatest(json);
        if (latest is not null)
        {
            var sumsUrl = latest.MsiUrl.Substring(0, latest.MsiUrl.LastIndexOf('/') + 1) + "SHA256SUMS.txt";
            var sums = await _http.GetStringAsync(sumsUrl).ConfigureAwait(false);
            latest = latest with { Sha256 = ShaFor(sums, latest.MsiName) };
        }

        WriteState(new State(DateTime.UtcNow, latest));
        return latest;
    }

    /// <summary>The newest non-draft release (pre-releases included: 0.x are betas) that has an MSI.</summary>
    internal static UpdateInfo? PickLatest(string releasesJson)
    {
        using var doc = JsonDocument.Parse(releasesJson);
        UpdateInfo? best = null;
        foreach (var r in doc.RootElement.EnumerateArray())
        {
            if (r.TryGetProperty("draft", out var draft) && draft.GetBoolean())
            {
                continue;
            }

            var tag = r.GetProperty("tag_name").GetString() ?? string.Empty;
            if (ParseVersion(tag) is not { } version)
            {
                continue;
            }

            var msi = r.GetProperty("assets").EnumerateArray()
                .Select(a => (Name: a.GetProperty("name").GetString() ?? string.Empty, Url: a.GetProperty("browser_download_url").GetString() ?? string.Empty))
                .FirstOrDefault(a => a.Name.EndsWith(".msi", StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrEmpty(msi.Name) || (best is not null && version <= best.Version))
            {
                continue;
            }

            best = new UpdateInfo(version, tag, r.GetProperty("html_url").GetString() ?? string.Empty, msi.Url, msi.Name, null);
        }

        return best;
    }

    /// <summary>Reads the hash of <paramref name="fileName"/> from a "hash  name" checksum list.</summary>
    internal static string? ShaFor(string sums, string fileName) =>
        sums.Split('\n')
            .Select(l => l.Trim().Split(new[] { ' ', '\t', '*' }, StringSplitOptions.RemoveEmptyEntries))
            .FirstOrDefault(p => p.Length == 2 && p[1].Equals(fileName, StringComparison.OrdinalIgnoreCase))?[0].ToLowerInvariant();

    internal static Version? ParseVersion(string text)
    {
        var core = text.Trim().TrimStart('v', 'V').Split('-', '+')[0];
        return Version.TryParse(core, out var v) ? new Version(v.Major, v.Minor, Math.Max(v.Build, 0)) : null;
    }

    private static bool Verify(string path, string? sha256)
    {
        if (sha256 is null)
        {
            return false; // never install without a published checksum
        }

        using (var stream = File.OpenRead(path))
        using (var sha = SHA256.Create())
        {
            var actual = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty).ToLowerInvariant();
            if (actual != sha256)
            {
                return false;
            }
        }

        return Authenticode.IsSignedByInsideApp(path, out _);
    }

    /// <summary>
    /// Starts a hidden helper that waits until no talk2tia.exe from this installation is running, then installs the
    /// downloaded MSI in the same scope (per user: no elevation; per machine: UAC prompt).
    /// </summary>
    public void ScheduleInstall()
    {
        if (_available?.LocalMsi is not { } msi || _installScheduled)
        {
            return;
        }

        var exe = Process.GetCurrentProcess().MainModule?.FileName ?? throw new InvalidOperationException("Cannot locate talk2tia.exe.");
        var perMachine = exe.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), StringComparison.OrdinalIgnoreCase);
        var properties = perMachine ? "ALLUSERS=1" : "ALLUSERS=2 MSIINSTALLPERUSER=1";
        var log = Path.Combine(DownloadDirectory, "install.log");
        static string Quote(string s) => "'" + s.Replace("'", "''") + "'";
        var script =
            $"$exe = {Quote(exe)}; " +
            "while (Get-Process talk2tia -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exe }) { Start-Sleep -Seconds 5 }; " +
            $"Start-Process msiexec.exe -ArgumentList {Quote($"/i \"{msi}\" /passive /norestart {properties} /l*v \"{log}\"")}" +
            (perMachine ? " -Verb RunAs" : string.Empty) + " -Wait";
        Process.Start(new ProcessStartInfo("powershell.exe", $"-NoProfile -NonInteractive -WindowStyle Hidden -Command \"{script.Replace("\"", "\\\"")}\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        });
        _installScheduled = true;
        Changed?.Invoke();
    }

    private sealed record State(DateTime CheckedUtc, UpdateInfo? Latest);

    private static State? ReadState()
    {
        try
        {
            return File.Exists(StateFile) ? JsonSerializer.Deserialize<State>(File.ReadAllText(StateFile)) : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"[talk2tia] ignoring update state: {ex.Message}");
            return null;
        }
    }

    private static void WriteState(State state)
    {
        try
        {
            File.WriteAllText(StateFile, JsonSerializer.Serialize(state));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"[talk2tia] cannot save update state: {ex.Message}");
        }
    }

    public void Dispose()
    {
        _http.Dispose();
        _gate.Dispose();
    }
}
