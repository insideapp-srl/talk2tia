using System;
using System.IO;
using System.Reflection;
using Microsoft.Win32;

namespace Talk2Tia.Core.Openness;

/// <summary>
/// Resolves Siemens.Engineering.* assemblies from the TIA Portal installation at runtime.
/// Must be installed before any type that references the Openness API is loaded.
/// </summary>
public static class OpennessResolver
{
    private const string TiaVersion = "21.0";
    private const string EnvOverride = "TALK2TIA_PUBLICAPI_DIR";
    private static string? _publicApiDir;

    public static string? PublicApiDirectory => _publicApiDir;

    public static void Install()
    {
        _publicApiDir = LocatePublicApiDirectory();
        AppDomain.CurrentDomain.AssemblyResolve += OnAssemblyResolve;
    }

    internal static string? LocatePublicApiDirectory()
    {
        var fromEnv = Environment.GetEnvironmentVariable(EnvOverride);
        if (!string.IsNullOrWhiteSpace(fromEnv) && Directory.Exists(fromEnv))
        {
            return fromEnv;
        }

        var keyPath = $@"SOFTWARE\Siemens\Automation\Openness\{TiaVersion}\PublicAPI\{TiaVersion}.0.0\net48";
        using (var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
        using (var key = baseKey.OpenSubKey(keyPath))
        {
            if (key?.GetValue("Siemens.Engineering.Base") is string basePath && File.Exists(basePath))
            {
                return Path.GetDirectoryName(basePath);
            }
        }

        var fallback = $@"C:\Program Files\Siemens\Automation\Portal V{TiaVersion.Split('.')[0]}\PublicAPI\V{TiaVersion.Split('.')[0]}\net48";
        return Directory.Exists(fallback) ? fallback : null;
    }

    private static Assembly? OnAssemblyResolve(object? sender, ResolveEventArgs args)
    {
        var name = new AssemblyName(args.Name).Name;
        if (_publicApiDir is null || name is null || !name.StartsWith("Siemens.Engineering", StringComparison.Ordinal))
        {
            return null;
        }

        var candidate = Path.Combine(_publicApiDir, name + ".dll");
        return File.Exists(candidate) ? Assembly.LoadFrom(candidate) : null;
    }
}
