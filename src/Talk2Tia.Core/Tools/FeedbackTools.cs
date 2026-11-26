using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using Talk2Tia.Core.Configuration;
using Talk2Tia.Core.Openness;

namespace Talk2Tia.Core.Tools;

/// <summary>Prepares bug reports and improvement requests for the Talk2Tia maintainers.</summary>
[McpServerToolType]
public sealed class FeedbackTools(TiaSession tia, Talk2TiaConfig config)
{
    [McpServerTool(Name = "portal_feedback_report", ReadOnly = true, Idempotent = true)]
    [Description("Prepares a bug report or improvement request for the Talk2Tia maintainers: a Markdown report with the " +
                 "environment (Talk2Tia and TIA Portal versions, Windows, configuration), a prefilled GitHub issue link and " +
                 "an e-mail link to " + FeedbackReport.SupportEmail + ". Nothing is sent: show the draft to the user, who " +
                 "chooses whether and how to send it. Never put project content (code, names, IPs) in it without the user's consent.")]
    public Task<string> Report(
        [Description("bug | improvement | question")] string kind,
        [Description("Short title, e.g. 'plc_read_block fails on names with /'.")] string title,
        [Description("What happened or what is missing, steps to reproduce, expected result, workaround used.")] string description,
        [Description("Tool involved, e.g. plc_read_block. Optional.")] string? tool = null,
        [Description("Error message returned by the tool, without project content the user did not approve. Optional.")] string? error = null,
        [Description("MCP client and version, e.g. 'Claude desktop 1.2 (MSIX)'. Optional.")] string? client = null)
        => tia.RunAsync(() => FeedbackReport.Build(kind, title, description, tool, error, client, Environment(), tia.IsConnected));

    private IReadOnlyList<(string Name, string Value)> Environment() => new List<(string, string)>
    {
        ("Talk2Tia", FeedbackReport.Talk2TiaVersion),
        ("TIA Portal", "V21, Openness API " + (OpennessResolver.PublicApiDirectory is null ? "not found" : "found")),
        ("Windows", System.Environment.OSVersion.VersionString + (System.Environment.Is64BitOperatingSystem ? " x64" : string.Empty)),
        ("MSIX client", Workspace.RunsInPackage() ? "yes" : "no"),
        ("Profile", config.Profile ?? Talk2TiaConfig.ProfileFull),
        ("Toolsets", config.Toolsets is { Count: > 0 } t ? string.Join(", ", t) : "all"),
        ("Web API PLCs configured", (config.Plcs?.Count ?? 0).ToString()),
    };
}

internal static class FeedbackReport
{
    public const string SupportEmail = "support@insideapp.help";
    public const string IssuesUrl = "https://github.com/insideapp-srl/talk2tia/issues/new";
    private const int MaxIssueBody = 6000;
    private const int MaxMailBody = 1500;

    public static string Talk2TiaVersion
    {
        get
        {
            var assembly = typeof(FeedbackReport).Assembly;
            var info = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            return (info ?? assembly.GetName().Version?.ToString() ?? "unknown").Split('+')[0];
        }
    }

    public static object Build(
        string kind, string title, string description, string? tool, string? error, string? client,
        IReadOnlyList<(string Name, string Value)> environment, bool connected)
    {
        var label = kind.Trim().ToLowerInvariant() switch
        {
            "bug" => "bug",
            "improvement" or "enhancement" or "feature" => "enhancement",
            "question" => "question",
            _ => throw new McpException($"Unknown kind '{kind}'. Use bug, improvement or question."),
        };
        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(description))
        {
            throw new McpException("title and description are required.");
        }

        var prefix = label switch { "bug" => "[Bug] ", "enhancement" => "[Improvement] ", _ => "[Question] " };
        var fullTitle = title.Trim().StartsWith("[", StringComparison.Ordinal) ? title.Trim() : prefix + title.Trim();

        var body = new StringBuilder();
        body.AppendLine(label == "enhancement" ? "### Proposal" : "### What happened").AppendLine().AppendLine(description.Trim()).AppendLine();
        if (!string.IsNullOrWhiteSpace(tool))
        {
            body.AppendLine("### Tool").AppendLine().AppendLine($"`{tool!.Trim()}`").AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(error))
        {
            body.AppendLine("### Error").AppendLine().AppendLine("```text").AppendLine(error!.Trim()).AppendLine("```").AppendLine();
        }

        body.AppendLine("### Environment").AppendLine();
        foreach (var (name, value) in environment)
        {
            body.AppendLine($"- {name}: {value}");
        }

        if (!string.IsNullOrWhiteSpace(client))
        {
            body.AppendLine($"- MCP client: {client!.Trim()}");
        }

        body.AppendLine($"- Connected to TIA Portal: {(connected ? "yes" : "no")}");
        body.AppendLine().AppendLine("_Prepared with portal_feedback_report._");
        var markdown = body.ToString();

        var issueUrl = $"{IssuesUrl}?title={Uri.EscapeDataString(fullTitle)}&labels={label}&body={Uri.EscapeDataString(Truncate(markdown, MaxIssueBody))}";
        var mailUrl = $"mailto:{SupportEmail}?subject={Uri.EscapeDataString("Talk2Tia: " + fullTitle)}&body={Uri.EscapeDataString(Truncate(markdown, MaxMailBody))}";

        return new
        {
            title = fullTitle,
            label,
            markdown,
            githubIssueUrl = issueUrl,
            mailtoUrl = mailUrl,
            email = SupportEmail,
            next = "Show the title and the Markdown to the user and ask how to send it: open githubIssueUrl (GitHub account " +
                   "needed; or `gh issue create` if available), or e-mail " + SupportEmail + " (mailtoUrl, or paste the Markdown). " +
                   "Remove anything the user does not want to share.",
        };
    }

    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : text.Substring(0, max) + "\n\n[truncated: paste the full report]";
}
