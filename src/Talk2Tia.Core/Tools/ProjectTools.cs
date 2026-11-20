using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using Siemens.Engineering;
using Talk2Tia.Core.Openness;

namespace Talk2Tia.Core.Tools;

[McpServerToolType]
public sealed class ProjectTools(TiaSession tia)
{
    [McpServerTool(Name = "project_info", ReadOnly = true, Idempotent = true)]
    [Description("Returns details of the active project: path, author, languages, devices and software (PLC/HMI).")]
    public Task<string> Info() => tia.RunAsync(() =>
    {
        var project = tia.Project;
        return new
        {
            name = project.Name,
            path = project.Path?.FullName,
            author = project.Author,
            version = project.Version,
            modified = project.IsModified,
            lastModified = ValueFormatter.Format(project.LastModified),
            lastModifiedBy = project.LastModifiedBy,
            comment = ValueFormatter.FormatMultilingual(project.Comment),
            editingLanguage = project.LanguageSettings.EditingLanguage?.Culture.Name,
            referenceLanguage = project.LanguageSettings.ReferenceLanguage?.Culture.Name,
            activeLanguages = project.LanguageSettings.ActiveLanguages.Select(l => l.Culture.Name).ToList(),
            devices = Locator.AllDevices(project).Select(d => new { name = d.Name, type = d.TypeIdentifier }).ToList(),
            software = Locator.AllSoftware(project).Select(s => new { name = s.Name, kind = s.Kind, device = s.Device.Name, deviceItem = s.DeviceItem.Name }).ToList(),
            subnets = project.Subnets.Select(s => new { name = s.Name, type = s.NetType.ToString() }).ToList(),
        };
    });

    [McpServerTool(Name = "project_open", Destructive = false)]
    [Description("Opens a TIA Portal project (.ap21). Set upgrade=true to open a project from an older TIA Portal version.")]
    public Task<string> Open(
        [Description("Full path of the .apXX project file.")] string path,
        [Description("Upgrade a project created with an older TIA Portal version.")] bool upgrade = false)
        => tia.RunAsync(() =>
        {
            var file = Guard.ExistingFile(path);
            var project = upgrade ? tia.Portal.Projects.OpenWithUpgrade(file) : tia.Portal.Projects.Open(file);
            tia.SetActiveProject(project);
            return new { opened = project.Name, path = project.Path?.FullName };
        });

    [McpServerTool(Name = "project_create", Destructive = false)]
    [Description("Creates a new empty project in the given directory and makes it active.")]
    public Task<string> Create(
        [Description("Parent directory; a sub-folder named after the project is created.")] string directory,
        [Description("Project name.")] string name)
        => tia.RunAsync(() =>
        {
            var project = tia.Portal.Projects.Create(Guard.Directory(directory, create: true), name);
            tia.SetActiveProject(project);
            return new { created = project.Name, path = project.Path?.FullName };
        });

    [McpServerTool(Name = "project_select", Destructive = false)]
    [Description("Selects which open project is the active one for the following tools.")]
    public Task<string> Select([Description("Project name.")] string name) => tia.RunAsync(() =>
    {
        var project = tia.Portal.Projects.FirstOrDefault(p => Locator.NameEquals(p.Name, name))
                      ?? throw new McpException($"Project '{name}' is not open. Open: {string.Join(", ", tia.Portal.Projects.Select(p => p.Name))}");
        tia.SetActiveProject(project);
        return new { active = project.Name };
    });

    [McpServerTool(Name = "project_save", Destructive = false, Idempotent = true)]
    [Description("Saves the active project.")]
    public Task<string> Save() => tia.RunAsync(() =>
    {
        var project = tia.Project;
        project.Save();
        return new { saved = project.Name };
    });

    [McpServerTool(Name = "project_save_as", Destructive = false)]
    [Description("Saves the active project under a new directory (TIA 'Save as').")]
    public Task<string> SaveAs([Description("Target directory.")] string directory) => tia.RunAsync(() =>
    {
        var project = tia.Project;
        project.SaveAs(Guard.Directory(directory, create: true));
        return new { saved = project.Name, path = project.Path?.FullName };
    });

    [McpServerTool(Name = "project_close", Destructive = true)]
    [Description("Closes the active project. Unsaved changes are lost: confirm=true is required if the project is modified.")]
    public Task<string> Close(
        [Description("Save before closing.")] bool save = false,
        [Description("Required to close a modified project without saving.")] bool confirm = false)
        => tia.RunAsync(() =>
        {
            var project = tia.Project;
            var name = project.Name;
            if (save)
            {
                project.Save();
            }
            else if (project.IsModified)
            {
                Guard.RequireConfirm(confirm, "close the project without saving");
            }

            project.Close();
            return new { closed = name, saved = save };
        });

    [McpServerTool(Name = "project_archive", Destructive = false)]
    [Description("Archives the active project (.zap21). Mode: Compressed (default), None, DiscardRestorableData, DiscardRestorableDataAndCompressed.")]
    public Task<string> Archive(
        [Description("Target directory.")] string directory,
        [Description("Archive file name without extension.")] string name,
        [Description("Archive mode.")] string mode = "Compressed")
        => tia.RunAsync(() =>
        {
            var archiveMode = Guard.ParseEnum(mode, ProjectArchivationMode.Compressed);
            var dir = Guard.Directory(directory, create: true);
            tia.Project.Archive(dir, name, archiveMode);
            return new { archived = Path.Combine(dir.FullName, name), mode = archiveMode.ToString() };
        });

    [McpServerTool(Name = "project_retrieve", Destructive = false)]
    [Description("Retrieves (extracts and opens) an archived project (.zapXX).")]
    public Task<string> Retrieve(
        [Description("Archive file path.")] string archivePath,
        [Description("Directory where the project is extracted.")] string targetDirectory,
        [Description("Upgrade an archive from an older TIA Portal version.")] bool upgrade = false)
        => tia.RunAsync(() =>
        {
            var file = Guard.ExistingFile(archivePath);
            var dir = Guard.Directory(targetDirectory, create: true);
            var project = upgrade ? tia.Portal.Projects.RetrieveWithUpgrade(file, dir) : tia.Portal.Projects.Retrieve(file, dir);
            tia.SetActiveProject(project);
            return new { retrieved = project.Name, path = project.Path?.FullName };
        });

    [McpServerTool(Name = "project_export_texts", Destructive = false)]
    [Description("Exports the project texts (for translation) to an .xlsx file.")]
    public Task<string> ExportTexts(
        [Description("Target .xlsx file.")] string path,
        [Description("Source culture, e.g. en-US.")] string sourceLanguage,
        [Description("Target culture, e.g. it-IT.")] string targetLanguage)
        => tia.RunAsync(() =>
        {
            var file = new FileInfo(Environment.ExpandEnvironmentVariables(path));
            tia.Project.ExportProjectTexts(file, new System.Globalization.CultureInfo(sourceLanguage), new System.Globalization.CultureInfo(targetLanguage));
            return new { exported = file.FullName };
        });

    [McpServerTool(Name = "project_import_texts", Destructive = false)]
    [Description("Imports translated project texts from an .xlsx file previously exported with project_export_texts.")]
    public Task<string> ImportTexts(
        [Description("Source .xlsx file.")] string path,
        [Description("Also update the source language texts.")] bool updateSourceLanguage = false)
        => tia.RunAsync(() =>
        {
            var result = tia.Project.ImportProjectTexts(Guard.ExistingFile(path), updateSourceLanguage);
            return new { imported = path, result = ValueFormatter.ReadAttributes(result) };
        });
}
