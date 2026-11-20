using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using Siemens.Engineering;
using Siemens.Engineering.SW.Tags;
using Talk2Tia.Core.Openness;

namespace Talk2Tia.Core.Tools;

public sealed class TagSpec
{
    [Description("Tag name.")] public string Name { get; set; } = string.Empty;
    [Description("Data type, e.g. Bool, Int, Real, \"MyUdt\".")] public string? DataType { get; set; }
    [Description("Address, e.g. %I0.0, %Q1.2, %MW10. Omit for unchanged.")] public string? Address { get; set; }
    [Description("Comment text.")] public string? Comment { get; set; }
    [Description("Accessible from HMI/OPC UA.")] public bool? ExternalAccessible { get; set; }
    [Description("Visible in HMI engineering.")] public bool? ExternalVisible { get; set; }
    [Description("Writable from HMI/OPC UA.")] public bool? ExternalWritable { get; set; }
}

public sealed class ConstantSpec
{
    [Description("Constant name.")] public string Name { get; set; } = string.Empty;
    [Description("Data type, e.g. Int.")] public string? DataType { get; set; }
    [Description("Value, e.g. 100.")] public string? Value { get; set; }
    [Description("Comment text.")] public string? Comment { get; set; }
}

[McpServerToolType]
public sealed class TagTools(TiaSession tia)
{
    private const string PlcParam = "PLC software, CPU or device name. Optional when the project has one PLC.";
    private const string TableParam = "Tag table name or 'Group/Name' path. Omit for the default tag table.";

    [McpServerTool(Name = "plc_list_tag_tables", ReadOnly = true, Idempotent = true)]
    [Description("Lists the PLC tag tables with tag and constant counts.")]
    public Task<string> ListTagTables([Description(PlcParam)] string? plc = null) => tia.RunAsync(() =>
        Locator.AllTagTables(Locator.FindPlc(tia.Project, plc).TagTableGroup).Select(e => new
        {
            path = e.Path,
            name = e.Table.Name,
            isDefault = e.Table.IsDefault,
            tags = e.Table.Tags.Count,
            userConstants = e.Table.UserConstants.Count,
        }).ToList());

