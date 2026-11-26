using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using Siemens.Engineering;
using Siemens.Engineering.Library;
using Siemens.Engineering.Library.MasterCopies;
using Siemens.Engineering.Library.Types;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.Types;
using Talk2Tia.Core.Openness;

namespace Talk2Tia.Core.Tools;

[McpServerToolType]
public sealed class LibraryTools(TiaSession tia)
{
    private const string LibParam = "'project' for the project library (default) or the name of an open global library.";
    private const string TypeParam = "Library type name, or 'Folder/Sub/Name' path inside the library.";
    private const string PlcParam = "PLC software, CPU or device name. Optional when the project has one PLC.";

    // ---------- Global libraries ----------

    [McpServerTool(Name = "lib_list_global", ReadOnly = true, Idempotent = true)]
    [Description("Lists the open global libraries and the known ones (system, corporate, user) with path and state.")]
    public Task<string> ListGlobal() => tia.RunAsync(() => new
    {
        open = tia.Portal.GlobalLibraries.Select(l => new
        {
            name = l.Name,
            kind = l.GetType().Name,
            path = l.Path?.FullName,
            readOnly = l.IsReadOnly,
            modified = l.IsModified,
            version = l.Version,
        }).ToList(),
        known = tia.Portal.GlobalLibraries.GetGlobalLibraryInfos().Select(i => new
        {
            name = i.Name,
            kind = i.LibraryType.ToString(),
            path = i.Path?.FullName,
            isOpen = i.IsOpen,
            readOnly = i.IsReadOnly,
        }).ToList(),
    });

    [McpServerTool(Name = "lib_open_global", Destructive = false)]
    [Description("Opens a global library (.al21). Opened read-only unless readOnly=false. Set upgrade=true for older versions.")]
    public Task<string> OpenGlobal(
        [Description("Library file path (.alXX), or the name of a known library from lib_list_global.")] string path,
        [Description("Open read-only.")] bool readOnly = true,
        [Description("Upgrade a library from an older TIA Portal version.")] bool upgrade = false)
        => tia.RunAsync(() =>
        {
            var libs = tia.Portal.GlobalLibraries;
            var known = libs.GetGlobalLibraryInfos().FirstOrDefault(i => Locator.NameEquals(i.Name, path));
            GlobalLibrary lib;
            if (known is not null && !File.Exists(path))
            {
                lib = libs.Open(known);
            }
            else
            {
                var file = Guard.ExistingFile(path);
                lib = upgrade ? libs.OpenWithUpgrade(file) : libs.Open(file, readOnly ? OpenMode.ReadOnly : OpenMode.ReadWrite);
            }

            return new { opened = lib.Name, path = lib.Path?.FullName, readOnly = lib.IsReadOnly };
        });

    [McpServerTool(Name = "lib_create_global", Destructive = false)]
    [Description("Creates a new user global library.")]
    public Task<string> CreateGlobal([Description("Parent directory.")] string directory, [Description("Library name.")] string name)
        => tia.RunAsync(() =>
        {
            var lib = tia.Portal.GlobalLibraries.Create<UserGlobalLibrary>(Guard.Directory(directory, create: true), name);
            return new { created = lib.Name, path = lib.Path?.FullName };
        });

    [McpServerTool(Name = "lib_save_global", Destructive = false, Idempotent = true)]
    [Description("Saves an open global library (must be opened read-write).")]
    public Task<string> SaveGlobal([Description("Global library name.")] string name) => tia.RunAsync(() =>
    {
        UserLibrary(name).Save();
        return new { saved = name };
    });

    [McpServerTool(Name = "lib_close_global", Destructive = true)]
    [Description("Closes a global library. Unsaved changes are lost unless save=true (confirm=true needed to discard them).")]
    public Task<string> CloseGlobal(
        [Description("Global library name.")] string name,
        [Description("Save the library before closing.")] bool save = false,
        [Description("Required to close a modified library without saving.")] bool confirm = false)
        => tia.RunAsync(() =>
        {
            var lib = UserLibrary(name);
            if (save)
            {
                lib.Save();
            }
            else if (lib.IsModified)
            {
                Guard.RequireConfirm(confirm, $"close library '{name}' without saving");
            }

            lib.Close();
            return new { closed = name, saved = save };
        });

