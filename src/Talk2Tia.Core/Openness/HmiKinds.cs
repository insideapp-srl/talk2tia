using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.Hmi;

namespace Talk2Tia.Core.Openness;

/// <summary>
/// HMI type checks that do not load the WinCC Unified assembly (Siemens.Engineering.HmiUnified), which is missing on
/// PCs where the WinCC Unified option is not installed. Code that touches Unified types lives in <see cref="UnifiedHmi"/>
/// and runs only for Unified software, so the JIT never needs that assembly otherwise.
/// </summary>
internal static class HmiKinds
{
    private const string UnifiedSoftware = "Siemens.Engineering.HmiUnified.HmiSoftware";

    public static bool IsClassic(Software software) => software is HmiTarget;

    public static bool IsUnified(Software software)
    {
        for (var t = software.GetType(); t is not null; t = t.BaseType)
        {
            if (t.FullName == UnifiedSoftware)
            {
                return true;
            }
        }

        return false;
    }

    public static bool IsHmi(Software software) => IsClassic(software) || IsUnified(software);
}

/// <summary>WinCC Unified access, kept out of line so the Unified assembly is loaded only when a Unified HMI exists.</summary>
internal static class UnifiedHmi
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static object Screens(Software software) =>
        ((Siemens.Engineering.HmiUnified.HmiSoftware)software).Screens.Select(s => new { path = s.Name, name = s.Name }).ToList();

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static object TagTables(Software software, string? table, IReadOnlyList<string> attributes) =>
        ((Siemens.Engineering.HmiUnified.HmiSoftware)software).TagTables
            .Where(t => table is null || Locator.NameEquals(t.Name, table))
            .Select(t => new
            {
                table = t.Name,
                tags = t.Tags.Select(tag => ValueFormatter.ReadAttributes(tag, attributes)).ToList(),
            }).ToList();
}
