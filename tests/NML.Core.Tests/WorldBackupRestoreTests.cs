using System.IO;
using NML.Core;

namespace NML.Core.Tests;

/// <summary>
/// Verifies the world backup/restore feature: <see cref="GameContentBrowser.ListBackups"/>
/// enumerates timestamped zips (newest first, parsing the stamp from the filename), and
/// <see cref="GameContentBrowser.RestoreWorld"/> extracts a backup back over the matching
/// saves folder — exactly replacing it. These power the saves-tab backups panel.
/// </summary>
public class WorldBackupRestoreTests
{
    private static string TempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "nml-bak-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void PutWorld(string root, string world, string content)
    {
        string dir = Path.Combine(root, "saves", world);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "level.dat"), content);
    }

    [Fact]
    public void ListBackups_Returns_Newest_First_And_Parses_World_Name()
    {
        string root = TempDir();
        try
        {
            var browser = new GameContentBrowser(new MinecraftDirectory(root));
            // Back up the same world twice with a known filename (the browser stamps with the
            // current second, so create them with a brief gap via direct zip naming).
            string backupDir = Path.Combine(root, "backups");
            Directory.CreateDirectory(backupDir);
            File.WriteAllBytes(Path.Combine(backupDir, "Survival-20230101-000000.zip"), new byte[] { 1 });
            File.WriteAllBytes(Path.Combine(backupDir, "Survival-20240607-120000.zip"), new byte[] { 2 });
            File.WriteAllBytes(Path.Combine(backupDir, "Creative-20240607-130000.zip"), new byte[] { 3 });

            var backups = browser.ListBackups();
            backups.Should().HaveCount(3);
            // Newest first.
            backups[0].Timestamp.DateTime.Should().BeAfter(backups[1].Timestamp.DateTime);
            backups[0].WorldName.Should().Be("Creative");
            // World name parsed from the filename.
            backups.Select(b => b.WorldName).Should().BeEquivalentTo(new[] { "Creative", "Survival", "Survival" });
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void ListBackups_Empty_When_No_Backups_Dir()
    {
        string root = TempDir();
        try
        {
            var browser = new GameContentBrowser(new MinecraftDirectory(root));
            browser.ListBackups().Should().BeEmpty();
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void BackupWorld_Creates_Timestamped_Zip_In_Backups_Dir()
    {
        string root = TempDir();
        try
        {
            PutWorld(root, "Survival", "v1");
            var browser = new GameContentBrowser(new MinecraftDirectory(root));
            string zip = browser.BackupWorld(Path.Combine(root, "saves", "Survival"));
            File.Exists(zip).Should().BeTrue();
            Path.GetDirectoryName(zip).Should().EndWith("backups");
            Path.GetFileName(zip).Should().StartWith("Survival-");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void RestoreWorld_Overwrites_Existing_World_With_Backup_Contents()
    {
        // Backup "v1", mutate the live world to "v2", then restore — the live folder must hold
        // "v1" again (the backup wins, no stale "v2" left behind).
        string root = TempDir();
        try
        {
            PutWorld(root, "Survival", "v1");
            var browser = new GameContentBrowser(new MinecraftDirectory(root));
            string worldDir = Path.Combine(root, "saves", "Survival");
            string zip = browser.BackupWorld(worldDir);

            // Mutate the live world.
            File.WriteAllText(Path.Combine(worldDir, "level.dat"), "v2");
            File.WriteAllText(Path.Combine(worldDir, "extra.txt"), "should-be-removed");

            string restored = browser.RestoreWorld(zip);
            restored.Should().Be(worldDir);
            File.ReadAllText(Path.Combine(worldDir, "level.dat")).Should().Be("v1");
            File.Exists(Path.Combine(worldDir, "extra.txt")).Should().BeFalse("restore replaces the folder exactly");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void RestoreWorld_Recreates_A_Deleted_World()
    {
        string root = TempDir();
        try
        {
            PutWorld(root, "Survival", "v1");
            var browser = new GameContentBrowser(new MinecraftDirectory(root));
            string worldDir = Path.Combine(root, "saves", "Survival");
            string zip = browser.BackupWorld(worldDir);

            // Delete the live world entirely, then restore from the backup.
            Directory.Delete(worldDir, recursive: true);
            Directory.Exists(worldDir).Should().BeFalse();

            browser.RestoreWorld(zip);
            File.Exists(Path.Combine(worldDir, "level.dat")).Should().BeTrue();
            File.ReadAllText(Path.Combine(worldDir, "level.dat")).Should().Be("v1");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void RestoreWorld_Throws_When_Backup_Missing()
    {
        string root = TempDir();
        try
        {
            var browser = new GameContentBrowser(new MinecraftDirectory(root));
            Action act = () => browser.RestoreWorld(Path.Combine(root, "ghost.zip"));
            act.Should().Throw<FileNotFoundException>();
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void WorldNameFromFileName_Strips_Timestamp_Stamp()
    {
        BackupInfo.WorldNameFromFileName("Survival-20240607-120000.zip").Should().Be("Survival");
        // Names containing hyphens are preserved.
        BackupInfo.WorldNameFromFileName("New-World-20240607-120000.zip").Should().Be("New-World");
    }
}
