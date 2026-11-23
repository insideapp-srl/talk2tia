using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Talk2Tia.Core.Diff;

/// <summary>A parameter of a LAD instruction or call: <c>pin := value</c> (input) or <c>pin =&gt; value</c> (output).</summary>
public sealed record SdParam(string Pin, bool IsOutput, string Value);

/// <summary>An item of a rung: a named wire (connection point) or an instruction.</summary>
public abstract record SdItem;

public sealed record SdWire(string Name) : SdItem;

/// <summary>A LAD instruction: contact, coil, comparator, box or block call.</summary>
public sealed record SdElement(string Name, string? Operand, IReadOnlyList<SdParam> Params, string? Template) : SdItem
{
    /// <summary>Stable text used to compare elements between two versions.</summary>
    public string Signature =>
        Name + "(" + (Operand ?? string.Join(", ", Params.Select(p => p.Pin + (p.IsOutput ? " => " : " := ") + p.Value))) + ")";
}

public sealed record SdRung(string Start, IReadOnlyList<SdItem> Items, string? End);

/// <summary>A network of a SIMATIC SD document. LAD networks have rungs; other languages keep their text only.</summary>
public sealed record SdNetwork(int Number, string Language, string? Title, string Body, IReadOnlyList<SdRung>? Rungs)
{
    public bool IsLadder => Rungs is not null;
}

/// <summary>
/// Minimal reader of SIMATIC SD block documents (.s7dcl + .s7res): splits the block into its declaration part and its
/// networks, and parses LAD networks into rungs (RUNG wire#start ... END_RUNG [wire#end]).
/// </summary>
public sealed class SdDocument
{
    private static readonly Regex NetworkStart = new(@"^\s*NETWORK\s*$", RegexOptions.Compiled);
    private static readonly Regex NetworkEnd = new(@"^\s*END_NETWORK\s*$", RegexOptions.Compiled);
    private static readonly Regex AttrLanguage = new(@"S7_Language\s*:=\s*""(?<v>[^""]*)""", RegexOptions.Compiled);
    private static readonly Regex AttrTitle = new(@"S7_NetworkTitle\s*:=\s*""(?<v>[^""]*)""", RegexOptions.Compiled);

    private SdDocument(string header, IReadOnlyList<SdNetwork> networks, string footer)
    {
        Header = header;
        Networks = networks;
        Footer = footer;
    }

    /// <summary>Declaration part: attributes, block header and interface (everything before the first network).</summary>
    public string Header { get; }

    public IReadOnlyList<SdNetwork> Networks { get; }

    /// <summary>Text after the last network (END_FUNCTION_BLOCK ...).</summary>
    public string Footer { get; }

    public static SdDocument Parse(string document, string? resources = null)
    {
        var texts = ParseResources(resources);
        var lines = TextDiff.SplitLines(document);
        var header = new StringBuilder();
        var footer = new StringBuilder();
        var networks = new List<SdNetwork>();
        var pendingAttributes = new StringBuilder();
        var i = 0;
        var seenNetwork = false;

        while (i < lines.Length)
        {
            var line = lines[i];
            if (NetworkStart.IsMatch(line))
            {
                seenNetwork = true;
                var body = new List<string>();
                i++;
                while (i < lines.Length && !NetworkEnd.IsMatch(lines[i]))
                {
                    body.Add(lines[i]);
                    i++;
                }

                i++; // END_NETWORK
                var attributes = pendingAttributes.ToString();
                pendingAttributes.Clear();
                var language = AttrLanguage.Match(attributes) is { Success: true } l ? l.Groups["v"].Value : "LAD";
                var titleKey = AttrTitle.Match(attributes) is { Success: true } t ? t.Groups["v"].Value : null;
                var title = titleKey is not null && texts.TryGetValue(titleKey, out var text) ? text : titleKey;
                var bodyText = Dedent(body);
                IReadOnlyList<SdRung>? rungs = null;
                if (language.Equals("LAD", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        rungs = ParseRungs(bodyText);
                    }
                    catch (FormatException)
                    {
                        rungs = null; // Unknown construct: the network is shown as text.
                    }
                }

                networks.Add(new SdNetwork(networks.Count + 1, language, title, bodyText, rungs));
                continue;
            }

            // An attribute block { ... } directly before NETWORK belongs to the network.
            if (line.TrimStart().StartsWith("{", StringComparison.Ordinal) && NextNetworkAfterAttributes(lines, i, out var endOfAttributes))
            {
                for (var k = i; k <= endOfAttributes; k++)
                {
                    pendingAttributes.AppendLine(lines[k]);
                }

                i = endOfAttributes + 1;
                continue;
            }

            (seenNetwork ? footer : header).AppendLine(line);
            i++;
        }

        return new SdDocument(header.ToString().TrimEnd(), networks, footer.ToString().Trim());
    }