    [McpServerTool(Name = "lib_archive_global", Destructive = false)]
    [Description("Archives a global library (.zal21).")]
    public Task<string> ArchiveGlobal(
        [Description("Global library name.")] string name,
        [Description("Target directory.")] string directory,
        [Description("Archive file name without extension.")] string archiveName,
        [Description("None | Compressed | DiscardRestorableData | DiscardRestorableDataAndCompressed")] string mode = "Compressed")
        => tia.RunAsync(() =>
        {
            UserLibrary(name).Archive(Guard.Directory(directory, create: true), archiveName, Guard.ParseEnum(mode, LibraryArchivationMode.Compressed));
            return new { archived = Path.Combine(directory, archiveName) };
        });

    // ---------- Browse ----------

    [McpServerTool(Name = "lib_browse", ReadOnly = true, Idempotent = true)]
    [Description("Shows the content of a library: type folders, types with their versions (state, default, author) and master copies.")]
    public Task<string> Browse(
        [Description(LibParam)] string library = "project",
        [Description("Include master copies.")] bool masterCopies = true,
        [Description("Include all versions (default: only default and in-work versions).")] bool allVersions = false)
        => tia.RunAsync(() =>
        {
            var lib = Library(library);
            return new
            {
                library = library,
                types = FormatTypeFolder(lib.TypeFolder, allVersions),
                masterCopies = masterCopies ? FormatMasterCopyFolder(lib.MasterCopyFolder) : null,
            };
        });

    [McpServerTool(Name = "lib_type_info", ReadOnly = true)]
    [Description("Details of a library type: all versions, dependencies, dependents and the instances in a PLC.")]
    public Task<string> TypeInfo(
        [Description(TypeParam)] string type,
        [Description(LibParam)] string library = "project",
        [Description("PLC to search for instances (optional).")] string? plc = null)
        => tia.RunAsync(() =>
        {
            var t = FindType(Library(library), type);
            PlcSoftware? software = plc is null ? null : Locator.FindPlc(tia.Project, plc);
            return new
            {
                name = t.Name,
                kind = t.GetType().Name,
                guid = t.Guid,
                author = t.Author,
                status = t.Status.ToString(),
                comment = ValueFormatter.FormatMultilingual(t.Comment),
                versions = t.Versions.Select(v => new
                {
                    version = v.VersionNumber.ToString(),
                    state = v.State.ToString(),
                    isDefault = v.IsDefault,
                    author = v.Author,
                    modified = ValueFormatter.Format(v.ModifiedDate),
                    comment = ValueFormatter.FormatMultilingual(v.Comment),
                    dependencies = v.Dependencies.Select(d => $"{d.TypeObject.Name} V{d.VersionNumber}").ToList(),
                    dependents = v.Dependents.Select(d => $"{d.TypeObject.Name} V{d.VersionNumber}").ToList(),
                    instances = software is null ? null : v.FindInstances(software).Select(i => ValueFormatter.Describe(i.LibraryTypeInstance)).ToList(),
                }).ToList(),
            };
        });

    // ---------- Types ----------

    [McpServerTool(Name = "lib_create_type_from_block", Destructive = false)]
    [Description("Creates a new library type from an existing block or PLC data type (like 'Add type' in TIA Portal). " +
                 "The new type is created in the project library (optionally in a type folder).")]
    public Task<string> CreateTypeFromBlock(
        [Description("Block or PLC data type name/path.")] string source,
        [Description(PlcParam)] string? plc = null,
        [Description("Type folder path in the project library (created if missing).")] string? folder = null,
        [Description(LibParam)] string library = "project")
        => tia.RunAsync(() =>
        {
            var software = Locator.FindPlc(tia.Project, plc);
            var lib = Library(library);
            var targetFolder = ResolveTypeFolder(lib.TypeFolder, folder, create: true);
            using var ws = new Workspace();
            var block = Locator.TryFindBlock(software, source);
            DocumentExportResult exported = block is not null
                ? block.ExportAsDocuments(ws.Directory, CodeTransfer.SafeFileName(block.Name))
                : Locator.FindType(software, source).ExportAsDocuments(ws.Directory, CodeTransfer.SafeFileName(source));
            if (exported.State == DocumentResultState.Failure)
            {
                throw new McpException("Export of the source failed: " + string.Join(" | ", exported.Messages.Select(m => m.Message)));
            }

            var baseName = CodeTransfer.SafeFileName(block?.Name ?? Locator.SplitLast(source).Name);
            var result = targetFolder.Types.CreateFromDocuments(ws.Directory, baseName, software, LibraryImportOptions.None);
            return new
            {
                state = result.TransferResultState.ToString(),
                created = result.CreatedType?.Name,
                messages = result.Messages.Select(m => m.Message).ToList(),
            };
        });

