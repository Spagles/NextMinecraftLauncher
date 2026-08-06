using NML.Core.Mods;

namespace NML.Core.Tests;

/// <summary>
/// Verifies the structured mod-config parser: <see cref="ConfigFileParser"/> turns key=value
/// dialects (.cfg/.ini/.properties) into <see cref="ConfigEntry"/> rows (comments + sections
/// preserved), round-trips losslessly, applies edits to Key/Value, and falls back to an opaque
/// blob for non-structured formats (TOML/JSON) so the plain-text editor is unchanged.
/// </summary>
public class ConfigFileParserTests
{
    [Theory]
    [InlineData("foo.cfg", true)]
    [InlineData("FOO.CFG", true)]        // case-insensitive extension
    [InlineData("server.properties", true)]
    [InlineData("mod.ini", true)]
    [InlineData("thing.conf", true)]
    [InlineData("data.toml", false)]      // not a structured dialect → blob
    [InlineData("data.json", false)]
    [InlineData("readme.txt", false)]
    public void IsStructured_Recognizes_KeyValue_Dialects(string fileName, bool expected)
        => ConfigFileParser.IsStructured(fileName).Should().Be(expected);

    [Fact]
    public void Parse_RoundTrips_Losslessly_For_Structured_File()
    {
        // A representative Forge-style .cfg: comments, a section header, blank line, key=value pairs.
        string body = "# This is a comment\n[general]\n\n# max items\nmaxItems=64\nenableFeature=true\n";
        var entries = ConfigFileParser.Parse(body, "mod.cfg");
        ConfigFileParser.Serialize(entries).Should().Be(body);
    }

    [Fact]
    public void Parse_Classifies_Comments_Blanks_Sections_And_KeyValues()
    {
        // Trailing newline omitted so the count is deterministic (else it yields a final Blank).
        var entries = ConfigFileParser.Parse("# c1\n[v]\n\nk=1", "m.cfg");
        entries.Should().HaveCount(4);
        entries[0].Kind.Should().Be(ConfigEntryKind.Comment);
        entries[1].Kind.Should().Be(ConfigEntryKind.Section);
        entries[1].Key.Should().Be("v");
        entries[2].Kind.Should().Be(ConfigEntryKind.Blank);
        entries[3].Kind.Should().Be(ConfigEntryKind.KeyValue);
        entries[3].Key.Should().Be("k");
        entries[3].Value.Should().Be("1");
    }

    [Fact]
    public void Parse_Treats_Semicolon_As_Comment()
    {
        // .properties files use ; as a comment marker too.
        var entries = ConfigFileParser.Parse("; prop comment\nkey=val", "server.properties");
        entries[0].Kind.Should().Be(ConfigEntryKind.Comment);
        entries[0].RawLine.Should().Be("; prop comment");
    }

    [Fact]
    public void Serialize_Applies_Edits_To_KeyValue()
    {
        // Edit a value on a parsed entry and serialize — the new value lands in the output.
        var entries = ConfigFileParser.Parse("count=10\n", "m.cfg");
        // Simulate the UI editing the value (ConfigEntryRow.ToEntry rebuilds the entry).
        var edited = entries.Select(e => e with { Value = e.Key == "count" ? "42" : e.Value }).ToList();
        ConfigFileParser.Serialize(edited).Should().Be("count=42\n");
    }

    [Fact]
    public void Serialize_Preserves_Trailing_EqualSign_When_Value_Empty()
    {
        // A key with no value round-trips as "key=" (not "key").
        var entries = ConfigFileParser.Parse("emptyKey=\n", "m.cfg");
        ConfigFileParser.Serialize(entries).Should().Be("emptyKey=\n");
    }

    [Fact]
    public void Parse_Preserves_Values_Containing_Equals()
    {
        // A value with an embedded '=' (e.g. a base64 or URL) must split only on the first '='.
        var entries = ConfigFileParser.Parse("token=abc=def=ghi", "m.cfg");
        entries.Single().Key.Should().Be("token");
        entries.Single().Value.Should().Be("abc=def=ghi");
    }

    [Fact]
    public void Parse_Ignores_Equals_Inside_Quoted_Keys()
    {
        // Defensive: an '=' inside a quoted segment is not treated as the separator.
        var entries = ConfigFileParser.Parse("\"a=b\"=value", "m.cfg");
        entries.Single().Kind.Should().Be(ConfigEntryKind.KeyValue);
        entries.Single().Value.Should().Be("value");
    }

    [Fact]
    public void Non_Structured_File_RoundTrips_As_Opaque_Blob()
    {
        // TOML/JSON → a single blob entry; serialize gives back the exact input.
        string body = "[table]\nkey = \"value\"\n";
        var entries = ConfigFileParser.Parse(body, "data.toml");
        entries.Should().ContainSingle();
        entries[0].Kind.Should().Be(ConfigEntryKind.Blob);
        ConfigFileParser.Serialize(entries).Should().Be(body);
    }

    [Fact]
    public void Empty_Structured_File_Produces_A_Single_Blob()
    {
        var entries = ConfigFileParser.Parse("", "empty.cfg");
        entries.Should().ContainSingle().Which.Kind.Should().Be(ConfigEntryKind.Blob);
    }
}
