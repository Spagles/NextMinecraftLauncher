namespace NML.Core;

/// <summary>
/// Read-only access to a Minecraft instance's user content: saves, screenshots,
/// resource packs and (for modded instances) the mods folder. Used by the management UI.
/// </summary>
public sealed class GameContentBrowser
{
    private readonly MinecraftDirectory _mc;

    public GameContentBrowser(MinecraftDirectory mc) => _mc = mc;

    /// <summary>List saved worlds (folders under <c>saves/</c>) with size and last-played time.</summary>
    public IReadOnlyList<GameSave> ListSaves()
    {
        string dir = Path.Combine(_mc.Root, "saves");
        return ListEntries(dir, includeExtensions: null, map: (name, full) =>
        {
            var fi = new DirectoryInfo(full);
            return new GameSave
            {
                Name = name,
                Path = full,
                SizeBytes = DirSize(full),
                LastModified = fi.LastWriteTimeUtc,
            };
        });
    }

    /// <summary>List screenshots (PNG files under <c>screenshots/</c>).</summary>
    public IReadOnlyList<GameFile> ListScreenshots()
    {
        string dir = Path.Combine(_mc.Root, "screenshots");
        return ListEntries(dir, includeExtensions: new[] { ".png", ".jpg" }, map: (name, full) =>
        {
            var fi = new FileInfo(full);
            return new GameFile { Name = name, Path = full, SizeBytes = fi.Length, LastModified = fi.LastWriteTimeUtc };
        }).Cast<GameFile>().ToList();
    }

    /// <summary>List installed resource packs (zip files under <c>resourcepacks/</c>).</summary>
    public IReadOnlyList<GameFile> ListResourcePacks()
    {
        string dir = Path.Combine(_mc.Root, "resourcepacks");
        return ListEntries(dir, includeExtensions: new[] { ".zip" }, map: (name, full) =>
        {
            var fi = new FileInfo(full);
            return new GameFile { Name = name, Path = full, SizeBytes = fi.Length, LastModified = fi.LastWriteTimeUtc };
        }).Cast<GameFile>().ToList();
    }

    /// <summary>List installed mods (jar files under <c>mods/</c>).</summary>
    public IReadOnlyList<GameFile> ListMods()
    {
        string dir = Path.Combine(_mc.Root, "mods");
        return ListEntries(dir, includeExtensions: new[] { ".jar", ".disabled" }, map: (name, full) =>
        {
            var fi = new FileInfo(full);
            return new GameFile { Name = name, Path = full, SizeBytes = fi.Length, LastModified = fi.LastWriteTimeUtc };
        }).Cast<GameFile>().ToList();
    }

    /// <summary>Toggle a mod between enabled (.jar) and disabled (.jar.disabled).</summary>
    public void ToggleMod(string modPath)
    {
        if (modPath.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
        {
            string enabled = modPath[..^".disabled".Length];
            if (File.Exists(enabled)) throw new IOException("An enabled mod with that name already exists.");
            File.Move(modPath, enabled);
        }
        else
        {
            string disabled = modPath + ".disabled";
            File.Move(modPath, disabled);
        }
    }

    private static IReadOnlyList<T> ListEntries<T>(
        string dir, string[]? includeExtensions, Func<string, string, T> map)
    {
        if (!Directory.Exists(dir)) return Array.Empty<T>();
        var list = new List<T>();

        IEnumerable<string> entries = Directory.EnumerateFileSystemEntries(dir);
        foreach (string entry in entries)
        {
            string name = Path.GetFileName(entry);
            string ext = Path.GetExtension(entry);

            // When filtering by extension, skip folders and non-matching files.
            if (includeExtensions is not null)
            {
                if (Directory.Exists(entry)) continue;
                if (!includeExtensions.Any(e => ext.Equals(e, StringComparison.OrdinalIgnoreCase))) continue;
            }
            else
            {
                // For save-listing we only want directories.
                if (!Directory.Exists(entry)) continue;
                // Skip Minecraft's own metadata folders.
                if (name.StartsWith(".", StringComparison.Ordinal)) continue;
            }

            list.Add(map(name, entry));
        }
        return list;
    }

    private static long DirSize(string dir)
    {
        try
        {
            return Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
                            .Sum(f => new FileInfo(f).Length);
        }
        catch { return 0; }
    }

    /// <summary>Backup a world save folder into a timestamped .zip in a backups/ directory.</summary>
    public string BackupWorld(string worldPath)
    {
        if (!Directory.Exists(worldPath))
            throw new DirectoryNotFoundException($"World not found: {worldPath}");

        string name = Path.GetFileName(worldPath);
        string backupDir = Path.Combine(_mc.Root, "backups");
        Directory.CreateDirectory(backupDir);
        string stamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss");
        string zipPath = Path.Combine(backupDir, $"{name}-{stamp}.zip");

        System.IO.Compression.ZipFile.CreateFromDirectory(worldPath, zipPath,
            System.IO.Compression.CompressionLevel.Optimal, includeBaseDirectory: false);
        return zipPath;
    }

    /// <summary>Delete a world save folder (after the caller confirms).</summary>
    public void DeleteWorld(string worldPath)
    {
        if (Directory.Exists(worldPath))
            Directory.Delete(worldPath, recursive: true);
    }

    /// <summary>Delete a screenshot file.</summary>
    public void DeleteScreenshot(string screenshotPath)
    {
        if (File.Exists(screenshotPath))
            File.Delete(screenshotPath);
    }

    /// <summary>Open a screenshot in the OS default image viewer.</summary>
    public void OpenScreenshot(string screenshotPath)
    {
        if (!File.Exists(screenshotPath)) return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(screenshotPath)
            {
                UseShellExecute = true,
            });
        }
        catch { /* non-fatal */ }
    }

    /// <summary>Delete a resource pack file.</summary>
    public void DeleteResourcePack(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }

    /// <summary>Read the most recent launch log file (if any) and return its content.</summary>
    public string ReadLatestLog(int maxChars = 50000)
    {
        string logsDir = Path.Combine(_mc.Root, "logs");
        if (!Directory.Exists(logsDir)) return string.Empty;

        var latest = Directory.GetFiles(logsDir, "launch-*.log")
            .Select(f => new FileInfo(f))
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .FirstOrDefault();
        if (latest is null) return string.Empty;

        try
        {
            string content = File.ReadAllText(latest.FullName);
            // Return the tail if the log is very large (shows the crash/end, not the start).
            if (content.Length > maxChars)
                content = "…[earlier lines truncated]…\n" + content[^maxChars..];
            return content;
        }
        catch { return string.Empty; }
    }
}

public sealed class GameSave
{
    public string Name { get; init; } = string.Empty;
    public string Path { get; init; } = string.Empty;
    public long SizeBytes { get; init; }
    public DateTimeOffset LastModified { get; init; }
}

public sealed class GameFile
{
    public string Name { get; init; } = string.Empty;
    public string Path { get; init; } = string.Empty;
    public long SizeBytes { get; init; }
    public DateTimeOffset LastModified { get; init; }
}
