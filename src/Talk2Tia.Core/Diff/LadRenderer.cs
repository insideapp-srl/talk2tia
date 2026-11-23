using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Talk2Tia.Core.Diff;

/// <summary>
/// Draws LAD networks as text (box-drawing characters), the way TIA Portal shows them: left power rail, contacts and
/// comparators in series and in parallel, boxes with their pins, coils at the end of each branch.
/// The rungs are reduced to a series/parallel tree; networks that cannot be reduced are drawn rung by rung.
/// </summary>
public static class LadRenderer
{
    private const int MaxLabel = 26;
    private const int MaxWidth = 130;

    /// <summary>Draws a LAD network. <paramref name="mark"/> returns a marker ("+", "-") for elements to highlight.</summary>
    public static string Render(SdNetwork network, Func<SdElement, string?>? mark = null)
    {
        if (network.Rungs is null)
        {
            throw new ArgumentException("Not a LAD network.", nameof(network));
        }

        mark ??= _ => null;
        var roots = Reduce(network.Rungs, out var pinLabels);
        if (roots is null)
        {
            return RenderFlat(network.Rungs, mark, pinLabels);
        }

        var output = new List<string>();
        var continuation = 0;
        foreach (var root in roots)
        {
            if (output.Count > 0)
            {
                output.Add("┃");
            }

            var segments = WrapSeries(root, mark, pinLabels);
            for (var s = 0; s < segments.Count; s++)
            {
                var block = segments[s];
                var startMark = s == 0 ? null : $"({continuation})▶";
                if (s < segments.Count - 1)
                {
                    continuation++;
                    block = DrawSeries(new List<Block> { block, new(new List<string> { string.Empty, $"──▶({continuation})" }, 1) });
                }

                for (var row = 0; row < block.Lines.Count; row++)
                {
                    var rail = startMark is null
                        ? (row == block.Wire ? "┠" : "┃")
                        : "┃ " + (row == block.Wire ? startMark : new string(' ', startMark.Length));
                    output.Add(rail + block.Lines[row].TrimEnd());
                }
            }
        }

        return string.Join("\n", output);
    }

    /// <summary>Splits a long series into several rows, like TIA Portal wraps wide rungs.</summary>
    private static List<Block> WrapSeries(Term root, Func<SdElement, string?> mark, IReadOnlyDictionary<string, string> pins)
    {
        var whole = Draw(root, mark, pins);
        if (whole.Width <= MaxWidth || root is not SeriesTerm series)
        {
            return new List<Block> { whole };
        }

        var segments = new List<Block>();
        var current = new List<Block>();
        var width = 0;
        foreach (var item in series.Items)
        {
            var block = Draw(item, mark, pins);
            if (current.Count > 0 && width + block.Width > MaxWidth)
            {
                segments.Add(DrawSeries(current));
                current = new List<Block>();
                width = 0;
            }

            current.Add(block);
            width += block.Width;
        }

        segments.Add(DrawSeries(current));
        return segments;
    }

    // ---------- Series/parallel reduction ----------

    private abstract record Term;

    private sealed record ElementTerm(SdElement Element) : Term;

    private sealed record PinTerm(string Wire) : Term;

    private sealed record SeriesTerm(IReadOnlyList<Term> Items) : Term;

    private sealed record ParallelTerm(IReadOnlyList<Term> Branches) : Term;

    /// <summary>Branches leaving the same point that do not join again (several coils or boxes).</summary>
    private sealed record ForkTerm(IReadOnlyList<Term> Branches) : Term;

    private sealed class Edge(string from, string to, Term term)
    {
        public string From { get; set; } = from;
        public string To { get; set; } = to;
        public Term Term { get; set; } = term;
    }

    private const string Rail = "powerrail";

