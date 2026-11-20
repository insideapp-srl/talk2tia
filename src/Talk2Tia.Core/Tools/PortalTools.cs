using System;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using Siemens.Engineering;
using Talk2Tia.Core.Openness;

namespace Talk2Tia.Core.Tools;

[McpServerToolType]
public sealed class PortalTools(TiaSession tia)
{
    [McpServerTool(Name = "portal_status", ReadOnly = true, Idempotent = true)]
    [Description("Shows whether Talk2Tia is connected to TIA Portal, the connection mode and the open projects.")]
    public Task<string> Status() => tia.RunAsync(() =>
    {
        if (!tia.IsConnected)
        {
            return new { connected = false, hint = "Use portal_list_instances + portal_attach, or portal_start." };
        }

        var active = tia.TryGetProject();
        return new
        {
            connected = true,
            processId = tia.ProcessId,
            mode = tia.Mode?.ToString(),
            startedByTalk2Tia = tia.OwnsProcess,
            activeProject = active?.Name,
            openProjects = tia.Portal.Projects.Select(p => new { name = p.Name, path = p.Path?.FullName, modified = p.IsModified }).ToList(),
            openGlobalLibraries = tia.Portal.GlobalLibraries.Select(l => l.Name).ToList(),
        };
    });

    [McpServerTool(Name = "portal_list_instances", ReadOnly = true, Idempotent = true)]
    [Description("Lists the running TIA Portal processes with their id, mode and open project.")]
    public Task<string> ListInstances() => tia.RunAsync(() =>
        TiaPortal.GetProcesses().Select(p => new
        {
            processId = p.Id,
            mode = p.Mode.ToString(),
            projectPath = p.ProjectPath?.FullName,
            startedAt = ValueFormatter.Format(p.AcquisitionTime),
            attachedSessions = p.AttachedSessions?.Count ?? 0,
        }).ToList());

    [McpServerTool(Name = "portal_attach", Destructive = false)]
    [Description("Attaches to a running TIA Portal instance. The user must confirm the Openness access dialog in TIA Portal. " +
                 "Without processId, attaches to the only running instance.")]
    public Task<string> Attach(
        [Description("Process id from portal_list_instances. Optional when only one instance is running.")] int? processId = null,
        [Description("Seconds to wait for the process to accept the connection.")] int timeoutSeconds = 60)
        => tia.RunAsync(() =>
        {
            var processes = TiaPortal.GetProcesses();
            TiaPortalProcess process;
            if (processId is { } id)
            {
                process = processes.FirstOrDefault(p => p.Id == id)
                          ?? TiaPortal.GetProcess(id, TimeSpan.FromSeconds(timeoutSeconds));
            }
            else
            {
                process = processes.Count switch
                {
                    0 => throw new McpException("No TIA Portal instance is running. Use portal_start."),
                    1 => processes[0],
                    _ => throw new McpException(
                        $"Several TIA Portal instances are running; pass processId: {string.Join(", ", processes.Select(p => $"{p.Id} ({p.ProjectPath?.Name ?? "no project"})"))}"),
                };
            }

            var mode = process.Mode;
            var portal = process.Attach();
            tia.Connect(portal, ownsProcess: false, mode);
            return new
            {
                attached = true,
                processId = tia.ProcessId,
                mode = mode.ToString(),
                openProjects = portal.Projects.Select(p => p.Name).ToList(),
            };
        });

    [McpServerTool(Name = "portal_start", Destructive = false)]
    [Description("Starts a new TIA Portal instance owned by Talk2Tia. Use withUserInterface=false for headless automation (faster).")]
    public Task<string> Start(
        [Description("Show the TIA Portal user interface.")] bool withUserInterface = true)
        => tia.RunAsync(() =>
        {
            var mode = withUserInterface ? TiaPortalMode.WithUserInterface : TiaPortalMode.WithoutUserInterface;
            var portal = new TiaPortal(mode);
            tia.Connect(portal, ownsProcess: true, mode);
            return new { started = true, processId = tia.ProcessId, mode = mode.ToString() };
        });

    [McpServerTool(Name = "portal_disconnect", Destructive = false)]
    [Description("Releases the connection. An instance started by Talk2Tia is closed (unsaved changes are lost, so confirm=true is " +
                 "required when a project has unsaved changes); an attached instance keeps running.")]
    public Task<string> Disconnect(
        [Description("Required when a portal started by Talk2Tia has projects with unsaved changes.")] bool confirm = false)
        => tia.RunAsync(() =>
        {
            if (!tia.IsConnected)
            {
                return new { disconnected = true, note = "Was not connected." };
            }

            if (tia.OwnsProcess && tia.Portal.Projects.Any(p => p.IsModified))
            {
                Guard.RequireConfirm(confirm, "close TIA Portal with unsaved changes");
            }

            var closed = tia.OwnsProcess;
            tia.Disconnect();
            return new { disconnected = true, portalClosed = closed };
        });
}
