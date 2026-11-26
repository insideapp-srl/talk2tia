using System;
using System.Collections.Generic;
using System.Linq;
using ModelContextProtocol;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.ExternalSources;
using Siemens.Engineering.SW.Tags;
using Siemens.Engineering.SW.Types;

namespace Talk2Tia.Core.Openness;

internal sealed record SoftwareRef(Device Device, DeviceItem DeviceItem, Software Software)
{
    public string Name => Software.Name;
    public string Kind => Software switch
    {
        PlcSoftware => "PLC",
        Siemens.Engineering.Hmi.HmiTarget => "HMI (WinCC Classic/Comfort/Advanced)",
        _ when HmiKinds.IsUnified(Software) => "HMI (WinCC Unified)",
        _ => Software.GetType().Name,
    };
}

/// <summary>Finds objects inside the project by name or slash-separated path.</summary>
internal static class Locator
{
    public const char PathSeparator = '/';

    // ---------- Devices ----------

    public static IEnumerable<Device> AllDevices(Project project)
    {
        foreach (var device in project.Devices)
        {
            yield return device;
        }

        foreach (var device in project.UngroupedDevicesGroup.Devices)
        {
            yield return device;
        }

        foreach (var group in project.DeviceGroups)
        {
            foreach (var device in DevicesInGroup(group))
            {
                yield return device;
            }
        }
    }

    private static IEnumerable<Device> DevicesInGroup(DeviceUserGroup group)
    {
        foreach (var device in group.Devices)
        {
            yield return device;
        }

        foreach (var sub in group.Groups)
        {
            foreach (var device in DevicesInGroup(sub))
            {
                yield return device;
            }
        }
    }

    public static IEnumerable<DeviceItem> AllItems(HardwareObject parent)
    {
        foreach (var item in parent.DeviceItems)
        {
            yield return item;
            foreach (var child in AllItems(item))
            {
                yield return child;
            }
        }
    }

    public static Device FindDevice(Project project, string name)
    {
        return AllDevices(project).FirstOrDefault(d => NameEquals(d.Name, name))
               ?? throw new McpException($"Device '{name}' not found. Available: {string.Join(", ", AllDevices(project).Select(d => d.Name))}");
    }

    /// <summary>
    /// Finds a device item by name anywhere in the project, or "Device/Item" for disambiguation. Names that contain '/'
    /// (e.g. "Standard I/O") are matched as a whole first.
    /// </summary>
    public static DeviceItem FindDeviceItem(Project project, string path)
    {
        var trimmed = path.Trim();
        var devices = AllDevices(project).ToList();
        var exact = devices.SelectMany(AllItems).FirstOrDefault(i => NameEquals(i.Name, trimmed));
        if (exact is not null)
        {
            return exact;
        }

        // "Device/Item": the device name is everything before one of the separators.
        for (var idx = trimmed.IndexOf(PathSeparator); idx > 0; idx = trimmed.IndexOf(PathSeparator, idx + 1))
        {
            var deviceName = trimmed.Substring(0, idx);
            var itemName = trimmed.Substring(idx + 1);
            var item = devices.Where(d => NameEquals(d.Name, deviceName)).SelectMany(AllItems).FirstOrDefault(i => NameEquals(i.Name, itemName));
            if (item is not null)
            {
                return item;
            }
        }

        throw new McpException($"Device item '{path}' not found. Use hw_list_devices to see the hardware tree.");
    }

    // ---------- Software containers ----------

    public static IEnumerable<SoftwareRef> AllSoftware(Project project)
    {
        foreach (var device in AllDevices(project))
        {
            foreach (var item in AllItems(device))
            {
                var container = item.GetService<SoftwareContainer>();
                if (container?.Software is { } software)
                {
                    yield return new SoftwareRef(device, item, software);
                }
            }
        }
    }

    public static SoftwareRef FindPlcRef(Project project, string? name)
    {
        var plcs = AllSoftware(project).Where(s => s.Software is PlcSoftware).ToList();
        return Pick(plcs, name, "PLC");
    }