    [McpServerTool(Name = "lib_instantiate_type", Destructive = false)]
    [Description("Creates an instance of a library type (block or PLC data type) in a PLC. Uses the default version unless 'version' is given.")]
    public Task<string> InstantiateType(
        [Description(TypeParam)] string type,
        [Description(LibParam)] string library = "project",
        [Description("Version, e.g. 1.0.2 (default: default version).")] string? version = null,
        [Description(PlcParam)] string? plc = null,
        [Description("Target block/type group path.")] string? group = null)
        => tia.RunAsync(() =>
        {
            var software = Locator.FindPlc(tia.Project, plc);
            var v = PickVersion(FindType(Library(library), type), version);
            switch (v)
            {
                case CodeBlockLibraryTypeVersion codeBlock:
                {
                    var block = Locator.ResolveBlockGroup(software, group, create: true).Blocks.CreateFrom(codeBlock);
                    return new { instantiated = block.Name, version = v.VersionNumber.ToString() };
                }

                case PlcTypeLibraryTypeVersion plcType:
                {
                    var created = Locator.ResolveTypeGroup(software, group, create: true).Types.CreateFrom(plcType);
                    return new { instantiated = created.Name, version = v.VersionNumber.ToString() };
                }

                default:
                    throw new McpException($"Type '{type}' ({v.GetType().Name}) cannot be instantiated in a PLC with this tool; use obj_create.");
            }
        });

    [McpServerTool(Name = "lib_type_edit", Destructive = false)]
    [Description("Starts editing a project library type: creates an in-work version and its test instance (like 'Edit type'). " +
                 "Then change the instance with plc_write_* tools, compile, and finish with lib_type_release (or lib_type_discard).")]
    public Task<string> EditType(
        [Description(TypeParam)] string type,
        [Description("Version to edit (default: default version).")] string? version = null,
        [Description("Instance block/type in the PLC to use as test instance (optional).")] string? instance = null,
        [Description(PlcParam)] string? plc = null)
        => tia.RunAsync(() =>
        {
            var t = FindType(tia.Project.ProjectLibrary, type);
            var v = PickVersion(t, version);
            LibraryTypeVersion inWork;
            if (instance is not null)
            {
                var software = Locator.FindPlc(tia.Project, plc);
                IEngineeringServiceProvider obj = (IEngineeringServiceProvider?)Locator.TryFindBlock(software, instance) ?? Locator.FindType(software, instance);
                var info = obj.GetService<LibraryTypeInstanceInfo>()
                           ?? throw new McpException($"'{instance}' is not an instance of a library type.");
                inWork = v.Edit(info);
            }
            else
            {
                inWork = v.Edit();
            }

            return new
            {
                type = t.Name,
                inWorkVersion = inWork.VersionNumber.ToString(),
                state = inWork.State.ToString(),
                hint = "Modify the test instance, run plc_compile, then lib_type_release.",
            };
        });

    [McpServerTool(Name = "lib_type_release", Destructive = false)]
    [Description("Releases the in-work version of a project library type (like 'Release version').")]
    public Task<string> ReleaseType(
        [Description(TypeParam)] string type,
        [Description("New version number, e.g. 1.0.3 (default: the in-work number).")] string? version = null,
        [Description("Author.")] string? author = null,
        [Description("Version comment.")] string? comment = null,
        [Description("Also release in-work dependencies automatically.")] bool releaseDependencies = true)
        => tia.RunAsync(() =>
        {
            var t = FindType(tia.Project.ProjectLibrary, type);
            var inWork = t.Versions.FirstOrDefault(v => v.State == LibraryTypeVersionState.InWork)
                         ?? throw new McpException($"Type '{t.Name}' has no in-work version. Use lib_type_edit first.");
            inWork.Release(
                releaseDependencies ? CreateOrReleaseDependenciesMode.AutomaticallyCreateOrReleaseDependenciesIfRequired : CreateOrReleaseDependenciesMode.DoNotAutomaticallyCreateOrReleaseDependencies,
                version is null ? inWork.VersionNumber : Version.Parse(version),
                author ?? inWork.Author ?? Environment.UserName,
                comment ?? string.Empty);
            var released = t.Versions.OrderByDescending(v => v.VersionNumber).First();
            return new { type = t.Name, released = released.VersionNumber.ToString(), state = released.State.ToString() };
        });

