using System.IO.Compression;
using NML.Core.Multiplayer;

namespace NML.Core.Tests;

/// <summary>
/// Verifies the server-favorites import/export: <see cref="ServerListStore.ExportToZip"/> writes a
/// portable zip (servers.json inside) and <see cref="ServerListStore.ImportFromZip"/> merges it back
/// (de-duped by host:port, imported entries replace existing ones with the same endpoint).
/// </summary>
public class ServerListImportExportTests
{
    private static string TempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "nml-srv-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static ServerEntry S(string name, string host, int port = 25565) => new()
    {
        Name = name, Host = host, Port = port,
    };

    [Fact]
    public void ExportToZip_Creates_A_Portable_Zip_With_ServersJson()
    {
        string dir = TempDir();
        try
        {
            var store = new ServerListStore(dir);
            store.Add(S("Alpha", "alpha.example", 25565));
            store.Add(S("Beta", "beta.example", 25570));

            string zipPath = Path.Combine(dir, "out.zip");
            string result = store.ExportToZip(zipPath);
            result.Should().Be(zipPath);
            File.Exists(zipPath).Should().BeTrue();

            using var archive = ZipFile.OpenRead(zipPath);
            archive.GetEntry("servers.json").Should().NotBeNull();
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void ImportFromZip_Restores_Exported_Servers()
    {
        // Export → fresh store → import should reproduce the same server set.
        string dirA = TempDir();
        string dirB = TempDir();
        try
        {
            var storeA = new ServerListStore(dirA);
            storeA.Add(S("Alpha", "alpha.example", 25565));
            storeA.Add(S("Beta", "beta.example", 25570));

            string zip = Path.Combine(dirA, "servers-export.zip");
            storeA.ExportToZip(zip);

            var storeB = new ServerListStore(dirB);
            storeB.LoadAll().Should().BeEmpty();

            int count = storeB.ImportFromZip(zip);
            count.Should().Be(2);
            var loaded = storeB.LoadAll();
            loaded.Should().HaveCount(2);
            loaded.Should().Contain(s => s.Host == "alpha.example" && s.Port == 25565);
            loaded.Should().Contain(s => s.Host == "beta.example" && s.Port == 25570);
        }
        finally { Directory.Delete(dirA, recursive: true); Directory.Delete(dirB, recursive: true); }
    }

    [Fact]
    public void ImportFromZip_Merges_And_Dedupes_By_Host_Port()
    {
        // An imported server with the same host:port as an existing one replaces it; new servers are added.
        string dirA = TempDir();
        string dirB = TempDir();
        try
        {
            var storeA = new ServerListStore(dirA);
            storeA.Add(S("Hypixel", "mc.hypixel.net", 25565));
            storeA.Add(S("NewServer", "new.example", 25599));

            var storeB = new ServerListStore(dirB);
            storeB.Add(S("OldHypixel", "mc.hypixel.net", 25565)); // same host:port, different name
            storeB.Add(S("Existing", "existing.example", 25580)); // not in the import, must stay

            string zip = Path.Combine(dirA, "export.zip");
            storeA.ExportToZip(zip);

            int count = storeB.ImportFromZip(zip);
            count.Should().Be(2); // imported 2 servers
            var loaded = storeB.LoadAll();
            // Hypixel replaced OldHypixel (same host:port); NewServer added; Existing kept.
            loaded.Should().HaveCount(3);
            loaded.Should().Contain(s => s.Name == "Hypixel" && s.Host == "mc.hypixel.net");
            loaded.Should().NotContain(s => s.Name == "OldHypixel");
            loaded.Should().Contain(s => s.Name == "NewServer");
            loaded.Should().Contain(s => s.Name == "Existing");
        }
        finally { Directory.Delete(dirA, recursive: true); Directory.Delete(dirB, recursive: true); }
    }

    [Fact]
    public void ImportFromZip_Throws_When_File_Missing()
    {
        string dir = TempDir();
        try
        {
            var store = new ServerListStore(dir);
            Action act = () => store.ImportFromZip(Path.Combine(dir, "ghost.zip"));
            act.Should().Throw<FileNotFoundException>();
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void ImportFromZip_Throws_When_No_ServersJson()
    {
        // A zip without servers.json must be rejected, not silently imported as empty.
        string dir = TempDir();
        try
        {
            string zip = Path.Combine(dir, "bad.zip");
            using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
                archive.CreateEntry("not-servers.txt");
            var store = new ServerListStore(dir);
            Action act = () => store.ImportFromZip(zip);
            act.Should().Throw<InvalidDataException>();
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Export_Empty_List_Produces_Valid_Zip_That_Imports_As_Zero()
    {
        string dir = TempDir();
        try
        {
            var store = new ServerListStore(dir);
            string zip = Path.Combine(dir, "empty.zip");
            store.ExportToZip(zip);
            store.ImportFromZip(zip).Should().Be(0);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
