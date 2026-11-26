using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using Siemens.Engineering;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.Blocks.Interface;
using Siemens.Engineering.SW.WatchAndForceTables;
using Talk2Tia.Core.Openness;
using Talk2Tia.Core.WebApi;

namespace Talk2Tia.Core.Tools;

/// <summary>
/// Runtime diagnostics: CPU state, diagnostic buffer, alarms and variable values through the CPU Web API,
/// plus watch tables and data block snapshots through Openness. Nothing here changes the PLC.
/// </summary>
[McpServerToolType]
public sealed class DiagnosticsTools(TiaSession tia, PlcWebApiRegistry webApi)
{
    internal const string WebPlcParam = "PLC name from the 'plcs' section of the Talk2Tia configuration. Optional when only one PLC is configured.";
    private const string TiaPlcParam = "PLC software, CPU or device name in the TIA project. Optional when the project has one PLC.";
    private const string LanguageParam = "Language of the texts, e.g. en-US, it-IT, de-DE (must be a project language loaded in the CPU).";

    [McpServerTool(Name = "diag_list_plcs", ReadOnly = true)]
    [Description("Lists the PLCs configured for the Web API (name, address, user). With check=true pings each PLC, logs in and " +
                 "reports the rights of the user and the Web API version.")]
    public async Task<string> ListPlcs(
        [Description("Contact each PLC to verify connection, login and rights.")] bool check = false,
        CancellationToken cancellationToken = default)
    {
        var result = new List<object>();
        foreach (var pair in webApi.Endpoints)
        {
            object? status = null;
            if (check)
            {
                try
                {
                    var calls = await webApi.Get(pair.Key).TryCallManyAsync(
                        new[] { new WebApiCall("Api.Ping"), new WebApiCall("Api.Version"), new WebApiCall("Api.GetPermissions") },
                        cancellationToken).ConfigureAwait(false);
                    status = new
                    {
                        reachable = true,
                        version = calls[1].Result,
                        permissions = calls[2].Result is JsonArray a ? a.Select(p => p?["name"]?.ToString()).ToList() : null,
                        errors = calls.Where(c => c.Error is not null).Select(c => c.Error!.Message).ToList(),
                    };
                }
                catch (Exception ex) when (ex is InvalidOperationException or WebApiException)
                {
                    status = new { reachable = false, error = ex.Message };
                }
            }

            result.Add(new
            {
                name = pair.Key,
                address = pair.Value.Address,
                user = string.IsNullOrWhiteSpace(pair.Value.Username) ? "(Everybody)" : pair.Value.Username,
                status,
            });
        }

        return Json.Serialize(new
        {
            plcs = result,
            hint = result.Count == 0 ? "No PLC configured: add a 'plcs' section to the Talk2Tia configuration file." : null,
        });
    }

    [McpServerTool(Name = "diag_cpu_state", ReadOnly = true)]
    [Description("Reads the CPU operating mode (run, stop, startup, hold, ...), its system time and the Web API version.")]
    public Task<string> CpuState([Description(WebPlcParam)] string? plc = null, CancellationToken cancellationToken = default) =>
        WebAsync(async () =>
        {
            var client = webApi.Get(plc);
            var calls = await client.TryCallManyAsync(
                new[] { new WebApiCall("Plc.ReadOperatingMode"), new WebApiCall("Plc.ReadSystemTime"), new WebApiCall("Api.Version") },
                cancellationToken).ConfigureAwait(false);
            if (calls[0].Error is { } error)
            {
                throw error;
            }

            return new
            {
                plc = client.Name,
                operatingMode = calls[0].Result,
                systemTime = calls[1].Result?["timestamp"] ?? calls[1].Result,
                webApiVersion = calls[2].Result,
                unsupported = calls.Skip(1).Where(c => c.Error is not null).Select(c => c.Error!.Message).ToList(),
            };
        });

