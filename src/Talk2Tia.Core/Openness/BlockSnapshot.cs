using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using Siemens.Engineering;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using Talk2Tia.Core.Diff;

namespace Talk2Tia.Core.Openness;

/// <summary>Captures the text form of blocks and types before and after a change, to report a diff.</summary>
internal static class BlockSnapshot
{
    /// <summary>Text of a block or PLC data type; null when it does not exist or cannot be exported.</summary>
    public static BlockText? TryCapture(PlcSoftware plc, string name, bool preferDocument = false)
    {
        try
        {
            var block = Locator.TryFindBlock(plc, name);
            if (block is not null)
            {
                return Capture(plc, block, preferDocument);
            }

            var type = Locator.AllTypes(plc.TypeGroup).Select(t => t.Type).FirstOrDefault(t => Locator.NameEquals(t.Name, name));
            if (type is null)
            {
                return null;
            }

            using var ws = new Workspace();
            var files = CodeTransfer.ExportSource(plc, type, CodeTransfer.SafeFileName(type.Name) + ".udt", false, ws);
            return new BlockText(type.Name, "udt", files[0].Content);
        }
        catch (Exception ex) when (ex is EngineeringException or InvalidOperationException or System.IO.IOException or NotSupportedException)
        {
            return null; // The diff is best effort: a block that cannot be exported (e.g. inconsistent) is reported without it.
        }
    }

    public static BlockText Capture(PlcSoftware plc, PlcBlock block, bool preferDocument = false)
    {
        using var ws = new Workspace();
        var graphic = block is not DataBlock && block.ProgrammingLanguage is ProgrammingLanguage.LAD or ProgrammingLanguage.FBD
            or ProgrammingLanguage.F_LAD or ProgrammingLanguage.F_FBD;
        var source = block is DataBlock || block.ProgrammingLanguage is ProgrammingLanguage.SCL or ProgrammingLanguage.STL or ProgrammingLanguage.F_STL;

        if (graphic || (preferDocument && block is not DataBlock))
        {
            var (files, _) = CodeTransfer.ExportDocuments(block.ExportAsDocuments, block.Name, ws);
            var document = files.FirstOrDefault(f => f.Name.EndsWith(".s7dcl", StringComparison.OrdinalIgnoreCase));
            if (document is not null)
            {
                var resources = files.FirstOrDefault(f => f.Name.EndsWith(".s7res", StringComparison.OrdinalIgnoreCase));
                return new BlockText(block.Name, BlockText.KindDocument, document.Content, resources?.Content);
            }
        }

        if (source)
        {
            var files = CodeTransfer.ExportSource(plc, block, CodeTransfer.SafeFileName(block.Name) + CodeTransfer.SourceExtension(block), false, ws);
            return new BlockText(block.Name, CodeTransfer.SourceExtension(block).TrimStart('.'), files[0].Content);
        }

        var xml = CodeTransfer.ExportXml(f => block.Export(f, ExportOptions.None), block.Name, ws);
        return new BlockText(block.Name, "xml", xml[0].Content);
    }

    /// <summary>Captures several objects (missing ones are skipped).</summary>
    public static Dictionary<string, BlockText> CaptureAll(PlcSoftware plc, IEnumerable<string> names) =>
        names.Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(n => TryCapture(plc, n))
            .Where(t => t is not null)
            .ToDictionary(t => t!.Name, t => t!, StringComparer.OrdinalIgnoreCase);

    /// <summary>Markdown diff for each object: before (may be missing) against after.</summary>
    public static string? DescribeChanges(PlcSoftware plc, IEnumerable<string> names, IReadOnlyDictionary<string, BlockText> before)
    {
        var parts = new List<string>();
        foreach (var name in names.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var after = TryCapture(plc, name);
            if (after is null)
            {
                continue;
            }

            before.TryGetValue(name, out var old);
            var text = BlockDiff.Describe(old, after);
            parts.Add(text ?? $"**`{after.Name}`** — no changes.");
        }

        return parts.Count == 0 ? null : string.Join("\n\n---\n\n", parts);
    }

    /// <summary>Names of the blocks declared in a SimaticML document.</summary>
    public static IReadOnlyList<string> NamesInXml(string xml)
    {
        try
        {
            return XDocument.Parse(xml).Descendants()
                .Where(e => e.Name.LocalName.StartsWith("SW.Blocks.", StringComparison.Ordinal) && e.Parent?.Name.LocalName == "Document")
                .Select(e => e.Element("AttributeList")?.Element("Name")?.Value)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Select(n => n!)
                .ToList();
        }
        catch (XmlException)
        {
            return Array.Empty<string>();
        }
    }
}
