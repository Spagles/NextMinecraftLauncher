using System.IO;
using System.IO.Compression;
using Microsoft.Extensions.Logging.Abstractions;
using NML.Core.Instances;

namespace NML.Core.Tests;

/// <summary>
/// Verifies the deep modpack export: <see cref="InstanceTransferService.ExportDeep"/> honors the
/// per-flag inclusion of worlds, screenshots, client-settings files, and logs on top of the
/// always-bundled mods/config dirs, while the default export omits them. These are the selectable
/// contents behind the Home page's "Deep export" panel.
/// </summary>
public class ModpackDeepExportTests
{
    private static string TempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "nml-pack-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>Build an instance game dir populated with one file in each content category.</summary>
    private static (string settingsDir, Instance instance, InstanceStore store) BuildInstance()
    {
        string settingsDir = TempDir();
        var store = new InstanceStore(settingsDir);
        var instance = new Instance { Name = "Survival", VersionId = "1.20.1" };
        store.Add(instance);

        string gameDir = store.GameDirFor("Survival");
        void Put(string rel, string content)
        {
            string p = Path.Combine(gameDir, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(p)!);
            File.WriteAllText(p, content);
        }
        Put("mods/sodium.jar", "mod");
        Put("config/foo.cfg", "cfg");
        Put("saves/World1/level.dat", "level");
        Put("screenshots/shot.png", "png");
        Put("logs/latest.log", "log");
        Put("options.txt", "opts");
        Put("servers.dat", "srv");

        return (settingsDir, instance, store);
    }

