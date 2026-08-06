using System.Collections.Generic;
using System.IO;

namespace NML.Core.Instances;

/// <summary>
/// Migrates an instance's user content (saves, mods, configs, resource/shader packs, client
/// settings) between two game directories when its isolation mode changes — the missing other half
/// of the version-isolation toggle. The toggle switches where the instance launches from; this
/// moves the user's worlds/mods/configs to the new location so the switch is non-destructive.
/// <para>
/// "Migratable" = the user-authored content worth carrying across: saves, mods, config,
/// resourcepacks, shaderpacks, options.txt/servers.dat (client settings). Logs and backups are
/// deliberately skipped (they're instance-history, not user content). Files are copied (not moved)
/// so a botched migration never loses the source; the caller can clean up the source afterward.
/// Existing files at the destination are overwritten only when the source is newer (merge, not a
/// blind clobber) — matches HMCL's migrate behavior.
/// </para>
/// </summary>
public static class InstanceMigrator
{
    /// <summary>The subfolders carried across an isolation-mode migration.</summary>
    public static readonly IReadOnlyList<string> MigratableDirs =
        new[] { "saves", "mods", "config", "resourcepacks", "shaderpacks" };

    /// <summary>The top-level client-settings files carried across (not a folder).</summary>
    public static readonly IReadOnlyList<string> MigratableFiles =
        new[] { "options.txt", "servers.dat", "servers.dat_old", "optionsof.txt", "realms_persistence.json" };

    /// <summary>
    /// Copy every migratable subfolder + settings file from <paramref name="sourceGameDir"/> to
    /// <paramref name="destGameDir"/>. Returns a report of how many files were copied. Source files
    /// are never deleted (the caller decides whether to clean up). Missing source folders/files are
    /// skipped silently.
    /// </summary>
    public static MigrationReport Migrate(string sourceGameDir, string destGameDir)
    {
        Directory.CreateDirectory(destGameDir);
        int dirsCopied = 0, filesCopied = 0, filesSkipped = 0;

        foreach (string sub in MigratableDirs)
        {
            string src = Path.Combine(sourceGameDir, sub);
            if (!Directory.Exists(src)) continue;
            string dst = Path.Combine(destGameDir, sub);
            int f;
            MergeDirectory(src, dst, out f, out int skipped);
            filesCopied += f;
            filesSkipped += skipped;
            dirsCopied++;
        }

        foreach (string fileName in MigratableFiles)
        {
            string src = Path.Combine(sourceGameDir, fileName);
            if (!File.Exists(src)) continue;
            string dst = Path.Combine(destGameDir, fileName);
            if (ShouldCopy(src, dst))
            {
                File.Copy(src, dst, overwrite: true);
                filesCopied++;
            }
            else filesSkipped++;
        }

        return new MigrationReport(dirsCopied, filesCopied, filesSkipped);
    }

    /// <summary>Recursively merge <paramref name="sourceDir"/> into <paramref name="destDir"/>,
    /// copying files that are missing or newer at the destination. Counts copied vs skipped.</summary>
    private static void MergeDirectory(string sourceDir, string destDir, out int copied, out int skipped)
    {
        copied = 0; skipped = 0;
        Directory.CreateDirectory(destDir);
        foreach (string file in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            string rel = Path.GetRelativePath(sourceDir, file);
            string dst = Path.Combine(destDir, rel);
            if (ShouldCopy(file, dst))
            {
                string? parent = Path.GetDirectoryName(dst);
                if (parent is not null) Directory.CreateDirectory(parent);
                File.Copy(file, dst, overwrite: true);
                copied++;
            }
            else skipped++;
        }
    }

    /// <summary>True when the destination is missing or the source is newer (so a merge prefers the
    /// freshest copy rather than blindly clobbering the destination).</summary>
    private static bool ShouldCopy(string sourceFile, string destFile)
    {
        if (!File.Exists(destFile)) return true;
        return File.GetLastWriteTimeUtc(sourceFile) > File.GetLastWriteTimeUtc(destFile);
    }
}

/// <summary>Outcome of an isolation-mode migration: how many dirs/files were copied and how many
/// destination files were left untouched (already newer).</summary>
public sealed record MigrationReport(int DirectoriesCopied, int FilesCopied, int FilesSkipped);
