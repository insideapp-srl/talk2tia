using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using Talk2Tia.Core.Openness;
using Talk2Tia.Core.Tools;

namespace Talk2Tia.Core;

public static class ServerHost
{
    public const string ServerName = "talk2tia";
    private const string ToolsetsEnv = "TALK2TIA_TOOLSETS";

    /// <summary>Tool classes grouped by toolset name. Selected with TALK2TIA_TOOLSETS (comma separated, default: all).</summary>
    public static readonly IReadOnlyDictionary<string, Type[]> Toolsets = new Dictionary<string, Type[]>(StringComparer.OrdinalIgnoreCase)
    {
        ["portal"] = new[] { typeof(PortalTools), typeof(ProjectTools) },
        ["hardware"] = new[] { typeof(HardwareTools) },
        ["plc"] = new[] { typeof(BlockTools), typeof(TypeTools), typeof(TagTools), typeof(ExternalSourceTools) },
        ["online"] = new[] { typeof(CompileTools), typeof(OnlineTools) },
        ["library"] = new[] { typeof(LibraryTools) },
        ["hmi"] = new[] { typeof(HmiTools) },
        ["object"] = new[] { typeof(ObjectModelTools) },
    };

    public static IEnumerable<Type> SelectToolTypes(string? setting)
    {
        if (string.IsNullOrWhiteSpace(setting) || setting!.Trim().Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            return Toolsets.Values.SelectMany(t => t).Distinct();
        }

        var requested = setting.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToList();
        var unknown = requested.Where(r => !Toolsets.ContainsKey(r)).ToList();
        if (unknown.Count > 0)
        {
            throw new ArgumentException(
                $"Unknown toolset(s) in {ToolsetsEnv}: {string.Join(", ", unknown)}. Valid: {string.Join(", ", Toolsets.Keys)}");
        }

        // "portal" is always required to connect to TIA Portal.
        return requested.Append("portal").Distinct(StringComparer.OrdinalIgnoreCase).SelectMany(r => Toolsets[r]).Distinct();
    }

    public static async Task<int> RunAsync(string[] args)
    {
        Workspace.CleanupStale();

        var builder = Host.CreateApplicationBuilder(args);
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);
        builder.Logging.SetMinimumLevel(LogLevel.Information);

        AddTalk2Tia(builder.Services, Environment.GetEnvironmentVariable(ToolsetsEnv)).WithStdioServerTransport();

        using var host = builder.Build();
        await host.RunAsync().ConfigureAwait(false);
        return 0;
    }

    /// <summary>Registers the Talk2Tia MCP server (session + selected toolsets). The caller adds the transport.</summary>
    public static IMcpServerBuilder AddTalk2Tia(IServiceCollection services, string? toolsets)
    {
        services.AddSingleton<TiaSession>();
        var mcp = services.AddMcpServer(options =>
        {
            options.ServerInfo = new Implementation
            {
                Name = ServerName,
                Title = "Talk2Tia — MCP server for Siemens TIA Portal V21 (Openness)",
                Version = typeof(ServerHost).Assembly.GetName().Version?.ToString(3) ?? "0.0.0",
            };
            options.ServerInstructions = Instructions.Text;
        });

        // Explicit IEnumerable<Type>: a Type[] argument would bind to the generic WithTools<T>(T target) overload
        // and register nothing.
        IEnumerable<Type> toolTypes = SelectToolTypes(toolsets).ToList();
        return mcp.WithTools(toolTypes);
    }
}