    [McpServerTool(Name = "lib_type_discard", Destructive = true)]
    [Description("Discards the in-work version of a project library type. Requires confirm=true.")]
    public Task<string> DiscardType([Description(TypeParam)] string type, [Description("Explicit user approval for this destructive action.")] bool confirm = false) => tia.RunAsync(() =>
    {
        Guard.RequireConfirm(confirm, $"discard the in-work version of '{type}'");
        var t = FindType(tia.Project.ProjectLibrary, type);
        var inWork = t.Versions.FirstOrDefault(v => v.State == LibraryTypeVersionState.InWork)
                     ?? throw new McpException($"Type '{t.Name}' has no in-work version.");
        inWork.Discard();
        return new { type = t.Name, discarded = true };
    });

    [McpServerTool(Name = "lib_type_set_default", Destructive = false)]
    [Description("Sets which version of a library type is the default.")]
    public Task<string> SetDefault([Description(TypeParam)] string type, [Description("Version, e.g. 1.0.2.")] string version, [Description(LibParam)] string library = "project")
        => tia.RunAsync(() =>
        {
            var v = PickVersion(FindType(Library(library), type), version);
            v.SetAsDefault();
            return new { type, defaultVersion = v.VersionNumber.ToString() };
        });

    [McpServerTool(Name = "lib_delete", Destructive = true)]
    [Description("Deletes a library type, one version of it, or a master copy. Requires confirm=true.")]
    public Task<string> Delete(
        [Description("Type path, or master copy path when kind='mastercopy'.")] string item,
        [Description("type | version | mastercopy")] string kind = "type",
        [Description("Version to delete when kind='version'.")] string? version = null,
        [Description(LibParam)] string library = "project",
        [Description("Explicit user approval for this destructive action.")] bool confirm = false)
        => tia.RunAsync(() =>
        {
            Guard.RequireConfirm(confirm, $"delete library {kind} '{item}'");
            var lib = Library(library);
            switch (kind.Trim().ToLowerInvariant())
            {
                case "type":
                    FindType(lib, item).Delete();
                    break;
                case "version":
                    PickVersion(FindType(lib, item), Guard.RequireText(version, "version")).Delete();
                    break;
                case "mastercopy":
                    FindMasterCopy(lib, item).Delete();
                    break;
                default:
                    throw new McpException("kind must be type, version or mastercopy.");
            }

            return new { deleted = item, kind };
        });

    [McpServerTool(Name = "lib_create_folder", Destructive = false)]
    [Description("Creates a folder (path) in the types or master copies area of a library.")]
    public Task<string> CreateFolder(
        [Description("Folder path, e.g. 'Motors/Drives'.")] string path,
        [Description("types | mastercopies")] string area = "types",
        [Description(LibParam)] string library = "project")
        => tia.RunAsync(() =>
        {
            var lib = Library(library);
            if (area.Equals("mastercopies", StringComparison.OrdinalIgnoreCase))
            {
                ResolveMasterCopyFolder(lib.MasterCopyFolder, path, create: true);
            }
            else
            {
                ResolveTypeFolder(lib.TypeFolder, path, create: true);
            }

            return new { created = path, area };
        });