    [McpServerTool(Name = "diag_read_variables", ReadOnly = true)]
    [Description("Reads the current values of PLC variables in one request. Use symbolic names in Web API syntax: " +
                 "\"DB name\".member.sub[2] for data blocks (optimized blocks included) or \"Tag name\" for PLC tags (inputs, " +
                 "outputs, memory). The variables must be accessible from HMI/OPC UA/Web API in TIA Portal.")]
    public Task<string> ReadVariables(
        [Description("Variables to read, e.g. [\"\\\"Motor_DB\\\".speed\", \"\\\"Start_Button\\\"\"].")] string[] variables,
        [Description(WebPlcParam)] string? plc = null,
        [Description("simple (typed values, default) or raw (bytes as stored in the PLC).")] string mode = "simple",
        CancellationToken cancellationToken = default) =>
        WebAsync(async () =>
        {
            if (variables is null || variables.Length == 0)
            {
                throw new McpException("Parameter 'variables' needs at least one variable.");
            }

            var dataMode = ParseChoice(mode, "mode", "simple", "raw");
            var client = webApi.Get(plc);
            var calls = await client.TryCallManyAsync(
                variables.Select(v => new WebApiCall("PlcProgram.Read", new JsonObject { ["var"] = v, ["mode"] = dataMode })).ToList(),
                cancellationToken).ConfigureAwait(false);
            return new
            {
                plc = client.Name,
                values = variables.Select((v, i) => new
                {
                    variable = v,
                    value = calls[i].Result,
                    error = calls[i].Error?.Message,
                }).ToList(),
            };
        });

    [McpServerTool(Name = "diag_browse_variables", ReadOnly = true)]
    [Description("Browses the variables available through the Web API: without 'variable' lists the top level (data blocks and " +
                 "tags); with 'variable' lists its members (children) or describes it (var). Returns names, data types and " +
                 "whether they are read-only.")]
    public Task<string> BrowseVariables(
        [Description("Variable to browse, e.g. \"\\\"Motor_DB\\\"\" or \"\\\"Motor_DB\\\".axis\". Omit for the top level.")] string? variable = null,
        [Description("children (default) or var.")] string mode = "children",
        [Description(WebPlcParam)] string? plc = null,
        CancellationToken cancellationToken = default) =>
        WebAsync(async () =>
        {
            var parameters = new JsonObject { ["mode"] = ParseChoice(mode, "mode", "children", "var") };
            if (!string.IsNullOrWhiteSpace(variable))
            {
                parameters["var"] = variable;
            }

            var client = webApi.Get(plc);
            return new { plc = client.Name, items = await client.CallAsync("PlcProgram.Browse", parameters, cancellationToken).ConfigureAwait(false) };
        });

    [McpServerTool(Name = "diag_diagnostic_buffer", ReadOnly = true)]
    [Description("Reads the CPU diagnostic buffer (newest first): timestamp, incoming/outgoing status, short, long and help texts. " +
                 "Requires CPU firmware with DiagnosticBuffer.Browse (S7-1500 V3.0 or later).")]
    public Task<string> DiagnosticBuffer(
        [Description("Maximum number of entries (default 50).")] int count = 50,
        [Description(LanguageParam)] string language = "en-US",
        [Description(WebPlcParam)] string? plc = null,
        CancellationToken cancellationToken = default) =>
        WebAsync(async () =>
        {
            var client = webApi.Get(plc);
            var result = await client.CallAsync("DiagnosticBuffer.Browse",
                new JsonObject { ["language"] = language, ["count"] = Math.Max(1, count) }, cancellationToken).ConfigureAwait(false);
            return new { plc = client.Name, buffer = result };
        });

    [McpServerTool(Name = "diag_alarms", ReadOnly = true)]
    [Description("Reads the active alarms of the CPU (program alarms and system diagnostics): id, number, status, timestamp, " +
                 "texts and acknowledgement state. Requires CPU firmware with Alarms.Browse (S7-1500 V3.0 or later).")]
    public Task<string> Alarms(
        [Description("Maximum number of alarms (default 50, max 5000).")] int count = 50,
        [Description(LanguageParam)] string language = "en-US",
        [Description(WebPlcParam)] string? plc = null,
        CancellationToken cancellationToken = default) =>
        WebAsync(async () =>
        {
            var client = webApi.Get(plc);
            var result = await client.CallAsync("Alarms.Browse",
                new JsonObject { ["language"] = language, ["count"] = Math.Min(5000, Math.Max(1, count)) }, cancellationToken).ConfigureAwait(false);
            return new { plc = client.Name, alarms = result };
        });

