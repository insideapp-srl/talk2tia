using System;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Talk2Tia.Core;
using Talk2Tia.Core.Configuration;
using Xunit;

namespace Talk2Tia.Tests;

/// <summary>End-to-end MCP checks: a real MCP client talks to the server over in-memory streams.</summary>
[Collection(OpennessCollection.Name)]
public sealed class McpProtocolTests
{
    private static async Task<(McpClient Client, ServiceProvider Provider, CancellationTokenSource Cts)> StartAsync(string? toolsets = null)
    {
        var clientToServer = new Pipe();
        var serverToClient = new Pipe();
        var services = new ServiceCollection();
        services.AddLogging();
        ServerHost.AddTalk2Tia(services, toolsets)
            .WithStreamServerTransport(clientToServer.Reader.AsStream(), serverToClient.Writer.AsStream());
        var provider = services.BuildServiceProvider();
        var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var server = provider.GetRequiredService<McpServer>();
        _ = server.RunAsync(cts.Token);
        var client = await McpClient.CreateAsync(
            new StreamClientTransport(clientToServer.Writer.AsStream(), serverToClient.Reader.AsStream()),
            cancellationToken: cts.Token);
        return (client, provider, cts);
    }

    [Fact]
    public async Task Server_advertises_identity_instructions_and_all_tools()
    {
        var (client, provider, cts) = await StartAsync();
        try
        {
            Assert.Equal("talk2tia", client.ServerInfo?.Name);
            Assert.Contains("TIA Portal", client.ServerInstructions ?? string.Empty);

            var tools = await client.ListToolsAsync(cancellationToken: cts.Token);
            Assert.True(tools.Count >= 90, $"Only {tools.Count} tools advertised.");

            foreach (var tool in tools)
            {
                Assert.False(string.IsNullOrWhiteSpace(tool.Description), $"{tool.Name} has no description");
                Assert.Equal(JsonValueKind.Object, tool.JsonSchema.ValueKind);
            }

            var upsert = tools.Single(t => t.Name == "plc_upsert_tags");
            var schema = upsert.JsonSchema.GetRawText();
            Assert.Contains("\"tags\"", schema);
            Assert.Contains("\"array\"", schema);

            var download = tools.Single(t => t.Name == "plc_download").ProtocolTool.Annotations;
            Assert.True(download?.DestructiveHint);
            Assert.True(tools.Single(t => t.Name == "plc_list_blocks").ProtocolTool.Annotations?.ReadOnlyHint);
        }
        finally
        {
            await client.DisposeAsync();
            cts.Cancel();
            await provider.DisposeAsync();
        }
    }

    [Fact]
    public async Task Toolset_filter_limits_advertised_tools()
    {
        var (client, provider, cts) = await StartAsync("library");
        try
        {
            var names = (await client.ListToolsAsync(cancellationToken: cts.Token)).Select(t => t.Name).ToList();
            Assert.Contains("lib_browse", names);
            Assert.Contains("portal_attach", names);
            Assert.DoesNotContain("plc_write_scl", names);
        }
        finally
        {
            await client.DisposeAsync();
            cts.Cancel();
            await provider.DisposeAsync();
        }
    }

    [Fact]
    public async Task Status_and_instance_listing_work_against_openness()
    {
        var (client, provider, cts) = await StartAsync();
        try
        {
            var status = await client.CallToolAsync("portal_status", new Dictionary<string, object?>(), cancellationToken: cts.Token);
            Assert.NotEqual(true, status.IsError);
            Assert.Contains("\"connected\":false", Text(status));

            // Real Openness call: enumerates running TIA Portal processes (no attach, no dialog).
            var instances = await client.CallToolAsync("portal_list_instances", new Dictionary<string, object?>(), cancellationToken: cts.Token);
            Assert.NotEqual(true, instances.IsError);
            Assert.StartsWith("[", Text(instances));
        }
        finally
        {
            await client.DisposeAsync();
            cts.Cancel();
            await provider.DisposeAsync();
        }
    }

    [Fact]
    public async Task Tools_report_a_clear_error_when_not_connected()
    {
        var (client, provider, cts) = await StartAsync();
        try
        {
            var result = await client.CallToolAsync("plc_list_blocks", new Dictionary<string, object?>(), cancellationToken: cts.Token);
            Assert.True(result.IsError);
            Assert.Contains("portal_attach", Text(result));

            var guarded = await client.CallToolAsync("plc_delete_block", new Dictionary<string, object?> { ["block"] = "Main" }, cancellationToken: cts.Token);
            Assert.True(guarded.IsError);
            Assert.Contains("confirm=true", Text(guarded));
        }
        finally
        {
            await client.DisposeAsync();
            cts.Cancel();
            await provider.DisposeAsync();
        }
    }

    [Fact]
    public async Task Readonly_profile_hides_write_tools_and_says_so()
    {
        var clientToServer = new Pipe();
        var serverToClient = new Pipe();
        var services = new ServiceCollection();
        services.AddLogging();
        var config = Talk2TiaConfig.Parse("{ \"profile\": \"readonly\" }");
        ServerHost.AddTalk2Tia(services, config, ToolPolicy.Select(config))
            .WithStreamServerTransport(clientToServer.Reader.AsStream(), serverToClient.Writer.AsStream());
        var provider = services.BuildServiceProvider();
        var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        _ = provider.GetRequiredService<McpServer>().RunAsync(cts.Token);
        var client = await McpClient.CreateAsync(
            new StreamClientTransport(clientToServer.Writer.AsStream(), serverToClient.Reader.AsStream()),
            cancellationToken: cts.Token);
        try
        {
            Assert.Contains("readonly profile", client.ServerInstructions ?? string.Empty);
            var names = (await client.ListToolsAsync(cancellationToken: cts.Token)).Select(t => t.Name).ToList();
            Assert.Contains("plc_read_block", names);
            Assert.Contains("diag_alarms", names);
            Assert.DoesNotContain("plc_write_scl", names);
            Assert.DoesNotContain("diag_write_variables", names);

            // Diagnostics without a configured PLC explain how to configure one.
            var result = await client.CallToolAsync("diag_cpu_state", new Dictionary<string, object?>(), cancellationToken: cts.Token);
            Assert.True(result.IsError);
            Assert.Contains("plcs", Text(result));
        }
        finally
        {
            await client.DisposeAsync();
            cts.Cancel();
            await provider.DisposeAsync();
        }
    }

    private static string Text(CallToolResult result) =>
        string.Join("\n", result.Content.OfType<TextContentBlock>().Select(c => c.Text));
}
