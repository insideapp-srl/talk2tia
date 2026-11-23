using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Talk2Tia.Core.Diff;

/// <summary>The text form of a block used for diffs: SIMATIC SD documents (LAD/FBD) or source text (SCL, DB, UDT, XML).</summary>
public sealed record BlockText(string Name, string Kind, string Content, string? Resources = null)
{
    public const string KindDocument = "document";
    public bool IsDocument => Kind == KindDocument;
}

/// <summary>Builds the Markdown shown to the user after a change: unified diffs for text, before/after drawings for LAD.</summary>
public static class BlockDiff
{
    public const int MaxChars = 60_000;

    /// <summary>Returns the Markdown diff between two versions of a block, or null when nothing changed.</summary>
    public static string? Describe(BlockText? before, BlockText after)
    {
        if (before is null)
        {
            return $"**`{after.Name}`** — new block.";
        }

        var markdown = before.IsDocument && after.IsDocument
            ? Documents(after.Name, before, after)
            : Text(after.Name, before.Content, after.Content, before.IsDocument || after.IsDocument ? "s7dcl" : after.Kind);
        if (markdown is null)
        {
            return null;
        }

        return markdown.Length <= MaxChars
            ? markdown
            : markdown.Substring(0, MaxChars) + "\n\n… (diff truncated: ask for single networks with plc_render_block)";
    }

    /// <summary>Unified diff of two texts as a ```diff block.</summary>
    public static string? Text(string name, string before, string after, string kind)
    {
        var a = TextDiff.SplitLines(before);
        var b = TextDiff.SplitLines(after);
        var unified = TextDiff.Unified(a, b);
        if (unified.Length == 0)
        {
            return null;
        }

        var (added, removed) = TextDiff.Stats(a, b);
        return $"**`{name}`** ({kind}) — +{added} −{removed} lines\n\n```diff\n{unified}\n```";
    }

    /// <summary>Network by network comparison of two SIMATIC SD documents.</summary>
    public static string? Documents(string name, BlockText before, BlockText after)
    {
        var a = SdDocument.Parse(before.Content, before.Resources);
        var b = SdDocument.Parse(after.Content, after.Resources);
        var sections = new List<string>();

        var interfaceDiff = TextDiff.Unified(TextDiff.SplitLines(a.Header), TextDiff.SplitLines(b.Header));
        if (interfaceDiff.Length > 0)
        {
            sections.Add($"**Interface**\n\n```diff\n{interfaceDiff}\n```");
        }

        var changes = Align(a.Networks, b.Networks);
        var counts = new Dictionary<string, int> { ["changed"] = 0, ["added"] = 0, ["removed"] = 0 };
        foreach (var (old, @new) in changes)
        {
            if (old is not null && @new is not null)
            {
                counts["changed"]++;
                sections.Add(Changed(old, @new));
            }
            else if (@new is not null)
            {
                counts["added"]++;
                sections.Add($"{Heading(@new)} — **added**\n\n{Show(@new, _ => null)}");
            }
            else
            {
                counts["removed"]++;
                sections.Add($"{Heading(old!)} — **removed**\n\n{Show(old!, _ => null)}");
            }
        }

        if (sections.Count == 0)
        {
            return null;
        }

        var summary = string.Join(", ", counts.Where(c => c.Value > 0).Select(c => $"{c.Value} network{(c.Value == 1 ? string.Empty : "s")} {c.Key}"));
        if (interfaceDiff.Length > 0)
        {
            summary = summary.Length == 0 ? "interface changed" : summary + ", interface changed";
        }

        return $"**`{name}`** — {summary}\n\n" + string.Join("\n\n", sections);
    }

    /// <summary>Draws the networks of a document (all, or the given 1-based numbers).</summary>
    public static string Render(string name, BlockText block, IReadOnlyCollection<int>? networks = null)
    {
        var doc = SdDocument.Parse(block.Content, block.Resources);
        var selected = doc.Networks.Where(n => networks is null || networks.Count == 0 || networks.Contains(n.Number)).ToList();
        var sb = new StringBuilder($"**`{name}`** — {doc.Networks.Count} network{(doc.Networks.Count == 1 ? string.Empty : "s")}");
        foreach (var network in selected)
        {
            sb.Append("\n\n").Append(Heading(network)).Append("\n\n").Append(Show(network, _ => null));
            if (sb.Length > MaxChars)
            {
                sb.Append("\n\n… (output truncated: ask for fewer networks)");
                break;
            }
        }

        return sb.ToString();
    }

