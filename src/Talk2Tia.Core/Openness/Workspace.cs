using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Talk2Tia.Core.Openness;

/// <summary>Temporary working directory for export/import round-trips. Deleted on dispose.</summary>
internal sealed class Workspace : IDisposable
{
    /// <summary>
    /// Folder for the export/import round-trips with TIA Portal. TIA Portal runs outside the MCP client, so this folder
    /// must be the same real folder for both processes: see <see cref="DefaultRoot"/>.
    /// </summary>
    public static string Root { get; private set; } = DefaultRoot();

    /// <summary>Uses the configured folder (config 'workDirectory' or TALK2TIA_WORKDIR), or the default one.</summary>
    public static void Configure(string? directory)
    {
        Root = string.IsNullOrWhiteSpace(directory)
            ? DefaultRoot()
            : Path.GetFullPath(Environment.ExpandEnvironmentVariables(directory!.Trim()));
    }

    /// <summary>
    /// %LOCALAPPDATA%\Talk2Tia\work, except when Talk2Tia runs inside an MSIX package (e.g. the Claude desktop app from the
    /// Microsoft Store or the MSIX installer): there Windows redirects AppData writes to a private copy that TIA Portal
    /// cannot see, so the default becomes %USERPROFILE%\Talk2Tia\work, which is not virtualized.
    /// </summary>
    public static string DefaultRoot() =>
        RunsInPackage()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Talk2Tia", "work")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Talk2Tia", "work");

    /// <summary>True when the process has an MSIX package identity (inherited from a packaged MCP client).</summary>
    public static bool RunsInPackage()
    {
        try
        {
            uint length = 0;
            const int AppModelErrorNoPackage = 15700;
            return GetCurrentPackageFullName(ref length, null) != AppModelErrorNoPackage;
        }
        catch (EntryPointNotFoundException)
        {
            return false; // Windows without the AppModel API
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetCurrentPackageFullName(ref uint packageFullNameLength, StringBuilder? packageFullName);

    public DirectoryInfo Directory { get; }

    public Workspace()
    {
        Directory = new DirectoryInfo(Path.Combine(Root, Guid.NewGuid().ToString("N")));
        Directory.Create();
    }

    public string PathOf(string fileName) => Path.Combine(Directory.FullName, fileName);

    public FileInfo FileOf(string fileName) => new(PathOf(fileName));

    public void Dispose()
    {
        try
        {
            Directory.Refresh();
            if (Directory.Exists)
            {
                Directory.Delete(recursive: true);
            }
        }
        catch (IOException ex)
        {
            // A file may still be locked by TIA Portal; the folder is reused-safe and cleaned on next start.
            Console.Error.WriteLine($"[talk2tia] could not delete workspace {Directory.FullName}: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            Console.Error.WriteLine($"[talk2tia] could not delete workspace {Directory.FullName}: {ex.Message}");
        }
    }

    /// <summary>Removes leftovers from previous runs.</summary>
    public static void CleanupStale()
    {
        var root = new DirectoryInfo(Root);
        if (!root.Exists)
        {
            return;
        }

        foreach (var dir in root.GetDirectories())
        {
            try
            {
                if (dir.CreationTimeUtc < DateTime.UtcNow.AddHours(-12))
                {
                    dir.Delete(recursive: true);
                }
            }
            catch (IOException ex)
            {
                Console.Error.WriteLine($"[talk2tia] stale workspace cleanup skipped {dir.Name}: {ex.Message}");
            }
            catch (UnauthorizedAccessException ex)
            {
                Console.Error.WriteLine($"[talk2tia] stale workspace cleanup skipped {dir.Name}: {ex.Message}");
            }
        }
    }
}
