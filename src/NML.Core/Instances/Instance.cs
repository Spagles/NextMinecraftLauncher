using System.Text.Json;
using NML.Core.Auth;
using NML.Core.Java;
using NML.Core.Models;

namespace NML.Core.Instances;

/// <summary>
/// A launchable profile: a Minecraft version + (optional) modloader + a self-contained
/// game directory (version isolation). This is the unit the UI lists and the user launches.
/// </summary>
public sealed class Instance
{
    public string Name { get; set; } = string.Empty;

    /// <summary>The vanilla or mod-loader profile id (e.g. "1.20.1", "fabric-loader-0.15.7-1.20.1").</summary>
    public string VersionId { get; set; } = string.Empty;

    /// <summary>Optional modloader descriptor for display; purely informational.</summary>
    public string? Modloader { get; set; }

    public int MinMemoryMb { get; set; } = 512;
    public int MaxMemoryMb { get; set; } = 2048;

    public int WindowWidth { get; set; } = 854;
    public int WindowHeight { get; set; } = 480;

    /// <summary>Custom JVM arguments appended after the built-in ones (e.g. "-XX:+UseG1GC -Dfml.ignoreInvalidMinecraftCertificates=true").</summary>
    public string CustomJvmArgs { get; set; } = string.Empty;

    /// <summary>Custom game arguments appended after the version.json game args.</summary>
    public string CustomGameArgs { get; set; } = string.Empty;

    /// <summary>Java runtime to use. If null, the launcher picks one at launch time.</summary>
    public JavaRuntime? Java { get; set; }

    /// <summary>Last account used (display only; the actual account is passed at launch).</summary>
    public string? LastUsername { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Persists the list of instances as a JSON file in the launcher's settings directory.
/// Each instance owns its own game dir for version isolation:
/// <c>{instancesRoot}/{name}/.minecraft</c>.
/// </summary>
public sealed class InstanceStore
{
    private readonly string _instancesFile;

    public InstanceStore(string settingsDir)
    {
        Directory.CreateDirectory(settingsDir);
        _instancesFile = Path.Combine(settingsDir, "instances.json");
    }

    /// <summary>The root directory under which each instance's isolated .minecraft lives.</summary>
    public string InstancesRoot => Path.GetDirectoryName(_instancesFile)!;

    /// <summary>The isolated game directory for a given instance name.</summary>
    public string GameDirFor(string name) =>
        Path.Combine(InstancesRoot, SafeName(name), ".minecraft");

    /// <summary>
    /// Clone an existing instance: creates a new Instance with the given name (sharing the
    /// same version/modloader/Java settings) and recursively copies the source game directory
    /// (mods/config/saves/etc.) so the clone is independently playable.
    /// </summary>
    public Instance Clone(Instance source, string newName)
    {
        if (string.IsNullOrWhiteSpace(newName))
            throw new ArgumentException("Clone name is required.", nameof(newName));

        // Ensure unique name.
        var existing = LoadAll();
        string name = newName;
        int suffix = 1;
        while (existing.Any(i => string.Equals(i.Name, name, StringComparison.OrdinalIgnoreCase)))
            name = $"{newName} ({suffix++})";

        var clone = new Instance
        {
            Name = name,
            VersionId = source.VersionId,
            Modloader = source.Modloader,
            MinMemoryMb = source.MinMemoryMb,
            MaxMemoryMb = source.MaxMemoryMb,
            WindowWidth = source.WindowWidth,
            WindowHeight = source.WindowHeight,
            CustomJvmArgs = source.CustomJvmArgs,
            CustomGameArgs = source.CustomGameArgs,
            Java = source.Java,
        };

        // Recursively copy the game directory.
        string srcDir = GameDirFor(source.Name);
        string dstDir = GameDirFor(name);
        if (Directory.Exists(srcDir))
        {
            Directory.CreateDirectory(dstDir);
            foreach (string file in Directory.EnumerateFiles(srcDir, "*", SearchOption.AllDirectories))
            {
                string rel = Path.GetRelativePath(srcDir, file);
                string dest = Path.Combine(dstDir, rel);
                string? dir = Path.GetDirectoryName(dest);
                if (dir is not null) Directory.CreateDirectory(dir);
                File.Copy(file, dest, overwrite: true);
            }
        }

        Add(clone);
        return clone;
    }

    /// <summary>Load all instances, or an empty list if none configured.</summary>
    public List<Instance> LoadAll()
    {
        if (!File.Exists(_instancesFile)) return new List<Instance>();
        string json = File.ReadAllText(_instancesFile);
        return JsonSerializer.Deserialize<List<Instance>>(json) ?? new List<Instance>();
    }

    /// <summary>Persist the full list.</summary>
    public void SaveAll(IEnumerable<Instance> instances)
    {
        var opts = new JsonSerializerOptions(JsonOptions.Default) { WriteIndented = true };
        string json = JsonSerializer.Serialize(instances.ToList(), opts);
        File.WriteAllText(_instancesFile, json);
    }

    public void Add(Instance instance)
    {
        var all = LoadAll();
        all.RemoveAll(i => string.Equals(i.Name, instance.Name, StringComparison.OrdinalIgnoreCase));
        all.Add(instance);
        SaveAll(all);
    }

    public void Remove(string name)
    {
        var all = LoadAll();
        all.RemoveAll(i => string.Equals(i.Name, name, StringComparison.OrdinalIgnoreCase));
        SaveAll(all);
    }

    /// <summary>Sanitize an instance name into a safe filesystem directory name.</summary>
    private static string SafeName(string name)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        var sb = new System.Text.StringBuilder();
        foreach (char c in name)
            sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
        return sb.ToString();
    }
}
