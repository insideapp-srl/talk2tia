using System;
using System.Text.Json;
using ModelContextProtocol;
using Talk2Tia.Core.Tools;
using Xunit;

namespace Talk2Tia.Tests;

[Collection(OpennessCollection.Name)]
public sealed class FeedbackTests
{
    private static readonly (string, string)[] Env = { ("Talk2Tia", "1.0.0"), ("Windows", "10.0") };

    private static JsonElement Build(string kind, string description = "Steps: call it.", string? error = null) =>
        JsonSerializer.SerializeToElement(FeedbackReport.Build(kind, "plc_read_block fails", description, "plc_read_block", error, "Claude desktop", Env, connected: true));

    [Fact]
    public void Report_contains_environment_and_prefilled_links()
    {
        var r = Build("bug", error: "The specified directory does not exist");
        Assert.Equal("[Bug] plc_read_block fails", r.GetProperty("title").GetString());
        var md = r.GetProperty("markdown").GetString()!;
        Assert.Contains("### What happened", md);
        Assert.Contains("`plc_read_block`", md);
        Assert.Contains("- Talk2Tia: 1.0.0", md);
        Assert.Contains("- MCP client: Claude desktop", md);
        var issue = r.GetProperty("githubIssueUrl").GetString()!;
        Assert.StartsWith(FeedbackReport.IssuesUrl + "?title=%5BBug%5D%20plc_read_block%20fails&labels=bug&body=", issue);
        Assert.StartsWith("mailto:" + FeedbackReport.SupportEmail + "?subject=Talk2Tia%3A%20", r.GetProperty("mailtoUrl").GetString());
    }

    [Theory]
    [InlineData("improvement", "enhancement")]
    [InlineData("Feature", "enhancement")]
    [InlineData("question", "question")]
    public void Kinds_map_to_issue_labels(string kind, string label)
    {
        Assert.Equal(label, Build(kind).GetProperty("label").GetString());
    }

    [Fact]
    public void Long_reports_are_truncated_in_the_mail_link()
    {
        var r = Build("bug", description: new string('x', 5000));
        var mail = Uri.UnescapeDataString(r.GetProperty("mailtoUrl").GetString()!);
        Assert.Contains("[truncated: paste the full report]", mail);
        Assert.Contains(new string('x', 5000), r.GetProperty("markdown").GetString());
    }

    [Fact]
    public void Unknown_kind_is_rejected()
    {
        Assert.Throws<McpException>(() => Build("rant"));
    }
}
