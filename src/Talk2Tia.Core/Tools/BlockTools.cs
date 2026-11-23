using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using Siemens.Engineering;
using Siemens.Engineering.CrossReference;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using Talk2Tia.Core.Openness;

namespace Talk2Tia.Core.Tools;

[McpServerToolType]
public sealed class BlockTools(TiaSession tia)
{
    private const string PlcParam = "PLC software, CPU or device name. Optional when the project has one PLC.";
    private const string BlockParam = "Block name, or 'Group/SubGroup/Name' path.";

    [McpServerTool(Name = "plc_list", ReadOnly = true, Idempotent = true)]
    [Description("Lists the PLCs in the active project with device, CPU and type.")]
    public Task<string> ListPlcs() => tia.RunAsync(() =>
        Locator.AllSoftware(tia.Project).Where(s => s.Software is PlcSoftware).Select(s => new
        {
            plc = s.Name,
            device = s.Device.Name,
            cpu = s.DeviceItem.Name,
            typeIdentifier = s.DeviceItem.TypeIdentifier,
        }).ToList());

    [McpServerTool(Name = "plc_list_blocks", ReadOnly = true, Idempotent = true)]
    [Description("Lists program blocks (OB, FB, FC, DB) with path, number, language and consistency. Optionally limited to a group or a name filter.")]
    public Task<string> ListBlocks(
        [Description(PlcParam)] string? plc = null,
        [Description("Only blocks inside this group path, e.g. 'Motors/Conveyor'.")] string? group = null,
        [Description("Case-insensitive substring filter on the block name.")] string? filter = null)
        => tia.RunAsync(() =>
        {
            var software = Locator.FindPlc(tia.Project, plc);
            var root = Locator.ResolveBlockGroup(software, group);
            return Locator.AllBlocks(root, group?.Trim('/') ?? string.Empty)
                .Where(e => filter is null || e.Block.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                .Select(e => new
                {
                    path = e.Path,
                    name = e.Block.Name,
                    type = e.Block.GetType().Name,
                    number = e.Block.Number,
                    language = e.Block.ProgrammingLanguage.ToString(),
                    consistent = e.Block.IsConsistent,
                    knowHowProtected = e.Block.IsKnowHowProtected,
                    modified = ValueFormatter.Format(e.Block.ModifiedDate),
                })
                .ToList();
        });

    [McpServerTool(Name = "plc_list_groups", ReadOnly = true, Idempotent = true)]
    [Description("Lists the block group (folder) tree of a PLC.")]
    public Task<string> ListGroups([Description(PlcParam)] string? plc = null) => tia.RunAsync(() =>
    {
        var software = Locator.FindPlc(tia.Project, plc);
        var result = new List<string>();
        void Walk(PlcBlockGroup g, string prefix)
        {
            foreach (var sub in g.Groups)
            {
                var path = Locator.Join(prefix, sub.Name);
                result.Add(path);
                Walk(sub, path);
            }
        }

        Walk(software.BlockGroup, string.Empty);
        return result;
    });

    [McpServerTool(Name = "plc_get_block", ReadOnly = true, Idempotent = true)]
    [Description("Returns all attributes of a block (number, language, header, memory, dates, optimized access, ...).")]
    public Task<string> GetBlock([Description(BlockParam)] string block, [Description(PlcParam)] string? plc = null)
        => tia.RunAsync(() =>
        {
            var b = Locator.FindBlock(Locator.FindPlc(tia.Project, plc), block);
            return new
            {
                name = b.Name,
                type = b.GetType().Name,
                attributes = ValueFormatter.ReadAttributes(b),
            };
        });

    [McpServerTool(Name = "plc_read_block", ReadOnly = true)]
    [Description("Reads the code of a block. format: 'auto' (SCL source for SCL/STL/DB, SIMATIC SD document for LAD/FBD, XML otherwise), " +
                 "'scl' (textual source: .scl/.db/.awl), 'document' (SIMATIC SD .s7dcl/.s7res, the editable text form for LAD/FBD and SCL), " +
                 "'xml' (SimaticML, works for every language but needs a consistent block). Returns the content inline or saves it to outputDirectory.")]
    public Task<string> ReadBlock(
        [Description(BlockParam)] string block,
        [Description(PlcParam)] string? plc = null,
        [Description("auto | scl | document | xml")] string format = "auto",
        [Description("Optional directory to save the exported files instead of returning them inline.")] string? outputDirectory = null,
        [Description("For 'scl': include the blocks/types this block depends on.")] bool withDependencies = false)
        => tia.RunAsync(() =>
        {
            var software = Locator.FindPlc(tia.Project, plc);
            var b = Locator.FindBlock(software, block);
            var chosen = ResolveFormat(format, b);
            using var ws = new Workspace();
            object? result = null;
            IList<TextFile> files;
            switch (chosen)
            {
                case "scl":
                    files = CodeTransfer.ExportSource(software, b, CodeTransfer.SafeFileName(b.Name) + CodeTransfer.SourceExtension(b), withDependencies, ws);
                    break;
                case "document":
                    (files, result) = CodeTransfer.ExportDocuments(b.ExportAsDocuments, b.Name, ws);
                    break;
                default:
                    files = CodeTransfer.ExportXml(f => b.Export(f, ExportOptions.WithDefaults), b.Name, ws);
                    break;
            }

            return new
            {
                block = b.Name,
                type = b.GetType().Name,
                language = b.ProgrammingLanguage.ToString(),
                format = chosen,
                exportResult = result,
                files = CodeTransfer.Deliver(files, outputDirectory),
            };
        });

    [McpServerTool(Name = "plc_write_scl", Destructive = true)]
    [Description("Creates or replaces blocks/types from SCL source text (FUNCTION_BLOCK, FUNCTION, ORGANIZATION_BLOCK, DATA_BLOCK, TYPE; " +
                 "also .db/.udt/.awl sources). This is how SCL blocks are edited: the block in the project open in TIA Portal is " +
                 "replaced with the edited source and the change is visible in the editor at once. Replacing existing blocks " +
                 "requires overwrite=true (the user asking for the change is the approval).")]
    public Task<string> WriteScl(
        [Description("Source text. Alternatively use filePath.")] string? content = null,
        [Description("Path of a source file to import instead of inline content.")] string? filePath = null,
        [Description(PlcParam)] string? plc = null,
        [Description("Target block group path (created if missing). Types are placed in the matching PLC data type group.")] string? group = null,
        [Description("Source kind: auto | scl | db | udt | awl.")] string kind = "auto",
        [Description("Allow replacing blocks/types that already exist.")] bool overwrite = false,
        [Description("Keep the external source in the project after generation.")] bool keepSource = false,
        [Description("Name of the external source (default: generated).")] string? sourceName = null)
        => tia.RunAsync(() =>
        {
            var source = CodeTransfer.ContentOrFile(content, filePath, "SCL source");
            var software = Locator.FindPlc(tia.Project, plc);
            var declared = CodeTransfer.DeclaredObjects(source);
            if (declared.Count == 0)
            {
                throw new McpException("No FUNCTION_BLOCK, FUNCTION, ORGANIZATION_BLOCK, DATA_BLOCK or TYPE declaration found in the source.");
            }

            var existing = declared.Where(d => d.Kind == "TYPE"
                    ? Locator.AllTypes(software.TypeGroup).Any(t => Locator.NameEquals(t.Type.Name, d.Name))
                    : Locator.TryFindBlock(software, d.Name) is not null)
                .Select(d => d.Name).ToList();
            if (existing.Count > 0 && !overwrite)
            {
                throw new McpException($"These objects already exist and would be replaced: {string.Join(", ", existing)}. Call again with overwrite=true.");
            }

            var extension = CodeTransfer.NormalizeSourceExtension(kind, source);
            using var ws = new Workspace();
            var onlyTypes = declared.All(d => d.Kind == "TYPE");
            var blockGroup = !onlyTypes && !string.IsNullOrWhiteSpace(group) ? Locator.ResolveBlockGroup(software, group, create: true) as PlcBlockUserGroup : null;
            var typeGroup = onlyTypes && !string.IsNullOrWhiteSpace(group) ? Locator.ResolveTypeGroup(software, group, create: true) as Siemens.Engineering.SW.Types.PlcTypeUserGroup : null;
            var result = CodeTransfer.ImportSource(software, source, extension, blockGroup, typeGroup, keepSource, sourceName, ws);
            return new { declared = declared.Select(d => $"{d.Kind} {d.Name}").ToList(), replaced = existing, result };
        });

    [McpServerTool(Name = "plc_write_document", Destructive = true)]
    [Description("Creates or replaces a block from SIMATIC SD documents (the text form returned by plc_read_block format='document'). " +
                 "Use it to edit LAD/FBD (KOP/FUP) and SCL blocks directly in the open project. Replacing an existing block requires " +
                 "overwrite=true (the user asking for the change is the approval).")]
    public Task<string> WriteDocument(
        [Description("Block name (also the document file name without extension).")] string name,
        [Description("Content of the .s7dcl document.")] string document,
        [Description("Content of the .s7res resource document (texts, comments). Optional.")] string? resources = null,
        [Description(PlcParam)] string? plc = null,
        [Description("Target block group path (created if missing).")] string? group = null,
        [Description("Allow replacing an existing block.")] bool overwrite = false)
        => tia.RunAsync(() =>
        {
            var software = Locator.FindPlc(tia.Project, plc);
            var exists = Locator.TryFindBlock(software, name) is not null;
            if (exists && !overwrite)
            {
                throw new McpException($"Block '{name}' already exists. Call again with overwrite=true to replace it.");
            }

            var target = Locator.ResolveBlockGroup(software, group, create: true);
            using var ws = new Workspace();
            var (dir, baseName) = CodeTransfer.WriteDocuments(name, document, resources, ws);
            var result = target.Blocks.ImportFromDocuments(dir, baseName, overwrite ? ImportDocumentOptions.Override : ImportDocumentOptions.None);
            return new
            {
                replaced = exists,
                result = CodeTransfer.Summarize(result.State.ToString(), result.Messages.Select(m => m.Message)),
                blocks = result.ImportedPlcBlocks.Select(b => b.Name).ToList(),
                hint = "Run plc_compile to check the block.",
            };
        });

    [McpServerTool(Name = "plc_write_xml", Destructive = true)]
    [Description("Imports blocks from SimaticML XML (as returned by plc_read_block format='xml'). Works for every language. " +
                 "Replacing existing blocks requires overwrite=true.")]
    public Task<string> WriteXml(
        [Description("SimaticML XML content. Alternatively use filePath.")] string? content = null,
        [Description("Path of a SimaticML file.")] string? filePath = null,
        [Description(PlcParam)] string? plc = null,
        [Description("Target block group path (created if missing).")] string? group = null,
        [Description("Replace existing blocks with the same name.")] bool overwrite = false,
        [Description("Import even if the block interface changes structurally.")] bool ignoreStructuralChanges = false,
        [Description("Import even if referenced objects are missing.")] bool ignoreMissingReferences = false)
        => tia.RunAsync(() =>
        {
            var software = Locator.FindPlc(tia.Project, plc);
            var target = Locator.ResolveBlockGroup(software, group, create: true);
            using var ws = new Workspace();
            var file = ws.FileOf("import.xml");
            System.IO.File.WriteAllText(file.FullName, CodeTransfer.ContentOrFile(content, filePath, "SimaticML XML"), new System.Text.UTF8Encoding(true));
            var swOptions = SWImportOptions.None;
            if (ignoreStructuralChanges)
            {
                swOptions |= SWImportOptions.IgnoreStructuralChanges;
            }

            if (ignoreMissingReferences)
            {
                swOptions |= SWImportOptions.IgnoreMissingReferencedObjects;
            }

            var imported = target.Blocks.Import(file, overwrite ? ImportOptions.Override : ImportOptions.None, swOptions);
            return new { imported = imported.Select(b => b.Name).ToList(), hint = "Run plc_compile to check the blocks." };
        });

    [McpServerTool(Name = "plc_create_group", Destructive = false)]
    [Description("Creates a block group path (all missing levels).")]
    public Task<string> CreateGroup([Description("Group path, e.g. 'Motors/Conveyor'.")] string group, [Description(PlcParam)] string? plc = null)
        => tia.RunAsync(() =>
        {
            Locator.ResolveBlockGroup(Locator.FindPlc(tia.Project, plc), group, create: true);
            return new { created = group };
        });

    [McpServerTool(Name = "plc_delete_group", Destructive = true)]
    [Description("Deletes a block group and all blocks inside it. Requires confirm=true.")]
    public Task<string> DeleteGroup([Description("Group path.")] string group, [Description(PlcParam)] string? plc = null, [Description("Explicit user approval for this destructive action.")] bool confirm = false)
        => tia.RunAsync(() =>
        {
            Guard.RequireConfirm(confirm, $"delete block group '{group}'");
            if (Locator.ResolveBlockGroup(Locator.FindPlc(tia.Project, plc), group) is not PlcBlockUserGroup userGroup)
            {
                throw new McpException("The root program block folder cannot be deleted.");
            }

            userGroup.Delete();
            return new { deleted = group };
        });

    [McpServerTool(Name = "plc_delete_block", Destructive = true)]
    [Description("Deletes a block. Requires confirm=true.")]
    public Task<string> DeleteBlock([Description(BlockParam)] string block, [Description(PlcParam)] string? plc = null, [Description("Explicit user approval for this destructive action.")] bool confirm = false)
        => tia.RunAsync(() =>
        {
            Guard.RequireConfirm(confirm, $"delete block '{block}'");
            var b = Locator.FindBlock(Locator.FindPlc(tia.Project, plc), block);
            var name = b.Name;
            b.Delete();
            return new { deleted = name };
        });

    [McpServerTool(Name = "plc_set_block_attributes", Destructive = false)]
    [Description("Changes block properties. Supported keys include Name, Number, AutoNumber, HeaderAuthor, HeaderFamily, HeaderName, " +
                 "HeaderVersion, MemoryLayout, and any writable attribute shown by plc_get_block. Use 'Title'/'Comment' with an optional culture.")]
    public Task<string> SetBlockAttributes(
        [Description(BlockParam)] string block,
        [Description("Attributes to set, e.g. {\"Number\": 120, \"HeaderAuthor\": \"me\", \"Title\": \"Conveyor\"}.")] Dictionary<string, System.Text.Json.JsonElement> attributes,
        [Description("Culture for Title/Comment, e.g. it-IT (default: first project language).")] string? culture = null,
        [Description(PlcParam)] string? plc = null)
        => tia.RunAsync(() =>
        {
            var b = Locator.FindBlock(Locator.FindPlc(tia.Project, plc), block);
            var applied = ObjectAttributes.Apply(b, attributes, culture, new Dictionary<string, MultilingualText>(StringComparer.OrdinalIgnoreCase)
            {
                ["Title"] = b.Title,
                ["Comment"] = b.Comment,
            });
            return new { block = b.Name, applied };
        });

    [McpServerTool(Name = "plc_create_instance_db", Destructive = false)]
    [Description("Creates an instance DB for a function block.")]
    public Task<string> CreateInstanceDb(
        [Description("Name of the new instance DB.")] string name,
        [Description("Name of the FB the DB is an instance of.")] string functionBlock,
        [Description("DB number; omit for automatic numbering.")] int? number = null,
        [Description("Target block group path.")] string? group = null,
        [Description(PlcParam)] string? plc = null)
        => tia.RunAsync(() =>
        {
            var software = Locator.FindPlc(tia.Project, plc);
            var target = Locator.ResolveBlockGroup(software, group, create: true);
            var db = target.Blocks.CreateInstanceDB(name, number is null, number ?? 1, functionBlock);
            return new { created = db.Name, number = db.Number };
        });

    [McpServerTool(Name = "plc_create_fb", Destructive = false)]
    [Description("Creates an empty function block in the given language (LAD, FBD, SCL, STL, GRAPH). For blocks with code prefer plc_write_scl or plc_write_document.")]
    public Task<string> CreateFb(
        [Description("FB name.")] string name,
        [Description("Programming language.")] string language = "SCL",
        [Description("FB number; omit for automatic numbering.")] int? number = null,
        [Description("Target block group path.")] string? group = null,
        [Description(PlcParam)] string? plc = null)
        => tia.RunAsync(() =>
        {
            var software = Locator.FindPlc(tia.Project, plc);
            var target = Locator.ResolveBlockGroup(software, group, create: true);
            var fb = target.Blocks.CreateFB(name, number is null, number ?? 1, Guard.ParseEnum(language, ProgrammingLanguage.SCL));
            return new { created = fb.Name, number = fb.Number, language = fb.ProgrammingLanguage.ToString() };
        });

    [McpServerTool(Name = "plc_show_in_editor", ReadOnly = true)]
    [Description("Opens the block in the TIA Portal editor (only with a user interface).")]
    public Task<string> ShowInEditor([Description(BlockParam)] string block, [Description(PlcParam)] string? plc = null)
        => tia.RunAsync(() =>
        {
            var b = Locator.FindBlock(Locator.FindPlc(tia.Project, plc), block);
            b.ShowInEditor();
            return new { shown = b.Name };
        });

    [McpServerTool(Name = "plc_cross_references", ReadOnly = true)]
    [Description("Returns the cross references of a block: where it is used and what it uses.")]
    public Task<string> CrossReferences(
        [Description(BlockParam)] string block,
        [Description(PlcParam)] string? plc = null,
        [Description("AllObjects | ObjectsWithReferences | ObjectsWithoutReferences | UnusedObjects")] string filter = "AllObjects")
        => tia.RunAsync(() =>
        {
            var b = Locator.FindBlock(Locator.FindPlc(tia.Project, plc), block);
            var service = b.GetService<CrossReferenceService>()
                          ?? throw new McpException("Cross references are not available for this object.");
            var result = service.GetCrossReferences(Guard.ParseEnum(filter, CrossReferenceFilter.AllObjects));
            return result.Sources.Select(FormatSource).ToList();
        });

    internal static object FormatSource(SourceObject s) => new
    {
        name = s.Name,
        type = s.TypeName,
        address = s.Address,
        device = s.Device,
        path = s.Path,
        references = s.References.Select(r => new
        {
            name = r.Name,
            type = r.TypeName,
            address = r.Address,
            device = r.Device,
            path = r.Path,
            locations = r.Locations.Select(l => new
            {
                location = l.ReferenceLocation,
                access = l.Access.ToString(),
                referenceType = l.ReferenceType.ToString(),
                referencedAs = l.ReferencedAsName,
                address = l.Address,
            }).ToList(),
        }).ToList(),
        children = s.Children.Select(FormatSource).ToList(),
    };

    private static string ResolveFormat(string format, PlcBlock block)
    {
        var f = format.Trim().ToLowerInvariant();
        if (f is "scl" or "document" or "xml")
        {
            return f;
        }

        if (f != "auto")
        {
            throw new McpException($"Unknown format '{format}'. Use auto, scl, document or xml.");
        }

        return block switch
        {
            DataBlock => "scl",
            _ => block.ProgrammingLanguage switch
            {
                ProgrammingLanguage.SCL or ProgrammingLanguage.STL or ProgrammingLanguage.F_STL => "scl",
                ProgrammingLanguage.LAD or ProgrammingLanguage.FBD or ProgrammingLanguage.F_LAD or ProgrammingLanguage.F_FBD => "document",
                _ => "xml",
            },
        };
    }
}