    /// <summary>Builds the rung graph and reduces it to one tree per rung that starts at the power rail.</summary>
    private static List<Term>? Reduce(IReadOnlyList<SdRung> rungs, out Dictionary<string, string> pinLabels)
    {
        pinLabels = new Dictionary<string, string>(StringComparer.Ordinal);
        var edges = new List<Edge>();
        var sinks = new HashSet<string>(StringComparer.Ordinal);
        var sinkIndex = 0;
        var pinWires = new HashSet<string>(StringComparer.Ordinal);

        foreach (var rung in rungs)
        {
            var current = rung.Start;
            var terms = new List<Term>();
            foreach (var item in rung.Items)
            {
                if (item is SdWire wire)
                {
                    edges.Add(new Edge(current, wire.Name, Series(terms)));
                    current = wire.Name;
                    terms = new List<Term>();
                }
                else if (item is SdElement element)
                {
                    terms.Add(new ElementTerm(element));
                    foreach (var p in element.Params.Where(p => p.Value.StartsWith("wire#", StringComparison.Ordinal)))
                    {
                        var name = p.Value.Substring(5);
                        pinWires.Add(name);
                        pinLabels[name] = $"{ShortName(element.Name)}.{p.Pin}";
                    }
                }
            }

            var end = rung.End;
            if (end is null)
            {
                end = $"\u0001sink{sinkIndex++}";
                sinks.Add(end);
            }

            edges.Add(new Edge(current, end, Series(terms)));
        }

        // A wire used as a box input ends in a pin marker.
        foreach (var wire in pinWires)
        {
            var sink = $"\u0001pin:{wire}";
            sinks.Add(sink);
            edges.Add(new Edge(wire, sink, new PinTerm(wire)));
        }

        bool changed;
        do
        {
            changed = false;

            // Parallel: several edges between the same two points.
            foreach (var group in edges.GroupBy(e => (e.From, e.To)).Where(g => g.Count() > 1).ToList())
            {
                var list = group.ToList();
                list[0].Term = new ParallelTerm(list.Select(e => e.Term).ToList());
                foreach (var e in list.Skip(1))
                {
                    edges.Remove(e);
                }

                changed = true;
            }

            // Series: a point with exactly one edge in and one edge out.
            foreach (var node in edges.Select(e => e.To).Distinct().ToList())
            {
                if (node == Rail || sinks.Contains(node))
                {
                    continue;
                }

                var incoming = edges.Where(e => e.To == node).ToList();
                var outgoing = edges.Where(e => e.From == node).ToList();
                if (incoming.Count == 1 && outgoing.Count == 1 && outgoing[0].To != node)
                {
                    incoming[0].To = outgoing[0].To;
                    incoming[0].Term = Series(new[] { incoming[0].Term, outgoing[0].Term });
                    edges.Remove(outgoing[0]);
                    changed = true;
                    break;
                }
            }
        }
        while (changed);

        // What is left must be a tree hanging from the power rail.
        if (edges.Any(e => e.To == Rail) || edges.GroupBy(e => e.To).Any(g => g.Count() > 1))
        {
            return null;
        }

        var reachable = new HashSet<string>(StringComparer.Ordinal) { Rail };
        var frontier = new Queue<string>(new[] { Rail });
        while (frontier.Count > 0)
        {
            foreach (var e in edges.Where(e => e.From == frontier.Peek()))
            {
                if (reachable.Add(e.To))
                {
                    frontier.Enqueue(e.To);
                }
            }

            frontier.Dequeue();
        }

        if (edges.Any(e => !reachable.Contains(e.From)))
        {
            return null;
        }

        Term Subtree(string node)
        {
            var outgoing = edges.Where(e => e.From == node).ToList();
            return outgoing.Count switch
            {
                0 => Series(Array.Empty<Term>()),
                1 => Series(new[] { outgoing[0].Term, Subtree(outgoing[0].To) }),
                _ => new ForkTerm(outgoing.Select(e => Series(new[] { e.Term, Subtree(e.To) })).ToList()),
            };
        }

        return edges.Where(e => e.From == Rail).Select(e => Series(new[] { e.Term, Subtree(e.To) })).ToList();
    }

    private static Term Series(IEnumerable<Term> terms)
    {
        var flat = new List<Term>();
        foreach (var t in terms)
        {
            if (t is SeriesTerm s)
            {
                flat.AddRange(s.Items);
            }
            else
            {
                flat.Add(t);
            }
        }

        return flat.Count == 1 ? flat[0] : new SeriesTerm(flat);
    }

    // ---------- Drawing ----------

    /// <summary>A rectangle of text; <see cref="Wire"/> is the row where the power flow enters and leaves.</summary>
    private sealed class Block
    {
        public Block(List<string> lines, int wire)
        {
            var width = lines.Count == 0 ? 0 : lines.Max(l => l.Length);
            Lines = lines.Select(l => l.PadRight(width)).ToList();
            Wire = wire;
        }

        public List<string> Lines { get; }
        public int Wire { get; }
        public int Width => Lines.Count == 0 ? 0 : Lines[0].Length;

        public Block PadTo(int above, int below)
        {
            var lines = new List<string>();
            lines.AddRange(Enumerable.Repeat(new string(' ', Width), above - Wire));
            lines.AddRange(Lines);
            lines.AddRange(Enumerable.Repeat(new string(' ', Width), below - (Lines.Count - Wire - 1)));
            return new Block(lines, above);
        }

