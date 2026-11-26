using System;
using System.Linq;
using System.Text.Json;
using Talk2Tia.Core.Openness;
using Xunit;

namespace Talk2Tia.Tests;

[Collection(OpennessCollection.Name)]
public sealed class HelperTests
{
    [Theory]
    [InlineData("Main", null, "Main")]
    [InlineData("Motors/Conveyor/FB_Conveyor", "Motors/Conveyor", "FB_Conveyor")]
    [InlineData("/Motors/FB/", "Motors", "FB")]
    public void Paths_are_split_into_group_and_name(string path, string? group, string name)
    {
        Assert.Equal((group, name), Locator.SplitLast(path));
    }

    private sealed record Item(string Name);

    private static readonly (string Path, Item Item)[] Listing =
    {
        ("Main", new Item("Main")),
        ("Alarms/_MachineAlarms_IR/HP", new Item("_MachineAlarms_IR/HP")),
        ("Motors/Conveyor/FB_Conveyor", new Item("FB_Conveyor")),
        ("Motors/FB_Conveyor", new Item("FB_Conveyor")),
    };

    [Theory]
    [InlineData("Main", "Main")]
    [InlineData("_MachineAlarms_IR/HP", "Alarms/_MachineAlarms_IR/HP")] // name containing '/'
    [InlineData("Alarms/_MachineAlarms_IR/HP", "Alarms/_MachineAlarms_IR/HP")] // path as listed
    [InlineData("/alarms/_machinealarms_ir/hp", "Alarms/_MachineAlarms_IR/HP")]
    [InlineData("Motors/FB_Conveyor", "Motors/FB_Conveyor")] // exact path wins over the name elsewhere
    [InlineData("Conveyor/FB_Conveyor", "Motors/Conveyor/FB_Conveyor")] // trailing part of a path
    public void Names_with_slashes_and_paths_are_resolved(string input, string expectedPath)
    {
        var found = Locator.FindByPath(Listing, i => i.Name, input);
        Assert.Same(Listing.Single(e => e.Path == expectedPath).Item, found);
    }

    [Fact]
    public void Unknown_paths_return_null()
    {
        Assert.Null(Locator.FindByPath(Listing, i => i.Name, "Alarms/Missing"));
    }

    [Fact]
    public void Work_directory_can_be_configured()
    {
        try
        {
            Workspace.Configure(@"%TEMP%\t2t-work");
            Assert.Equal(System.IO.Path.GetFullPath(Environment.ExpandEnvironmentVariables(@"%TEMP%\t2t-work")), Workspace.Root);
            Workspace.Configure(null);
            Assert.Equal(Workspace.DefaultRoot(), Workspace.Root);
        }
        finally
        {
            Workspace.Configure(null);
        }
    }

    [Fact]
    public void Segments_ignore_empty_parts()
    {
        Assert.Equal(new[] { "A", "B" }, Locator.Segments("/A//B/ ").Where(s => s.Length > 0).ToArray());
        Assert.Empty(Locator.Segments(null));
    }

    [Fact]
    public void Object_paths_are_split()
    {
        Assert.Equal(
            new[] { "device:PLC_1", "DeviceItems[PLC_1]", "@SoftwareContainer", "Software" },
            ObjectPath.Split("device:PLC_1/DeviceItems[PLC_1]/@SoftwareContainer/Software").ToArray());
    }

    private enum Sample { Alpha, Beta }

    [Fact]
    public void Json_values_are_converted_to_attribute_types()
    {
        Assert.Equal(42, ValueFormatter.ConvertForAttribute(JsonDocument.Parse("42").RootElement, new[] { typeof(int) }));
        Assert.Equal(true, ValueFormatter.ConvertForAttribute(JsonDocument.Parse("true").RootElement, new[] { typeof(bool) }));
        Assert.Equal(Sample.Beta, ValueFormatter.ConvertForAttribute(JsonDocument.Parse("\"beta\"").RootElement, new[] { typeof(Sample) }));
        Assert.Equal(new Version(1, 2, 3), ValueFormatter.ConvertForAttribute(JsonDocument.Parse("\"1.2.3\"").RootElement, new[] { typeof(Version) }));
        Assert.Equal(1.5, ValueFormatter.ConvertForAttribute(JsonDocument.Parse("1.5").RootElement, new[] { typeof(double) }));
    }

    [Fact]
    public void Unconvertible_values_report_supported_types()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            ValueFormatter.ConvertForAttribute(JsonDocument.Parse("\"abc\"").RootElement, new[] { typeof(int) }));
        Assert.Contains("Int32", ex.Message);
    }

    [Fact]
    public void Formatter_handles_common_values()
    {
        Assert.Null(ValueFormatter.Format(null));
        Assert.Equal("Beta", ValueFormatter.Format(Sample.Beta));
        Assert.Null(ValueFormatter.Format(DateTime.MinValue));
        Assert.Equal("1.0", ValueFormatter.Format(new Version(1, 0)));
    }
}
