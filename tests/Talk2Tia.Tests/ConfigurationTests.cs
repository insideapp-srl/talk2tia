using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Talk2Tia.Core;
using Talk2Tia.Core.Configuration;
using Xunit;

namespace Talk2Tia.Tests;

[Collection(OpennessCollection.Name)]
public sealed class ConfigurationTests
{
    private static Func<string, string?> Env(params (string Key, string Value)[] values)
    {
        var map = values.ToDictionary(v => v.Key, v => v.Value);
        return key => map.TryGetValue(key, out var value) ? value : null;
    }

    private static List<string> Names(string json) => ToolPolicy.Select(Talk2TiaConfig.Parse(json)).Select(t => t.Name).ToList();

    [Fact]
    public void Default_configuration_exposes_every_tool()
    {
        var config = Talk2TiaConfig.Parse("{}");
        Assert.Equal(Talk2TiaConfig.ProfileFull, config.Profile);
        Assert.Equal(ToolPolicy.Catalog.Count, ToolPolicy.Select(config).Count);
    }

    [Fact]
    public void Configuration_accepts_comments_and_trailing_commas()
    {
        var config = Talk2TiaConfig.Parse("""
            {
              // only reading
              "profile": "ReadOnly",
              "deny": ["obj_*",],
            }
            """);
        Assert.True(config.IsReadOnly);
    }

    [Theory]
    [InlineData("{ \"profil\": \"readonly\" }")]
    [InlineData("{ \"profile\": \"admin\" }")]
    [InlineData("{ \"plcs\": { \"PLC_1\": { } } }")]
    [InlineData("{ \"plcs\": { \"PLC_1\": { \"address\": \"10.0.0.1\", \"username\": \"u\" } } }")]
    [InlineData("{ \"plcs\": { \"PLC_1\": { \"address\": \"10.0.0.1\", \"certificateThumbprint\": \"zz\" } } }")]
    [InlineData("{ \"plcs\": { \"PLC_1\": { \"address\": \"10.0.0.1\", \"password\": \"secret\" } } }")]
    public void Invalid_configuration_is_rejected(string json)
    {
        Assert.Throws<ArgumentException>(() => Talk2TiaConfig.Parse(json));
    }

    [Fact]
    public void Thumbprint_is_normalized()
    {
        var config = Talk2TiaConfig.Parse(
            "{ \"plcs\": { \"PLC_1\": { \"address\": \"10.0.0.1\", \"certificateThumbprint\": \"ab:cd:ef:01:23:45:67:89:ab:cd:ef:01:23:45:67:89:ab:cd:ef:01\" } } }");
        Assert.Equal("ABCDEF0123456789ABCDEF0123456789ABCDEF01", config.Plcs!["PLC_1"].CertificateThumbprint);
    }

    [Fact]
    public void Readonly_profile_keeps_reads_and_session_but_drops_every_change()
    {
        var names = Names("{ \"profile\": \"readonly\" }");
        Assert.Contains("plc_read_block", names);
        Assert.Contains("portal_attach", names);
        Assert.Contains("project_open", names);
        Assert.Contains("lib_open_global", names);
        Assert.DoesNotContain("lib_close_global", names);
        Assert.Contains("diag_read_variables", names);
        Assert.Contains("diag_db_snapshot", names);
        Assert.Contains("plc_compare_online", names);

        foreach (var forbidden in new[]
                 {
                     "plc_write_scl", "plc_download", "plc_delete_block", "plc_upsert_tags", "project_save", "plc_compile",
                     "lib_type_release", "obj_set", "obj_invoke", "obj_create", "hw_create_device",
                     "diag_write_variables", "diag_set_operating_mode", "diag_acknowledge_alarm", "diag_webapi_call",
                 })
        {
            Assert.DoesNotContain(forbidden, names);
        }
    }

    [Fact]
    public void Readonly_profile_only_contains_readonly_tools_or_declared_extras()
    {
        var tools = ToolPolicy.Select(Talk2TiaConfig.Parse("{ \"profile\": \"readonly\" }"));
        Assert.All(tools, t => Assert.True(t.Attribute.ReadOnly || ToolPolicy.ReadOnlyProfileExtras.Contains(t.Name), t.Name));
    }

    [Fact]
    public void Readonly_extras_exist_in_the_catalog()
    {
        var names = ToolPolicy.Catalog.Select(t => t.Name).ToHashSet();
        Assert.All(ToolPolicy.ReadOnlyProfileExtras, n => Assert.Contains(n, names));
    }

    [Fact]
    public void Allow_adds_and_deny_removes_with_wildcards()
    {
        var names = Names("{ \"profile\": \"readonly\", \"allow\": [\"plc_compile\"], \"deny\": [\"diag_*\", \"plc_compile\"] }");
        Assert.DoesNotContain("plc_compile", names); // Deny wins over allow.
        Assert.DoesNotContain(names, n => n.StartsWith("diag_", StringComparison.Ordinal));

        names = Names("{ \"profile\": \"readonly\", \"allow\": [\"plc_compile\"] }");
        Assert.Contains("plc_compile", names);
    }

    [Fact]
    public void Allow_can_add_a_tool_outside_the_selected_toolsets()
    {
        var names = Names("{ \"toolsets\": [\"diagnostics\"], \"allow\": [\"plc_read_block\"] }");
        Assert.Contains("plc_read_block", names);
        Assert.Contains("diag_alarms", names);
        Assert.Contains("portal_attach", names);
        Assert.DoesNotContain("plc_write_scl", names);
        Assert.DoesNotContain("diag_write_variables", names);
    }

    [Fact]
    public void Patterns_that_match_nothing_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => ToolPolicy.Select(Talk2TiaConfig.Parse("{ \"deny\": [\"plc_wrte_scl\"] }")));
    }

    [Fact]
    public void Environment_overrides_the_file()
    {
        var path = Path.Combine(Path.GetTempPath(), $"talk2tia-test-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, "{ \"profile\": \"full\", \"toolsets\": [\"library\"] }");
        try
        {
            var config = Talk2TiaConfig.Load(null, Env(
                (Talk2TiaConfig.ConfigEnv, path),
                (Talk2TiaConfig.ReadOnlyEnv, "1"),
                (Talk2TiaConfig.ToolsetsEnv, "plc,diagnostics")));
            Assert.Equal(path, config.Source);
            Assert.True(config.IsReadOnly);
            Assert.Equal(new[] { "plc", "diagnostics" }, config.Toolsets);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Explicit_path_wins_and_missing_file_is_an_error()
    {
        Assert.Throws<ArgumentException>(() => Talk2TiaConfig.Load(@"C:\does\not\exist\talk2tia.json", Env()));
        Assert.Throws<ArgumentException>(() => Talk2TiaConfig.Load(null, Env((Talk2TiaConfig.ReadOnlyEnv, "maybe"))));
    }

    [Fact]
    public void Command_line_options_are_extracted()
    {
        var options = CommandLine.Parse(new[] { "--config", "c.json", "--read-only", "--other", "x" });
        Assert.Equal("c.json", options.ConfigPath);
        Assert.True(options.ReadOnly);
        Assert.Equal(new[] { "--other", "x" }, options.HostArgs);
        Assert.Equal("d.json", CommandLine.Parse(new[] { "--config=d.json" }).ConfigPath);
    }
}