        /// <summary>Extends the block to <paramref name="width"/>, continuing the wire with ─.</summary>
        public Block Widen(int width, bool continueWire = true) =>
            new(Lines.Select((l, i) => l + new string(i == Wire && continueWire ? '─' : ' ', width - Width)).ToList(), Wire);
    }

    private static Block Draw(Term term, Func<SdElement, string?> mark, IReadOnlyDictionary<string, string> pins) => term switch
    {
        ElementTerm e => DrawElement(e.Element, mark(e.Element), pins),
        PinTerm p => new Block(new List<string> { string.Empty, $"──▶ [{p.Wire}]" }, 1),
        SeriesTerm s => DrawSeries(s.Items.Select(t => Draw(t, mark, pins)).ToList()),
        ParallelTerm p => DrawBranches(p.Branches.Select(t => Draw(t, mark, pins)).ToList(), join: true),
        ForkTerm f => DrawBranches(f.Branches.Select(t => Draw(t, mark, pins)).ToList(), join: false),
        _ => throw new InvalidOperationException(term.GetType().Name),
    };

    private static Block DrawSeries(List<Block> blocks)
    {
        if (blocks.Count == 0)
        {
            return new Block(new List<string> { "──" }, 0);
        }

        var above = blocks.Max(b => b.Wire);
        var below = blocks.Max(b => b.Lines.Count - b.Wire - 1);
        var padded = blocks.Select(b => b.PadTo(above, below)).ToList();
        var lines = Enumerable.Range(0, above + below + 1).Select(r => string.Concat(padded.Select(b => b.Lines[r]))).ToList();
        return new Block(lines, above);
    }

    private static Block DrawBranches(List<Block> branches, bool join)
    {
        var width = branches.Max(b => b.Width);
        var lines = new List<string>();
        var wires = new List<int>();
        foreach (var branch in branches)
        {
            var widened = branch.Widen(width, continueWire: join);
            wires.Add(lines.Count + widened.Wire);
            lines.AddRange(widened.Lines);
        }

        var first = wires[0];
        var last = wires[wires.Count - 1];
        for (var row = 0; row < lines.Count; row++)
        {
            var inside = row > first && row < last;
            var isWire = wires.Contains(row);
            var left = row == first ? '┬' : row == last ? '└' : isWire ? '├' : inside ? '│' : ' ';
            var right = row == first ? '┬' : row == last ? '┘' : isWire ? '┤' : inside ? '│' : ' ';
            lines[row] = (row == first ? "─" : " ") + left + lines[row] + (join ? right.ToString() + (row == first ? "─" : " ") : string.Empty);
        }

        return new Block(lines, first);
    }

    private static Block DrawElement(SdElement e, string? marker, IReadOnlyDictionary<string, string> pins)
    {
        var name = e.Name;
        var prefix = marker is null ? string.Empty : marker + " ";

        if (IsContact(name) && e.Operand is not null || name is "P_Contact" or "N_Contact")
        {
            var symbol = name switch
            {
                "Contact" => "┤ ├",
                "I_Contact" => "┤/├",
                "P_Contact" => "┤P├",
                "N_Contact" => "┤N├",
                "NOT_Contact" or "NOT" => "┤NOT├",
                _ => $"┤{name.Replace("_Contact", string.Empty)}├",
            };
            var operand = e.Operand ?? e.Params.FirstOrDefault(p => p.Pin == "operand")?.Value ?? string.Empty;
            var below = e.Params.Where(p => p.Pin != "operand").Select(p => p.Value).ToList();
            return Cell(prefix, Wrap(operand), symbol, below.SelectMany(Wrap).ToList());
        }

        if (Comparator(name) is { } op)
        {
            var in1 = e.Params.FirstOrDefault(p => p.Pin == "in1")?.Value ?? string.Empty;
            var in2 = e.Params.FirstOrDefault(p => p.Pin == "in2")?.Value ?? string.Empty;
            var type = TemplateType(e.Template);
            var below = new List<string>();
            if (type is not null)
            {
                below.Add(type);
            }

            below.AddRange(Wrap(in2));
            return Cell(prefix, Wrap(in1), $"┤{op}├", below);
        }

        if (IsCoil(name) && e.Operand is not null)
        {
            var symbol = name switch
            {
                "Coil" => "( )",
                "I_Coil" => "(/)",
                "S_Coil" => "(S)",
                "R_Coil" => "(R)",
                "P_Coil" => "(P)",
                "N_Coil" => "(N)",
                _ => $"({name.Replace("_Coil", string.Empty)})",
            };
            return Cell(prefix, Wrap(e.Operand), symbol, new List<string>());
        }

        return DrawBox(e, prefix, pins);
    }