    [McpServerTool(Name = "lib_export_type", ReadOnly = true)]
    [Description("Exports a library type version as SIMATIC SD documents (or SimaticML with format='xml') to a directory.")]
    public Task<string> ExportType(
        [Description(TypeParam)] string type,
        [Description("Output directory.")] string outputDirectory,
        [Description(LibParam)] string library = "project",
        [Description("Version (default: default version).")] string? version = null,
        [Description("document | xml")] string format = "document")
        => tia.RunAsync(() =>
        {
            var t = FindType(Library(library), type);
            var v = PickVersion(t, version);
            var dir = Guard.Directory(outputDirectory, create: true);
            if (format.Equals("xml", StringComparison.OrdinalIgnoreCase))
            {
                var file = new FileInfo(Path.Combine(dir.FullName, CodeTransfer.SafeFileName($"{t.Name}_V{v.VersionNumber}") + ".xml"));
                v.Export(file, ExportOptions.WithDefaults);
                return new { exported = new[] { file.FullName } };
            }

            var supported = t.GetSupportedExportFormats().ToList();
            var exportFormat = supported.FirstOrDefault() ?? throw new McpException($"Type '{t.Name}' supports no document export.");
            var result = v.ExportAsDocuments(dir, CodeTransfer.SafeFileName(t.Name), exportFormat, LibraryExportOptions.None);
            return new
            {
                state = result.TransferResultState.ToString(),
                exported = result.ExportedDocuments.Select(f => f.FullName).ToList(),
                formats = supported,
                messages = result.Messages.Select(m => m.Message).ToList(),
            };
        });

    [McpServerTool(Name = "lib_import_type", Destructive = false)]
    [Description("Creates a library type (or a new version of an existing type) from SIMATIC SD documents in a directory.")]
    public Task<string> ImportType(
        [Description("Directory with the documents.")] string directory,
        [Description("Document file name without extension.")] string name,
        [Description("Target type folder path.")] string? folder = null,
        [Description(LibParam)] string library = "project",
        [Description("Add as new version of this existing type instead of creating a new type.")] string? asVersionOf = null,
        [Description(PlcParam + " Used as target environment.")] string? plc = null)
        => tia.RunAsync(() =>
        {
            var lib = Library(library);
            var dir = Guard.Directory(directory);
            IEngineeringObject env = Locator.FindPlc(tia.Project, plc);
            if (asVersionOf is not null)
            {
                var r = FindType(lib, asVersionOf).Versions.CreateFromDocuments(dir, name, env, CreateOptions.None, LibraryImportOptions.None);
                return new { state = r.TransferResultState.ToString(), messages = r.Messages.Select(m => m.Message).ToList() };
            }

            var result = ResolveTypeFolder(lib.TypeFolder, folder, create: true).Types.CreateFromDocuments(dir, name, env, LibraryImportOptions.None);
            return new { state = result.TransferResultState.ToString(), created = result.CreatedType?.Name, messages = result.Messages.Select(m => m.Message).ToList() };
        });

    // ---------- Synchronisation ----------

    [McpServerTool(Name = "lib_update_project", Destructive = false)]
    [Description("Updates the PLC instances to the latest versions of library types (like 'Update project'). " +
                 "Without types, the whole library is used.")]
    public Task<string> UpdateProject(
        [Description(LibParam)] string library = "project",
        [Description("Types or folders (paths) to use; omit for the whole library.")] string[]? types = null,
        [Description("PLCs to update; omit for all PLCs.")] string[]? plcs = null,
        [Description("Delete versions that are no longer used.")] bool deleteUnusedVersions = false)
        => tia.RunAsync(() =>
        {
            var lib = Library(library);
            var selection = Selection(lib, types);
            var scopes = (plcs is null || plcs.Length == 0
                    ? Locator.AllSoftware(tia.Project).Select(s => s.Software).OfType<PlcSoftware>()
                    : plcs.Select(p => Locator.FindPlc(tia.Project, p)))
                .Cast<IUpdateProjectScope>().ToList();
            if (lib is ProjectLibrary projectLibrary)
            {
                projectLibrary.UpdateProject(selection, scopes, deleteUnusedVersions ? DeleteUnusedVersionsMode.AutomaticallyDelete : DeleteUnusedVersionsMode.DoNotDelete);
            }
            else
            {
                ((GlobalLibrary)lib).UpdateProject(selection, scopes, ForceUpdateMode.SetOnlyHigherUpdatedVersionAsDefault,
                    deleteUnusedVersions ? DeleteUnusedVersionsMode.AutomaticallyDelete : DeleteUnusedVersionsMode.DoNotDelete,
                    StructureConflictResolutionMode.UpdateStructure);
            }

            return new { updated = scopes.Count, from = library };
        });

