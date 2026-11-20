using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using ModelContextProtocol.Server;
using Talk2Tia.Core;
using Talk2Tia.Core.Openness;
using Xunit;

namespace Talk2Tia.Tests;

[Collection(OpennessCollection.Name)]
public sealed class ToolCatalogTests
{
    private static readonly Regex ToolName = new("^[a-z][a-z0-9_]{2,63}$");

    private static IEnumerable<(Type Type, MethodInfo Method, McpServerToolAttribute Attribute)> AllTools() =>
        ServerHost.Toolsets.Values.SelectMany(t => t).Distinct()
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                .Select(m => (t, m, m.GetCustomAttribute<McpServerToolAttribute>()))
                .Where(x => x.Item3 is not null)
                .Select(x => (x.t, x.m, x.Item3!)));

    [Fact]
    public void Every_tool_type_is_registered_in_a_toolset()
    {
        var registered = ServerHost.Toolsets.Values.SelectMany(t => t).ToHashSet();
        var declared = typeof(ServerHost).Assembly.GetTypes()
            .Where(t => t.GetCustomAttribute<McpServerToolTypeAttribute>() is not null);
        Assert.All(declared, t => Assert.Contains(t, registered));
    }

    [Fact]
    public void Tool_names_are_unique_and_well_formed()
    {
        var names = AllTools().Select(t => t.Attribute.Name ?? t.Method.Name).ToList();
        Assert.True(names.Count >= 90, $"Expected a broad catalog, found {names.Count} tools.");
        Assert.Empty(names.GroupBy(n => n).Where(g => g.Count() > 1).Select(g => g.Key));
        Assert.All(names, n => Assert.Matches(ToolName, n));
    }

    [Fact]
    public void Every_tool_and_parameter_is_described()
    {
        foreach (var (type, method, attribute) in AllTools())
        {
            var description = method.GetCustomAttribute<DescriptionAttribute>()?.Description;
            Assert.False(string.IsNullOrWhiteSpace(description), $"{attribute.Name} has no description");
            foreach (var parameter in method.GetParameters())
            {
                if (parameter.ParameterType == typeof(System.Threading.CancellationToken))
                {
                    continue;
                }

                Assert.True(parameter.GetCustomAttribute<DescriptionAttribute>() is not null,
                    $"{type.Name}.{method.Name}: parameter '{parameter.Name}' has no description");
            }
        }
    }

    [Fact]
    public void Destructive_tools_require_confirmation_or_overwrite()
    {
        var exempt = new HashSet<string> { "plc_generate_blocks_from_source", "obj_invoke" };
        foreach (var (_, method, attribute) in AllTools().Where(t => t.Attribute.Destructive && !t.Attribute.ReadOnly && !exempt.Contains(t.Attribute.Name!)))
        {
            var parameters = method.GetParameters().Select(p => p.Name).ToList();
            Assert.True(parameters.Contains("confirm") || parameters.Contains("overwrite"),
                $"{attribute.Name} is destructive but has neither 'confirm' nor 'overwrite'");
        }
    }

    [Fact]
    public void Toolset_selection_always_includes_portal()
    {
        var types = ServerHost.SelectToolTypes("plc").ToList();
        Assert.Contains(typeof(Talk2Tia.Core.Tools.PortalTools), types);
        Assert.Contains(typeof(Talk2Tia.Core.Tools.BlockTools), types);
        Assert.DoesNotContain(typeof(Talk2Tia.Core.Tools.LibraryTools), types);
    }

    [Fact]
    public void Unknown_toolset_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => ServerHost.SelectToolTypes("plc,nope").ToList());
    }

    [Fact]
    public void Openness_api_directory_is_found()
    {
        Assert.NotNull(OpennessResolver.LocatePublicApiDirectory());
    }
}
