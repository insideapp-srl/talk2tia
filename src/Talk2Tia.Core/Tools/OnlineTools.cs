using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using Siemens.Engineering;
using Siemens.Engineering.Compare;
using Siemens.Engineering.Connection;
using Siemens.Engineering.Download;
using Siemens.Engineering.Download.Configurations;
using Siemens.Engineering.HW;
using Siemens.Engineering.Online;
using Siemens.Engineering.SW;
using Talk2Tia.Core.Openness;

namespace Talk2Tia.Core.Tools;

[McpServerToolType]
public sealed class OnlineTools(TiaSession tia)
{
    private const string PlcParam = "PLC software, CPU or device name. Optional when the project has one PLC.";

    [McpServerTool(Name = "online_connection_options", ReadOnly = true)]
    [Description("Lists the connection modes (e.g. PN/IE), PC interfaces, target interfaces and addresses available for a PLC, " +
                 "plus the currently configured connection. Use the names with online_configure.")]
    public Task<string> ConnectionOptions([Description(PlcParam)] string? plc = null) => tia.RunAsync(() =>
    {
        var cpu = Locator.FindPlcRef(tia.Project, plc).DeviceItem;
        var online = cpu.GetService<OnlineProvider>() ?? throw new McpException("This device does not support online access.");
        var config = online.Configuration;
        return new
        {
            configured = config.IsConfigured,
            modes = config.Modes.Select(m => new
            {
                mode = m.Name,
                pcInterfaces = m.PcInterfaces.Select(pc => new
                {
                    name = pc.Name,
                    number = pc.Number,
                    addresses = pc.Addresses.Select(a => a.Name).ToList(),
                    targetInterfaces = pc.TargetInterfaces.Select(t => new { name = t.Name, addresses = t.Addresses.Select(a => a.Name).ToList() }).ToList(),
                }).ToList(),
            }).ToList(),
        };
    });

    [McpServerTool(Name = "online_accessible_devices", ReadOnly = true)]
    [Description("Scans the network through a PC interface and lists the accessible devices (like 'Accessible devices' in TIA Portal).")]
    public Task<string> AccessibleDevices(
        [Description("Connection mode, e.g. PN/IE.")] string mode,
        [Description("PC interface name, from online_connection_options.")] string pcInterface,
        [Description(PlcParam)] string? plc = null)
        => tia.RunAsync(() =>
        {
            var pc = FindPcInterface(Locator.FindPlcRef(tia.Project, plc).DeviceItem, mode, pcInterface);
            return pc.GetAccessibleDevices().Select(d => ValueFormatter.ReadAttributes((IEngineeringObject)d)).ToList();
        });

    [McpServerTool(Name = "online_configure", Destructive = false)]
    [Description("Configures the online connection of a PLC (mode, PC interface and target interface or IP address).")]
    public Task<string> Configure(
        [Description("Connection mode, e.g. PN/IE.")] string mode,
        [Description("PC interface name.")] string pcInterface,
        [Description("Target interface name, e.g. '1 X1'. Optional when address is given.")] string? targetInterface = null,
        [Description("Target address (e.g. IP) to use instead of a target interface.")] string? address = null,
        [Description(PlcParam)] string? plc = null)
        => tia.RunAsync(() =>
        {
            var cpu = Locator.FindPlcRef(tia.Project, plc).DeviceItem;
            var config = cpu.GetService<OnlineProvider>()!.Configuration;
            var pc = FindPcInterface(cpu, mode, pcInterface);
            bool applied;
            if (!string.IsNullOrWhiteSpace(address))
            {
                var addr = pc.Addresses.FirstOrDefault(a => a.Name == address)
                           ?? pc.TargetInterfaces.SelectMany(t => t.Addresses).FirstOrDefault(a => a.Name == address)
                           ?? throw new McpException($"Address '{address}' not available on {pcInterface}.");
                applied = config.ApplyConfiguration(addr);
            }
            else
            {
                var target = pc.TargetInterfaces.FirstOrDefault(t => Locator.NameEquals(t.Name, Guard.RequireText(targetInterface, "targetInterface")))
                             ?? throw new McpException(
                                 $"Target interface '{targetInterface}' not found. Available: {string.Join(", ", pc.TargetInterfaces.Select(t => t.Name))}");
                applied = config.ApplyConfiguration(target);
            }

            return new { applied, configured = config.IsConfigured };
        });