    [McpServerTool(Name = "lib_update_library", Destructive = false)]
    [Description("Copies types from one library to another (e.g. project library -> global library, like 'Update library').")]
    public Task<string> UpdateLibrary(
        [Description("Target library name ('project' or a global library opened read-write).")] string target,
        [Description("Source library (default: project).")] string source = "project",
        [Description("Types or folders to copy; omit for all.")] string[]? types = null,
        [Description("Delete versions that are no longer used in the target.")] bool deleteUnusedVersions = false,
        [Description("SetOnlyHigherUpdatedVersionAsDefault | ForceSetAnyUpdatedVersionAsDefault | NoDefaultVersionChange")] string defaultVersion = "SetOnlyHigherUpdatedVersionAsDefault")
        => tia.RunAsync(() =>
        {
            var src = Library(source);
            var dst = Library(target);
            src.UpdateLibrary(Selection(src, types), dst, Guard.ParseEnum(defaultVersion, ForceUpdateMode.SetOnlyHigherUpdatedVersionAsDefault),
                deleteUnusedVersions ? DeleteUnusedVersionsMode.AutomaticallyDelete : DeleteUnusedVersionsMode.DoNotDelete,
                StructureConflictResolutionMode.UpdateStructure);
            return new { copiedFrom = source, to = target };
        });

    [McpServerTool(Name = "lib_harmonize", Destructive = false)]
    [Description("Harmonizes the project with the library (names and/or paths of instances follow the library structure).")]
    public Task<string> Harmonize(
        [Description(LibParam)] string library = "project",
        [Description("Types or folders; omit for all.")] string[]? types = null,
        [Description("PLCs; omit for all.")] string[]? plcs = null,
        [Description("Harmonize paths (group structure).")] bool paths = true,
        [Description("Harmonize names.")] bool names = true)
        => tia.RunAsync(() =>
        {
            var lib = Library(library);
            var scopes = (plcs is null || plcs.Length == 0
                    ? Locator.AllSoftware(tia.Project).Select(s => s.Software).OfType<PlcSoftware>()
                    : plcs.Select(p => Locator.FindPlc(tia.Project, p)))
                .Cast<IUpdateProjectScope>().ToList();
            var options = HarmonizeProjectOptions.None;
            if (paths) options |= HarmonizeProjectOptions.HarmonizePaths;
            if (names) options |= HarmonizeProjectOptions.HarmonizeNames;
            lib.HarmonizeProject(Selection(lib, types), scopes, options);
            return new { harmonized = scopes.Count, options = options.ToString() };
        });

    [McpServerTool(Name = "lib_cleanup", Destructive = true)]
    [Description("Cleans up a library: deletes unused types/versions (like 'Clean up library'). Requires confirm=true.")]
    public Task<string> Cleanup(
        [Description(LibParam)] string library = "project",
        [Description("Types or folders; omit for all.")] string[]? types = null,
        [Description("Project library only: cleanup mode name (see TIA Portal CleanUpMode), default first mode.")] string? mode = null,
        [Description("Explicit user approval for this destructive action.")] bool confirm = false)
        => tia.RunAsync(() =>
        {
            Guard.RequireConfirm(confirm, $"clean up library '{library}'");
            var lib = Library(library);
            var selection = Selection(lib, types);
            if (lib is ProjectLibrary projectLibrary)
            {
                var cleanUpMode = Guard.ParseEnum(mode, (CleanUpMode)Enum.GetValues(typeof(CleanUpMode)).GetValue(0)!);
                projectLibrary.CleanUpLibrary(selection, cleanUpMode);
            }
            else
            {
                UserLibrary(library).CleanUpLibrary(selection);
            }

            return new { cleaned = library };
        });

    [McpServerTool(Name = "lib_update_check", ReadOnly = true)]
    [Description("Checks which instances in the project are out of date compared to the library.")]
    public Task<string> UpdateCheck(
        [Description(LibParam)] string library = "project",
        [Description("Also report up-to-date instances.")] bool includeUpToDate = false)
        => tia.RunAsync(() =>
        {
            var result = Library(library).UpdateCheck(tia.Project,
                includeUpToDate ? UpdateCheckMode.ReportOutOfDateAndUpToDate : UpdateCheckMode.ReportOutOfDateOnly);
            var lines = new List<string>();
            void Walk(UpdateCheckResultMessageComposition messages, int level)
            {
                foreach (var m in messages)
                {
                    lines.Add(new string(' ', level * 2) + m.Description);
                    Walk(m.Messages, level + 1);
                }
            }

            Walk(result.Messages, 0);
            return new { library, report = lines };
        });

