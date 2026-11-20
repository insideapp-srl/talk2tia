using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using Siemens.Engineering;
using Siemens.Engineering.Hmi;
using Siemens.Engineering.Hmi.Screen;
using Siemens.Engineering.Hmi.Tag;
using Siemens.Engineering.HmiUnified;
using Talk2Tia.Core.Openness;

namespace Talk2Tia.Core.Tools;

[McpServerToolType]
public sealed class HmiTools(TiaSession tia)
{
    private const string HmiParam = "HMI software, panel or device name. Optional when the project has one HMI.";

    [McpServerTool(Name = "hmi_list", ReadOnly = true, Idempotent = true)]
    [Description("Lists the HMI devices (WinCC Comfort/Advanced and WinCC Unified) in the project.")]
    public Task<string> List() => tia.RunAsync(() =>
        Locator.AllSoftware(tia.Project)
            .Where(s => s.Software is HmiTarget or HmiSoftware)
            .Select(s => new { hmi = s.Name, kind = s.Kind, device = s.Device.Name, typeIdentifier = s.DeviceItem.TypeIdentifier })
            .ToList());

    [McpServerTool(Name = "hmi_list_screens", ReadOnly = true, Idempotent = true)]
    [Description("Lists the screens of an HMI (with folder path).")]
    public Task<string> ListScreens([Description(HmiParam)] string? hmi = null) => tia.RunAsync(() =>
    {
        var software = Locator.FindHmiRef(tia.Project, hmi).Software;
        return software switch
        {
            HmiTarget classic => (object)ClassicScreens(classic.ScreenFolder, string.Empty).ToList(),
            HmiSoftware unified => unified.Screens.Select(s => new { path = s.Name, name = s.Name }).ToList(),
            _ => throw new McpException("Unsupported HMI type."),
        };
    });

    [McpServerTool(Name = "hmi_list_tags", ReadOnly = true, Idempotent = true)]
    [Description("Lists the HMI tag tables and their tags (name, data type, connection, PLC tag).")]
    public Task<string> ListTags(
        [Description(HmiParam)] string? hmi = null,
        [Description("Only this tag table.")] string? table = null)
        => tia.RunAsync(() =>
        {
            var software = Locator.FindHmiRef(tia.Project, hmi).Software;
            var attrs = new[] { "Name", "DataType", "Connection", "PlcTag", "Address", "AcquisitionCycle", "HmiDataType" };
            return software switch
            {
                HmiTarget classic => (object)ClassicTagTables(classic.TagFolder, string.Empty)
                    .Where(t => table is null || Locator.NameEquals(t.Table.Name, table))
                    .Select(t => new
                    {
                        table = t.Path,
                        tags = t.Table.Tags.Select(tag => ValueFormatter.ReadAttributes(tag, attrs)).ToList(),
                    }).ToList(),
                HmiSoftware unified => unified.TagTables
                    .Where(t => table is null || Locator.NameEquals(t.Name, table))
                    .Select(t => new
                    {
                        table = t.Name,
                        tags = t.Tags.Select(tag => ValueFormatter.ReadAttributes(tag, attrs)).ToList(),
                    }).ToList(),
                _ => throw new McpException("Unsupported HMI type."),
            };
        });