    [McpServerTool(Name = "plc_get_tag_table", ReadOnly = true, Idempotent = true)]
    [Description("Returns the tags and user constants of a tag table.")]
    public Task<string> GetTagTable(
        [Description(TableParam)] string? table = null,
        [Description(PlcParam)] string? plc = null,
        [Description("Substring filter on tag names.")] string? filter = null)
        => tia.RunAsync(() =>
        {
            var t = Locator.FindTagTable(Locator.FindPlc(tia.Project, plc), table);
            return new
            {
                table = t.Name,
                tags = t.Tags
                    .Where(tag => filter is null || tag.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                    .Select(tag => new
                    {
                        name = tag.Name,
                        dataType = tag.DataTypeName,
                        address = tag.LogicalAddress,
                        comment = ValueFormatter.FormatMultilingual(tag.Comment),
                        externalAccessible = tag.ExternalAccessible,
                        externalVisible = tag.ExternalVisible,
                        externalWritable = tag.ExternalWritable,
                    }).ToList(),
                userConstants = t.UserConstants.Select(c => new
                {
                    name = c.Name,
                    attributes = ValueFormatter.ReadAttributes(c),
                }).ToList(),
            };
        });

    [McpServerTool(Name = "plc_find_tag", ReadOnly = true, Idempotent = true)]
    [Description("Searches tags by name or address in all tag tables of a PLC.")]
    public Task<string> FindTag(
        [Description("Substring of the tag name or address, e.g. 'Motor' or '%I0.'.")] string query,
        [Description(PlcParam)] string? plc = null)
        => tia.RunAsync(() =>
            Locator.AllTagTables(Locator.FindPlc(tia.Project, plc).TagTableGroup)
                .SelectMany(e => e.Table.Tags.Select(tag => (e.Path, tag)))
                .Where(x => x.tag.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
                            (x.tag.LogicalAddress ?? string.Empty).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                .Select(x => new { table = x.Path, name = x.tag.Name, dataType = x.tag.DataTypeName, address = x.tag.LogicalAddress })
                .ToList());

    [McpServerTool(Name = "plc_create_tag_table", Destructive = false)]
    [Description("Creates a tag table (and missing groups).")]
    public Task<string> CreateTagTable([Description("Table name or 'Group/Name' path.")] string table, [Description(PlcParam)] string? plc = null)
        => tia.RunAsync(() =>
        {
            var software = Locator.FindPlc(tia.Project, plc);
            var (groupPath, name) = Locator.SplitLast(table);
            var group = Locator.ResolveTagTableGroup(software, groupPath, create: true);
            var created = group.TagTables.Find(name) ?? group.TagTables.Create(name);
            return new { table = created.Name };
        });

    [McpServerTool(Name = "plc_delete_tag_table", Destructive = true)]
    [Description("Deletes a tag table with all its tags. Requires confirm=true.")]
    public Task<string> DeleteTagTable([Description("Table name or path.")] string table, [Description(PlcParam)] string? plc = null, [Description("Explicit user approval for this destructive action.")] bool confirm = false)
        => tia.RunAsync(() =>
        {
            Guard.RequireConfirm(confirm, $"delete tag table '{table}'");
            var t = Locator.FindTagTable(Locator.FindPlc(tia.Project, plc), table);
            var name = t.Name;
            t.Delete();
            return new { deleted = name };
        });

    [McpServerTool(Name = "plc_upsert_tags", Destructive = false)]
    [Description("Creates or updates tags in a tag table in one call. Existing tags (same name) are updated with the given fields.")]
    public Task<string> UpsertTags(
        [Description("Tags to create or update.")] TagSpec[] tags,
        [Description(TableParam)] string? table = null,
        [Description(PlcParam)] string? plc = null,
        [Description("Culture for comments, e.g. it-IT.")] string? culture = null)
        => tia.RunAsync(() =>
        {
            var t = Locator.FindTagTable(Locator.FindPlc(tia.Project, plc), table);
            var created = new List<string>();
            var updated = new List<string>();
            var errors = new List<object>();
            foreach (var spec in tags)
            {
                try
                {
                    var tag = t.Tags.Find(spec.Name);
                    if (tag is null)
                    {
                        tag = t.Tags.Create(spec.Name, spec.DataType ?? "Bool", spec.Address ?? string.Empty);
                        created.Add(spec.Name);
                    }
                    else
                    {
                        if (spec.DataType is not null)
                        {
                            tag.DataTypeName = spec.DataType;
                        }

                        if (spec.Address is not null)
                        {
                            tag.LogicalAddress = spec.Address;
                        }

                        updated.Add(spec.Name);
                    }

                    if (spec.Comment is not null)
                    {
                        ValueFormatter.SetMultilingual(tag.Comment, spec.Comment, culture);
                    }

                    if (spec.ExternalAccessible is { } a)
                    {
                        tag.ExternalAccessible = a;
                    }

                    if (spec.ExternalVisible is { } v)
                    {
                        tag.ExternalVisible = v;
                    }

                    if (spec.ExternalWritable is { } w)
                    {
                        tag.ExternalWritable = w;
                    }
                }
                catch (Exception ex) when (ex is EngineeringException or ArgumentException)
                {
                    errors.Add(new { tag = spec.Name, error = ex.Message });
                }
            }

            return new { table = t.Name, created, updated, errors };
        });

    [McpServerTool(Name = "plc_delete_tags", Destructive = true)]
    [Description("Deletes tags from a tag table. Requires confirm=true.")]
    public Task<string> DeleteTags(
        [Description("Tag names.")] string[] names,
        [Description(TableParam)] string? table = null,
        [Description(PlcParam)] string? plc = null,
        [Description("Explicit user approval for this destructive action.")] bool confirm = false)
        => tia.RunAsync(() =>
        {
            Guard.RequireConfirm(confirm, $"delete {names.Length} tag(s)");
            var t = Locator.FindTagTable(Locator.FindPlc(tia.Project, plc), table);
            var deleted = new List<string>();
            var missing = new List<string>();
            foreach (var name in names)
            {
                var tag = t.Tags.Find(name);
                if (tag is null)
                {
                    missing.Add(name);
                    continue;
                }

                tag.Delete();
                deleted.Add(name);
            }

            return new { deleted, missing };
        });

    [McpServerTool(Name = "plc_upsert_constants", Destructive = false)]
    [Description("Creates or updates user constants in a tag table.")]
    public Task<string> UpsertConstants(
        [Description("Constants to create or update.")] ConstantSpec[] constants,
        [Description(TableParam)] string? table = null,
        [Description(PlcParam)] string? plc = null,
        [Description("Culture for comments.")] string? culture = null)
        => tia.RunAsync(() =>
        {
            var t = Locator.FindTagTable(Locator.FindPlc(tia.Project, plc), table);
            var created = new List<string>();
            var updated = new List<string>();
            var errors = new List<object>();
            foreach (var spec in constants)
            {
                try
                {
                    var constant = t.UserConstants.Find(spec.Name);
                    if (constant is null)
                    {
                        constant = t.UserConstants.Create(spec.Name, spec.DataType ?? "Int", spec.Value ?? "0");
                        created.Add(spec.Name);
                    }
                    else
                    {
                        if (spec.DataType is not null)
                        {
                            constant.SetAttribute("DataTypeName", spec.DataType);
                        }

                        if (spec.Value is not null)
                        {
                            constant.SetAttribute("Value", spec.Value);
                        }

                        updated.Add(spec.Name);
                    }

                    if (spec.Comment is not null && constant.GetAttribute("Comment") is MultilingualText comment)
                    {
                        ValueFormatter.SetMultilingual(comment, spec.Comment, culture);
                    }
                }
                catch (Exception ex) when (ex is EngineeringException or ArgumentException)
                {
                    errors.Add(new { constant = spec.Name, error = ex.Message });
                }
            }

            return new { table = t.Name, created, updated, errors };
        });

    [McpServerTool(Name = "plc_export_tag_table", ReadOnly = true)]
    [Description("Exports a tag table as SimaticML XML (inline or to outputDirectory).")]
    public Task<string> ExportTagTable(
        [Description(TableParam)] string? table = null,
        [Description(PlcParam)] string? plc = null,
        [Description("Directory to save the file instead of returning it inline.")] string? outputDirectory = null)
        => tia.RunAsync(() =>
        {
            var t = Locator.FindTagTable(Locator.FindPlc(tia.Project, plc), table);
            using var ws = new Workspace();
            var files = CodeTransfer.ExportXml(f => t.Export(f, ExportOptions.WithDefaults), t.Name, ws);
            return new { table = t.Name, files = CodeTransfer.Deliver(files, outputDirectory) };
        });

    [McpServerTool(Name = "plc_import_tag_table", Destructive = true)]
    [Description("Imports tag tables from SimaticML XML. Replacing existing tables requires overwrite=true.")]
    public Task<string> ImportTagTable(
        [Description("XML content.")] string? content = null,
        [Description("XML file path.")] string? filePath = null,
        [Description("Target group path.")] string? group = null,
        [Description(PlcParam)] string? plc = null,
        [Description("Replace existing tables.")] bool overwrite = false)
        => tia.RunAsync(() =>
        {
            var target = Locator.ResolveTagTableGroup(Locator.FindPlc(tia.Project, plc), group, create: true);
            using var ws = new Workspace();
            var file = ws.FileOf("tags.xml");
            System.IO.File.WriteAllText(file.FullName, CodeTransfer.ContentOrFile(content, filePath, "tag table XML"), new System.Text.UTF8Encoding(true));
            var imported = target.TagTables.Import(file, overwrite ? ImportOptions.Override : ImportOptions.None);
            return new { imported = imported.Select(t => t.Name).ToList() };
        });
}
