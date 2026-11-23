using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using ModelContextProtocol.Server;

namespace Talk2Tia.Core.Configuration;

/// <summary>A tool method with its MCP metadata and toolset.</summary>
public sealed record ToolInfo(string Name, string Toolset, Type Type, MethodInfo Method, McpServerToolAttribute Attribute);

/// <summary>Decides which tools are exposed: toolsets, then profile, then allow, then deny (deny always wins).</summary>
public static class ToolPolicy
{
    /// <summary>
    /// Tools that are not annotated read-only but are still part of the readonly profile: they manage the session,
    /// the online connection or take a snapshot of actual values, without changing the program, the hardware
    /// configuration, the libraries or the PLC.
    /// </summary>
    public static readonly IReadOnlyCollection<string> ReadOnlyProfileExtras = new HashSet<string>(StringComparer.Ordinal)
    {
        "portal_attach",
        "portal_start",
        "portal_disconnect",
        "project_open",
        "project_select",
        "project_close",
        "lib_open_global",
        "online_configure",
        "online_go_online",
        "online_go_offline",
        "diag_db_snapshot",
    };

    /// <summary>Every tool of every toolset.</summary>
    public static IReadOnlyList<ToolInfo> Catalog { get; } = BuildCatalog();

    private static List<ToolInfo> BuildCatalog() =>
        ServerHost.Toolsets
            .SelectMany(set => set.Value.Select(type => (Toolset: set.Key, Type: type)))
            .SelectMany(x => x.Type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                .Select(m => (x.Toolset, x.Type, Method: m, Attribute: m.GetCustomAttribute<McpServerToolAttribute>()))
                .Where(m => m.Attribute is not null)
                .Select(m => new ToolInfo(m.Attribute!.Name ?? m.Method.Name, m.Toolset, m.Type, m.Method, m.Attribute!)))
            .ToList();

    public static bool IsInReadOnlyProfile(ToolInfo tool) =>
        tool.Attribute.ReadOnly || ReadOnlyProfileExtras.Contains(tool.Name);

    public static IReadOnlyList<ToolInfo> Select(Talk2TiaConfig config)
    {
        var toolsets = ResolveToolsets(config.Toolsets);
        var selected = new HashSet<ToolInfo>(Catalog.Where(t => toolsets.Contains(t.Toolset) && (!config.IsReadOnly || IsInReadOnlyProfile(t))));
        selected.UnionWith(Match(config.Allow, "allow"));
        selected.ExceptWith(Match(config.Deny, "deny"));
        return Catalog.Where(selected.Contains).ToList();
    }

    private static HashSet<string> ResolveToolsets(IReadOnlyCollection<string>? requested)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (requested is null || requested.Count == 0 || requested.Any(r => r.Trim().Equals("all", StringComparison.OrdinalIgnoreCase)))
        {
            names.UnionWith(ServerHost.Toolsets.Keys);
            return names;
        }

        var unknown = requested.Where(r => !ServerHost.Toolsets.ContainsKey(r.Trim())).ToList();
        if (unknown.Count > 0)
        {
            throw new ArgumentException(
                $"Unknown toolset(s): {string.Join(", ", unknown)}. Valid: {string.Join(", ", ServerHost.Toolsets.Keys)}");
        }

        names.UnionWith(requested.Select(r => r.Trim()));
        names.Add("portal"); // Always required to connect to TIA Portal.
        return names;
    }

    private static IEnumerable<ToolInfo> Match(IReadOnlyCollection<string>? patterns, string setting)
    {
        foreach (var pattern in patterns ?? Array.Empty<string>())
        {
            var regex = new Regex("^" + Regex.Escape(pattern.Trim()).Replace(@"\*", ".*") + "$", RegexOptions.IgnoreCase);
            var matches = Catalog.Where(t => regex.IsMatch(t.Name)).ToList();
            if (matches.Count == 0)
            {
                throw new ArgumentException($"'{pattern}' in '{setting}' matches no tool.");
            }

            foreach (var match in matches)
            {
                yield return match;
            }
        }
    }
}
