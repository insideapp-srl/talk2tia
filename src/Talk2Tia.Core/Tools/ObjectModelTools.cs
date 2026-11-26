using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using Siemens.Engineering;
using Talk2Tia.Core.Openness;

namespace Talk2Tia.Core.Tools;

/// <summary>Generic access to any Openness object: covers the API surface that has no dedicated tool.</summary>
[McpServerToolType]
public sealed class ObjectModelTools(TiaSession tia)
{
    private const string PathHelp =
        "Object path. Roots: project | portal | plc:NAME | hmi:NAME | device:NAME | item:NAME | lib:project | lib:NAME. " +
        "Segments: Composition, Composition[name] or [#index], Attribute/Property, @Service. " +
        "Example: device:PLC_1/DeviceItems[PLC_1]/@SoftwareContainer/Software/BlockGroup/Blocks[Main]";

    [McpServerTool(Name = "obj_browse", ReadOnly = true, Idempotent = true)]
    [Description("Describes any Openness object: its compositions, attributes (with access mode and types), services and actions. " +
                 "For a composition, lists its items and what can be created in it. Start from 'project' and walk down.")]
    public Task<string> Browse(
        [Description(PathHelp)] string path = "project",
        [Description("Maximum composition items to list.")] int maxItems = 200)
        => tia.RunAsync(() => ObjectPath.Describe(ObjectPath.Resolve(tia, path), maxItems));

    [McpServerTool(Name = "obj_get", ReadOnly = true, Idempotent = true)]
    [Description("Reads the attributes of any Openness object (all readable attributes, or only the listed ones).")]
    public Task<string> Get(
        [Description(PathHelp)] string path,
        [Description("Attribute names to read; omit for all.")] string[]? attributes = null)
        => tia.RunAsync(() =>
        {
            var obj = ObjectPath.ResolveObject(tia, path);
            return new { type = obj.GetType().Name, attributes = ValueFormatter.ReadAttributes(obj, attributes) };
        });

    [McpServerTool(Name = "obj_set", Destructive = false)]
    [Description("Writes attributes of any Openness object. Values are converted to the attribute type (enums by name). " +
                 "Multilingual texts are set for 'culture' (default: first project language).")]
    public Task<string> Set(
        [Description(PathHelp)] string path,
        [Description("Attributes to write, e.g. {\"Name\": \"PLC_Main\", \"AutoNumber\": false}.")] Dictionary<string, JsonElement> attributes,
        [Description("Culture for multilingual texts, e.g. it-IT.")] string? culture = null)
        => tia.RunAsync(() =>
        {
            var obj = ObjectPath.ResolveObject(tia, path);
            return new { applied = ObjectAttributes.Apply(obj, attributes, culture) };
        });

