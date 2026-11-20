using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using ModelContextProtocol;
using Siemens.Engineering;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.ExternalSources;
using Siemens.Engineering.SW.Types;

namespace Talk2Tia.Core.Openness;

internal sealed record TextFile(string Name, string Content);

/// <summary>Export/import helpers shared by block and type tools (SCL source, SIMATIC SD documents, SimaticML).</summary>
internal static class CodeTransfer
{
    public const int MaxInlineChars = 400_000;

    private static readonly Regex SourceObjectPattern = new(
        @"^\s*(FUNCTION_BLOCK|FUNCTION|ORGANIZATION_BLOCK|DATA_BLOCK|TYPE)\s+(""(?<q>[^""]+)""|(?<u>[A-Za-z_][\w]*))",
        RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Extracts the names of the blocks/types declared in an SCL/DB/UDT/AWL source.</summary>
    public static IReadOnlyList<(string Kind, string Name)> DeclaredObjects(string source)
    {
        return SourceObjectPattern.Matches(source)
            .Cast<Match>()
            .Select(m => (m.Groups[1].Value.ToUpperInvariant(), m.Groups["q"].Success ? m.Groups["q"].Value : m.Groups["u"].Value))
            .ToList();
    }

    /// <summary>Source file extension TIA Portal expects for GenerateSource / CreateFromFile.</summary>
    public static string SourceExtension(PlcBlock block) => block switch
    {
        DataBlock => ".db",
        _ when block.ProgrammingLanguage is ProgrammingLanguage.STL or ProgrammingLanguage.F_STL => ".awl",
        _ => ".scl",
    };

    public static string NormalizeSourceExtension(string? kind, string source)
    {
        switch (kind?.Trim().TrimStart('.').ToLowerInvariant())
        {
            case null or "" or "auto":
                var declared = DeclaredObjects(source);
                if (declared.Count > 0 && declared.All(d => d.Kind == "TYPE"))
                {
                    return ".udt";
                }

                if (declared.Count > 0 && declared.All(d => d.Kind == "DATA_BLOCK"))
                {
                    return ".db";
                }

                return ".scl";
            case "scl":
                return ".scl";
            case "db":
                return ".db";
            case "udt":
                return ".udt";
            case "awl" or "stl":
                return ".awl";
            default:
                throw new McpException($"Unknown source kind '{kind}'. Use scl, db, udt or awl.");
        }
    }

    public static string SafeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(name.Length);
        foreach (var ch in name)
        {
            sb.Append(invalid.Contains(ch) ? '_' : ch);
        }

        return sb.ToString();
    }

    // ---------- Export ----------

    public static IList<TextFile> ExportSource(PlcSoftware plc, IGenerateSource item, string fileName, bool withDependencies, Workspace ws)
    {
        var file = ws.FileOf(fileName);
        plc.ExternalSourceGroup.GenerateSource(new[] { item }, file, withDependencies ? GenerateOptions.WithDependencies : GenerateOptions.None);
        return new List<TextFile> { new(file.Name, ReadText(file)) };
    }

    public static (IList<TextFile> Files, object Result) ExportDocuments(Func<DirectoryInfo, string, DocumentExportResult> export, string name, Workspace ws)
    {
        var result = export(ws.Directory, SafeFileName(name));
        var files = ws.Directory.GetFiles().OrderBy(f => f.Name).Select(f => new TextFile(f.Name, ReadText(f))).ToList();
        return (files, Summarize(result.State.ToString(), result.Messages.Select(m => m.Message)));
    }

    public static IList<TextFile> ExportXml(Action<FileInfo> export, string name, Workspace ws)
    {
        var file = ws.FileOf(SafeFileName(name) + ".xml");
        export(file);
        return new List<TextFile> { new(file.Name, ReadText(file)) };
    }