    // ---------- Master copies ----------

    [McpServerTool(Name = "lib_create_master_copy", Destructive = false)]
    [Description("Creates a master copy from a project object (e.g. a PLC data type, a tag table or any object path via obj_* syntax).")]
    public Task<string> CreateMasterCopy(
        [Description("Object path (obj_browse syntax), e.g. plc:PLC_1/TypeGroup/Types[MyUdt].")] string sourcePath,
        [Description("Master copy folder path in the library (created if missing).")] string? folder = null,
        [Description(LibParam)] string library = "project")
        => tia.RunAsync(() =>
        {
            var source = ObjectPath.Resolve(tia, sourcePath) as IMasterCopySource
                         ?? throw new McpException($"'{sourcePath}' cannot be used as master copy source.");
            var copy = ResolveMasterCopyFolder(Library(library).MasterCopyFolder, folder, create: true).MasterCopies.Create(source);
            return new { created = copy.Name };
        });

    [McpServerTool(Name = "lib_instantiate_master_copy", Destructive = false)]
    [Description("Creates objects in the project from a master copy. target: blocks | types | tagtables | devices.")]
    public Task<string> InstantiateMasterCopy(
        [Description("Master copy name or 'Folder/Name' path.")] string masterCopy,
        [Description("blocks | types | tagtables | devices")] string target,
        [Description(LibParam)] string library = "project",
        [Description(PlcParam)] string? plc = null,
        [Description("Target group path inside the PLC.")] string? group = null,
        [Description("ThrowIfExists | Rename | Replace")] string mode = "ThrowIfExists")
        => tia.RunAsync(() =>
        {
            var copy = FindMasterCopy(Library(library), masterCopy);
            var copyMode = Guard.ParseEnum(mode, MasterCopyMode.ThrowIfExists);
            object created = target.Trim().ToLowerInvariant() switch
            {
                "blocks" => Locator.ResolveBlockGroup(Locator.FindPlc(tia.Project, plc), group, create: true).Blocks.CreateFrom(copy, copyMode),
                "types" => Locator.ResolveTypeGroup(Locator.FindPlc(tia.Project, plc), group, create: true).Types.CreateFrom(copy, copyMode),
                "tagtables" => Locator.ResolveTagTableGroup(Locator.FindPlc(tia.Project, plc), group, create: true).TagTables.CreateFrom(copy, copyMode),
                "devices" => tia.Project.Devices.CreateFrom(copy),
                _ => throw new McpException("target must be blocks, types, tagtables or devices."),
            };
            return new { created = created is IEngineeringObject eo ? ValueFormatter.Describe(eo) : created.ToString() };
        });

    // ---------- helpers ----------

    private ILibrary Library(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name!.Equals("project", StringComparison.OrdinalIgnoreCase))
        {
            return tia.Project.ProjectLibrary;
        }