    public static PlcSoftware FindPlc(Project project, string? name) => (PlcSoftware)FindPlcRef(project, name).Software;

    public static SoftwareRef FindHmiRef(Project project, string? name)
    {
        var hmis = AllSoftware(project)
            .Where(s => HmiKinds.IsHmi(s.Software))
            .ToList();
        return Pick(hmis, name, "HMI");
    }

    private static SoftwareRef Pick(List<SoftwareRef> candidates, string? name, string kind)
    {
        if (candidates.Count == 0)
        {
            throw new McpException($"The project contains no {kind}.");
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            if (candidates.Count == 1)
            {
                return candidates[0];
            }

            throw new McpException(
                $"The project contains several {kind}s; specify one of: {string.Join(", ", candidates.Select(c => $"{c.Name} (device {c.Device.Name})"))}");
        }

        return candidates.FirstOrDefault(c => NameEquals(c.Name, name!))
               ?? candidates.FirstOrDefault(c => NameEquals(c.DeviceItem.Name, name!))
               ?? candidates.FirstOrDefault(c => NameEquals(c.Device.Name, name!))
               ?? throw new McpException($"{kind} '{name}' not found. Available: {string.Join(", ", candidates.Select(c => c.Name))}");
    }

    // ---------- Block groups & blocks ----------

    public static PlcBlockGroup ResolveBlockGroup(PlcSoftware plc, string? groupPath, bool create = false)
    {
        PlcBlockGroup group = plc.BlockGroup;
        foreach (var segment in Segments(groupPath))
        {
            var next = group.Groups.Find(segment);
            if (next is null)
            {
                if (!create)
                {
                    throw new McpException($"Block group '{groupPath}' not found (missing '{segment}').");
                }

                next = group.Groups.Create(segment);
            }

            group = next;
        }

        return group;
    }

    public static IEnumerable<(string Path, PlcBlock Block)> AllBlocks(PlcBlockGroup group, string prefix = "")
    {
        foreach (var block in group.Blocks)
        {
            yield return (Join(prefix, block.Name), block);
        }

        foreach (var sub in group.Groups)
        {
            foreach (var entry in AllBlocks(sub, Join(prefix, sub.Name)))
            {
                yield return entry;
            }
        }
    }

    /// <summary>Finds a block by name (searched in all groups) or by "Group/Sub/Name".</summary>
    public static PlcBlock FindBlock(PlcSoftware plc, string path)
    {
        return TryFindBlock(plc, path)
               ?? throw new McpException($"Block '{path}' not found. Use plc_list_blocks to see the available blocks.");
    }

    public static PlcBlock? TryFindBlock(PlcSoftware plc, string path) => FindByPath(AllBlocks(plc.BlockGroup), b => b.Name, path);

    // ---------- Types (UDT) ----------

    public static PlcTypeGroup ResolveTypeGroup(PlcSoftware plc, string? groupPath, bool create = false)
    {
        PlcTypeGroup group = plc.TypeGroup;
        foreach (var segment in Segments(groupPath))
        {
            var next = group.Groups.Find(segment);
            if (next is null)
            {
                if (!create)
                {
                    throw new McpException($"Type group '{groupPath}' not found (missing '{segment}').");
                }

                next = group.Groups.Create(segment);
            }

            group = next;
        }

        return group;
    }

    public static IEnumerable<(string Path, PlcType Type)> AllTypes(PlcTypeGroup group, string prefix = "")
    {
        foreach (var type in group.Types)
        {
            yield return (Join(prefix, type.Name), type);
        }

        foreach (var sub in group.Groups)
        {
            foreach (var entry in AllTypes(sub, Join(prefix, sub.Name)))
            {
                yield return entry;
            }
        }
    }

    public static PlcType FindType(PlcSoftware plc, string path)
    {
        return FindByPath(AllTypes(plc.TypeGroup), t => t.Name, path)
               ?? throw new McpException($"PLC data type '{path}' not found. Use plc_list_types.");
    }

    // ---------- Tag tables ----------