    private static IReadOnlyCollection<string> EntryNames(string zipPath)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        return archive.Entries.Select(e => e.FullName).OrderBy(n => n).ToList();
    }

    [Fact]
    public void Default_Export_Omits_Worlds_Screenshots_Settings_Logs()
    {
        var (dir, instance, store) = BuildInstance();
        string zipPath = Path.Combine(dir, "default.zip");
        try
        {
            var svc = new InstanceTransferService(store, NullLogger<InstanceTransferService>.Instance);
            svc.Export(instance, zipPath); // default options
            var names = EntryNames(zipPath);

            names.Should().Contain(new[] { "instance.json", "mods/sodium.jar", "config/foo.cfg" });
            // The personal/large content is NOT included by default.
            names.Should().NotContain(n => n.StartsWith("saves/"));
            names.Should().NotContain(n => n.StartsWith("screenshots/"));
            names.Should().NotContain(n => n.StartsWith("logs/"));
            names.Should().NotContain("options.txt");
            names.Should().NotContain("servers.dat");
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Deep_Export_With_Everything_Includes_All_Selected_Content()
    {
        var (dir, instance, store) = BuildInstance();
        string zipPath = Path.Combine(dir, "deep.zip");
        try
        {
            var svc = new InstanceTransferService(store, NullLogger<InstanceTransferService>.Instance);
            svc.ExportDeep(instance, zipPath, new ModpackExportOptions
            {
                IncludeSaves = true,
                IncludeScreenshots = true,
                IncludeClientSettings = true,
                IncludeLogs = true,
            });
            var names = EntryNames(zipPath);

            // Always-bundled mod dirs still present.
            names.Should().Contain(new[] { "mods/sodium.jar", "config/foo.cfg" });
            // Every optional category the user ticked is now included.
            names.Should().Contain(new[] { "saves/World1/level.dat", "screenshots/shot.png", "logs/latest.log", "options.txt", "servers.dat" });
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Deep_Export_Honors_Partial_Selection()
    {
        // Tick only saves: worlds in, but screenshots/settings/logs still out.
        var (dir, instance, store) = BuildInstance();
        string zipPath = Path.Combine(dir, "partial.zip");
        try
        {
            var svc = new InstanceTransferService(store, NullLogger<InstanceTransferService>.Instance);
            svc.ExportDeep(instance, zipPath, new ModpackExportOptions { IncludeSaves = true });
            var names = EntryNames(zipPath);

            names.Should().Contain("saves/World1/level.dat");
            names.Should().NotContain(n => n.StartsWith("screenshots/"));
            names.Should().NotContain(n => n.StartsWith("logs/"));
            names.Should().NotContain("options.txt");
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Deep_Export_Is_Round_Trippable_By_Import()
    {
        // A deep export of saves should be re-importable, restoring the world file — verifying the
        // bundle layout matches what Import() expects (it extracts everything except instance.json).
        var (dir, instance, store) = BuildInstance();
        string zipPath = Path.Combine(dir, "roundtrip.zip");
        try
        {
            var svc = new InstanceTransferService(store, NullLogger<InstanceTransferService>.Instance);
            svc.ExportDeep(instance, zipPath, new ModpackExportOptions { IncludeSaves = true });

            Instance imported = svc.Import(zipPath);
            imported.Name.Should().NotBe("Survival"); // deduped since the original exists
            string importedLevel = Path.Combine(store.GameDirFor(imported.Name), "saves", "World1", "level.dat");
            File.Exists(importedLevel).Should().BeTrue();
            File.ReadAllText(importedLevel).Should().Be("level");
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    /// <summary>
    /// End-to-end modpack export from a realistic game directory (multiple mods, nested config,
    /// a resource pack, a world, options.txt) — verifies the resulting zip preserves directory
    /// structure with forward-slash entry names, round-trips file contents faithfully, carries the
    /// instance.json manifest, and is itself a valid zip a fresh instance store can import. This
    /// is the scenario behind the Home page "Export instance" / "Deep export" buttons.
    /// </summary>
    [Fact]
    public void End_To_End_Export_Produces_Valid_Modpack_Archive()
    {
        string dir = TempDir();
        try
        {
            var store = new InstanceStore(dir);
            var instance = new Instance
            {
                Name = "Modded",
                VersionId = "1.20.1",
                IsIsolated = true,
                MaxMemoryMb = 4096,
            };
            store.Add(instance);
            string gameDir = store.GameDirFor("Modded");

            // Populate a realistic modded game directory.
            void Put(string rel, byte[] content)
            {
                string p = Path.Combine(gameDir, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(p)!);
                File.WriteAllBytes(p, content);
            }
            // Several mods (incl. nested path that must NOT be flattened).
            Put("mods/sodium.jar", new byte[] { 0x50, 0x4B, 0x03, 0x04, 1, 2, 3 });
            Put("mods/fabric-api.jar", new byte[] { 0xAB, 0xCD });
            Put("mods/.disabled/old.jar", new byte[] { 0x00 });
            // Nested config tree.
            Put("config/create/settings.toml", new byte[] { 0x7B, 0x7D });
            Put("config/foo.cfg", new byte[] { 0x01 });
            // A resource pack and shader pack.
            Put("resourcepacks/Faithful.zip", new byte[] { 0x50, 0x4B });
            Put("shaderpacks/BSL.zip", new byte[] { 0x50, 0x4B, 0x05 });
            // Personal content (must be excluded by default export, included by deep export).
            Put("saves/MyWorld/level.dat", new byte[] { 0x09, 0x08, 0x07 });
            Put("options.txt", new byte[] { 0x0A });

            var svc = new InstanceTransferService(store, NullLogger<InstanceTransferService>.Instance);

            // --- Default export (no personal content) ---
            string defaultZip = Path.Combine(dir, "Modded-default.zip");
            svc.Export(instance, defaultZip);
            File.Exists(defaultZip).Should().BeTrue("the export must create the zip file");
            var defaultEntries = EntryNames(defaultZip);
            defaultEntries.Should().Contain(new[]
            {
                "instance.json",
                "mods/sodium.jar",
                "mods/fabric-api.jar",
                "mods/.disabled/old.jar",
                "config/create/settings.toml",
                "config/foo.cfg",
                "resourcepacks/Faithful.zip",
                "shaderpacks/BSL.zip",
            }, "the always-bundled mod/config dirs must be present with structure preserved");
            // Entry names must use forward slashes (ZIP spec / cross-platform extract).
            defaultEntries.Should().NotContain(n => n.Contains('\\', StringComparison.Ordinal),
                "zip entry names must use forward slashes");
            // Personal content excluded by default.
            defaultEntries.Should().NotContain(n => n.StartsWith("saves/"));
            defaultEntries.Should().NotContain("options.txt");

            // The manifest must deserialize back to a valid Instance with the right version id.
            Instance? manifest;
            using (var a = ZipFile.OpenRead(defaultZip))
            using (var s = a.GetEntry("instance.json")!.Open())
                manifest = System.Text.Json.JsonSerializer.Deserialize<Instance>(s);
            manifest!.VersionId.Should().Be("1.20.1");
            manifest.IsIsolated.Should().BeTrue();

            // Binary contents must round-trip byte-for-byte (no text mangling).
            using (var a2 = ZipFile.OpenRead(defaultZip))
            using (var ms = new MemoryStream())
            {
                using var es = a2.GetEntry("mods/sodium.jar")!.Open();
                es.CopyTo(ms);
                ms.ToArray().Should().Equal(new byte[] { 0x50, 0x4B, 0x03, 0x04, 1, 2, 3 });
            }

            // --- Deep export with saves + client settings ---
            string deepZip = Path.Combine(dir, "Modded-deep.zip");
            svc.ExportDeep(instance, deepZip, new ModpackExportOptions
            {
                IncludeSaves = true,
                IncludeClientSettings = true,
            });
            var deepEntries = EntryNames(deepZip);
            deepEntries.Should().Contain(new[] { "saves/MyWorld/level.dat", "options.txt" },
                "the deep-export flags must pull in the personal content");
            deepEntries.Should().Contain(defaultEntries, "deep export is a superset of the default export");

            // --- The default zip must be importable into a fresh store (full round trip) ---
            string freshDir = TempDir();
            try
            {
                var freshStore = new InstanceStore(freshDir);
                var freshSvc = new InstanceTransferService(freshStore, NullLogger<InstanceTransferService>.Instance);
                Instance imported = freshSvc.Import(defaultZip);
                imported.VersionId.Should().Be("1.20.1");
                string importedMod = Path.Combine(freshStore.GameDirFor(imported.Name), "mods", "sodium.jar");
                File.Exists(importedMod).Should().BeTrue();
                File.ReadAllBytes(importedMod).Should().Equal(new byte[] { 0x50, 0x4B, 0x03, 0x04, 1, 2, 3 });
            }
            finally { Directory.Delete(freshDir, recursive: true); }
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