    [McpServerTool(Name = "diag_webapi_methods", ReadOnly = true)]
    [Description("Lists the Web API methods supported by the CPU firmware and the rights of the configured user. " +
                 "Use it to find out which diagnostics are available on a given CPU.")]
    public Task<string> WebApiMethods([Description(WebPlcParam)] string? plc = null, CancellationToken cancellationToken = default) =>
        WebAsync(async () =>
        {
            var client = webApi.Get(plc);
            var calls = await client.TryCallManyAsync(
                new[] { new WebApiCall("Api.GetMethods"), new WebApiCall("Api.Browse"), new WebApiCall("Api.GetPermissions") },
                cancellationToken).ConfigureAwait(false);
            var methods = calls[0].Result ?? calls[1].Result;
            if (methods is null && calls[1].Error is { } error)
            {
                throw error;
            }

            return new
            {
                plc = client.Name,
                methods = methods is JsonArray a ? a.Select(m => m?["name"]?.ToString()).ToList() : null,
                permissions = calls[2].Result is JsonArray p ? p.Select(x => x?["name"]?.ToString()).ToList() : null,
            };
        });

    [McpServerTool(Name = "diag_list_watch_tables", ReadOnly = true, Idempotent = true)]
    [Description("Lists the watch tables of a PLC in the TIA project (with group paths) and their number of entries.")]
    public Task<string> ListWatchTables([Description(TiaPlcParam)] string? plc = null) => tia.RunAsync(() =>
        AllWatchTables(Locator.FindPlc(tia.Project, plc).WatchAndForceTableGroup)
            .Select(t => new { path = t.Path, entries = t.Table.Entries.OfType<PlcWatchTableEntry>().Count() })
            .ToList());

    [McpServerTool(Name = "diag_watch_table", ReadOnly = true)]
    [Description("Returns the entries of a watch table of the TIA project and, with monitor=true (default), reads their current " +
                 "values from the CPU through the Web API, like 'Monitor all' in TIA Portal. Entries with only an absolute " +
                 "address cannot be read through the Web API.")]
    public async Task<string> WatchTable(
        [Description("Watch table name or 'Group/Name' path.")] string table,
        [Description(TiaPlcParam)] string? plc = null,
        [Description("Read the current values from the CPU.")] bool monitor = true,
        [Description("PLC name in the Talk2Tia configuration, when it differs from the TIA PLC name and several PLCs are configured.")] string? webPlc = null,
        CancellationToken cancellationToken = default)
    {
        var json = await tia.RunAsync(() =>
        {
            var group = Locator.FindPlc(tia.Project, plc).WatchAndForceTableGroup;
            var found = AllWatchTables(group).FirstOrDefault(t => Locator.NameEquals(t.Path, table) || Locator.NameEquals(t.Table.Name, table));
            if (found.Table is null)
            {
                throw new McpException(
                    $"Watch table '{table}' not found. Available: {string.Join(", ", AllWatchTables(group).Select(t => t.Path))}");
            }

            return found.Table.Entries.OfType<PlcWatchTableEntry>().Select(e => new WatchEntry(e.Name, e.Address, e.DisplayFormat.ToString())).ToList();
        }).ConfigureAwait(false);

        var entries = Json.Deserialize<List<WatchEntry>>(json) ?? new List<WatchEntry>();
        if (!monitor)
        {
            return Json.Serialize(new { table, entries });
        }

        return await WebAsync(async () =>
        {
            var client = webApi.Get(ResolveWebPlc(webPlc, plc));
            var readable = entries.Where(e => !string.IsNullOrWhiteSpace(e.Name)).ToList();
            var calls = await client.TryCallManyAsync(
                readable.Select(e => new WebApiCall("PlcProgram.Read", new JsonObject { ["var"] = e.Name, ["mode"] = "simple" })).ToList(),
                cancellationToken).ConfigureAwait(false);
            var values = readable.Select((e, i) => (e, i)).ToDictionary(x => x.e, x => calls[x.i]);
            return new
            {
                table,
                plc = client.Name,
                entries = entries.Select(e => new
                {
                    name = e.Name,
                    address = e.Address,
                    displayFormat = e.DisplayFormat,
                    value = values.TryGetValue(e, out var r) ? r.Result : null,
                    error = values.TryGetValue(e, out var r2) ? r2.Error?.Message : "no symbolic name: not readable through the Web API",
                }).ToList(),
            };
        }).ConfigureAwait(false);
    }