    public static PlcTagTableGroup ResolveTagTableGroup(PlcSoftware plc, string? groupPath, bool create = false)
    {
        PlcTagTableGroup group = plc.TagTableGroup;
        foreach (var segment in Segments(groupPath))
        {
            var next = group.Groups.Find(segment);
            if (next is null)
            {
                if (!create)
                {
                    throw new McpException($"Tag table group '{groupPath}' not found (missing '{segment}').");
                }

                next = group.Groups.Create(segment);
            }

            group = next;
        }

        return group;
    }

    public static IEnumerable<(string Path, PlcTagTable Table)> AllTagTables(PlcTagTableGroup group, string prefix = "")
    {
        foreach (var table in group.TagTables)
        {
            yield return (Join(prefix, table.Name), table);
        }

        foreach (var sub in group.Groups)
        {
            foreach (var entry in AllTagTables(sub, Join(prefix, sub.Name)))
            {
                yield return entry;
            }
        }
    }

    public static PlcTagTable FindTagTable(PlcSoftware plc, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return AllTagTables(plc.TagTableGroup).Select(e => e.Table).FirstOrDefault(t => t.IsDefault)
                   ?? throw new McpException("The PLC has no default tag table.");
        }

        return FindByPath(AllTagTables(plc.TagTableGroup), t => t.Name, path!)
               ?? throw new McpException($"Tag table '{path}' not found. Use plc_list_tag_tables.");
    }

    // ---------- External sources ----------

    public static IEnumerable<(string Path, PlcExternalSource Source)> AllExternalSources(PlcExternalSourceGroup group, string prefix = "")
    {
        foreach (var source in group.ExternalSources)
        {
            yield return (Join(prefix, source.Name), source);
        }

        foreach (var sub in group.Groups)
        {
            foreach (var entry in AllExternalSources(sub, Join(prefix, sub.Name)))
            {
                yield return entry;
            }
        }
    }

    public static PlcExternalSource FindExternalSource(PlcSoftware plc, string name)
    {
        return AllExternalSources(plc.ExternalSourceGroup).Select(e => e.Source).FirstOrDefault(s => NameEquals(s.Name, name))
               ?? throw new McpException($"External source '{name}' not found. Use plc_list_external_sources.");
    }

    // ---------- Helpers ----------

    /// <summary>
    /// Resolves a name or "Group/Sub/Name" path against a listing whose paths join names with '/'. Object names may
    /// themselves contain '/' (e.g. "_MachineAlarms_IR/HP"), so the input is not split: the lookup tries the full path
    /// as listed (the 'path' returned by the list tools), then the object name anywhere, then a trailing part of a path.
    /// </summary>
    public static T? FindByPath<T>(IEnumerable<(string Path, T Item)> entries, Func<T, string> nameOf, string path)
        where T : class
    {
        var wanted = path.Trim();
        if (wanted.Length > 1 && wanted[0] == PathSeparator)
        {
            wanted = wanted.Substring(1);
        }

        var list = entries.ToList();
        return list.FirstOrDefault(e => NameEquals(e.Path, wanted)).Item
               ?? list.FirstOrDefault(e => NameEquals(nameOf(e.Item), wanted)).Item
               ?? list.FirstOrDefault(e => e.Path.EndsWith(PathSeparator + wanted, StringComparison.OrdinalIgnoreCase)).Item;
    }

    public static IEnumerable<string> Segments(string? path) =>
        string.IsNullOrWhiteSpace(path)
            ? Enumerable.Empty<string>()
            : path!.Split(new[] { PathSeparator }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim());

    public static (string? GroupPath, string Name) SplitLast(string path)
    {
        var trimmed = path.Trim().Trim(PathSeparator);
        var idx = trimmed.LastIndexOf(PathSeparator);
        return idx < 0 ? (null, trimmed) : (trimmed.Substring(0, idx), trimmed.Substring(idx + 1));
    }

    public static string Join(string prefix, string name) => prefix.Length == 0 ? name : prefix + PathSeparator + name;

    public static bool NameEquals(string? a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
