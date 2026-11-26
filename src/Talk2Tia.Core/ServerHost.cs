using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Talk2Tia.Core.Configuration;
using Talk2Tia.Core.Openness;
using Talk2Tia.Core.Tools;
using Talk2Tia.Core.WebApi;

namespace Talk2Tia.Core;

public static class ServerHost
{
    public const string ServerName = "talk2tia";

    /// <summary>Tool classes grouped by toolset name. Selected with the configuration file or TALK2TIA_TOOLSETS (default: all).</summary>
    public static readonly IReadOnlyDictionary<string, Type[]> Toolsets = new Dictionary<string, Type[]>(StringComparer.OrdinalIgnoreCase)
    {
        ["portal"] = new[] { typeof(PortalTools), typeof(ProjectTools), typeof(FeedbackTools) },
        ["hardware"] = new[] { typeof(HardwareTools) },
        ["plc"] = new[] { typeof(BlockTools), typeof(TypeTools), typeof(TagTools), typeof(ExternalSourceTools) },
        ["online"] = new[] { typeof(CompileTools), typeof(OnlineTools) },
        ["diagnostics"] = new[] { typeof(DiagnosticsTools) },
        ["diagnostics_write"] = new[] { typeof(DiagnosticsWriteTools) },
        ["library"] = new[] { typeof(LibraryTools) },
        ["hmi"] = new[] { typeof(HmiTools) },
        ["object"] = new[] { typeof(ObjectModelTools) },
    };

    public static async Task<int> RunAsync(string[] args)
    {
        var options = CommandLine.Parse(args);
        var config = Talk2TiaConfig.Load(options.ConfigPath);
        Workspace.Configure(config.WorkDirectory);
        Workspace.CleanupStale();
        if (options.ReadOnly)
        {
            config.Profile = Talk2TiaConfig.ProfileReadOnly;
        }

        var tools = ToolPolicy.Select(config);

        var builder = Host.CreateApplicationBuilder(options.HostArgs);
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);
        builder.Logging.SetMinimumLevel(LogLevel.Information);

        AddTalk2Tia(builder.Services, config, tools).WithStdioServerTransport();

        using var host = builder.Build();
        host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("talk2tia").LogInformation(
            "Configuration: {Source}; profile {Profile}; {Count} tools; {Plcs} PLC(s) for the Web API",
            config.Source ?? "none", config.Profile, tools.Count, config.Plcs?.Count ?? 0);
        await host.RunAsync().ConfigureAwait(false);
        return 0;
    }

    /// <summary>Registers the Talk2Tia MCP server with the given toolsets (comma separated, null = all). The caller adds the transport.</summary>
    public static IMcpServerBuilder AddTalk2Tia(IServiceCollection services, string? toolsets)
    {
        var config = Talk2TiaConfig.Parse("{}");
        config.ApplyEnvironment(name => name == Talk2TiaConfig.ToolsetsEnv ? toolsets : null);
        return AddTalk2Tia(services, config, ToolPolicy.Select(config));
    }

    /// <summary>Registers the Talk2Tia MCP server (session, Web API clients and the selected tools). The caller adds the transport.</summary>
    public static IMcpServerBuilder AddTalk2Tia(IServiceCollection services, Talk2TiaConfig config, IReadOnlyList<ToolInfo> tools)
    {
        services.AddSingleton(config);
        services.AddSingleton<TiaSession>();
        services.AddSingleton(sp => new PlcWebApiRegistry(sp.GetRequiredService<Talk2TiaConfig>()));
        var mcp = services.AddMcpServer(options =>
        {
            options.ServerInfo = new Implementation
            {
                Name = ServerName,
                Title = "Talk2Tia — MCP server for Siemens TIA Portal V21 (Openness)",
                Version = typeof(ServerHost).Assembly.GetName().Version?.ToString(3) ?? "0.0.0",
            };
            options.ServerInstructions = Instructions.For(config);
        });

        // One registration per tool (instead of per class) so that single tools can be allowed or denied.
        foreach (var tool in tools)
        {
            var type = tool.Type;
            var method = tool.Method;
            services.AddSingleton(sp => McpServerTool.Create(
                method,
                (Func<RequestContext<CallToolRequestParams>, object>)(ctx => ActivatorUtilities.CreateInstance(ctx.Services ?? sp, type)),
                new McpServerToolCreateOptions { Services = sp }));
        }

        return mcp;
    }
}

/// <summary>Talk2Tia command line options; the remaining arguments go to the generic host.</summary>
public sealed record CommandLine(string? ConfigPath, bool ReadOnly, string[] HostArgs)
{
    public static CommandLine Parse(string[] args)
    {
        string? config = null;
        var readOnly = false;
        var rest = new List<string>();
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--config":
                    if (i + 1 >= args.Length)
                    {
                        throw new ArgumentException("--config needs a file path.");
                    }

                    config = args[++i];
                    break;
                case "--read-only":
                case "--readonly":
                    readOnly = true;
                    break;
                default:
                    if (args[i].StartsWith("--config=", StringComparison.Ordinal))
                    {
                        config = args[i].Substring("--config=".Length);
                    }
                    else
                    {
                        rest.Add(args[i]);
                    }

                    break;
            }
        }

        return new CommandLine(config, readOnly, rest.ToArray());
    }
}
