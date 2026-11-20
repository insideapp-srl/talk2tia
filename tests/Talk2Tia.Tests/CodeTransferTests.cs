using System;
using System.Linq;
using ModelContextProtocol;
using Talk2Tia.Core.Openness;
using Xunit;

namespace Talk2Tia.Tests;

[Collection(OpennessCollection.Name)]
public sealed class CodeTransferTests
{
    [Fact]
    public void Declared_objects_are_extracted_from_scl()
    {
        const string source = """
            TYPE "MotorData"
            VERSION : 0.1
              STRUCT
                Speed : Real;
              END_STRUCT;
            END_TYPE

            FUNCTION_BLOCK "Motor"
            { S7_Optimized_Access := 'TRUE' }
            VAR_INPUT Start : Bool; END_VAR
            BEGIN
              ;
            END_FUNCTION_BLOCK

            FUNCTION Scale : Real
            BEGIN
              Scale := 1.0;
            END_FUNCTION

            DATA_BLOCK "Motor_DB"
            "Motor"
            BEGIN
            END_DATA_BLOCK
            """;

        var declared = CodeTransfer.DeclaredObjects(source);

        Assert.Equal(
            new[] { ("TYPE", "MotorData"), ("FUNCTION_BLOCK", "Motor"), ("FUNCTION", "Scale"), ("DATA_BLOCK", "Motor_DB") },
            declared.ToArray());
    }

    [Fact]
    public void End_keywords_are_not_mistaken_for_declarations()
    {
        var declared = CodeTransfer.DeclaredObjects("END_FUNCTION_BLOCK\nEND_TYPE\n// FUNCTION_BLOCK in a comment line start is fine\n");
        Assert.DoesNotContain(declared, d => d.Name == "in");
    }

    [Theory]
    [InlineData("TYPE \"A\"\nEND_TYPE", ".udt")]
    [InlineData("DATA_BLOCK \"D\"\nEND_DATA_BLOCK", ".db")]
    [InlineData("FUNCTION_BLOCK \"F\"\nEND_FUNCTION_BLOCK\nTYPE \"A\"\nEND_TYPE", ".scl")]
    public void Source_extension_is_inferred(string source, string expected)
    {
        Assert.Equal(expected, CodeTransfer.NormalizeSourceExtension("auto", source));
    }

    [Fact]
    public void Unknown_source_kind_is_rejected()
    {
        Assert.Throws<McpException>(() => CodeTransfer.NormalizeSourceExtension("cpp", "FUNCTION F : Void END_FUNCTION"));
    }

    [Fact]
    public void Unsafe_file_names_are_sanitized()
    {
        Assert.Equal("Motor_Conveyor_1_", CodeTransfer.SafeFileName("Motor/Conveyor:1?"));
    }

    [Fact]
    public void Large_content_is_truncated_when_returned_inline()
    {
        var big = new string('x', CodeTransfer.MaxInlineChars + 10);
        var delivered = CodeTransfer.Deliver(new[] { new TextFile("a.scl", big) }, null);
        var json = Json.Serialize(delivered);
        Assert.Contains("\"truncated\":true", json);
    }
}
