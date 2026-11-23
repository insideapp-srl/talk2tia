using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using Siemens.Engineering;
using Siemens.Engineering.SW;
using Talk2Tia.Core.Diff;
using Talk2Tia.Core.Openness;

namespace Talk2Tia.Core.Tools;

[McpServerToolType]
public sealed class TypeTools(TiaSession tia)
{
    private const string PlcParam = "PLC software, CPU or device name. Optional when the project has one PLC.";
    private const string TypeParam = "PLC data type (UDT) name, or 'Group/Name' path.";

    [McpServerTool(Name = "plc_list_types", ReadOnly = true, Idempotent = true)]
    [Description("Lists the PLC data types (UDT) with their group path and consistency.")]
    public Task<string> ListTypes([Description(PlcParam)] string? plc = null, [Description("Substring filter.")] string? filter = null)
        => tia.RunAsync(() =>
            Locator.AllTypes(Locator.FindPlc(tia.Project, plc).TypeGroup)
                .Where(e => filter is null || e.Type.Name.IndexOf(filter, System.StringComparison.OrdinalIgnoreCase) >= 0)
                .Select(e => new
                {
                    path = e.Path,
                    name = e.Type.Name,
                    consistent = e.Type.IsConsistent,
                    knowHowProtected = e.Type.IsKnowHowProtected,
                    modified = ValueFormatter.Format(e.Type.ModifiedDate),
                }).ToList());

    [McpServerTool(Name = "plc_read_type", ReadOnly = true)]
    [Description("Reads a PLC data type. format: 'scl' (.udt source, default), 'document' (SIMATIC SD) or 'xml' (SimaticML).")]
    public Task<string> ReadType(
        [Description(TypeParam)] string type,
        [Description(PlcParam)] string? plc = null,
        [Description("scl | document | xml")] string format = "scl",
        [Description("Optional directory to save the files instead of returning them inline.")] string? outputDirectory = null,
        [Description("For 'scl': include the types this type depends on.")] bool withDependencies = false)
        => tia.RunAsync(() =>
        {
            var software = Locator.FindPlc(tia.Project, plc);
            var t = Locator.FindType(software, type);
            using var ws = new Workspace();
            object? result = null;
            IList<TextFile> files;
            switch (format.Trim().ToLowerInvariant())
            {
                case "scl":
                    files = CodeTransfer.ExportSource(software, t, CodeTransfer.SafeFileName(t.Name) + ".udt", withDependencies, ws);
                    break;
                case "document":
                    (files, result) = CodeTransfer.ExportDocuments(t.ExportAsDocuments, t.Name, ws);
                    break;
                case "xml":
                    files = CodeTransfer.ExportXml(f => t.Export(f, ExportOptions.WithDefaults), t.Name, ws);
                    break;
                default:
                    throw new McpException($"Unknown format '{format}'. Use scl, document or xml.");
            }

            return new { type = t.Name, format, exportResult = result, files = CodeTransfer.Deliver(files, outputDirectory) };
        });

    [McpServerTool(Name = "plc_write_type", Destructive = true)]
    [Description("Creates or replaces a PLC data type from SIMATIC SD documents (format 'document') or SimaticML (format 'xml'). " +
                 "For TYPE ... END_TYPE source text use plc_write_scl. Replacing requires overwrite=true.")]
    public Task<string> WriteType(
        [Description("document | xml")] string format,
        [Description("Document (.s7dcl) or XML content.")] string? content = null,
        [Description("File path instead of inline content.")] string? filePath = null,
        [Description("Resource document (.s7res) content for format 'document'.")] string? resources = null,
        [Description("Type name (required for format 'document').")] string? name = null,
        [Description(PlcParam)] string? plc = null,
        [Description("Target type group path (created if missing).")] string? group = null,
        [Description("Replace an existing type.")] bool overwrite = false,
        [Description("Return a diff of the changes (Markdown, with LAD networks drawn before and after). Show it to the user.")] bool showDiff = true)
        => tia.RunAsync(() =>
        {
            var software = Locator.FindPlc(tia.Project, plc);
            var target = Locator.ResolveTypeGroup(software, group, create: true);
            var text = CodeTransfer.ContentOrFile(content, filePath, format);
            var typeNames = format.Trim().Equals("xml", StringComparison.OrdinalIgnoreCase)
                ? BlockSnapshot.NamesInXml(text).ToList()
                : new List<string> { name ?? string.Empty };
            var before = showDiff ? BlockSnapshot.CaptureAll(software, typeNames) : new Dictionary<string, BlockText>();
            using var ws = new Workspace();
            switch (format.Trim().ToLowerInvariant())
            {
                case "document":
                {
                    var typeName = Guard.RequireText(name, "name");
                    if (!overwrite && Locator.AllTypes(software.TypeGroup).Any(t => Locator.NameEquals(t.Type.Name, typeName)))
                    {
                        throw new McpException($"Type '{typeName}' already exists. Call again with overwrite=true.");
                    }

                    var (dir, baseName) = CodeTransfer.WriteDocuments(typeName, text, resources, ws);
                    var result = target.Types.ImportFromDocuments(dir, baseName, overwrite ? ImportDocumentOptions.Override : ImportDocumentOptions.None);
                    return new
                    {
                        result = CodeTransfer.Summarize(result.State.ToString(), result.Messages.Select(m => m.Message)),
                        diff = showDiff ? BlockSnapshot.DescribeChanges(software, typeNames, before) : null,
                    };
                }

                case "xml":
                {
                    var file = ws.FileOf("type.xml");
                    System.IO.File.WriteAllText(file.FullName, text, new System.Text.UTF8Encoding(true));
                    var imported = target.Types.Import(file, overwrite ? ImportOptions.Override : ImportOptions.None);
                    return new
                    {
                        imported = imported.Select(t => t.Name).ToList(),
                        diff = showDiff ? BlockSnapshot.DescribeChanges(software, imported.Select(t => t.Name), before) : null,
                    };
                }

                default:
                    throw new McpException($"Unknown format '{format}'. Use document or xml (or plc_write_scl for source text).");
            }
        });

    [McpServerTool(Name = "plc_delete_type", Destructive = true)]
    [Description("Deletes a PLC data type. Requires confirm=true.")]
    public Task<string> DeleteType([Description(TypeParam)] string type, [Description(PlcParam)] string? plc = null, [Description("Explicit user approval for this destructive action.")] bool confirm = false)
        => tia.RunAsync(() =>
        {
            Guard.RequireConfirm(confirm, $"delete data type '{type}'");
            var t = Locator.FindType(Locator.FindPlc(tia.Project, plc), type);
            var name = t.Name;
            t.Delete();
            return new { deleted = name };
        });
}