    private static Block Cell(string prefix, List<string> above, string symbol, List<string> below)
    {
        if (above.Count == 0)
        {
            above.Add(string.Empty);
        }

        above[0] = prefix + above[0];
        var inner = Math.Max(symbol.Length, above.Concat(below).Max(l => l.Length));
        var width = inner + 2;
        var lines = new List<string>();
        lines.AddRange(above.Select(l => Center(l, width)));
        var left = (width - symbol.Length) / 2;
        lines.Add(new string('─', left) + symbol + new string('─', width - left - symbol.Length));
        lines.AddRange(below.Select(l => Center(l, width)));
        return new Block(lines, above.Count);
    }

    private static Block DrawBox(SdElement e, string prefix, IReadOnlyDictionary<string, string> pins)
    {
        var (title, typeLine, inPin, outPin) = BoxInfo(e);
        string Show(string value) => value.StartsWith("wire#", StringComparison.Ordinal) ? $"[{value.Substring(5)}]" : value;

        var inputs = e.Params.Where(p => !p.IsOutput && p.Pin.Length > 0).ToList();
        var outputs = e.Params.Where(p => p.IsOutput).ToList();
        if (e.Operand is not null)
        {
            inputs.Insert(0, new SdParam("IN", false, e.Operand));
        }

        var leftLabels = inputs.Select(p => TruncateStart(Show(p.Value), MaxLabel + 4)).ToList();
        var rightLabels = outputs.Select(p => TruncateStart(Show(p.Value), MaxLabel + 4)).ToList();
        var leftWidth = leftLabels.DefaultIfEmpty(string.Empty).Max(l => l.Length);
        var rightWidth = rightLabels.DefaultIfEmpty(string.Empty).Max(l => l.Length);

        var rowsCount = Math.Max(inputs.Count, outputs.Count) + 1; // +1: EN/ENO row
        var pinRows = new List<(string Left, string Right)> { (inPin, outPin) };
        for (var i = 0; i < rowsCount - 1; i++)
        {
            pinRows.Add((i < inputs.Count ? inputs[i].Pin : string.Empty, i < outputs.Count ? outputs[i].Pin : string.Empty));
        }

        var headerLines = Wrap(title);
        headerLines[0] = prefix + headerLines[0];
        if (typeLine is not null)
        {
            headerLines.Add(typeLine);
        }

        var inner = Math.Max(headerLines.Max(l => l.Length), pinRows.Max(r => r.Left.Length + r.Right.Length + 2));
        inner = Math.Max(inner, 8);

        var lead = leftWidth + (leftWidth > 0 ? 3 : 2); // "label ─┤" or "──┤"
        var trail = rightWidth + (rightWidth > 0 ? 3 : 2);
        var lines = headerLines.Select(l => new string(' ', lead) + Center(l, inner + 2)).ToList();

        lines.Add(new string(' ', lead) + "┌" + new string('─', inner) + "┐");
        var wireRow = lines.Count;
        for (var r = 0; r < pinRows.Count; r++)
        {
            var (leftPin, rightPin) = pinRows[r];
            var content = leftPin + new string(' ', inner - leftPin.Length - rightPin.Length) + rightPin;
            string leftPart;
            string rightPart;
            if (r == 0)
            {
                leftPart = new string('─', lead - 1) + "┤";
                rightPart = "├" + new string('─', trail - 1);
            }
            else
            {
                var label = r - 1 < leftLabels.Count ? leftLabels[r - 1] : string.Empty;
                leftPart = label.Length > 0 ? label.PadLeft(leftWidth) + " ─┤" : new string(' ', lead - 1) + "│";
                var outLabel = r - 1 < rightLabels.Count ? rightLabels[r - 1] : string.Empty;
                rightPart = outLabel.Length > 0 ? "├─ " + outLabel : "│";
            }

            lines.Add(leftPart + content + rightPart);
        }

        lines.Add(new string(' ', lead) + "└" + new string('─', inner) + "┘");
        return new Block(lines, wireRow);
    }

