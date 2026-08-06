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
}