    [McpServerTool(Name = "hmi_export", ReadOnly = true)]
    [Description("Exports a WinCC Comfort/Advanced screen or tag table as SimaticML XML (inline or to outputDirectory). " +
                 "For WinCC Unified objects use obj_browse / obj_get.")]
    public Task<string> Export(
        [Description("screen | tagtable")] string kind,
        [Description("Screen or tag table name.")] string name,
        [Description(HmiParam)] string? hmi = null,
        [Description("Directory to save the file instead of returning it inline.")] string? outputDirectory = null)
        => tia.RunAsync(() =>
        {
            if (Locator.FindHmiRef(tia.Project, hmi).Software is not HmiTarget classic)
            {
                throw new McpException("hmi_export supports WinCC Comfort/Advanced. For WinCC Unified use the obj_* tools.");
            }

            using var ws = new Workspace();
            IList<TextFile> files = kind.Trim().ToLowerInvariant() switch
            {
                "screen" => CodeTransfer.ExportXml(f => FindScreen(classic, name).Export(f, ExportOptions.WithDefaults), name, ws),
                "tagtable" => CodeTransfer.ExportXml(f => FindTagTable(classic, name).Export(f, ExportOptions.WithDefaults), name, ws),
                _ => throw new McpException("kind must be screen or tagtable."),
            };
            return new { kind, name, files = CodeTransfer.Deliver(files, outputDirectory) };
        });

    [McpServerTool(Name = "hmi_import", Destructive = true)]
    [Description("Imports a WinCC Comfort/Advanced screen or tag table from SimaticML XML. Replacing requires overwrite=true.")]
    public Task<string> Import(
        [Description("screen | tagtable")] string kind,
        [Description("XML content.")] string? content = null,
        [Description("XML file path.")] string? filePath = null,
        [Description(HmiParam)] string? hmi = null,
        [Description("Replace existing objects.")] bool overwrite = false)
        => tia.RunAsync(() =>
        {
            if (Locator.FindHmiRef(tia.Project, hmi).Software is not HmiTarget classic)
            {
                throw new McpException("hmi_import supports WinCC Comfort/Advanced. For WinCC Unified use the obj_* tools.");
            }

            using var ws = new Workspace();
            var file = ws.FileOf("hmi.xml");
            System.IO.File.WriteAllText(file.FullName, CodeTransfer.ContentOrFile(content, filePath, "XML"), new System.Text.UTF8Encoding(true));
            var options = overwrite ? ImportOptions.Override : ImportOptions.None;
            var imported = kind.Trim().ToLowerInvariant() switch
            {
                "screen" => classic.ScreenFolder.Screens.Import(file, options).Select(s => s.Name).ToList(),
                "tagtable" => classic.TagFolder.TagTables.Import(file, options).Select(t => t.Name).ToList(),
                _ => throw new McpException("kind must be screen or tagtable."),
            };
            return new { imported };
        });

    private static IEnumerable<object> ClassicScreens(ScreenFolder folder, string prefix)
    {
        foreach (var screen in folder.Screens)
        {
            yield return new { path = Locator.Join(prefix, screen.Name), name = screen.Name };
        }

        foreach (var sub in folder.Folders)
        {
            foreach (var s in ClassicScreens(sub, Locator.Join(prefix, sub.Name)))
            {
                yield return s;
            }
        }
    }

    private static IEnumerable<(string Path, TagTable Table)> ClassicTagTables(TagFolder folder, string prefix)
    {
        foreach (var table in folder.TagTables)
        {
            yield return (Locator.Join(prefix, table.Name), table);
        }

        foreach (var sub in folder.Folders)
        {
            foreach (var t in ClassicTagTables(sub, Locator.Join(prefix, sub.Name)))
            {
                yield return t;
            }
        }
    }

    private static Screen FindScreen(HmiTarget hmi, string name)
    {
        IEnumerable<Screen> All(ScreenFolder f) => f.Screens.Concat(f.Folders.SelectMany(All));
        return All(hmi.ScreenFolder).FirstOrDefault(s => Locator.NameEquals(s.Name, name))
               ?? throw new McpException($"Screen '{name}' not found. Use hmi_list_screens.");
    }

    private static TagTable FindTagTable(HmiTarget hmi, string name) =>
        ClassicTagTables(hmi.TagFolder, string.Empty).Select(t => t.Table).FirstOrDefault(t => Locator.NameEquals(t.Name, name))
        ?? throw new McpException($"HMI tag table '{name}' not found. Use hmi_list_tags.");
}
