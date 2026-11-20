using System;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Principal;
using Talk2Tia.Core.Openness;

namespace Talk2Tia.Core;

/// <summary>Environment self-check: `talk2tia --doctor`.</summary>
public static class Doctor
{
    public static int Run()
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

        Console.WriteLine(ok ? "All checks passed." : "Some checks failed.");
        return ok ? 0 : 1;
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