        return ObjectPath.FindGlobalLibrary(tia, name);
    }

    private UserGlobalLibrary UserLibrary(string name) =>
        Library(name) as UserGlobalLibrary ?? throw new McpException($"'{name}' is not a user global library (system and corporate libraries are read-only).");

    private static LibraryTypeFolder ResolveTypeFolder(LibraryTypeFolder root, string? path, bool create)
    {
        var folder = root;
        foreach (var segment in Locator.Segments(path))
        {
            var next = folder.Folders.Find(segment);
            if (next is null)
            {
                if (!create)
                {
                    throw new McpException($"Type folder '{path}' not found.");
                }

                next = folder.Folders.Create(segment);
            }

            folder = next;
        }

        return folder;
    }

    private static MasterCopyFolder ResolveMasterCopyFolder(MasterCopyFolder root, string? path, bool create)
    {
        var folder = root;
        foreach (var segment in Locator.Segments(path))
        {
            var next = folder.Folders.Find(segment);
            if (next is null)
            {
                if (!create)
                {
                    throw new McpException($"Master copy folder '{path}' not found.");
                }

                next = folder.Folders.Create(segment);
            }

            folder = next;
        }

        return folder;
    }

    private static IEnumerable<LibraryType> AllTypes(LibraryTypeFolder folder) => TypesWithPath(folder).Select(e => e.Item);

    private static IEnumerable<(string Path, LibraryType Item)> TypesWithPath(LibraryTypeFolder folder, string prefix = "")
    {
        foreach (var t in folder.Types)
        {
            yield return (Locator.Join(prefix, t.Name), t);
        }

        foreach (var sub in folder.Folders)
        {
            foreach (var e in TypesWithPath(sub, Locator.Join(prefix, sub.Name)))
            {
                yield return e;
            }
        }
    }

    private static LibraryType FindType(ILibrary lib, string path)
    {
        return Locator.FindByPath(TypesWithPath(lib.TypeFolder), t => t.Name, path)
               ?? throw new McpException($"Library type '{path}' not found. Use lib_browse.");
    }

    private static LibraryTypeVersion PickVersion(LibraryType type, string? version)
    {
        if (version is null)
        {
            return type.Versions.FirstOrDefault(v => v.IsDefault)
                   ?? type.Versions.OrderByDescending(v => v.VersionNumber).FirstOrDefault()
                   ?? throw new McpException($"Type '{type.Name}' has no versions.");
        }

        return type.Versions.Find(Version.Parse(version))
               ?? throw new McpException($"Version {version} of '{type.Name}' not found. Versions: {string.Join(", ", type.Versions.Select(v => v.VersionNumber))}");
    }

    private static IEnumerable<(string Path, MasterCopy Item)> MasterCopiesWithPath(MasterCopyFolder folder, string prefix = "")
    {
        foreach (var m in folder.MasterCopies)
        {
            yield return (Locator.Join(prefix, m.Name), m);
        }

        foreach (var sub in folder.Folders)
        {
            foreach (var e in MasterCopiesWithPath(sub, Locator.Join(prefix, sub.Name)))
            {
                yield return e;
            }
        }
    }

    private static MasterCopy FindMasterCopy(ILibrary lib, string path)
    {
        return Locator.FindByPath(MasterCopiesWithPath(lib.MasterCopyFolder), m => m.Name, path)
               ?? throw new McpException($"Master copy '{path}' not found. Use lib_browse.");
    }

    private static List<ILibraryTypeOrFolderSelection> Selection(ILibrary lib, string[]? items)
    {
        if (items is null || items.Length == 0)
        {
            return new List<ILibraryTypeOrFolderSelection> { lib.TypeFolder };
        }

        return items.Select(i =>
        {
            var type = Locator.FindByPath(TypesWithPath(lib.TypeFolder), t => t.Name, i);
            if (type is not null)
            {
                return (ILibraryTypeOrFolderSelection)type;
            }

            // Not a type: interpret the path as a folder.
            return ResolveTypeFolder(lib.TypeFolder, i, create: false);
        }).ToList();
    }

    private static object FormatTypeFolder(LibraryTypeFolder folder, bool allVersions) => new
    {
        name = folder.Name,
        types = folder.Types.Select(t => new
        {
            name = t.Name,
            kind = t.GetType().Name,
            status = t.Status.ToString(),
            versions = t.Versions
                .Where(v => allVersions || v.IsDefault || v.State == LibraryTypeVersionState.InWork)
                .Select(v => new { version = v.VersionNumber.ToString(), state = v.State.ToString(), isDefault = v.IsDefault, author = v.Author })
                .ToList(),
            versionCount = t.Versions.Count,
        }).ToList(),
        folders = folder.Folders.Select(f => FormatTypeFolder(f, allVersions)).ToList(),
    };

    private static object FormatMasterCopyFolder(MasterCopyFolder folder) => new
    {
        name = folder.Name,
        masterCopies = folder.MasterCopies.Select(m => new
        {
            name = m.Name,
            author = m.Author,
            created = ValueFormatter.Format(m.CreationDate),
            content = m.ContentDescriptions.Select(c => ValueFormatter.ReadAttributes(c)).ToList(),
        }).ToList(),
        folders = folder.Folders.Select(FormatMasterCopyFolder).ToList(),
    };
}
