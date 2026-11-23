using System.Linq;
using Talk2Tia.Core.Diff;
using Xunit;

namespace Talk2Tia.Tests;

public sealed class DiffTests
{
    private const string Document = """
        {
            S7_BlockNumber := "10";
            S7_PreferredLanguage := "LAD"
        }
        FUNCTION_BLOCK "Motor"
            VAR_INPUT
                Start : Bool;
                Stop : Bool;
            END_VAR
            VAR
                Run : Bool;
                Mode : Int;
            END_VAR

            {
                S7_Language := "LAD";
                S7_NetworkTitle := "T1"
            }
            NETWORK
                RUNG wire#powerrail
                    Contact( #Start )
                END_RUNG wire#w1
                RUNG wire#powerrail
                    Contact( #Run )
                END_RUNG wire#w1
                RUNG wire#w1
                    I_Contact( #Stop )
                    wire#w2
                    Coil( #Run )
                END_RUNG
                RUNG wire#w2
                    { S7_Templates := "SrcType := Int" }
                    EQ_Contact(
                        in1 := #Mode,
                        in2 := 2
                    )
                    S_Coil( #"Alarm".Active )
                END_RUNG
            END_NETWORK
            { S7_Language := "SCL" }
            NETWORK
                #Mode := 1;
            END_NETWORK
        END_FUNCTION_BLOCK
        """;

    private const string Resources = """
        MultiLingualTexts:
          - id: T1
            it-IT: 'Start/stop with self-holding'
        """;

    [Fact]
    public void Document_is_split_into_header_and_networks()
    {
        var doc = SdDocument.Parse(Document, Resources);
        Assert.Contains("FUNCTION_BLOCK \"Motor\"", doc.Header);
        Assert.Equal(2, doc.Networks.Count);
        Assert.Equal("Start/stop with self-holding", doc.Networks[0].Title);
        Assert.True(doc.Networks[0].IsLadder);
        Assert.Equal("SCL", doc.Networks[1].Language);
        Assert.False(doc.Networks[1].IsLadder);
        Assert.Equal("END_FUNCTION_BLOCK", doc.Footer);

        var rungs = doc.Networks[0].Rungs!;
        Assert.Equal(4, rungs.Count);
        Assert.Equal("w1", rungs[0].End);
        var compare = rungs[3].Items.OfType<SdElement>().First();
        Assert.Equal("EQ_Contact", compare.Name);
        Assert.Equal("SrcType := Int", compare.Template);
        Assert.Equal(new[] { "in1", "in2" }, compare.Params.Select(p => p.Pin));
    }

    [Fact]
    public void Self_holding_rung_is_drawn_with_a_parallel_branch_and_a_fork()
    {
        var drawing = LadRenderer.Render(SdDocument.Parse(Document).Networks[0]);
        var lines = drawing.Split('\n');

        Assert.All(lines, l => Assert.True(l.StartsWith("┃") || l.StartsWith("┠"), l));
        Assert.Single(lines, l => l.StartsWith("┠")); // One rung from the power rail.
        Assert.Contains("┤ ├", drawing);
        Assert.Contains("┤/├", drawing);
        Assert.Contains("( )", drawing);
        Assert.Contains("(S)", drawing);
        Assert.Contains("┤==├", drawing);
        Assert.Contains("#Run", drawing);
        Assert.Contains("#\"Alarm\".Active", drawing);
        Assert.Contains("┘", drawing); // The OR branch closes again...
        Assert.Contains("└", drawing); // ...and the two outputs fork.
        Assert.DoesNotContain("complex network", drawing);
    }

