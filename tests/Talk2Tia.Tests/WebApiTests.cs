using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol;
using Talk2Tia.Core.Configuration;
using Talk2Tia.Core.Tools;
using Talk2Tia.Core.WebApi;
using Xunit;

namespace Talk2Tia.Tests;

/// <summary>Web API client against a fake CPU that answers JSON-RPC bulk requests.</summary>
public sealed class WebApiTests
{
    private sealed class FakeCpu : HttpMessageHandler
    {
        public List<(string? Token, JsonArray Body)> Requests { get; } = new();
        public Func<string, JsonNode?, string?, JsonObject> Answer { get; set; } = (_, _, _) => new JsonObject { ["result"] = true };

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var token = request.Headers.TryGetValues("X-Auth-Token", out var values) ? values.Single() : null;
            var body = (JsonArray)JsonNode.Parse(await request.Content!.ReadAsStringAsync().ConfigureAwait(false))!;
            Requests.Add((token, body));
            var response = new JsonArray();
            foreach (var call in body.OfType<JsonObject>())
            {
                var answer = Answer(call["method"]!.GetValue<string>(), call["params"], token);
                answer["jsonrpc"] = "2.0";
                answer["id"] = call["id"]!.GetValue<int>();
                response.Add(answer);
            }

            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(response.ToJsonString(), Encoding.UTF8, "application/json") };
        }
    }

    private static JsonObject Error(int code, string message) => new() { ["error"] = new JsonObject { ["code"] = code, ["message"] = message } };

    private static PlcWebApiClient Client(FakeCpu cpu, string? user = "talk2tia") =>
        new("PLC_1", new PlcEndpoint { Address = "192.168.0.1", Username = user, PasswordEnv = "PLC1_PWD" }, cpu,
            name => name == "PLC1_PWD" ? "secret" : null);

    [Fact]
    public async Task Logs_in_once_and_sends_the_token()
    {
        var cpu = new FakeCpu
        {
            Answer = (method, p, _) => method == "Api.Login"
                ? new JsonObject { ["result"] = new JsonObject { ["token"] = "T1" } }
                : new JsonObject { ["result"] = "run" },
        };
        using var client = Client(cpu);

        Assert.Equal("run", (await client.CallAsync("Plc.ReadOperatingMode"))!.GetValue<string>());
        await client.CallAsync("Plc.ReadOperatingMode");

        Assert.Equal(3, cpu.Requests.Count);
        Assert.Equal("Api.Login", cpu.Requests[0].Body[0]!["method"]!.GetValue<string>());
        Assert.Equal("secret", cpu.Requests[0].Body[0]!["params"]!["password"]!.GetValue<string>());
        Assert.Null(cpu.Requests[0].Token);
        Assert.Equal("T1", cpu.Requests[1].Token);
        Assert.Equal("T1", cpu.Requests[2].Token);
    }

    [Fact]
    public async Task Anonymous_access_sends_no_login()
    {
        var cpu = new FakeCpu();
        using var client = Client(cpu, user: null);
        await client.CallAsync("Api.Ping");
        Assert.Single(cpu.Requests);
        Assert.Null(cpu.Requests[0].Token);
    }

    [Fact]
    public async Task Bulk_results_are_matched_by_id_and_errors_are_reported_per_call()
    {
        var cpu = new FakeCpu
        {
            Answer = (method, p, _) => p?["var"]?.GetValue<string>() == "\"Missing\""
                ? Error(200, "Address does not exist")
                : new JsonObject { ["result"] = 42 },
        };
        using var client = Client(cpu, user: null);
        var results = await client.TryCallManyAsync(new[]
        {
            new WebApiCall("PlcProgram.Read", new JsonObject { ["var"] = "\"DB\".a" }),
            new WebApiCall("PlcProgram.Read", new JsonObject { ["var"] = "\"Missing\"" }),
        });

        Assert.Equal(42, results[0].Result!.GetValue<int>());
        Assert.Equal(200, results[1].Error!.Code);
        Assert.Single(cpu.Requests);
        Assert.Equal(2, cpu.Requests[0].Body.Count);
    }

    [Fact]
    public async Task Expired_token_triggers_one_new_login()
    {
        var logins = 0;
        var cpu = new FakeCpu();
        using var client = Client(cpu);
        cpu.Answer = (method, _, _) => method == "Api.Login"
            ? new JsonObject { ["result"] = new JsonObject { ["token"] = $"T{++logins}" } }
            : new JsonObject { ["result"] = "ok" };
        await client.CallAsync("Api.Ping"); // Login T1.

        cpu.Answer = (method, _, token) => method switch
        {
            "Api.Login" => new JsonObject { ["result"] = new JsonObject { ["token"] = $"T{++logins}" } },
            _ when token == "T1" => Error(WebApiException.PermissionDenied, "Permission denied"),
            _ => new JsonObject { ["result"] = "ok" },
        };
        Assert.Equal("ok", (await client.CallAsync("Api.Ping"))!.GetValue<string>());
        Assert.Equal(2, logins);
        Assert.Equal("T2", cpu.Requests.Last().Token);
    }

    [Fact]
    public async Task Missing_password_variable_is_a_clear_error()
    {
        using var client = new PlcWebApiClient("PLC_1",
            new PlcEndpoint { Address = "10.0.0.1", Username = "u", PasswordEnv = "NOT_SET" }, new FakeCpu(), _ => null);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => client.CallAsync("Api.Ping"));
        Assert.Contains("NOT_SET", ex.Message);
    }

    [Fact]
    public async Task Rpc_errors_explain_permissions_and_firmware()
    {
        var cpu = new FakeCpu { Answer = (method, _, _) => Error(WebApiException.MethodNotFound, "Method not found") };
        using var client = Client(cpu, user: null);
        var ex = await Assert.ThrowsAsync<WebApiException>(() => client.CallAsync("DiagnosticBuffer.Browse"));
        Assert.Contains("firmware", ex.Message);
    }

    private sealed class Unreachable : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("No connection could be made");
    }

    [Fact]
    public async Task Unreachable_plc_reports_what_to_check()
    {
        using var client = new PlcWebApiClient("PLC_1", new PlcEndpoint { Address = "10.0.0.1" }, new Unreachable());
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => client.CallAsync("Api.Ping"));
        Assert.Contains("web server", ex.Message);
        Assert.Contains("10.0.0.1", ex.Message);
    }

    [Fact]
    public void Certificate_validation_honours_pinning_and_explicit_opt_out()
    {
        var pinned = new PlcEndpoint { Address = "x", CertificateThumbprint = new string('A', 40) };
        Assert.False(PlcWebApiClient.ValidateCertificate(pinned, null, SslPolicyErrors.RemoteCertificateChainErrors));
        Assert.True(PlcWebApiClient.ValidateCertificate(new PlcEndpoint { Address = "x" }, null, SslPolicyErrors.None));
        Assert.False(PlcWebApiClient.ValidateCertificate(new PlcEndpoint { Address = "x" }, null, SslPolicyErrors.RemoteCertificateChainErrors));
        Assert.True(PlcWebApiClient.ValidateCertificate(new PlcEndpoint { Address = "x", AllowUntrustedCertificate = true }, null,
            SslPolicyErrors.RemoteCertificateChainErrors));
    }

    [Fact]
    public void Registry_resolves_the_only_plc_and_rejects_unknown_names()
    {
        var config = Talk2TiaConfig.Parse("{ \"plcs\": { \"Line1\": { \"address\": \"10.0.0.1\" } } }");
        using var registry = new PlcWebApiRegistry(config);
        Assert.Equal("Line1", registry.Get(null).Name);
        Assert.Equal("Line1", registry.Get("line1").Name);
        Assert.Throws<McpException>(() => registry.Get("Line2"));

        using var empty = new PlcWebApiRegistry(Talk2TiaConfig.Parse("{}"));
        Assert.Contains("plcs", Assert.Throws<McpException>(() => empty.Get(null)).Message);
    }

    [Fact]
    public void Snapshot_values_are_extracted()
    {
        const string xml = """
            <Document>
              <SW.Blocks.InterfaceSnapshot ID="0">
                <AttributeList>
                  <Snapshot>
                    <SnapshotValues xmlns="http://www.siemens.com/automation/Openness/SW/Interface/Snapshot/v1">
                      <Value Path="speed" Type="Real">12.5</Value>
                      <Value Path="axis.enabled" Type="Bool">TRUE</Value>
                    </SnapshotValues>
                  </Snapshot>
                </AttributeList>
              </SW.Blocks.InterfaceSnapshot>
            </Document>
            """;
        var values = DiagnosticsTools.ParseSnapshot(xml);
        Assert.Equal(2, values.Count);
        Assert.Contains("axis.enabled", System.Text.Json.JsonSerializer.Serialize(values));
    }
}