    private static bool NextNetworkAfterAttributes(string[] lines, int start, out int end)
    {
        var depth = 0;
        for (var k = start; k < lines.Length; k++)
        {
            depth += lines[k].Count(c => c == '{') - lines[k].Count(c => c == '}');
            if (depth <= 0)
            {
                end = k;
                var next = k + 1;
                while (next < lines.Length && string.IsNullOrWhiteSpace(lines[next]))
                {
                    next++;
                }

                return next < lines.Length && NetworkStart.IsMatch(lines[next]);
            }
        }

        end = start;
        return false;
    }

    private static string Dedent(List<string> body)
    {
        while (body.Count > 0 && string.IsNullOrWhiteSpace(body[0]))
        {
            body.RemoveAt(0);
        }

        while (body.Count > 0 && string.IsNullOrWhiteSpace(body[body.Count - 1]))
        {
            body.RemoveAt(body.Count - 1);
        }

        var indent = body.Where(l => l.Trim().Length > 0).Select(l => l.Length - l.TrimStart().Length).DefaultIfEmpty(0).Min();
        return string.Join("\n", body.Select(l => l.Length >= indent ? l.Substring(indent).TrimEnd() : l.Trim()));
    }

    /// <summary>Reads the multilingual texts of a .s7res document (first language of each id).</summary>
    public static Dictionary<string, string> ParseResources(string? resources)
    {
        var texts = new Dictionary<string, string>(StringComparer.Ordinal);
        string? id = null;
        foreach (var raw in TextDiff.SplitLines(resources))
        {
            var line = raw.Trim();
            if (line.StartsWith("- id:", StringComparison.Ordinal))
            {
                id = line.Substring(5).Trim();
            }
            else if (id is not null && !texts.ContainsKey(id) && line.IndexOf(':') is var colon and > 0)
            {
                var value = line.Substring(colon + 1).Trim();
                if (value.Length >= 2 && value[0] == '\'' && value[value.Length - 1] == '\'')
                {
                    value = value.Substring(1, value.Length - 2).Replace("''", "'");
                }

                texts[id] = value;
            }
        }

        return texts;
    }

    // ---------- LAD ----------

    public static IReadOnlyList<SdRung> ParseRungs(string body)
    {
        var scanner = new Scanner(StripComments(body));
        var rungs = new List<SdRung>();
        while (scanner.SkipSpace())
        {
            scanner.Expect("RUNG");
            var start = scanner.ReadWire() ?? throw new FormatException("RUNG without start wire.");
            var items = new List<SdItem>();
            string? template = null;
            while (true)
            {
                scanner.SkipSpace();
                if (scanner.TryKeyword("END_RUNG"))
                {
                    scanner.SkipInlineSpace();
                    var end = scanner.ReadWire();
                    rungs.Add(new SdRung(start, items, end));
                    break;
                }

                if (scanner.Peek() == '{')
                {
                    var attributes = scanner.ReadBalanced('{', '}');
                    var m = Regex.Match(attributes, @"S7_Templates\s*:=\s*""(?<v>[^""]*)""");
                    template = m.Success ? m.Groups["v"].Value : template;
                    continue;
                }

                var wire = scanner.ReadWire();
                if (wire is not null)
                {
                    items.Add(new SdWire(wire));
                    continue;
                }

                items.Add(ReadElement(scanner, template));
                template = null;
            }
        }

        return rungs;
    }

    private static SdElement ReadElement(Scanner scanner, string? template)
    {
        var name = scanner.ReadName();
        if (name.Length == 0)
        {
            throw new FormatException($"Unexpected text in rung at: {scanner.Rest(30)}");
        }

        scanner.SkipSpace();
        if (scanner.Peek() != '(')
        {
            return new SdElement(name, null, Array.Empty<SdParam>(), template);
        }

        var inner = scanner.ReadBalanced('(', ')');
        var parts = SplitTopLevel(inner, ',').Select(p => p.Trim()).Where(p => p.Length > 0).ToList();
        var parameters = new List<SdParam>();
        foreach (var part in parts)
        {
            var assign = IndexOfTopLevel(part, ":=");
            var output = IndexOfTopLevel(part, "=>");
            if (assign > 0 && (output < 0 || assign < output))
            {
                parameters.Add(new SdParam(part.Substring(0, assign).Trim(), false, part.Substring(assign + 2).Trim()));
            }
            else if (output > 0)
            {
                parameters.Add(new SdParam(part.Substring(0, output).Trim(), true, part.Substring(output + 2).Trim()));
            }
            else if (parts.Count == 1)
            {
                return new SdElement(name, part, Array.Empty<SdParam>(), template);
            }
            else
            {
                parameters.Add(new SdParam(string.Empty, false, part));
            }
        }

        return new SdElement(name, null, parameters, template);
    }

