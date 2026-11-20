using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using ModelContextProtocol;
using Siemens.Engineering;
using Siemens.Engineering.Library;

namespace Talk2Tia.Core.Openness;

/// <summary>
/// Generic navigation over the Openness object model.
/// Syntax: root[/segment]... where root is one of
///   project | portal | plc:NAME | hmi:NAME | device:NAME | item:NAME | lib:project | lib:GLOBAL_LIBRARY_NAME
/// and each segment is
///   Name          composition, attribute or property returning an object
///   Name[key]     item of a composition by name, or [#3] by index
///   @Service      a service of the current object (e.g. @SoftwareContainer, @NetworkInterface, @ICompilable)
/// Example: device:PLC_1/DeviceItems[PLC_1]/@SoftwareContainer/Software/BlockGroup/Blocks[Main]
/// </summary>
internal static class ObjectPath
{
    private static readonly Regex SegmentPattern = new(@"^(?<name>@?[^\[\]]+?)(\[(?<key>[^\]]*)\])?$", RegexOptions.Compiled);

    public static object Resolve(TiaSession tia, string path)
    {
        var segments = Split(path);
        if (segments.Count == 0)
        {
            throw new McpException("Empty object path.");
        }

        object current = ResolveRoot(tia, segments[0]);
        for (var i = 1; i < segments.Count; i++)
        {
            current = Step(current, segments[i], string.Join("/", segments.Take(i + 1)));
        }

        return current;
    }

    public static IEngineeringObject ResolveObject(TiaSession tia, string path) =>
        Resolve(tia, path) as IEngineeringObject
        ?? throw new McpException($"'{path}' is a composition, not an object. Add [name] or [#index] to select an item.");

    private static object ResolveRoot(TiaSession tia, string root)
    {
        var idx = root.IndexOf(':');
        var kind = (idx < 0 ? root : root.Substring(0, idx)).Trim().ToLowerInvariant();
        var arg = idx < 0 ? null : root.Substring(idx + 1).Trim();
        return kind switch
        {
            "project" => tia.Project,
            "portal" => tia.Portal,
            "plc" => Locator.FindPlcRef(tia.Project, arg).Software,
            "hmi" => Locator.FindHmiRef(tia.Project, arg).Software,
            "device" => Locator.FindDevice(tia.Project, Required(arg, root)),
            "item" => Locator.FindDeviceItem(tia.Project, Required(arg, root)),
            "lib" when arg is null || arg.Equals("project", StringComparison.OrdinalIgnoreCase) => tia.Project.ProjectLibrary,
            "lib" => FindGlobalLibrary(tia, arg),
            _ => throw new McpException($"Unknown root '{root}'. Use project, portal, plc:NAME, hmi:NAME, device:NAME, item:NAME, lib:project or lib:NAME."),
        };
    }

    internal static GlobalLibrary FindGlobalLibrary(TiaSession tia, string name) =>
        tia.Portal.GlobalLibraries.FirstOrDefault(l => Locator.NameEquals(l.Name, name))
        ?? throw new McpException($"Global library '{name}' is not open. Open: {string.Join(", ", tia.Portal.GlobalLibraries.Select(l => l.Name))}");

    private static string Required(string? arg, string root) =>
        string.IsNullOrWhiteSpace(arg) ? throw new McpException($"Root '{root}' needs a name, e.g. device:PLC_1.") : arg!;

    private static object Step(object current, string segment, string pathSoFar)
    {
        var match = SegmentPattern.Match(segment);
        if (!match.Success)
        {
            throw new McpException($"Invalid path segment '{segment}'.");
        }

        var name = match.Groups["name"].Value.Trim();
        var next = name.StartsWith("@", StringComparison.Ordinal)
            ? GetService(current, name.Substring(1))
            : GetMember(current, name);

        if (next is null)
        {
            throw new McpException($"'{pathSoFar}' resolved to null.");
        }

        if (match.Groups["key"].Success)
        {
            next = SelectItem(next, match.Groups["key"].Value, pathSoFar);
        }

        return next;
    }

    private static object? GetMember(object current, string name)
    {
        if (current is IEngineeringObject eo)
        {
            if (eo.GetCompositionInfos().Any(c => c.Name == name))
            {
                return eo.GetComposition(name);
            }

            if (eo.GetAttributeInfos().Any(a => a.Name == name))
            {
                return eo.GetAttribute(name);
            }
        }