    [McpServerTool(Name = "diag_db_snapshot", Destructive = false)]
    [Description("Takes a snapshot of the actual values of a data block from the CPU through TIA Portal (the PLC must be " +
                 "reachable online) and returns them. With create=false returns the last snapshot stored in the project. " +
                 "Works without the Web API. The snapshot is stored in the offline project; the PLC is not changed.")]
    public Task<string> DbSnapshot(
        [Description("Data block name or 'Group/Name' path.")] string block,
        [Description(TiaPlcParam)] string? plc = null,
        [Description("Take a new snapshot from the CPU (default) instead of reading the stored one.")] bool create = true,
        [Description("Also return the SimaticML export of the snapshot.")] bool includeXml = false,
        [Description("When taking a new snapshot, go online automatically if the PLC is offline, and back offline afterwards.")] bool autoOnline = true)
        => tia.RunAsync(() =>
        {
            var target = Locator.FindPlcRef(tia.Project, plc);
            var b = Locator.FindBlock((PlcSoftware)target.Software, block);
            if (b is not DataBlock)
            {
                throw new McpException($"'{block}' is a {b.GetType().Name}: snapshots are available for data blocks only.");
            }

            if (create)
            {
                // The ValueService exists only while the PLC is online, hence the online scope.
                OnlineScope.Run(target, autoOnline, "diag_db_snapshot", () =>
                {
                    var values = b.GetService<ValueService>()
                                 ?? throw new McpException(
                                     $"TIA Portal offers no snapshot of actual values for '{b.Name}' even online. Check that the " +
                                     "data block is downloaded and identical online and offline (plc_compare_online), or use " +
                                     "diag_read_variables through the Web API.");
                    values.CreateSnapshot();
                    return true;
                });
            }

            var snapshot = b.GetService<InterfaceSnapshot>() ?? throw new McpException("This data block has no snapshot service.");
            using var ws = new Workspace();
            var file = ws.FileOf(CodeTransfer.SafeFileName(b.Name) + ".snapshot.xml");
            snapshot.Export(file, ExportOptions.None);
            var xml = File.ReadAllText(file.FullName);
            return new { block = b.Name, values = ParseSnapshot(xml), xml = includeXml ? xml : null };
        });

    /// <summary>Extracts path/type/value triples from a snapshot export; falls back to every leaf element with text.</summary>
    internal static IReadOnlyList<object> ParseSnapshot(string xml)
    {
        var doc = XDocument.Parse(xml);
        var values = doc.Descendants()
            .Where(e => e.Name.LocalName == "Value" && e.Attribute("Path") is not null)
            .Select(e => (object)new { path = e.Attribute("Path")!.Value, type = e.Attribute("Type")?.Value, value = e.Value })
            .ToList();
        if (values.Count > 0)
        {
            return values;
        }

        return doc.Descendants()
            .Where(e => !e.HasElements && !string.IsNullOrWhiteSpace(e.Value) && e.Ancestors().Any(a => a.Name.LocalName.Contains("Snapshot")))
            .Select(e => (object)new
            {
                path = string.Join(".", e.AncestorsAndSelf().Reverse()
                    .Select(a => a.Attribute("Name")?.Value ?? a.Attribute("Path")?.Value)
                    .Where(n => !string.IsNullOrEmpty(n))),
                element = e.Name.LocalName,
                value = e.Value,
            })
            .ToList();
    }

    private string? ResolveWebPlc(string? webPlc, string? tiaPlc)
    {
        if (!string.IsNullOrWhiteSpace(webPlc))
        {
            return webPlc;
        }

        return !string.IsNullOrWhiteSpace(tiaPlc) && webApi.Endpoints.Keys.Any(k => Locator.NameEquals(k, tiaPlc!)) ? tiaPlc : null;
    }

    private static IEnumerable<(string Path, PlcWatchTable Table)> AllWatchTables(PlcWatchAndForceTableGroup group, string prefix = "")
    {
        foreach (var table in group.WatchTables)
        {
            yield return (Locator.Join(prefix, table.Name), table);
        }

        foreach (var sub in group.Groups)
        {
            foreach (var item in AllWatchTables(sub, Locator.Join(prefix, sub.Name)))
            {
                yield return item;
            }
        }
    }

    internal static string ParseChoice(string? value, string parameter, params string[] allowed)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? allowed[0] : value!.Trim().ToLowerInvariant();
        return allowed.Contains(normalized)
            ? normalized
            : throw new McpException($"Invalid {parameter} '{value}'. Allowed: {string.Join(", ", allowed)}");
    }

    /// <summary>Runs a Web API operation and maps connection and protocol errors to tool errors.</summary>
    internal static async Task<string> WebAsync(Func<Task<object?>> work)
    {
        try
        {
            return Json.Serialize(await work().ConfigureAwait(false));
        }
        catch (McpException)
        {
            throw;
        }
        catch (Exception ex) when (ex is WebApiException or InvalidOperationException or ArgumentException or FormatException)
        {
            throw new McpException(ex.Message, ex);
        }
    }

    private sealed record WatchEntry(string? Name, string? Address, string? DisplayFormat);
}
