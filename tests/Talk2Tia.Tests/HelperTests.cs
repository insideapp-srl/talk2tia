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