    /// <summary>Returns the files inline, or saves them to <paramref name="outputDirectory"/> and returns paths.</summary>
    public static object Deliver(IList<TextFile> files, string? outputDirectory)
    {
        if (!string.IsNullOrWhiteSpace(outputDirectory))
        {
            var dir = Directory.CreateDirectory(Environment.ExpandEnvironmentVariables(outputDirectory));
            return files.Select(f =>
            {
                var path = Path.Combine(dir.FullName, f.Name);
                File.WriteAllText(path, f.Content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
                return new { name = f.Name, path, length = f.Content.Length };
            }).ToList();
        }

        var total = 0;
        return files.Select(f =>
        {
            total += f.Content.Length;
            if (total > MaxInlineChars)
            {
                return (object)new
                {
                    name = f.Name,
                    truncated = true,
                    length = f.Content.Length,
                    content = f.Content.Substring(0, Math.Max(0, Math.Min(f.Content.Length, MaxInlineChars - (total - f.Content.Length)))),
                    hint = "Content too large to return inline; call again with outputDirectory to save it to disk.",
                };
            }

            return new { name = f.Name, content = f.Content };
        }).ToList();
    }

    // ---------- Import ----------

    /// <summary>Imports an SCL/DB/UDT/AWL source through a temporary external source and generates the blocks.</summary>
    public static object ImportSource(PlcSoftware plc, string source, string extension, PlcBlockUserGroup? blockGroup,
        PlcTypeUserGroup? typeGroup, bool keepSource, string? sourceName, Workspace ws)
    {
        var name = SafeFileName(string.IsNullOrWhiteSpace(sourceName) ? $"talk2tia_{DateTime.Now:yyyyMMdd_HHmmss}" : sourceName!);
        var path = ws.PathOf(name + extension);
        File.WriteAllText(path, source, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        var existing = plc.ExternalSourceGroup.ExternalSources.Find(name + extension) ?? plc.ExternalSourceGroup.ExternalSources.Find(name);
        existing?.Delete();

        var external = plc.ExternalSourceGroup.ExternalSources.CreateFromFile(name + extension, path);
        try
        {
            IEnumerable<IEngineeringObject> generated;
            if (typeGroup is not null)
            {
                generated = external.GenerateBlocksFromSource(typeGroup, GenerateBlockOption.KeepOnError).Cast<IEngineeringObject>();
            }
            else if (blockGroup is not null)
            {
                generated = external.GenerateBlocksFromSource(blockGroup, GenerateBlockOption.KeepOnError).Cast<IEngineeringObject>();
            }
            else
            {
                generated = external.GenerateBlocksFromSource(GenerateBlockOption.KeepOnError).Cast<IEngineeringObject>();
            }

            return new
            {
                generated = generated.Select(ValueFormatter.Describe).ToList(),
                externalSource = keepSource ? external.Name : null,
                hint = "Run plc_compile to check the generated code.",
            };
        }
        finally
        {
            if (!keepSource)
            {
                external.Delete();
            }
        }
    }

    public static (DirectoryInfo Dir, string Name) WriteDocuments(string name, string document, string? resources, Workspace ws)
    {
        var baseName = SafeFileName(name);
        File.WriteAllText(ws.PathOf(baseName + ".s7dcl"), document, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        if (!string.IsNullOrWhiteSpace(resources))
        {
            File.WriteAllText(ws.PathOf(baseName + ".s7res"), resources, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        }

        return (ws.Directory, baseName);
    }

    public static object Summarize(string state, IEnumerable<string> messages) => new
    {
        state,
        messages = messages.Where(m => !string.IsNullOrWhiteSpace(m)).ToList(),
    };

    public static string ReadText(FileInfo file) => File.ReadAllText(file.FullName, Encoding.UTF8);

    public static string ContentOrFile(string? content, string? filePath, string what)
    {
        if (!string.IsNullOrEmpty(content))
        {
            return content!;
        }

        if (!string.IsNullOrWhiteSpace(filePath))
        {
            return File.ReadAllText(Guard(filePath!), Encoding.UTF8);
        }

        throw new McpException($"Provide the {what} either inline ('content') or as a file ('filePath').");
    }

    private static string Guard(string path)
    {
        var full = Environment.ExpandEnvironmentVariables(path);
        if (!File.Exists(full))
        {
            throw new McpException($"File not found: {full}");
        }

        return full;
    }
}
