using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using Talk2Tia.Core.WebApi;

namespace Talk2Tia.Core.Tools;

/// <summary>
/// Runtime actions on a running CPU through the Web API. Every tool changes the plant and requires confirm=true.
/// Excluded from the readonly profile.
/// </summary>
[McpServerToolType]
public sealed class DiagnosticsWriteTools(PlcWebApiRegistry webApi)
{
    private const string ConfirmParam = "Must be true: this acts on the running plant. Ask the user first.";

    [McpServerTool(Name = "diag_write_variables", Destructive = true)]
    [Description("Writes values to PLC variables of the running CPU (like 'Modify' in a watch table). This changes the plant: " +
                 "ALWAYS ask the user first and pass confirm=true. Variables use Web API syntax (\"DB name\".member).")]
    public Task<string> WriteVariables(
        [Description("Variables and values, e.g. {\"\\\"Motor_DB\\\".setpoint\": 1200, \"\\\"Motor_DB\\\".enable\": true}.")] Dictionary<string, JsonElement> values,
        [Description(DiagnosticsTools.WebPlcParam)] string? plc = null,
        [Description(ConfirmParam)] bool confirm = false,
        CancellationToken cancellationToken = default) =>
        DiagnosticsTools.WebAsync(async () =>
        {
            Guard.RequireConfirm(confirm, "diag_write_variables");
            if (values is null || values.Count == 0)
            {
                throw new McpException("Parameter 'values' needs at least one variable.");
            }

            var client = webApi.Get(plc);
            var items = values.ToList();
            var calls = await client.TryCallManyAsync(
                items.Select(v => new WebApiCall("PlcProgram.Write",
                    new JsonObject { ["var"] = v.Key, ["value"] = JsonNode.Parse(v.Value.GetRawText()), ["mode"] = "simple" })).ToList(),
                cancellationToken).ConfigureAwait(false);
            return new
            {
                plc = client.Name,
                results = items.Select((v, i) => new { variable = v.Key, written = calls[i].Error is null, error = calls[i].Error?.Message }).ToList(),
            };
        });

    [McpServerTool(Name = "diag_set_operating_mode", Destructive = true)]
    [Description("Requests a CPU operating mode change to run or stop. Stopping the CPU stops the machine: ALWAYS ask the user " +
                 "first and pass confirm=true.")]
    public Task<string> SetOperatingMode(
        [Description("run or stop.")] string mode,
        [Description(DiagnosticsTools.WebPlcParam)] string? plc = null,
        [Description(ConfirmParam)] bool confirm = false,
        CancellationToken cancellationToken = default) =>
        DiagnosticsTools.WebAsync(async () =>
        {
            Guard.RequireConfirm(confirm, "diag_set_operating_mode");
            var target = DiagnosticsTools.ParseChoice(Guard.RequireText(mode, "mode"), "mode", "run", "stop");
            var client = webApi.Get(plc);
            var result = await client.CallAsync("Plc.RequestChangeOperatingMode", new JsonObject { ["mode"] = target }, cancellationToken)
                .ConfigureAwait(false);
            var now = await client.CallAsync("Plc.ReadOperatingMode", null, cancellationToken).ConfigureAwait(false);
            return new { plc = client.Name, requested = target, accepted = result, operatingMode = now };
        });

    [McpServerTool(Name = "diag_acknowledge_alarm", Destructive = true)]
    [Description("Acknowledges an alarm of the CPU by id (from diag_alarms). Ask the user first and pass confirm=true.")]
    public Task<string> AcknowledgeAlarm(
        [Description("Alarm id as returned by diag_alarms.")] string alarmId,
        [Description(DiagnosticsTools.WebPlcParam)] string? plc = null,
        [Description(ConfirmParam)] bool confirm = false,
        CancellationToken cancellationToken = default) =>
        DiagnosticsTools.WebAsync(async () =>
        {
            Guard.RequireConfirm(confirm, "diag_acknowledge_alarm");
            var client = webApi.Get(plc);
            var result = await client.CallAsync("Alarms.Acknowledge", new JsonObject { ["id"] = Guard.RequireText(alarmId, "alarmId") }, cancellationToken)
                .ConfigureAwait(false);
            return new { plc = client.Name, alarmId, acknowledged = result };
        });

    [McpServerTool(Name = "diag_webapi_call", Destructive = true)]
    [Description("Calls any Web API method of the CPU (e.g. Modules.ReadStatus, Syslog.Browse, Plc.ReadSystemTime) for what has " +
                 "no dedicated tool. It can also change the CPU, so it always needs confirm=true: ask the user first. " +
                 "Use diag_webapi_methods to list the supported methods.")]
    public Task<string> Call(
        [Description("Method name, e.g. Modules.ReadStatus.")] string method,
        [Description("Method parameters as a JSON object, e.g. {\"hwid\": 49, \"language\": \"en-US\"}.")] JsonElement? parameters = null,
        [Description(DiagnosticsTools.WebPlcParam)] string? plc = null,
        [Description(ConfirmParam)] bool confirm = false,
        CancellationToken cancellationToken = default) =>
        DiagnosticsTools.WebAsync(async () =>
        {
            Guard.RequireConfirm(confirm, "diag_webapi_call");
            var name = Guard.RequireText(method, "method").Trim();
            if (name.StartsWith("Api.Login") || name.StartsWith("Api.Logout"))
            {
                throw new McpException("Login and logout are handled by Talk2Tia with the configured user.");
            }

            JsonNode? body = null;
            if (parameters is { ValueKind: not JsonValueKind.Null and not JsonValueKind.Undefined } p)
            {
                body = JsonNode.Parse(p.GetRawText()) as JsonObject ?? throw new McpException("'parameters' must be a JSON object.");
            }

            var client = webApi.Get(plc);
            return new { plc = client.Name, method = name, result = await client.CallAsync(name, body, cancellationToken).ConfigureAwait(false) };
        });
}
