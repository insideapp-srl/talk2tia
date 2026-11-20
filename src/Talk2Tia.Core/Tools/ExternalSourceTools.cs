using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.ExternalSources;
using Talk2Tia.Core.Openness;

namespace Talk2Tia.Core.Tools;

[McpServerToolType]
public sealed class ExternalSourceTools(TiaSession tia)
{
    private const string PlcParam = "PLC software, CPU or device name. Optional when the project has one PLC.";

    [McpServerTool(Name = "plc_list_external_sources", ReadOnly = true, Idempotent = true)]
    [Description("Lists the external source files stored in the PLC.")]
    public Task<string> List([Description(PlcParam)] string? plc = null) => tia.RunAsync(() =>
        Locator.AllExternalSources(Locator.FindPlc(tia.Project, plc).ExternalSourceGroup)
            .Select(e => new { path = e.Path, name = e.Source.Name }).ToList());

    [McpServerTool(Name = "plc_add_external_source", Destructive = false)]
    [Description("Adds a source file (.scl, .db, .udt, .awl) to the PLC external sources without generating blocks.")]
    public Task<string> Add(
        [Description("Path of the source file.")] string filePath,
        [Description("Name in the project (default: file name).")] string? name = null,
        [Description(PlcParam)] string? plc = null)
        => tia.RunAsync(() =>
        {
            var file = Guard.ExistingFile(filePath);
            var source = Locator.FindPlc(tia.Project, plc).ExternalSourceGroup.ExternalSources.CreateFromFile(name ?? file.Name, file.FullName);
            return new { added = source.Name };
        });

    [McpServerTool(Name = "plc_generate_blocks_from_source", Destructive = true)]
    [Description("Generates (or regenerates) blocks from an external source already in the project. Existing blocks with the same name are replaced.")]
    public Task<string> GenerateBlocks(
        [Description("External source name.")] string name,
        [Description("Target block group path (created if missing).")] string? group = null,
        [Description(PlcParam)] string? plc = null,
        [Description("Keep generated blocks even when the source has errors.")] bool keepOnError = false)
        => tia.RunAsync(() =>
        {
            var software = Locator.FindPlc(tia.Project, plc);
            var source = Locator.FindExternalSource(software, name);
            var option = keepOnError ? GenerateBlockOption.KeepOnError : GenerateBlockOption.None;
            var generated = !string.IsNullOrWhiteSpace(group) && Locator.ResolveBlockGroup(software, group, create: true) is PlcBlockUserGroup userGroup
                ? source.GenerateBlocksFromSource(userGroup, option)
                : source.GenerateBlocksFromSource(option);
            return new { generated = generated.Select(o => ValueFormatter.Describe(o)).ToList() };
        });

    [McpServerTool(Name = "plc_generate_source", Destructive = false)]
    [Description("Generates one source file from several blocks and/or types (e.g. a full SCL library for review or versioning).")]
    public Task<string> GenerateSource(
        [Description("Block or type names/paths.")] string[] objects,
        [Description("Output file path (.scl, .db, .udt or .awl).")] string outputFile,
        [Description(PlcParam)] string? plc = null,
        [Description("Include dependencies.")] bool withDependencies = false)
        => tia.RunAsync(() =>
        {
            var software = Locator.FindPlc(tia.Project, plc);
            var items = objects.Select(o => (IGenerateSource?)Locator.TryFindBlock(software, o) ?? Locator.FindType(software, o)).ToList();
            var file = new FileInfo(System.Environment.ExpandEnvironmentVariables(outputFile));
            file.Directory?.Create();
            software.ExternalSourceGroup.GenerateSource(items, file, withDependencies ? GenerateOptions.WithDependencies : GenerateOptions.None);
            return new { generated = file.FullName, objects = items.Count };
        });

    [McpServerTool(Name = "plc_delete_external_source", Destructive = true)]
    [Description("Deletes an external source from the project (generated blocks are kept). Requires confirm=true.")]
    public Task<string> Delete([Description("External source name.")] string name, [Description(PlcParam)] string? plc = null, [Description("Explicit user approval for this destructive action.")] bool confirm = false)
        => tia.RunAsync(() =>
        {
            Guard.RequireConfirm(confirm, $"delete external source '{name}'");
            var source = Locator.FindExternalSource(Locator.FindPlc(tia.Project, plc), name);
            source.Delete();
            return new { deleted = name };
        });
}