    [McpServerTool(Name = "online_state", ReadOnly = true)]
    [Description("Returns the online state of a PLC (Offline, Online, NotReachable, ...).")]
    public Task<string> State([Description(PlcParam)] string? plc = null) => tia.RunAsync(() =>
    {
        var online = Locator.FindPlcRef(tia.Project, plc).DeviceItem.GetService<OnlineProvider>()
                     ?? throw new McpException("This device does not support online access.");
        return new { state = online.State.ToString(), configured = online.Configuration.IsConfigured };
    });

    [McpServerTool(Name = "online_go_online", Destructive = false)]
    [Description("Establishes an online connection to the PLC using the configured connection.")]
    public Task<string> GoOnline([Description(PlcParam)] string? plc = null) => tia.RunAsync(() =>
    {
        var online = Locator.FindPlcRef(tia.Project, plc).DeviceItem.GetService<OnlineProvider>()
                     ?? throw new McpException("This device does not support online access.");
        if (!online.Configuration.IsConfigured)
        {
            throw new McpException("The online connection is not configured. Use online_connection_options and online_configure.");
        }

        return new { state = online.GoOnline().ToString() };
    });

    [McpServerTool(Name = "online_go_offline", Destructive = false)]
    [Description("Closes the online connection to the PLC.")]
    public Task<string> GoOffline([Description(PlcParam)] string? plc = null) => tia.RunAsync(() =>
    {
        var online = Locator.FindPlcRef(tia.Project, plc).DeviceItem.GetService<OnlineProvider>()
                     ?? throw new McpException("This device does not support online access.");
        online.GoOffline();
        return new { state = online.State.ToString() };
    });

    [McpServerTool(Name = "plc_compare_online", ReadOnly = true)]
    [Description("Compares the offline program with the PLC (online). Lists the objects that differ. If the PLC is offline " +
                 "it goes online with the configured connection, compares and goes back offline (autoOnline=false to disable).")]
    public Task<string> CompareOnline(
        [Description(PlcParam)] string? plc = null,
        [Description("Also list identical objects.")] bool includeIdentical = false,
        [Description("Go online automatically when the PLC is offline, and back offline afterwards.")] bool autoOnline = true)
        => tia.RunAsync(() =>
        {
            var target = Locator.FindPlcRef(tia.Project, plc);
            return OnlineScope.Run(target, autoOnline, "plc_compare_online",
                () => FormatCompare(((Siemens.Engineering.SW.PlcSoftware)target.Software).CompareToOnline(), includeIdentical));
        });

    [McpServerTool(Name = "plc_compare", ReadOnly = true)]
    [Description("Compares the program of two PLCs in the project (offline/offline).")]
    public Task<string> Compare(
        [Description("Left PLC.")] string left,
        [Description("Right PLC.")] string right,
        [Description("Also list identical objects.")] bool includeIdentical = false)
        => tia.RunAsync(() =>
        {
            var a = Locator.FindPlc(tia.Project, left);
            var b = Locator.FindPlc(tia.Project, right);
            return FormatCompare(a.CompareTo(b), includeIdentical);
        });

