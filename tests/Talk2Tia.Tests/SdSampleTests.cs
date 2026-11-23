using System;
using System.IO;
using System.Linq;
using System.Text;
using Talk2Tia.Core.Diff;
using Xunit;

namespace Talk2Tia.Tests;

/// <summary>
/// Renders every LAD network of the .s7dcl files in TALK2TIA_SD_SAMPLES (exported from real projects, not committed).
/// Skipped when the variable is not set. The drawings are written to render.txt in the same folder for review.
/// </summary>
public sealed class SdSampleTests
{
    [Fact]
    public void Real_documents_parse_and_render()
    {
        var dir = Environment.GetEnvironmentVariable("TALK2TIA_SD_SAMPLES");
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
        {
            return;
        }

        var report = new StringBuilder();
        int networks = 0, ladder = 0, flat = 0;
        foreach (var file in Directory.GetFiles(dir!, "*.s7dcl").OrderBy(f => f))
        {
            var resources = Path.ChangeExtension(file, ".s7res");
            var doc = SdDocument.Parse(File.ReadAllText(file), File.Exists(resources) ? File.ReadAllText(resources) : null);
            foreach (var network in doc.Networks)
            {
                networks++;
                report.AppendLine($"===== {Path.GetFileName(file)} · network {network.Number} · {network.Title} ({network.Language})");
                if (network.Language == "LAD")
                {
                    Assert.True(network.IsLadder, $"{file} network {network.Number} did not parse:\n{network.Body}");
                    ladder++;
                    var drawing = LadRenderer.Render(network);
                    flat += drawing.StartsWith("(complex", StringComparison.Ordinal) ? 1 : 0;
                    report.AppendLine(drawing);
                }
                else
                {
                    report.AppendLine(network.Body);
                }

                report.AppendLine();
            }
        }

        report.Insert(0, $"networks {networks}, LAD {ladder}, drawn rung by rung {flat}\n\n");
        File.WriteAllText(Path.Combine(dir!, "render.txt"), report.ToString(), new UTF8Encoding(true));

        // Diff demo: add a contact in series in the first rung and drop the last line of the interface.
        var sample = Directory.GetFiles(dir!, "*.s7dcl").OrderBy(f => new FileInfo(f).Length).First(f => File.ReadAllText(f).Contains("RUNG wire#powerrail"));
        var original = File.ReadAllText(sample);
        var index = original.IndexOf("RUNG wire#powerrail", StringComparison.Ordinal) + "RUNG wire#powerrail".Length;
        var changed = original.Insert(index, "\n            Contact( #DemoEnable )");
        var res = Path.ChangeExtension(sample, ".s7res");
        var resText = File.Exists(res) ? File.ReadAllText(res) : null;
        var diff = BlockDiff.Describe(
            new BlockText("demo", BlockText.KindDocument, original, resText),
            new BlockText("demo", BlockText.KindDocument, changed, resText));
        Assert.NotNull(diff);
        Assert.Contains("+ Contact(#DemoEnable)", diff);
        File.WriteAllText(Path.Combine(dir!, "diff.md"), diff, new UTF8Encoding(true));
    }
}