        var property = current.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
        if (property is not null)
        {
            return property.GetValue(current);
        }

        throw new McpException($"{current.GetType().Name} has no composition, attribute or property '{name}'. Use obj_browse to see the members.");
    }

    private static object SelectItem(object composition, string key, string pathSoFar)
    {
        if (composition is not IEnumerable items || composition is string)
        {
            throw new McpException($"'{pathSoFar}' is not a collection; remove [{key}].");
        }

        var list = items.Cast<object>().ToList();
        if (key.StartsWith("#", StringComparison.Ordinal) && int.TryParse(key.Substring(1), out var index))
        {
            if (index < 0 || index >= list.Count)
            {
                throw new McpException($"Index {index} out of range (0..{list.Count - 1}) in '{pathSoFar}'.");
            }

            return list[index];
        }

        return list.FirstOrDefault(o => o is IEngineeringObject e && Locator.NameEquals(ValueFormatter.TryGetName(e), key))
               ?? throw new McpException(
                   $"No item named '{key}' in '{pathSoFar}'. Items: {string.Join(", ", list.OfType<IEngineeringObject>().Select(ValueFormatter.TryGetName).Take(50))}");
    }

    private static readonly MethodInfo GetServiceMethod =
        typeof(IEngineeringServiceProvider).GetMethod(nameof(IEngineeringServiceProvider.GetService))!;

    private static object? GetService(object current, string serviceName)
    {
        if (current is not IEngineeringServiceProvider provider)
        {
            throw new McpException($"{current.GetType().Name} does not provide services.");
        }

        var info = provider.GetServiceInfos().FirstOrDefault(s => Locator.NameEquals(s.Type.Name, serviceName))
                   ?? throw new McpException(
                       $"Service '{serviceName}' not available on {current.GetType().Name}. Available: {string.Join(", ", provider.GetServiceInfos().Select(s => s.Type.Name))}");
        return GetServiceMethod.MakeGenericMethod(info.Type).Invoke(provider, null);
    }

    public static IList<string> Split(string path) =>
        path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).Where(s => s.Length > 0).ToList();

    /// <summary>Describes an object or composition for obj_browse.</summary>
    public static object Describe(object target, int maxItems)
    {
        if (target is IEngineeringObject eo)
        {
            return new
            {
                type = eo.GetType().FullName,
                name = ValueFormatter.TryGetName(eo),
                compositions = eo.GetCompositionInfos().Select(c => new { name = c.Name, itemType = c.Type?.Name }).ToList(),
                attributes = eo.GetAttributeInfos().Select(a => new
                {
                    name = a.Name,
                    access = a.AccessMode.ToString(),
                    types = a.SupportedTypes.Select(t => t.Name).ToList(),
                }).ToList(),
                services = (eo as IEngineeringServiceProvider)?.GetServiceInfos().Select(s => s.Type.Name).ToList(),
                actions = eo.GetInvocationInfos().Select(FormatInvocation).ToList(),
            };
        }

        if (target is IEngineeringComposition composition)
        {
            var items = composition.Cast<object>().ToList();
            return new
            {
                type = composition.GetType().FullName,
                count = items.Count,
                items = items.Take(maxItems).Select((o, i) => new
                {
                    index = i,
                    name = o is IEngineeringObject e ? ValueFormatter.TryGetName(e) : o.ToString(),
                    type = o.GetType().Name,
                }).ToList(),
                creatable = composition.GetCreationInfos().Select(c => new
                {
                    type = c.Type.FullName,
                    parameters = c.ParameterInfos.Select(p => new { name = p.Name, mandatory = p.IsMandatory, types = p.SupportedTypes.Select(t => t.Name).ToList() }).ToList(),
                }).ToList(),
                actions = composition.GetInvocationInfos().Select(FormatInvocation).ToList(),
            };
        }

        return new { type = target.GetType().FullName, value = ValueFormatter.Format(target) };
    }

    private static object FormatInvocation(EngineeringInvocationInfo info) => new
    {
        name = info.Name,
        parameters = info.ParameterInfos.Select(p => new { name = p.Name, type = p.Type.FullName }).ToList(),
    };
}