    private static (string Title, string? TypeLine, string InPin, string OutPin) BoxInfo(SdElement e)
    {
        var name = e.Name;
        var dot = LastTopLevelDot(name);
        if (dot > 0)
        {
            var kind = name.Substring(dot + 1);
            var instance = name.Substring(0, dot);
            var type = TemplateType(e.Template);
            var (inPin, outPin) = kind.ToUpperInvariant() switch
            {
                "TON" or "TOF" or "TP" or "TONR" => ("IN", "Q"),
                "CTU" => ("CU", "QU"),
                "CTD" => ("CD", "QD"),
                "CTUD" => ("CU", "QU"),
                "R_TRIG" or "F_TRIG" => ("CLK", "Q"),
                _ => ("EN", "ENO"),
            };
            return (instance, type is null ? kind.ToUpperInvariant() : $"{kind.ToUpperInvariant()} {type}", inPin, outPin);
        }

        if (name.StartsWith("#", StringComparison.Ordinal))
        {
            return (name, "(multi-instance)", "EN", "ENO");
        }

        var upper = name.Equals("Move", StringComparison.OrdinalIgnoreCase) ? "MOVE" : name;
        return (upper, TemplateType(e.Template), "EN", "ENO");
    }

    private static int LastTopLevelDot(string name)
    {
        var inQuotes = false;
        var last = -1;
        for (var i = 0; i < name.Length; i++)
        {
            if (name[i] == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (name[i] == '.' && !inQuotes)
            {
                last = i;
            }
        }

        return last;
    }

    private static string RenderFlat(IReadOnlyList<SdRung> rungs, Func<SdElement, string?> mark, IReadOnlyDictionary<string, string> pins)
    {
        var output = new List<string> { "(complex network: drawn rung by rung; [wN] = connection point)" };
        foreach (var rung in rungs)
        {
            var blocks = new List<Block>();
            foreach (var item in rung.Items)
            {
                blocks.Add(item is SdElement e
                    ? DrawElement(e, mark(e), pins)
                    : new Block(new List<string> { string.Empty, $"──●[{((SdWire)item).Name}]" }, 1));
            }

            if (rung.End is not null)
            {
                blocks.Add(new Block(new List<string> { string.Empty, $"──▶ [{rung.End}]" }, 1));
            }

            var block = DrawSeries(blocks);
            var start = rung.Start == Rail ? "┠" : $"[{rung.Start}]";
            for (var row = 0; row < block.Lines.Count; row++)
            {
                output.Add((row == block.Wire ? start : new string(' ', start.Length)) + block.Lines[row].TrimEnd());
            }
        }

        return string.Join("\n", output);
    }

    // ---------- Helpers ----------

    private static bool IsContact(string name) => name == "Contact" || name.EndsWith("_Contact", StringComparison.Ordinal) && Comparator(name) is null;

    private static bool IsCoil(string name) => name == "Coil" || name.EndsWith("_Coil", StringComparison.Ordinal);

    private static string? Comparator(string name) => name switch
    {
        "EQ_Contact" => "==",
        "NE_Contact" => "<>",
        "GT_Contact" => ">",
        "GE_Contact" => ">=",
        "LT_Contact" => "<",
        "LE_Contact" => "<=",
        _ => null,
    };

    private static string? TemplateType(string? template)
    {
        if (string.IsNullOrWhiteSpace(template))
        {
            return null;
        }

        var parts = template!.Split(new[] { ":=" }, StringSplitOptions.None);
        return parts.Length == 2 ? parts[1].Trim() : template;
    }

    private static string ShortName(string name) => name.Trim('"');

    /// <summary>Splits a long operand on its dots so that labels stay narrow (like TIA Portal does).</summary>
    internal static List<string> Wrap(string text)
    {
        if (text.Length <= MaxLabel)
        {
            return new List<string> { text };
        }

        var lines = new List<string>();
        var current = new StringBuilder();
        foreach (var part in SplitKeepingDots(text))
        {
            if (current.Length > 0 && current.Length + part.Length > MaxLabel)
            {
                lines.Add(current.ToString());
                current.Clear();
            }

            current.Append(part);
        }

        if (current.Length > 0)
        {
            lines.Add(current.ToString());
        }

        return lines.Select(l => Truncate(l, MaxLabel + 10)).ToList();
    }

    private static IEnumerable<string> SplitKeepingDots(string text)
    {
        var start = 0;
        var inQuotes = false;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (text[i] == '.' && !inQuotes && i > start)
            {
                yield return text.Substring(start, i - start);
                start = i;
            }
        }

        yield return text.Substring(start);
    }

    private static string TruncateStart(string text, int max) => text.Length <= max ? text : "…" + text.Substring(text.Length - max + 1);

    private static string Truncate(string text, int max) => text.Length <= max ? text : text.Substring(0, max - 1) + "…";

    private static string Center(string text, int width)
    {
        if (text.Length >= width)
        {
            return text;
        }

        var left = (width - text.Length) / 2;
        return new string(' ', left) + text + new string(' ', width - left - text.Length);
    }
}
