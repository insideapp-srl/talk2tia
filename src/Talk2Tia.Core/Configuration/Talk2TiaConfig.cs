using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Talk2Tia.Core.Configuration;

/// <summary>
/// Optional server configuration (JSON). Located with <c>--config &lt;path&gt;</c>, the <c>TALK2TIA_CONFIG</c> environment
/// variable or <c>%APPDATA%\Talk2Tia\config.json</c>. Environment variables override the file.
/// </summary>
public sealed class Talk2TiaConfig
{
    public const string ConfigEnv = "TALK2TIA_CONFIG";
    public const string ToolsetsEnv = "TALK2TIA_TOOLSETS";
    public const string ReadOnlyEnv = "TALK2TIA_READONLY";
    public const string ProfileFull = "full";
    public const string ProfileReadOnly = "readonly";

    /// <summary>"full" (default) or "readonly".</summary>
    public string? Profile { get; set; }

    /// <summary>Toolsets to expose; null or ["all"] exposes every toolset.</summary>
    public List<string>? Toolsets { get; set; }

    /// <summary>Tool names (or patterns with *) added on top of the profile.</summary>
    public List<string>? Allow { get; set; }

    /// <summary>Tool names (or patterns with *) removed in any case. Deny always wins.</summary>
    public List<string>? Deny { get; set; }

    /// <summary>PLCs reachable through the CPU Web API, keyed by a name used in the diagnostics tools.</summary>
    public Dictionary<string, PlcEndpoint>? Plcs { get; set; }

    /// <summary>File the configuration was read from, if any.</summary>
    [JsonIgnore]
    public string? Source { get; private set; }

    [JsonIgnore]
    public bool IsReadOnly => string.Equals(Profile, ProfileReadOnly, StringComparison.OrdinalIgnoreCase);

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public static Talk2TiaConfig Parse(string json, string? source = null)
    {
        Talk2TiaConfig config;
        try
        {
            config = JsonSerializer.Deserialize<Talk2TiaConfig>(json, Options) ?? new Talk2TiaConfig();
        }
        catch (JsonException ex)
        {
            throw new ArgumentException($"Invalid Talk2Tia configuration{(source is null ? string.Empty : $" ({source})")}: {ex.Message}", ex);
        }

        config.Source = source;
        config.Validate();
        return config;
    }

    /// <summary>Loads the configuration file (if any) and applies the environment overrides.</summary>
    public static Talk2TiaConfig Load(string? explicitPath, Func<string, string?>? env = null)
    {
        env ??= Environment.GetEnvironmentVariable;
        var path = ResolvePath(explicitPath, env);
        Talk2TiaConfig config;
        if (path is null)
        {
            config = new Talk2TiaConfig();
            config.Validate();
        }
        else
        {
            if (!File.Exists(path))
            {
                throw new ArgumentException($"Talk2Tia configuration file not found: {path}");
            }

            config = Parse(File.ReadAllText(path), path);
        }

        config.ApplyEnvironment(env);
        return config;
    }

    internal static string? ResolvePath(string? explicitPath, Func<string, string?> env)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            return Path.GetFullPath(Environment.ExpandEnvironmentVariables(explicitPath!));
        }

        var fromEnv = env(ConfigEnv);
        if (!string.IsNullOrWhiteSpace(fromEnv))
        {
            return Path.GetFullPath(Environment.ExpandEnvironmentVariables(fromEnv!));
        }

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var fallback = Path.Combine(appData, "Talk2Tia", "config.json");
        return string.IsNullOrEmpty(appData) || !File.Exists(fallback) ? null : fallback;
    }

    internal void ApplyEnvironment(Func<string, string?> env)
    {
        var toolsets = env(ToolsetsEnv);
        if (!string.IsNullOrWhiteSpace(toolsets))
        {
            Toolsets = toolsets!.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToList();
        }

        var readOnly = env(ReadOnlyEnv);
        if (!string.IsNullOrWhiteSpace(readOnly))
        {
            var value = readOnly!.Trim();
            if (value is "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase) || value.Equals("yes", StringComparison.OrdinalIgnoreCase))
            {
                Profile = ProfileReadOnly;
            }
            else if (value is not "0" && !value.Equals("false", StringComparison.OrdinalIgnoreCase) && !value.Equals("no", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException($"Invalid {ReadOnlyEnv} value '{readOnly}'. Use 1/true or 0/false.");
            }
        }
    }

    private void Validate()
    {
        Profile = string.IsNullOrWhiteSpace(Profile) ? ProfileFull : Profile!.Trim().ToLowerInvariant();
        if (Profile is not ProfileFull and not ProfileReadOnly)
        {
            throw new ArgumentException($"Invalid profile '{Profile}'. Use '{ProfileFull}' or '{ProfileReadOnly}'.");
        }

        foreach (var pair in Plcs ?? new Dictionary<string, PlcEndpoint>())
        {
            if (string.IsNullOrWhiteSpace(pair.Key))
            {
                throw new ArgumentException("PLC names in 'plcs' must not be empty.");
            }

            pair.Value.Validate(pair.Key);
        }
    }
}

/// <summary>Connection to the Web API of a CPU (S7-1500, S7-1200 G2, PLCSIM Advanced with web server).</summary>
public sealed class PlcEndpoint
{
    /// <summary>IP address or host name of the CPU web server.</summary>
    public string Address { get; set; } = string.Empty;

    /// <summary>Web server user. Omit to use the rights of the "Everybody" user.</summary>
    public string? Username { get; set; }

    /// <summary>Name of the environment variable holding the password (never put the password in the file).</summary>
    public string? PasswordEnv { get; set; }

    /// <summary>SHA-1 or SHA-256 thumbprint of the CPU certificate to trust (for the self-signed default certificate).</summary>
    public string? CertificateThumbprint { get; set; }

    /// <summary>Accept any certificate. Only for isolated test networks.</summary>
    public bool AllowUntrustedCertificate { get; set; }

    /// <summary>Request timeout in seconds (default 10).</summary>
    public int TimeoutSeconds { get; set; } = 10;

    internal void Validate(string name)
    {
        if (string.IsNullOrWhiteSpace(Address))
        {
            throw new ArgumentException($"PLC '{name}': 'address' is required.");
        }

        if (!string.IsNullOrWhiteSpace(Username) && string.IsNullOrWhiteSpace(PasswordEnv))
        {
            throw new ArgumentException($"PLC '{name}': 'passwordEnv' is required with 'username' (the password is read from that environment variable).");
        }

        if (TimeoutSeconds is < 1 or > 300)
        {
            throw new ArgumentException($"PLC '{name}': 'timeoutSeconds' must be between 1 and 300.");
        }

        if (CertificateThumbprint is not null)
        {
            CertificateThumbprint = NormalizeThumbprint(CertificateThumbprint);
            if (CertificateThumbprint.Length is not 40 and not 64 || !CertificateThumbprint.All(Uri.IsHexDigit))
            {
                throw new ArgumentException($"PLC '{name}': 'certificateThumbprint' must be a SHA-1 (40) or SHA-256 (64) hex string.");
            }
        }
    }

    internal static string NormalizeThumbprint(string value) =>
        new string(value.Where(c => !char.IsWhiteSpace(c) && c != ':').ToArray()).ToUpperInvariant();
}
