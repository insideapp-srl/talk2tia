using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Principal;
using System.Threading;
using Talk2Tia.Core.Configuration;
using Talk2Tia.Core.WebApi;
using Talk2Tia.Core.Openness;

namespace Talk2Tia.Core;

/// <summary>Environment self-check: `talk2tia --doctor`.</summary>
public static class Doctor
{
    public static int Run(string[]? args = null)
    {
        var ok = true;
        Console.WriteLine($"Talk2Tia {typeof(Doctor).Assembly.GetName().Version}");
        Console.WriteLine($"Process: {(Environment.Is64BitProcess ? "x64" : "x86")}, .NET {Environment.Version}");

        var dir = OpennessResolver.PublicApiDirectory;
        ok &= Check("TIA Portal V21 Openness API", dir is not null, dir ?? "not found (set TALK2TIA_PUBLICAPI_DIR)");

        var inGroup = IsInOpennessGroup();
        ok &= Check("User in 'Siemens TIA Openness' group", inGroup,
            inGroup ? WindowsIdentity.GetCurrent().Name : "add the user to the group, then sign out and in again");

        if (dir is not null)
        {
            try
            {
                var count = CountProcesses();
                Check("Running TIA Portal instances", true, count.ToString());
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                ok &= Check("Openness API loads", false, $"{ex.GetType().Name}: {ex.Message}");
            }
        }

        ok &= CheckConfiguration(args ?? Array.Empty<string>());

        Console.WriteLine(ok ? "All checks passed." : "Some checks failed.");
        return ok ? 0 : 1;
    }

    private static bool CheckConfiguration(string[] args)
    {
        Talk2TiaConfig config;
        IReadOnlyList<ToolInfo> tools;
        try
        {
            var options = CommandLine.Parse(args.Where(a => a != "--doctor").ToArray());
            config = Talk2TiaConfig.Load(options.ConfigPath);
            if (options.ReadOnly)
            {
                config.Profile = Talk2TiaConfig.ProfileReadOnly;
            }

            tools = ToolPolicy.Select(config);
        }
        catch (ArgumentException ex)
        {
            return Check("Configuration", false, ex.Message);
        }

        var ok = Check("Configuration", true, config.Source ?? "no file (defaults)");
        Check("Tools exposed", true, $"{tools.Count} of {ToolPolicy.Catalog.Count}, profile '{config.Profile}'");
        Workspace.Configure(config.WorkDirectory);
        ok &= CheckWorkDirectory(config);
        using var registry = new PlcWebApiRegistry(config);
        foreach (var name in registry.Endpoints.Keys)
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                var calls = registry.Get(name).TryCallManyAsync(
                    new[] { new WebApiCall("Api.Ping"), new WebApiCall("Plc.ReadOperatingMode") }, cts.Token).GetAwaiter().GetResult();
                var detail = calls[1].Error is null ? $"reachable, mode {calls[1].Result}" : $"reachable; {calls[1].Error!.Message}";
                ok &= Check($"Web API PLC '{name}' ({registry.Endpoints[name].Address})", calls[0].Error is null, calls[0].Error?.Message ?? detail);
            }
            catch (Exception ex) when (ex is InvalidOperationException or WebApiException or OperationCanceledException)
            {
                ok &= Check($"Web API PLC '{name}' ({registry.Endpoints[name].Address})", false, ex.Message);
            }
        }

        return ok;
    }

    private static bool CheckWorkDirectory(Talk2TiaConfig config)
    {
        var origin = config.WorkDirectory is null
            ? Workspace.RunsInPackage() ? "default, MSIX client detected" : "default"
            : "configured";
        try
        {
            using var ws = new Workspace();
            System.IO.File.WriteAllText(ws.PathOf("probe.txt"), "ok");
            return Check("Work directory", true, $"{Workspace.Root} ({origin})");
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return Check("Work directory", false, $"{Workspace.Root} ({origin}): {ex.Message}");
        }
    }

    private static bool Check(string name, bool passed, string detail)
    {
        Console.WriteLine($"[{(passed ? " OK " : "FAIL")}] {name}: {detail}");
        return passed;
    }

    private static bool IsInOpennessGroup()
    {
        var identity = WindowsIdentity.GetCurrent();
        return identity.Groups?
            .Select(g =>
            {
                try
                {
                    return g.Translate(typeof(NTAccount)).Value;
                }
                catch (IdentityNotMappedException)
                {
                    return string.Empty;
                }
            })
            .Any(n => n.EndsWith(@"\Siemens TIA Openness", StringComparison.OrdinalIgnoreCase)) ?? false;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int CountProcesses() => Siemens.Engineering.TiaPortal.GetProcesses().Count;
}
