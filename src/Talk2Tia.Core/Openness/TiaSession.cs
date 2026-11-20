using System;
using System.Linq;
using System.Threading.Tasks;
using ModelContextProtocol;
using Siemens.Engineering;

namespace Talk2Tia.Core.Openness;

/// <summary>
/// Holds the connection to a TIA Portal instance and the active project.
/// Every member that touches Openness objects must run through <see cref="RunAsync"/>.
/// </summary>
public sealed class TiaSession : IDisposable
{
    private readonly StaDispatcher _sta = new();
    private TiaPortal? _portal;
    private Project? _activeProject;

    public bool IsConnected => _portal is not null;
    public bool OwnsProcess { get; private set; }
    public int? ProcessId { get; private set; }
    public TiaPortalMode? Mode { get; private set; }

    /// <summary>Runs <paramref name="work"/> on the Openness thread and serializes the result as JSON.</summary>
    public Task<string> RunAsync(Func<object?> work) => _sta.InvokeAsync(() => Execute(work));

    private static string Execute(Func<object?> work)
    {
        try
        {
            return Json.Serialize(work());
        }
        catch (McpException)
        {
            throw;
        }
        catch (EngineeringSecurityException ex)
        {
            throw new McpException(
                "TIA Portal denied Openness access. Make sure the user is a member of the 'Siemens TIA Openness' " +
                $"group and confirm the access dialog in TIA Portal. Details: {ex.Message}", ex);
        }
        catch (EngineeringUserAbortException ex)
        {
            throw new McpException($"The operation was cancelled in TIA Portal: {ex.Message}", ex);
        }
        catch (EngineeringObjectDisposedException ex)
        {
            throw new McpException(
                $"A TIA Portal object is no longer valid (the project or the portal may have been closed): {ex.Message}", ex);
        }
        catch (EngineeringException ex)
        {
            throw new McpException($"TIA Portal error ({ex.GetType().Name}): {ex.Message}", ex);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.IO.IOException
                                       or UnauthorizedAccessException or NotSupportedException or FormatException)
        {
            throw new McpException(ex.Message, ex);
        }
    }

    public TiaPortal Portal =>
        _portal ?? throw new McpException("Not connected to TIA Portal. Call portal_attach or portal_start first.");

    /// <summary>The active project: the one selected explicitly, or the only open project.</summary>
    public Project Project
    {
        get
        {
            var projects = Portal.Projects;
            if (_activeProject is not null && projects.Contains(_activeProject))
            {
                return _activeProject;
            }

            _activeProject = null;
            return projects.Count switch
            {
                0 => throw new McpException("No project is open. Use project_open or project_create."),
                1 => projects.First(),
                _ => throw new McpException(
                    "Several projects are open. Use project_select to choose the active one: " +
                    string.Join(", ", projects.Select(p => p.Name))),
            };
        }
    }

    public Project? TryGetProject()
    {
        if (_portal is null)
        {
            return null;
        }

        var projects = _portal.Projects;
        if (_activeProject is not null && projects.Contains(_activeProject))
        {
            return _activeProject;
        }

        return projects.Count == 1 ? projects.First() : null;
    }

    public void SetActiveProject(Project project) => _activeProject = project;

    public void Connect(TiaPortal portal, bool ownsProcess, TiaPortalMode mode)
    {
        Disconnect();
        _portal = portal;
        OwnsProcess = ownsProcess;
        Mode = mode;
        ProcessId = portal.GetCurrentProcess().Id;
    }

    /// <summary>Releases the connection. A portal started by Talk2Tia is closed; an attached one keeps running.</summary>
    public void Disconnect()
    {
        _activeProject = null;
        _portal?.Dispose();
        _portal = null;
        OwnsProcess = false;
        ProcessId = null;
        Mode = null;
    }

    public void Dispose()
    {
        _sta.InvokeAsync(() =>
        {
            Disconnect();
            return true;
        }).GetAwaiter().GetResult();
        _sta.Dispose();
    }
}