    [McpServerTool(Name = "obj_invoke", Destructive = true)]
    [Description("Invokes an action of an Openness object or composition (see 'actions' in obj_browse), e.g. Delete, Compile, ShowInEditor. " +
                 "Parameters are passed by name; an object parameter can be given as {\"path\": \"...\"}. Deleting actions require confirm=true.")]
    public Task<string> Invoke(
        [Description(PathHelp)] string path,
        [Description("Action name.")] string action,
        [Description("Parameters by name, e.g. {\"name\": \"Conveyor\"}.")] Dictionary<string, JsonElement>? parameters = null,
        [Description("Required for actions that delete or overwrite data.")] bool confirm = false)
        => tia.RunAsync(() =>
        {
            if (action.IndexOf("Delete", StringComparison.OrdinalIgnoreCase) >= 0 ||
                action.IndexOf("Remove", StringComparison.OrdinalIgnoreCase) >= 0 ||
                action.IndexOf("Download", StringComparison.OrdinalIgnoreCase) >= 0 ||
                action.IndexOf("Reset", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                Guard.RequireConfirm(confirm, $"invoke '{action}' on {path}");
            }

            var target = ObjectPath.Resolve(tia, path);
            var args = parameters ?? new Dictionary<string, JsonElement>();
            IList<EngineeringInvocationInfo> infos = target switch
            {
                IEngineeringObject eo => eo.GetInvocationInfos(),
                IEngineeringComposition c => c.GetInvocationInfos(),
                _ => throw new McpException($"'{path}' does not support actions."),
            };

            var info = infos
                           .Where(i => i.Name.Equals(action, StringComparison.OrdinalIgnoreCase) && i.ParameterInfos.Count == args.Count)
                           .FirstOrDefault(i => i.ParameterInfos.All(p => args.Keys.Any(k => k.Equals(p.Name, StringComparison.OrdinalIgnoreCase))))
                       ?? throw new McpException(
                           $"No action '{action}' with parameters ({string.Join(", ", args.Keys)}). Available: " +
                           string.Join("; ", infos.Select(i => $"{i.Name}({string.Join(", ", i.ParameterInfos.Select(p => p.Name + ":" + p.Type.Name))})")));

            var invokeArgs = info.ParameterInfos
                .Select(p => new KeyValuePair<Type, object?>(p.Type, ConvertArgument(args.First(a => a.Key.Equals(p.Name, StringComparison.OrdinalIgnoreCase)).Value, p.Type)))
                .Select(kv => new KeyValuePair<Type, object>(kv.Key, kv.Value!))
                .ToList();

            var result = target is IEngineeringObject obj ? obj.Invoke(info.Name, invokeArgs) : ((IEngineeringComposition)target).Invoke(info.Name, invokeArgs);
            return new { invoked = info.Name, result = result is IEngineeringObject r ? ObjectPath.Describe(r, 20) : ValueFormatter.Format(result) };
        });

    [McpServerTool(Name = "obj_create", Destructive = false)]
    [Description("Creates a new object in a composition through the generic API (see 'creatable' in obj_browse on the composition). " +
                 "Parameters are passed by name.")]
    public Task<string> Create(
        [Description("Path of the composition, e.g. plc:PLC_1/TagTableGroup/TagTables.")] string path,
        [Description("Full type name of the object to create, from obj_browse 'creatable'.")] string type,
        [Description("Creation parameters by name.")] Dictionary<string, JsonElement>? parameters = null)
        => tia.RunAsync(() =>
        {
            if (ObjectPath.Resolve(tia, path) is not IEngineeringComposition composition)
            {
                throw new McpException($"'{path}' is not a composition.");
            }

            // Creating blocks through the generic API has crashed TIA Portal V21: use the dedicated tools.
            if (composition is Siemens.Engineering.SW.Blocks.PlcBlockComposition)
            {
                throw new McpException(
                    "Blocks cannot be created with obj_create (it can crash TIA Portal). Use plc_create_instance_db for instance " +
                    "data blocks, plc_create_fb, or plc_write_scl / plc_write_document / plc_write_xml with the block source.");
            }

            var args = parameters ?? new Dictionary<string, JsonElement>();
            var info = composition.GetCreationInfos()
                           .FirstOrDefault(c => c.Type.FullName == type && c.ParameterInfos.Count == args.Count)
                       ?? throw new McpException(
                           $"Cannot create '{type}' with ({string.Join(", ", args.Keys)}). Options: " +
                           string.Join("; ", composition.GetCreationInfos().Select(c => $"{c.Type.FullName}({string.Join(", ", c.ParameterInfos.Select(p => p.Name + ":" + string.Join("|", p.SupportedTypes.Select(t => t.Name))))})")));

            var createArgs = info.ParameterInfos
                .Select(p => new KeyValuePair<string, object>(p.Name,
                    ConvertCreationArgument(args.First(a => a.Key.Equals(p.Name, StringComparison.OrdinalIgnoreCase)).Value, p.SupportedTypes)!))
                .ToList();
            var created = composition.Create(info.Type, createArgs);
            return ObjectPath.Describe(created, 20);
        });

    private object? ConvertCreationArgument(JsonElement value, IEnumerable<Type> supportedTypes)
    {
        var types = supportedTypes.ToList();
        if (value.ValueKind == JsonValueKind.Object && value.TryGetProperty("path", out var p) && p.ValueKind == JsonValueKind.String)
        {
            var resolved = ObjectPath.Resolve(tia, p.GetString()!);
            return types.Any(t => t.IsInstanceOfType(resolved))
                ? resolved
                : throw new McpException($"Object at '{p.GetString()}' is {resolved.GetType().Name}, expected {string.Join(" or ", types.Select(t => t.Name))}.");
        }

        return ValueFormatter.ConvertForAttribute(value, types);
    }

    private object? ConvertArgument(JsonElement value, Type type)
    {
        if (value.ValueKind == JsonValueKind.Object && value.TryGetProperty("path", out var p) && p.ValueKind == JsonValueKind.String)
        {
            var resolved = ObjectPath.Resolve(tia, p.GetString()!);
            if (!type.IsInstanceOfType(resolved))
            {
                throw new McpException($"Object at '{p.GetString()}' is {resolved.GetType().Name}, expected {type.Name}.");
            }

            return resolved;
        }

        // Export targets: create the missing folders, TIA Portal fails with "directory does not exist" otherwise.
        if (type == typeof(System.IO.FileInfo))
        {
            var file = new System.IO.FileInfo(Environment.ExpandEnvironmentVariables(value.GetString() ?? string.Empty));
            file.Directory?.Create();
            return file;
        }

        if (type == typeof(System.IO.DirectoryInfo))
        {
            var dir = new System.IO.DirectoryInfo(Environment.ExpandEnvironmentVariables(value.GetString() ?? string.Empty));
            dir.Create();
            return dir;
        }

        return ValueFormatter.ConvertForAttribute(value, new[] { type });
    }
}
