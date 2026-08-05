using System.IO.Compression;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using NML.Core;
using NML.Core.Instances;

namespace NML.Core.Instances;

/// <summary>
/// Exports and imports launcher instances as portable .zip archives. An export bundle contains
/// an <c>instance.json</c> (the Instance metadata) plus selected game-dir subfolders
/// (<c>mods/</c>, <c>config/</c>, <c>resourcepacks/</c>) so the instance can be recreated on
/// another machine or shared with another user. Import reverses the process into a new instance.
/// </summary>
public sealed class InstanceTransferService
{
    private readonly InstanceStore _instances;
    private readonly ILogger<InstanceTransferService> _logger;

    private static readonly string[] ExportDirs = { "mods", "config", "resourcepacks", "shaderpacks" };

    public InstanceTransferService(InstanceStore instances, ILogger<InstanceTransferService> logger)
    {
        _instances = instances;
        _logger = logger;
    }

    /// <summary>
    /// Export an instance to a .zip at <paramref name="outputPath"/>. The zip contains
    /// <c>instance.json</c> + the contents of the instance's mods/config/etc. dirs.
    /// </summary>
    public void Export(Instance instance, string outputPath)
    {
        if (!File.Exists(outputPath) && Directory.Exists(Path.GetDirectoryName(outputPath)) == false)
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        string gameDir = _instances.GameDirFor(instance.Name);
        _logger.LogInformation("Exporting instance {Name} to {Path}…", instance.Name, outputPath);

        using (var zip = ZipFile.Open(outputPath, ZipArchiveMode.Create))
        {
            // Write instance.json into the archive.
            string json = JsonSerializer.Serialize(instance, new JsonSerializerOptions { WriteIndented = true });
            AddTextEntry(zip, "instance.json", json);

            // Add each exportable subdirectory if it exists.
            foreach (string sub in ExportDirs)
            {
                string dir = Path.Combine(gameDir, sub);
                if (!Directory.Exists(dir)) continue;

                foreach (string file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                {
                    string rel = Path.Combine(sub, Path.GetRelativePath(dir, file));
                    zip.CreateEntryFromFile(file, rel, CompressionLevel.Optimal);
                }
            }

            _logger.LogInformation("Exported {Name} ({Count} entries).", instance.Name, zip.Entries.Count);
        }
    }

    /// <summary>
    /// Import an instance from a .zip bundle. Creates a new instance with the archived name
    /// + metadata, extracts the bundled mods/config/etc. into the new game dir.
    /// </summary>
    public Instance Import(string zipPath)
    {
        if (!File.Exists(zipPath))
            throw new FileNotFoundException("Instance bundle not found.", zipPath);

        _logger.LogInformation("Importing instance from {Path}…", zipPath);

        using var archive = ZipFile.OpenRead(zipPath);

        // Read instance.json from the archive.
        var entry = archive.GetEntry("instance.json")
            ?? throw new InvalidDataException("Bundle has no instance.json.");

        using var stream = entry.Open();
        using var reader = new StreamReader(stream);
        string json = reader.ReadToEnd();
        Instance? instance = JsonSerializer.Deserialize<Instance>(json)
            ?? throw new InvalidDataException("instance.json is invalid.");

        // Ensure a unique name if one with the same name already exists.
        string name = instance.Name;
        int suffix = 1;
        var existing = _instances.LoadAll();
        while (existing.Any(i => string.Equals(i.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            name = $"{instance.Name} ({suffix++})";
        }
        instance.Name = name;

        // Create the game dir and extract bundled subfolders.
        string gameDir = _instances.GameDirFor(name);
        Directory.CreateDirectory(gameDir);

        foreach (ZipArchiveEntry e in archive.Entries)
        {
            if (e.FullName == "instance.json") continue;
            string dest = Path.Combine(gameDir, e.FullName);
            string? dir = Path.GetDirectoryName(dest);
            if (dir is not null) Directory.CreateDirectory(dir);
            e.ExtractToFile(dest, overwrite: true);
        }

        // Persist the new instance.
        _instances.Add(instance);
        _logger.LogInformation("Imported instance {Name}.", name);
        return instance;
    }

    /// <summary>Helper to write a text entry into a zip archive.</summary>
    private static void AddTextEntry(ZipArchive zip, string entryName, string content)
    {
        var entry = zip.CreateEntry(entryName);
        using var s = entry.Open();
        using var w = new StreamWriter(s);
        w.Write(content);
    }
}
