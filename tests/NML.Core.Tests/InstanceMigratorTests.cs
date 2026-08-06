using NML.Core.Instances;

namespace NML.Core.Tests;

/// <summary>
/// Verifies <see cref="InstanceMigrator"/> — the non-destructive counterpart to the version-
/// isolation toggle. It copies the user-authored subfolders (saves/mods/config/etc.) and client-
/// settings files between game dirs, leaves logs/backups behind, and merges (prefers newer) rather
/// than blindly clobbering the destination. Pure file ops, no UI.
/// </summary>
public class InstanceMigratorTests
{
    private static string TempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "nml-mig-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void Put(string root, string rel, string content)
    {
        string p = Path.Combine(root, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllText(p, content);
    }

    [Fact]
    public void Migrate_Copies_Every_Migratable_Subfolder_And_Settings_File()
    {
        string src = TempDir();
        string dst = TempDir();
        try
        {
            // Populate every migratable folder + file, plus a log (which must NOT migrate).
            Put(src, "saves/World1/level.dat", "L");
            Put(src, "mods/sodium.jar", "S");
            Put(src, "config/foo.cfg", "C");
            Put(src, "resourcepacks/pack.zip", "R");
            Put(src, "shaderpacks/shaders.zip", "H");
            Put(src, "options.txt", "OPT");
            Put(src, "servers.dat", "SRV");
            Put(src, "logs/latest.log", "LOG"); // intentionally not migratable

            var report = InstanceMigrator.Migrate(src, dst);

            report.DirectoriesCopied.Should().Be(5);
            report.FilesCopied.Should().Be(7); // 5 folder files + options.txt + servers.dat
            File.ReadAllText(Path.Combine(dst, "saves", "World1", "level.dat")).Should().Be("L");
            File.ReadAllText(Path.Combine(dst, "mods", "sodium.jar")).Should().Be("S");
            File.ReadAllText(Path.Combine(dst, "options.txt")).Should().Be("OPT");
            File.ReadAllText(Path.Combine(dst, "servers.dat")).Should().Be("SRV");
            // Logs are deliberately left behind.
            File.Exists(Path.Combine(dst, "logs", "latest.log")).Should().BeFalse();
        }
        finally { Directory.Delete(src, recursive: true); Directory.Delete(dst, recursive: true); }
    }

    [Fact]
    public void Migrate_Does_Not_Delete_Source()
    {
        // The migration must be non-destructive: the source files stay in place so a botched run
        // never loses the user's data.
        string src = TempDir();
        string dst = TempDir();
        try
        {
            Put(src, "saves/w/level.dat", "L");
            InstanceMigrator.Migrate(src, dst);
            File.Exists(Path.Combine(src, "saves", "w", "level.dat")).Should().BeTrue();
        }
        finally { Directory.Delete(src, recursive: true); Directory.Delete(dst, recursive: true); }
    }

    [Fact]
    public void Migrate_Skips_Missing_Source_Folders_Silently()
    {
        string src = TempDir();
        string dst = TempDir();
        try
        {
            // Only one migratable folder exists; the rest are absent and must be skipped (no throw).
            Put(src, "mods/only.jar", "M");
            var report = InstanceMigrator.Migrate(src, dst);
            report.DirectoriesCopied.Should().Be(1);
            report.FilesCopied.Should().Be(1);
            File.Exists(Path.Combine(dst, "mods", "only.jar")).Should().BeTrue();
        }
        finally { Directory.Delete(src, recursive: true); Directory.Delete(dst, recursive: true); }
    }

    [Fact]
    public void Migrate_Does_Not_Overwrite_Newer_Destination_File()
    {
        // Merge semantics: a destination file newer than the source is kept (the source copy is skipped).
        string src = TempDir();
        string dst = TempDir();
        try
        {
            Put(src, "mods/newer.jar", "OLD-CONTENT");
            Put(dst, "mods/newer.jar", "NEW-CONTENT");
            // Make the destination file strictly newer.
            File.SetLastWriteTimeUtc(Path.Combine(src, "mods", "newer.jar"), new DateTime(2020, 1, 1));
            File.SetLastWriteTimeUtc(Path.Combine(dst, "mods", "newer.jar"), new DateTime(2024, 1, 1));

            var report = InstanceMigrator.Migrate(src, dst);
            report.FilesSkipped.Should().BeGreaterThanOrEqualTo(1);
            File.ReadAllText(Path.Combine(dst, "mods", "newer.jar")).Should().Be("NEW-CONTENT");
        }
        finally { Directory.Delete(src, recursive: true); Directory.Delete(dst, recursive: true); }
    }

    [Fact]
    public void Migrate_Overwrites_Older_Destination_File()
    {
        // When the source is newer, it wins (the destination's stale copy is replaced).
        string src = TempDir();
        string dst = TempDir();
        try
        {
            Put(src, "mods/x.jar", "FRESH");
            Put(dst, "mods/x.jar", "STALE");
            File.SetLastWriteTimeUtc(Path.Combine(src, "mods", "x.jar"), new DateTime(2024, 1, 1));
            File.SetLastWriteTimeUtc(Path.Combine(dst, "mods", "x.jar"), new DateTime(2020, 1, 1));

            InstanceMigrator.Migrate(src, dst);
            File.ReadAllText(Path.Combine(dst, "mods", "x.jar")).Should().Be("FRESH");
        }
        finally { Directory.Delete(src, recursive: true); Directory.Delete(dst, recursive: true); }
    }

    [Fact]
    public void Migrate_Into_Nested_Subfolders_Recreates_Dir_Structure()
    {
        // A deep saves/World1/playerdata/x.nbt must recreate the same nested structure at the dest.
        string src = TempDir();
        string dst = TempDir();
        try
        {
            Put(src, "saves/World1/playerdata/uuid.dat", "D");
            InstanceMigrator.Migrate(src, dst);
            File.ReadAllText(Path.Combine(dst, "saves", "World1", "playerdata", "uuid.dat")).Should().Be("D");
        }
        finally { Directory.Delete(src, recursive: true); Directory.Delete(dst, recursive: true); }
    }
}
