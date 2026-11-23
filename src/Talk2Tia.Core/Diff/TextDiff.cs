using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Talk2Tia.Core.Diff;

/// <summary>Line based diff (longest common subsequence) with unified output.</summary>
public static class TextDiff
{
    private const int MaxCells = 4_000_000; // Above this the middle part is reported as replaced, without LCS.

    public enum Op
    {
        Equal,
        Delete,
        Insert,
    }

    public readonly record struct Line(Op Op, string Text);

    public static string[] SplitLines(string? text) =>
        string.IsNullOrEmpty(text) ? Array.Empty<string>() : text!.Replace("\r\n", "\n").TrimStart('﻿').TrimEnd('\n').Split('\n');

    /// <summary>Computes the edit script between two line arrays.</summary>
    public static IReadOnlyList<Line> Compute(IReadOnlyList<string> before, IReadOnlyList<string> after)
    {
        var prefix = 0;
        while (prefix < before.Count && prefix < after.Count && before[prefix] == after[prefix])
        {
            prefix++;
        }

        var suffix = 0;
        while (suffix < before.Count - prefix && suffix < after.Count - prefix &&
               before[before.Count - 1 - suffix] == after[after.Count - 1 - suffix])
        {
            suffix++;
        }

        var result = new List<Line>(before.Count + after.Count);
        for (var i = 0; i < prefix; i++)
        {
            result.Add(new Line(Op.Equal, before[i]));
        }

        var a = before.Skip(prefix).Take(before.Count - prefix - suffix).ToArray();
        var b = after.Skip(prefix).Take(after.Count - prefix - suffix).ToArray();
        if ((long)a.Length * b.Length > MaxCells)
        {
            result.AddRange(a.Select(l => new Line(Op.Delete, l)));
            result.AddRange(b.Select(l => new Line(Op.Insert, l)));
        }
        else
        {
            result.AddRange(Lcs(a, b));
        }

        for (var i = before.Count - suffix; i < before.Count; i++)
        {
            result.Add(new Line(Op.Equal, before[i]));
        }

        return result;
    }

    private static IEnumerable<Line> Lcs(string[] a, string[] b)
    {
        var n = a.Length;
        var m = b.Length;
        var len = new int[n + 1, m + 1];
        for (var i = n - 1; i >= 0; i--)
        {
            for (var j = m - 1; j >= 0; j--)
            {
                len[i, j] = a[i] == b[j] ? len[i + 1, j + 1] + 1 : Math.Max(len[i + 1, j], len[i, j + 1]);
            }
        }

        int x = 0, y = 0;
        while (x < n && y < m)
        {
            if (a[x] == b[y])
            {
                yield return new Line(Op.Equal, a[x]);
                x++;
                y++;
            }
            else if (len[x + 1, y] >= len[x, y + 1])
            {
                yield return new Line(Op.Delete, a[x++]);
            }
            else
            {
                yield return new Line(Op.Insert, b[y++]);
            }
        }

        while (x < n)
        {
            yield return new Line(Op.Delete, a[x++]);
        }

        while (y < m)
        {
            yield return new Line(Op.Insert, b[y++]);
        }
    }

    /// <summary>Unified diff hunks (without file headers). Returns an empty string when the texts are equal.</summary>
    public static string Unified(IReadOnlyList<string> before, IReadOnlyList<string> after, int context = 3)
    {
        var lines = Compute(before, after);
        if (lines.All(l => l.Op == Op.Equal))
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        var changed = lines.Select((l, i) => (l, i)).Where(x => x.l.Op != Op.Equal).Select(x => x.i).ToList();
        var start = 0;
        while (start < changed.Count)
        {
            var first = changed[start];
            var end = start;
            while (end + 1 < changed.Count && changed[end + 1] - changed[end] <= context * 2 + 1)
            {
                end++;
            }

            var from = Math.Max(0, first - context);
            var to = Math.Min(lines.Count - 1, changed[end] + context);
            var oldStart = 1 + lines.Take(from).Count(l => l.Op != Op.Insert);
            var newStart = 1 + lines.Take(from).Count(l => l.Op != Op.Delete);
            var hunk = lines.Skip(from).Take(to - from + 1).ToList();
            sb.Append("@@ -").Append(oldStart).Append(',').Append(hunk.Count(l => l.Op != Op.Insert))
              .Append(" +").Append(newStart).Append(',').Append(hunk.Count(l => l.Op != Op.Delete)).Append(" @@\n");
            foreach (var line in hunk)
            {
                sb.Append(line.Op switch { Op.Delete => '-', Op.Insert => '+', _ => ' ' }).Append(line.Text).Append('\n');
            }

            start = end + 1;
        }

        return sb.ToString().TrimEnd('\n');
    }

    public static (int Added, int Removed) Stats(IReadOnlyList<string> before, IReadOnlyList<string> after)
    {
        var lines = Compute(before, after);
        return (lines.Count(l => l.Op == Op.Insert), lines.Count(l => l.Op == Op.Delete));
    }
}