    private static string Changed(SdNetwork old, SdNetwork @new)
    {
        if (old.IsLadder && @new.IsLadder)
        {
            var oldElements = Elements(old);
            var newElements = Elements(@new);
            var removed = MultisetMinus(oldElements, newElements);
            var added = MultisetMinus(newElements, oldElements);
            var list = new StringBuilder();
            foreach (var s in added)
            {
                list.Append("\n+ ").Append(s);
            }

            foreach (var s in removed)
            {
                list.Append("\n- ").Append(s);
            }

            var changesText = list.Length > 0
                ? $"```diff{list}\n```"
                : "_Same elements, different connections._";
            var removedSet = new HashSet<string>(removed);
            var addedSet = new HashSet<string>(added);
            return $"{Heading(@new)} — **changed**\n\n{changesText}\n\nBefore:\n\n{Show(old, e => removedSet.Contains(e.Signature) ? "-" : null)}" +
                   $"\n\nAfter:\n\n{Show(@new, e => addedSet.Contains(e.Signature) ? "+" : null)}";
        }

        var title = Heading(@new) + " — **changed**";
        var body = TextDiff.Unified(TextDiff.SplitLines(old.Body), TextDiff.SplitLines(@new.Body));
        if (old.Language != @new.Language)
        {
            title += $" ({old.Language} → {@new.Language})";
        }

        return $"{title}\n\n```diff\n{body}\n```";
    }

    private static string Show(SdNetwork network, Func<SdElement, string?> mark)
    {
        if (network.IsLadder)
        {
            try
            {
                return $"```text\n{LadRenderer.Render(network, mark)}\n```";
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
            {
                return $"```text\n{network.Body}\n```";
            }
        }

        var lang = network.Language.Equals("SCL", StringComparison.OrdinalIgnoreCase) ? "pascal" : "text";
        return $"```{lang}\n{network.Body}\n```";
    }

    private static string Heading(SdNetwork n) =>
        $"**Network {n.Number}**{(string.IsNullOrWhiteSpace(n.Title) ? string.Empty : $" · {n.Title}")} ({n.Language})";

    private static List<string> Elements(SdNetwork n) =>
        n.Rungs!.SelectMany(r => r.Items.OfType<SdElement>()).Select(e => e.Signature).ToList();

    private static List<string> MultisetMinus(List<string> a, List<string> b)
    {
        var remaining = b.GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count());
        var result = new List<string>();
        foreach (var x in a)
        {
            if (remaining.TryGetValue(x, out var c) && c > 0)
            {
                remaining[x] = c - 1;
            }
            else
            {
                result.Add(x);
            }
        }

        return result;
    }

    /// <summary>Pairs the networks of two versions: unchanged ones are skipped, the others are changed, added or removed.</summary>
    internal static List<(SdNetwork? Old, SdNetwork? New)> Align(IReadOnlyList<SdNetwork> before, IReadOnlyList<SdNetwork> after)
    {
        string Key(SdNetwork n) => n.Language + "\u0001" + n.Title + "\u0001" + n.Body;
        var script = TextDiff.Compute(before.Select(Key).ToList(), after.Select(Key).ToList());
        var result = new List<(SdNetwork?, SdNetwork?)>();
        int i = 0, j = 0, k = 0;
        while (k < script.Count)
        {
            if (script[k].Op == TextDiff.Op.Equal)
            {
                i++;
                j++;
                k++;
                continue;
            }

            var deleted = new List<SdNetwork>();
            var inserted = new List<SdNetwork>();
            while (k < script.Count && script[k].Op != TextDiff.Op.Equal)
            {
                if (script[k].Op == TextDiff.Op.Delete)
                {
                    deleted.Add(before[i++]);
                }
                else
                {
                    inserted.Add(after[j++]);
                }

                k++;
            }

            var pairs = Math.Min(deleted.Count, inserted.Count);
            for (var p = 0; p < pairs; p++)
            {
                result.Add((deleted[p], inserted[p]));
            }

            result.AddRange(deleted.Skip(pairs).Select(d => ((SdNetwork?)d, (SdNetwork?)null)));
            result.AddRange(inserted.Skip(pairs).Select(n => ((SdNetwork?)null, (SdNetwork?)n)));
        }

        return result;
    }
}
