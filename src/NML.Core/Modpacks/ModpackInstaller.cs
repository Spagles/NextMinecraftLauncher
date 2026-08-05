using System.IO.Compression;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using NML.Core;
using NML.Core.Download;
using NML.Core.Models;

namespace NML.Core.Modpacks;

/// <summary>
/// Installs a modpack into a new isolated instance. Supports Modrinth's <c>.mrpack</c>
/// format (a zip with <c>modrinth.index.json</c> + <c>overrides/</c>) and the CurseForge
/// format (a zip with <c>manifest.json</c> + <c>overrides/</c>). After unpacking the
/// overrides (config files, saves, etc.) it queues the mod files for download.
/// </summary>
public sealed class ModpackInstaller
{
    private readonly IHttpFetcher _http;
    private readonly Downloader _downloader;
    private readonly ILogger<ModpackInstaller> _logger;

    public ModpackInstaller(IHttpFetcher http, Downloader downloader, ILogger<ModpackInstaller> logger)
    {
        _http = http;
        _downloader = downloader;
        _logger = logger;
    }

    /// <summary>
    /// Install a modpack from a downloaded archive path into a new isolated game dir.
    /// Returns the instance name to create.
    /// </summary>
    public async Task<string> InstallAsync(
        string archivePath,
        string instanceName,
        MinecraftDirectory mc,
        DownloadCancel? cancel = null,
        ProgressReporter? progress = null,
        CancellationToken ct = default)
    {
        _logger.LogInformation("Installing modpack {Archive}…", Path.GetFileName(archivePath));

        Directory.CreateDirectory(mc.Root);
        using var archive = ZipFile.OpenRead(archivePath);

        // Detect format: Modrinth has modrinth.index.json, CurseForge has manifest.json.
        ZipArchiveEntry? modrinthEntry = archive.GetEntry("modrinth.index.json");
        ZipArchiveEntry? curseEntry = archive.GetEntry("manifest.json");

        if (modrinthEntry is not null)
            await InstallModrinthAsync(archive, modrinthEntry, mc, cancel, progress, ct);
        else if (curseEntry is not null)
            await InstallCurseForgeAsync(archive, curseEntry, mc, cancel, progress, ct);
        else
            throw new InvalidDataException(
                "Unrecognized modpack format (no modrinth.index.json or manifest.json).");

        // Extract the overrides/ folder over the game dir for both formats.
        ExtractOverrides(archive, mc.Root);

        _logger.LogInformation("Modpack installed into {Dir}.", mc.Root);
        return instanceName;
    }

    private async Task InstallModrinthAsync(
        ZipArchive archive, ZipArchiveEntry indexEntry, MinecraftDirectory mc,
        DownloadCancel? cancel, ProgressReporter? progress, CancellationToken ct)
    {
        using var stream = indexEntry.Open();
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        JsonElement root = doc.RootElement;

        // modrinth.index.json: { "game": "1.20.1", "dependencies": { "fabric-loader": "...", "minecraft": "..." }, "files": [...] }
        string gameVersion = root.TryGetProperty("game", out var g) && g.ValueKind == JsonValueKind.String
            ? g.GetString() ?? string.Empty : string.Empty;

        if (root.TryGetProperty("files", out var files) && files.ValueKind == JsonValueKind.Array)
        {
            var toFetch = new List<(Downloadable File, string RelativePath)>();
            foreach (var f in files.EnumerateArray())
            {
                string path = f.TryGetProperty("path", out var p) ? p.GetString() ?? "" : "";
                var downloads = f.TryGetProperty("downloads", out var dl) && dl.ValueKind == JsonValueKind.Array
                    ? dl.EnumerateArray().FirstOrDefault().GetString() ?? "" : "";
                var hashes = f.TryGetProperty("hashes", out var h) ? h : default;
                string sha1 = hashes.ValueKind == JsonValueKind.Object
                              && hashes.TryGetProperty("sha1", out var s1) ? s1.GetString() ?? "" : "";
                long size = f.TryGetProperty("size", out var sz) && sz.TryGetInt64(out long sv) ? sv : 0;

                if (!string.IsNullOrEmpty(path) && !string.IsNullOrEmpty(downloads))
                {
                    // Files are stored under the game dir at the given path (e.g. "mods/sodium.jar").
                    toFetch.Add((new Downloadable { Url = downloads, Sha1 = sha1, Size = size, Path = path }, path));
                }
            }

            if (toFetch.Count > 0)
            {
                _logger.LogInformation("Downloading {Count} modpack files…", toFetch.Count);
                await _downloader.DownloadBatchAsync(toFetch, mc.Root, maxConcurrency: 8, cancel, progress, ct);
            }
        }
    }

    private async Task InstallCurseForgeAsync(
        ZipArchive archive, ZipArchiveEntry manifestEntry, MinecraftDirectory mc,
        DownloadCancel? cancel, ProgressReporter? progress, CancellationToken ct)
    {
        using var stream = manifestEntry.Open();
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        JsonElement root = doc.RootElement;

        // manifest.json: { "minecraft": { "version": "1.20.1", "modLoaders": [...] }, "files": [ { "projectID":..., "fileID":..., "required": true } ] }
        // CurseForge mod files require the API to resolve download URLs (need an API key).
        // For the MVP we log a warning — full CurseForge modpack support needs the CF key + API.
        if (root.TryGetProperty("files", out var files) && files.GetArrayLength() > 0)
        {
            _logger.LogWarning(
                "CurseForge modpack has {Count} mod files requiring the CurseForge API to resolve. " +
                "Configure a CurseForge API key for full modpack support; overrides still extracted.",
                files.GetArrayLength());
        }
        await Task.CompletedTask;
    }

    /// <summary>Extract every entry under <c>overrides/</c> (or <c>client-overrides/</c>) into the game dir.</summary>
    private static void ExtractOverrides(ZipArchive archive, string gameDir)
    {
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            string prefix = entry.FullName.StartsWith("overrides/", StringComparison.OrdinalIgnoreCase)
                ? "overrides/"
                : entry.FullName.StartsWith("client-overrides/", StringComparison.OrdinalIgnoreCase)
                    ? "client-overrides/"
                    : null;

            if (prefix is null) continue;
            string rel = entry.FullName[prefix.Length..];
            if (string.IsNullOrEmpty(rel) || rel.EndsWith('/')) continue;

            string dest = Path.Combine(gameDir, rel);
            string? dir = Path.GetDirectoryName(dest);
            if (dir is not null) Directory.CreateDirectory(dir);
            entry.ExtractToFile(dest, overwrite: true);
        }
    }
}