    private static string StripComments(string text)
    {
        var sb = new StringBuilder(text.Length);
        var inString = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '"')
            {
                inString = !inString;
            }

            if (!inString && c == '/' && i + 1 < text.Length && text[i + 1] == '/')
            {
                while (i < text.Length && text[i] != '\n')
                {
                    i++;
                }

                sb.Append('\n');
                continue;
            }

            sb.Append(c);
        }

        return sb.ToString();
    }

    internal static List<string> SplitTopLevel(string text, char separator)
    {
        var parts = new List<string>();
        var depth = 0;
        var inString = false;
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c is '"' or '\'')
            {
                inString = !inString;
            }
            else if (!inString && c is '(' or '[')
            {
                depth++;
            }
            else if (!inString && c is ')' or ']')
            {
                depth--;
            }
            else if (!inString && depth == 0 && c == separator)
            {
                parts.Add(text.Substring(start, i - start));
                start = i + 1;
            }
        }

        parts.Add(text.Substring(start));
        return parts;
    }

    private static int IndexOfTopLevel(string text, string token)
    {
        var depth = 0;
        var inString = false;
        for (var i = 0; i + token.Length <= text.Length; i++)
        {
            var c = text[i];
            if (c is '"' or '\'')
            {
                inString = !inString;
            }
            else if (!inString && c is '(' or '[')
            {
                depth++;
            }
            else if (!inString && c is ')' or ']')
            {
                depth--;
            }
            else if (!inString && depth == 0 && string.CompareOrdinal(text, i, token, 0, token.Length) == 0)
            {
                return i;
            }
        }

        return -1;
    }

    private sealed class Scanner(string text)
    {
        private int _pos;

        public char Peek() => _pos < text.Length ? text[_pos] : '\0';

        public string Rest(int max) => text.Substring(_pos, Math.Min(max, text.Length - _pos));

        /// <summary>Skips whitespace; returns false at the end of the text.</summary>
        public bool SkipSpace()
        {
            while (_pos < text.Length && char.IsWhiteSpace(text[_pos]))
            {
                _pos++;
            }

            return _pos < text.Length;
        }

        public void SkipInlineSpace()
        {
            while (_pos < text.Length && text[_pos] is ' ' or '\t')
            {
                _pos++;
            }
        }

        public bool TryKeyword(string keyword)
        {
            if (string.CompareOrdinal(text, _pos, keyword, 0, keyword.Length) != 0)
            {
                return false;
            }

            var after = _pos + keyword.Length;
            if (after < text.Length && (char.IsLetterOrDigit(text[after]) || text[after] == '_'))
            {
                return false;
            }

            _pos = after;
            return true;
        }

        public void Expect(string keyword)
        {
            SkipSpace();
            if (!TryKeyword(keyword))
            {
                throw new FormatException($"Expected {keyword} at: {Rest(30)}");
            }
        }

        public string? ReadWire()
        {
            SkipInlineSpace();
            if (string.CompareOrdinal(text, _pos, "wire#", 0, 5) != 0)
            {
                return null;
            }

            _pos += 5;
            var start = _pos;
            while (_pos < text.Length && (char.IsLetterOrDigit(text[_pos]) || text[_pos] == '_'))
            {
                _pos++;
            }

            return text.Substring(start, _pos - start);
        }

        /// <summary>Reads an instruction or call name: identifier, "quoted", #local.path, "DB".member ...</summary>
        public string ReadName()
        {
            var start = _pos;
            while (_pos < text.Length)
            {
                var c = text[_pos];
                if (c == '"')
                {
                    var close = text.IndexOf('"', _pos + 1);
                    if (close < 0)
                    {
                        throw new FormatException("Unterminated quoted name.");
                    }

                    _pos = close + 1;
                }
                else if (char.IsLetterOrDigit(c) || c is '_' or '#' or '.')
                {
                    _pos++;
                }
                else
                {
                    break;
                }
            }

            return text.Substring(start, _pos - start);
        }

        /// <summary>Reads a balanced (...) or {...} group and returns its inner text.</summary>
        public string ReadBalanced(char open, char close)
        {
            if (Peek() != open)
            {
                throw new FormatException($"Expected '{open}' at: {Rest(30)}");
            }

            var depth = 0;
            var inString = false;
            var start = _pos + 1;
            for (; _pos < text.Length; _pos++)
            {
                var c = text[_pos];
                if (c is '"' or '\'')
                {
                    inString = !inString;
                }
                else if (!inString && c == open)
                {
                    depth++;
                }
                else if (!inString && c == close && --depth == 0)
                {
                    var inner = text.Substring(start, _pos - start);
                    _pos++;
                    return inner;
                }
            }

            throw new FormatException($"Unbalanced '{open}'.");
        }
    }
}