    [Fact]
    public void Boxes_show_their_pins_and_timer_names()
    {
        const string body = """
            RUNG wire#powerrail
                Contact( #Start )
                { S7_Templates := "time_type := Time" }
                #Delay.TON(
                    pt := T#2s,
                    et =>
                )
                Coil( #Done )
            END_RUNG
            RUNG wire#powerrail
                Move(
                    in := 5,
                    out1 => #Value
                )
            END_RUNG
            """;
        var network = new SdNetwork(1, "LAD", null, body, SdDocument.ParseRungs(body));
        var drawing = LadRenderer.Render(network);
        Assert.Contains("TON Time", drawing);
        Assert.Contains("#Delay", drawing);
        Assert.Contains("┤IN", drawing);
        Assert.Contains("Q├", drawing);
        Assert.Contains("T#2s ─┤pt", drawing);
        Assert.Contains("MOVE", drawing);
        Assert.Contains("├─ #Value", drawing);
    }

    [Fact]
    public void Unknown_rung_syntax_falls_back_to_text()
    {
        var doc = SdDocument.Parse("FUNCTION \"F\" : Void\n{ S7_Language := \"LAD\" }\nNETWORK\nRUNG\nEND_NETWORK\nEND_FUNCTION");
        Assert.False(doc.Networks[0].IsLadder);
    }

    [Fact]
    public void Ladder_diff_lists_and_marks_changed_elements()
    {
        var changed = Document.Replace("I_Contact( #Stop )", "I_Contact( #Stop )\n                    Contact( #Enable )");
        var diff = BlockDiff.Describe(
            new BlockText("Motor", BlockText.KindDocument, Document, Resources),
            new BlockText("Motor", BlockText.KindDocument, changed, Resources))!;

        Assert.Contains("1 network changed", diff);
        Assert.Contains("Start/stop with self-holding", diff);
        Assert.Contains("+ Contact(#Enable)", diff);
        Assert.Contains("Before:", diff);
        Assert.Contains("After:", diff);
        Assert.Contains("+ #Enable", diff); // Marked in the drawing.
        Assert.DoesNotContain("Network 2", diff); // The SCL network did not change.
    }

    [Fact]
    public void Interface_scl_networks_and_added_networks_are_reported()
    {
        var changed = Document
            .Replace("Mode : Int;", "Mode : Int;\n        Count : Int;")
            .Replace("#Mode := 1;", "#Mode := 2;")
            .Replace("END_FUNCTION_BLOCK", "{ S7_Language := \"LAD\" }\nNETWORK\nRUNG wire#powerrail\nCoil( #Run )\nEND_RUNG\nEND_NETWORK\nEND_FUNCTION_BLOCK");
        var diff = BlockDiff.Describe(
            new BlockText("Motor", BlockText.KindDocument, Document),
            new BlockText("Motor", BlockText.KindDocument, changed))!;

        Assert.Contains("interface changed", diff);
        Assert.Matches(@"\n\+\s*Count : Int;", diff);
        Assert.Contains("-#Mode := 1;", diff);
        Assert.Contains("+#Mode := 2;", diff);
        Assert.Contains("**Network 3** (LAD) — **added**", diff);
    }

    [Fact]
    public void Identical_versions_have_no_diff()
    {
        var text = new BlockText("Motor", BlockText.KindDocument, Document);
        Assert.Null(BlockDiff.Describe(text, text));
        Assert.Null(BlockDiff.Describe(new BlockText("F", "scl", "a\nb"), new BlockText("F", "scl", "a\r\nb\r\n")));
    }

    [Fact]
    public void Source_diff_is_unified_with_context()
    {
        var before = string.Join("\n", Enumerable.Range(1, 20).Select(i => $"line {i};"));
        var after = before.Replace("line 10;", "line ten;");
        var diff = BlockDiff.Describe(new BlockText("FC1", "scl", before), new BlockText("FC1", "scl", after))!;
        Assert.Contains("+1 −1 lines", diff);
        Assert.Contains("@@ -7,7 +7,7 @@", diff);
        Assert.Contains("-line 10;", diff);
        Assert.Contains("+line ten;", diff);
        Assert.DoesNotContain("line 1;", diff);
    }

    [Fact]
    public void New_blocks_are_reported_as_new()
    {
        Assert.Contains("new block", BlockDiff.Describe(null, new BlockText("FC9", "scl", "x")));
    }
}
