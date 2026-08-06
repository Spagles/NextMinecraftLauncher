using System.IO.Compression;
using NML.Core.Modpacks;

namespace NML.Core.Tests;

/// <summary>
/// Verifies <see cref="ResourcePackMetadataReader"/> — the parser behind the resource-pack preview.
/// It must extract description + pack_format from pack.mcmeta (both string and chat-component
/// description variants), tolerate missing/malformed JSON, and read from a real zip.
/// </summary>
public class ResourcePackMetadataReaderTests
{
    [Fact]
    public void ParsePackMcMeta_Extracts_String_Description_And_Format()
    {
        string json = """
            { "pack": { "description": "A cool pack", "pack_format": 15 } }
            """;
        var (desc, format) = ResourcePackMetadataReader.ParsePackMcMeta(json);
        desc.Should().Be("A cool pack");
        format.Should().Be(15);
    }

    [Fact]
    public void ParsePackMcMeta_Handles_Chat_Component_Description()
    {
        // Some packs use a chat-component object instead of a plain string.
        string json = """
            { "pack": { "description": { "text": "Fancy pack" }, "pack_format": 22 } }
            """;
        var (desc, _) = ResourcePackMetadataReader.ParsePackMcMeta(json);
        desc.Should().Be("Fancy pack");
    }

    [Fact]
    public void ParsePackMcMeta_Returns_Defaults_For_Empty()
    {
        var (desc, format) = ResourcePackMetadataReader.ParsePackMcMeta("");
        desc.Should().BeEmpty();
        format.Should().Be(0);
    }

    [Fact]
    public void ParsePackMcMeta_Returns_Defaults_For_Malformed_JSON()
    {
        var (desc, format) = ResourcePackMetadataReader.ParsePackMcMeta("not json at all");
        desc.Should().BeEmpty();
        format.Should().Be(0);
    }

    [Fact]
    public void ParsePackMcMeta_Tolerates_Missing_Pack_Key()
    {
        string json = """{ "other": 42 }""";
        var (desc, format) = ResourcePackMetadataReader.ParsePackMcMeta(json);
        desc.Should().BeEmpty();
        format.Should().Be(0);
    }

    [Fact]
    public void Read_Extracts_Metadata_From_Real_Zip()
    {
        string zipPath = Path.Combine(Path.GetTempPath(), "rpack-" + Guid.NewGuid().ToString("N")[..8] + ".zip");
        try
        {
            using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                var mcmeta = archive.CreateEntry("pack.mcmeta");
                using (var s = mcmeta.Open())
                using (var w = new StreamWriter(s))
                    w.Write("""{ "pack": { "description": "Vanilla+", "pack_format": 18 } }""");
                archive.CreateEntry("pack.png"); // empty placeholder
                archive.CreateEntry("assets/minecraft/textures/block/stone.png"); // content
            }

            var meta = ResourcePackMetadataReader.Read(zipPath);
            meta.Should().NotBeNull();
            meta!.Description.Should().Be("Vanilla+");
            meta.PackFormat.Should().Be(18);
            meta.IconPath.Should().NotBeNull(); // pack.png exists
        }
        finally { if (File.Exists(zipPath)) File.Delete(zipPath); }
    }

    [Fact]
    public void Read_Returns_Null_When_File_Missing()
    {
        ResourcePackMetadataReader.Read(Path.Combine(Path.GetTempPath(), "ghost-pack.zip")).Should().BeNull();
    }

    [Fact]
    public void Read_Returns_Empty_Metadata_When_No_Mcmeta()
    {
        // A zip without pack.mcmeta should return empty metadata (not null) — the zip is valid.
        string zipPath = Path.Combine(Path.GetTempPath(), "nopack-" + Guid.NewGuid().ToString("N")[..8] + ".zip");
        try
        {
            using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
                archive.CreateEntry("assets/foo.txt");

            var meta = ResourcePackMetadataReader.Read(zipPath);
            meta.Should().NotBeNull();
            meta!.Description.Should().BeEmpty();
            meta.PackFormat.Should().Be(0);
        }
        finally { if (File.Exists(zipPath)) File.Delete(zipPath); }
    }
}
