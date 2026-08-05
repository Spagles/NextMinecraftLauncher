using System.IO.Compression;
using Microsoft.Extensions.Logging.Abstractions;
using NML.Core;
using NML.Core.Download;
using NML.Core.Modpacks;

namespace NML.Core.Tests;

/// <summary>
/// Verifies the CurseForge modpack path resolves and downloads every mod file when a resolver
/// is available, and degrades gracefully (overrides only) when no API key/resolver is present.
/// Uses a fake resolver + fake fetcher + temp dir — no network.
/// </summary>
public class CurseForgeModpackTests
{
    [Fact]
    public async Task CurseForge_modpack_downloads_all_resolved_mods_when_resolver_present()
    {
        // Build a minimal CurseForge modpack zip in memory: manifest.json with 2 files.
        string zipPath = Path.Combine(Path.GetTempPath(), $"cf-modpack-{Guid.NewGuid():N}.zip");
        using (var fs = File.Create(zipPath))
        using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            AddEntry(zip, "manifest.json", """
                {
                  "minecraft": { "version": "1.20.1", "modLoaders": [{ "id": "fabric-0.15.7", "primary": true }] },
                  "files": [
                    { "projectID": 1, "fileID": 10, "required": true },
                    { "projectID": 2, "fileID": 20, "required": true }
                  ],
                  "overrides": "overrides"
                }
                """);
            AddEntry(zip, "overrides/options.txt", "test");
        }

        // Fake resolver returns a real URL for each requested id pair.
        var fakeResolver = new FakeResolver();
        var fakeFetcher = new ByteArrayFetcher(); // returns canned bytes for any URL
        var downloader = new Downloader(fakeFetcher, NullLogger<Downloader>.Instance);
        var installer = new ModpackInstaller(fakeFetcher, downloader,
            NullLogger<ModpackInstaller>.Instance, curseForgeResolver: fakeResolver);

        string gameDir = Path.Combine(Path.GetTempPath(), $"cf-game-{Guid.NewGuid():N}");
        var mc = new MinecraftDirectory(gameDir);

        await installer.InstallAsync(zipPath, "Test Modpack", mc);

        // Both resolved mods must be downloaded into mods/.
        fakeResolver.ResolveCallCount.Should().Be(1);
        Directory.GetFiles(Path.Combine(gameDir, "mods"))
            .Select(Path.GetFileName)
            .Should().BeEquivalentTo(new[] { "mod-1.jar", "mod-2.jar" });

        // Overrides must still be extracted.
        File.Exists(Path.Combine(gameDir, "options.txt")).Should().BeTrue();

        File.Delete(zipPath);
    }

    [Fact]
    public async Task CurseForge_modpack_skips_mods_but_extracts_overrides_when_no_resolver()
    {
        string zipPath = Path.Combine(Path.GetTempPath(), $"cf-modpack-{Guid.NewGuid():N}.zip");
        using (var fs = File.Create(zipPath))
        using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            AddEntry(zip, "manifest.json",
                """{"minecraft":{"version":"1.20.1"},"files":[{"projectID":1,"fileID":1,"required":true}],"overrides":"overrides"}""");
            AddEntry(zip, "overrides/options.txt", "x");
        }

        var fakeFetcher = new ByteArrayFetcher();
        var installer = new ModpackInstaller(fakeFetcher,
            new Downloader(fakeFetcher, NullLogger<Downloader>.Instance),
            NullLogger<ModpackInstaller>.Instance,
            curseForgeResolver: null); // no API key → no resolver

        string gameDir = Path.Combine(Path.GetTempPath(), $"cf-game-{Guid.NewGuid():N}");
        await installer.InstallAsync(zipPath, "NoKey Pack", new MinecraftDirectory(gameDir));

        // No mods/ dir (nothing downloaded), but overrides are present.
        Directory.Exists(Path.Combine(gameDir, "mods")).Should().BeFalse();
        File.Exists(Path.Combine(gameDir, "options.txt")).Should().BeTrue();

        File.Delete(zipPath);
    }

    private static void AddEntry(ZipArchive zip, string name, string content)
    {
        ZipArchiveEntry e = zip.CreateEntry(name);
        using var s = e.Open();
        using var w = new StreamWriter(s);
        w.Write(content);
    }

    /// <summary>Fake resolver that returns one mod file per (projectID, fileID).</summary>
    private sealed class FakeResolver : ICurseForgeFileResolver
    {
        public int ResolveCallCount { get; private set; }
        public Task<IReadOnlyList<CurseForgeResolvedFile>> ResolveAsync(
            IReadOnlyList<(int ProjectId, int FileId)> ids, CancellationToken ct = default)
        {
            ResolveCallCount++;
            return Task.FromResult<IReadOnlyList<CurseForgeResolvedFile>>(ids.Select(p =>
                new CurseForgeResolvedFile
                {
                    ProjectId = p.ProjectId,
                    FileId = p.FileId,
                    FileName = $"mod-{p.ProjectId}.jar",
                    DownloadUrl = $"https://example.test/mod-{p.ProjectId}.jar",
                    Sha1 = "", // unverified
                    Size = 4, // "DATA"
                }).ToList());
        }
    }
}

/// <summary>Fake IHttpFetcher returning the bytes "DATA" for any URL.</summary>
internal sealed class ByteArrayFetcher : IHttpFetcher
{
    public Task<byte[]> GetByteArrayAsync(string url, CancellationToken ct = default) =>
        Task.FromResult(System.Text.Encoding.UTF8.GetBytes("DATA"));
    public Task<string> GetStringAsync(string url, CancellationToken ct = default) =>
        Task.FromResult("{}");
    public Task StreamToAsync(string url, Stream dest, IProgress<long>? bytesReceived = null, CancellationToken ct = default)
    {
        byte[] b = System.Text.Encoding.UTF8.GetBytes("DATA");
        return dest.WriteAsync(b.AsMemory(0, b.Length), ct).AsTask();
    }
    public Task<RangeResponse?> TryRangeDownloadAsync(string url, long from, long? to, CancellationToken ct = default) =>
        Task.FromResult<RangeResponse?>(null);
}