    [McpServerTool(Name = "plc_download", Destructive = true, OpenWorld = true)]
    [Description("Downloads to the real PLC using the configured online connection. This changes the running plant: " +
                 "ALWAYS ask the user first and pass confirm=true. options: Software (default), SoftwareOnlyChanges, Hardware, or 'Hardware,Software'. " +
                 "The CPU is stopped only if allowStop=true; data blocks are re-initialized only if reinitializeDataBlocks=true.")]
    public Task<string> Download(
        [Description("Explicit user approval to download to the PLC.")] bool confirm,
        [Description("Connection mode, e.g. PN/IE (see online_connection_options).")] string mode,
        [Description("PC interface name.")] string pcInterface,
        [Description("Target interface name, e.g. '1 X1'.")] string targetInterface,
        [Description(PlcParam)] string? plc = null,
        [Description("Software | SoftwareOnlyChanges | Hardware | Hardware,Software")] string options = "Software",
        [Description("Allow stopping the CPU when the download requires it.")] bool allowStop = false,
        [Description("Restart the CPU after the download.")] bool startAfter = true,
        [Description("Allow re-initializing data blocks (actual values are lost).")] bool reinitializeDataBlocks = false,
        [Description("Overwrite system data / target languages / memory card content when asked.")] bool allowOverwrite = false)
        => tia.RunAsync(() =>
        {
            Guard.RequireConfirm(confirm, "download to the PLC");
            var cpu = Locator.FindPlcRef(tia.Project, plc).DeviceItem;
            var provider = cpu.GetService<DownloadProvider>() ?? throw new McpException("This device does not support download.");
            var pc = FindPcInterface(provider.Configuration, mode, pcInterface);
            var target = pc.TargetInterfaces.FirstOrDefault(t => Locator.NameEquals(t.Name, targetInterface))
                         ?? throw new McpException(
                             $"Target interface '{targetInterface}' not found. Available: {string.Join(", ", pc.TargetInterfaces.Select(t => t.Name))}");

            var downloadOptions = options.Split(new[] { ',', '|', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(o => Guard.ParseEnum(o, DownloadOptions.Software))
                .Aggregate(DownloadOptions.None, (acc, o) => acc | o);

            var decisions = new List<object>();
            var blocked = new List<string>();
            void Pre(DownloadConfiguration c) => Decide(c, allowStop, startAfter, reinitializeDataBlocks, allowOverwrite, decisions, blocked);

            var result = provider.Download(target, Pre, Pre, downloadOptions);
            return new
            {
                state = result.State.ToString(),
                errors = result.ErrorCount,
                warnings = result.WarningCount,
                decisions,
                blockedBecauseNotAllowed = blocked,
                messages = Flatten(result.Messages).Take(300).ToList(),
            };
        });

    // ---------- helpers ----------

    private static ConfigurationPcInterface FindPcInterface(DeviceItem cpu, string mode, string pcInterface) =>
        FindPcInterface((cpu.GetService<OnlineProvider>() ?? throw new McpException("This device does not support online access.")).Configuration, mode, pcInterface);

    private static ConfigurationPcInterface FindPcInterface(ConnectionConfiguration config, string mode, string pcInterface)
    {
        var m = config.Modes.FirstOrDefault(x => Locator.NameEquals(x.Name, mode))
                ?? throw new McpException($"Mode '{mode}' not found. Available: {string.Join(", ", config.Modes.Select(x => x.Name))}");
        return m.PcInterfaces.FirstOrDefault(p => Locator.NameEquals(p.Name, pcInterface))
               ?? throw new McpException($"PC interface '{pcInterface}' not found. Available: {string.Join(", ", m.PcInterfaces.Select(p => p.Name))}");
    }

    /// <summary>Answers the download dialogs conservatively; anything risky is only accepted when explicitly allowed.</summary>
    private static void Decide(DownloadConfiguration c, bool allowStop, bool startAfter, bool reinit, bool allowOverwrite,
        List<object> decisions, List<string> blocked)
    {
        string choice;
        switch (c)
        {
            case StopModules s:
                s.CurrentSelection = allowStop ? StopModulesSelections.StopAll : StopModulesSelections.NoAction;
                choice = s.CurrentSelection.ToString();
                if (!allowStop) blocked.Add("StopModules");
                break;
            case StartModules s:
                s.CurrentSelection = startAfter ? StartModulesSelections.StartModule : StartModulesSelections.NoAction;
                choice = s.CurrentSelection.ToString();
                break;
            case DataBlockReinitialization s:
                s.CurrentSelection = reinit ? DataBlockReinitializationSelections.StopPlcAndReinitialize : DataBlockReinitializationSelections.NoAction;
                choice = s.CurrentSelection.ToString();
                if (!reinit) blocked.Add("DataBlockReinitialization");
                break;
            case DataBlockReinitializationOrKeepActualValues s:
                s.CurrentSelection = reinit
                    ? DataBlockReinitializationOrKeepActualValuesSelections.StopPlcAndReinitialize
                    : DataBlockReinitializationOrKeepActualValuesSelections.KeepActualValues;
                choice = s.CurrentSelection.ToString();
                break;
            case ConsistentBlocksDownload s:
                s.CurrentSelection = ConsistentBlocksDownloadSelections.ConsistentDownload;
                choice = s.CurrentSelection.ToString();
                break;
            case AllBlocksDownload s:
                s.CurrentSelection = AllBlocksDownloadSelections.DownloadAllBlocks;
                choice = s.CurrentSelection.ToString();
                break;
            case AlarmTextLibrariesDownload s:
                s.CurrentSelection = AlarmTextLibrariesDownloadSelections.ConsistentDownload;
                choice = s.CurrentSelection.ToString();
                break;
            case OverwriteSystemData s:
                s.CurrentSelection = allowOverwrite ? OverwriteSystemDataSelections.Overwrite : OverwriteSystemDataSelections.NoAction;
                choice = s.CurrentSelection.ToString();
                if (!allowOverwrite) blocked.Add("OverwriteSystemData");
                break;
            case OverwriteOnMemoryCard s:
                s.CurrentSelection = allowOverwrite ? OverwriteOnMemoryCardSelections.Load : OverwriteOnMemoryCardSelections.NoAction;
                choice = s.CurrentSelection.ToString();
                if (!allowOverwrite) blocked.Add("OverwriteOnMemoryCard");
                break;
            case DownloadCheckConfiguration check when c is OverwriteTargetLanguages or CheckBeforeDownload:
                check.Checked = allowOverwrite || c is CheckBeforeDownload;
                choice = check.Checked ? "Checked" : "Unchecked";
                break;
            case DownloadCheckConfiguration check:
                choice = $"left unchanged (Checked={check.Checked})";
                break;
            case DownloadPasswordConfiguration:
                choice = "password required - not supported, download will fail";
                blocked.Add(c.GetType().Name);
                break;
            default:
                choice = "default";
                break;
        }

        decisions.Add(new { configuration = c.GetType().Name, message = c.Message, choice });
    }

    private static IEnumerable<object> Flatten(DownloadResultMessageComposition messages)
    {
        foreach (var m in messages)
        {
            if (!string.IsNullOrWhiteSpace(m.Message))
            {
                yield return new { state = m.State.ToString(), message = m.Message };
            }

            foreach (var child in Flatten(m.Messages))
            {
                yield return child;
            }
        }
    }

    private static object FormatCompare(CompareResult result, bool includeIdentical)
    {
        var differences = new List<object>();
        void Walk(CompareResultElement e, string path)
        {
            var name = e.LeftName ?? e.RightName ?? string.Empty;
            var current = path.Length == 0 ? name : $"{path}/{name}";
            var leaf = e.Elements.Count == 0;
            if (leaf && (includeIdentical || e.ComparisonResult is not (CompareResultState.ObjectsIdentical or CompareResultState.FolderContentsIdentical or CompareResultState.CompareIrrelevant)))
            {
                differences.Add(new
                {
                    path = current,
                    result = e.ComparisonResult.ToString(),
                    left = e.LeftName,
                    right = e.RightName,
                    details = string.IsNullOrWhiteSpace(e.DetailedInformation) ? null : e.DetailedInformation,
                });
            }

            foreach (var child in e.Elements)
            {
                Walk(child, current);
            }
        }

        Walk(result.RootElement, string.Empty);
        return new
        {
            overall = result.RootElement.ComparisonResult.ToString(),
            count = differences.Count,
            elements = differences.Take(1000).ToList(),
        };
    }
}
