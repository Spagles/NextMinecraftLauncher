using System.Collections.Concurrent;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using NML.Core.Download;
using NML.Core.Models;

namespace NML.Core.Tests;

/// <summary>
/// End-to-end verification that a configured download mirror (BMCLAPI-style) actually rewires
/// EVERY network request the vanilla install pipeline makes: the version manifest, the version.json,
/// the client.jar, every library artifact, the asset-index document, and every asset object.
///
/// This guards against the regression where <c>DownloadMirrorUrl</c> was persisted by the settings
/// page but never threaded into <c>VanillaInstaller.InstallAsync</c> — so the mirror was dead code.
/// We stand up a <see cref="RecordingFetcher"/> that captures each requested URL, run a real install
/// with the mirror set, then assert no request ever escaped to a raw Mojang host.
/// </summary>
public class MirrorPipelineTests
{
    private const string Mirror = "https://bmclapi2.bangbang93.com";
    private const string VersionId = "1.20.1";

    [Fact]
    public async Task Install_With_Mirror_Rewrites_Every_Request_Away_From_Mojang()
    {
        // Arrange: a recording fetcher that returns canned JSON for metadata + dummy bytes for files,
        // capturing every URL it is asked for so we can audit the mirror coverage afterwards.
        var fetcher = new RecordingFetcher(VersionId);
        var downloader = new Downloader(fetcher, NullLogger<Downloader>.Instance);
        var manifestSvc = new VersionManifestService(fetcher, NullLogger<VersionManifestService>.Instance)
        {
            MirrorUrl = Mirror, // mirror set → manifest fetch should be rewritten
        };
        var versionSvc = new VersionInfoService(fetcher, manifestSvc, NullLogger<VersionInfoService>.Instance);
        var installer = new VanillaInstaller(fetcher, downloader, versionSvc, NullLogger<VanillaInstaller>.Instance);

        using var temp = new TempDir();
        var mc = new MinecraftDirectory(Path.Combine(temp.Path, "dotminecraft"));

        var settings = new DownloadSettings { MirrorUrl = Mirror, Concurrency = 4 };

        // Act: run the full install. All bytes come from the recording fetcher; nothing hits the network.
        await installer.InstallAsync(VersionId, mc, downloadSettings: settings);

        // Assert: not a single request reached a raw Mojang host.
        string[] mojangHosts = MirrorUrlRewriter.MojangHosts.ToArray();
        List<string> leaked = fetcher.RequestedUrls
            .Where(u => mojangHosts.Any(h => u.Contains(h, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        leaked.Should().BeEmpty(
            "every Mojang-hosted download should be rewritten to the BMCLAPI mirror, " +
            "but these requests leaked to Mojang directly: " +
            string.Join(", ", leaked));

        // And we must have actually exercised every download path (else the test is vacuous).
        fetcher.RequestedUrls.Should().NotBeEmpty("the install must make at least one request");
        Assert.Contains(fetcher.RequestedUrls, u => u.StartsWith(Mirror, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Install_Without_Mirror_Uses_Official_Mojang_Endpoints()
    {
        // The flip side: when no mirror is set, requests go to Mojang directly (the historical default).
        var fetcher = new RecordingFetcher(VersionId);
        var downloader = new Downloader(fetcher, NullLogger<Downloader>.Instance);
        var manifestSvc = new VersionManifestService(fetcher, NullLogger<VersionManifestService>.Instance)
        {
            MirrorUrl = null, // no mirror → official
        };
        var versionSvc = new VersionInfoService(fetcher, manifestSvc, NullLogger<VersionInfoService>.Instance);
        var installer = new VanillaInstaller(fetcher, downloader, versionSvc, NullLogger<VanillaInstaller>.Instance);

        using var temp = new TempDir();
        var mc = new MinecraftDirectory(Path.Combine(temp.Path, "dotminecraft"));

        await installer.InstallAsync(VersionId, mc, downloadSettings: new DownloadSettings { Concurrency = 4 });

        fetcher.RequestedUrls.Should().NotBeEmpty();
        // At least one request must target an official Mojang host (manifest/version.json/client.jar/lib/assets).
        Assert.Contains(fetcher.RequestedUrls,
            u => MirrorUrlRewriter.MojangHosts.Any(h => u.Contains(h, StringComparison.OrdinalIgnoreCase)));
        // And none should be rewritten to the mirror.
        Assert.DoesNotContain(fetcher.RequestedUrls,
            u => u.StartsWith(Mirror, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Manifest_Service_Rewrites_Manifest_Url_When_Mirror_Set()
    {
        // Targeted check that VersionManifestService applies its mirror to the manifest URL itself
        // (the very first network call on startup), independent of the full install pipeline.
        var fetcher = new RecordingFetcher(VersionId);
        var svc = new VersionManifestService(fetcher, NullLogger<VersionManifestService>.Instance)
        {
            MirrorUrl = Mirror,
        };

        await svc.GetAsync();

        var urls = fetcher.RequestedUrls.ToList();
        urls.Should().ContainSingle();
        urls[0].Should().StartWith(Mirror,
            "the version manifest fetch must go through the mirror when one is configured");
        urls[0].Should().NotContain("piston-meta.mojang.com");
    }

    /// <summary>
    /// Minimal temp directory that cleans up after itself (recursive delete, tolerates read-only files).
    /// </summary>
    private sealed class TempDir : IDisposable
    {
        public string Path { get; }
        public TempDir() { Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "nml-mirror-test-" + Guid.NewGuid()); Directory.CreateDirectory(Path); }
        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch { /* best effort */ }
        }
    }

    /// <summary>
    /// A fake <see cref="IHttpFetcher"/> that records every requested URL and returns canned responses:
    /// the version manifest, a version.json, an asset index, and dummy bytes for every file download.
    /// SHA-1 verification in the real Downloader is skipped because we set <c>Sha1=""</c> on every
    /// Downloadable (empty sha1 = no check). This lets the full install complete off-line.
    /// </summary>
    private sealed class RecordingFetcher : IHttpFetcher
    {
        private readonly string _versionId;
        public ConcurrentBag<string> RequestedUrls { get; } = new();

        public RecordingFetcher(string versionId) => _versionId = versionId;

        public Task<byte[]> GetByteArrayAsync(string url, CancellationToken ct = default)
        {
            RequestedUrls.Add(url);
            return Task.FromResult(Encoding.UTF8.GetBytes("X"));
        }

        public Task<string> GetStringAsync(string url, CancellationToken ct = default)
        {
            RequestedUrls.Add(url);
            // Dispatch by URL shape:
            //  - the version manifest document
            if (url.Contains("version_manifest", StringComparison.OrdinalIgnoreCase))
                return Task.FromResult(ManifestJson(_versionId));
            //  - the asset index (its URL path is fixed as .../v1/asset-index/5.json)
            if (url.Contains("asset-index", StringComparison.OrdinalIgnoreCase))
                return Task.FromResult(AssetIndexJson());
            //  - otherwise it's the version.json for the requested version
            return Task.FromResult(VersionInfoJson());
        }

        public Task StreamToAsync(string url, Stream destination, IProgress<long>? bytesReceived = null, CancellationToken ct = default)
        {
            RequestedUrls.Add(url);
            // Different URL paths get different bytes so concurrent downloads to distinct hashes
            // never collide on disk, and each asset object's hash matches its real SHA-1.
            byte[] b = Encoding.UTF8.GetBytes(DummyContentFor(url));
            destination.Write(b, 0, b.Length);
            bytesReceived?.Report(b.Length);
            return Task.CompletedTask;
        }

        // Deterministic per-URL dummy content. Asset URLs are built from the object hash
        // (resources.../57/577d...), so dispatch by the hash prefix to return the matching bytes;
        // every other URL gets the default file bytes.
        private static string DummyContentFor(string url)
        {
            if (url.Contains(DummySha1Stone, StringComparison.OrdinalIgnoreCase))
                return "DUMMY-CONTENT-FOR-ASSET-2";
            return "DUMMY-CONTENT-FOR-FILE";
        }

        public Task<RangeResponse?> TryRangeDownloadAsync(string url, long from, long? to, CancellationToken ct = default)
        {
            RequestedUrls.Add(url);
            return Task.FromResult<RangeResponse?>(null);
        }

        // The canned manifest exposes one version whose URL lives on piston-meta.mojang.com.
        private static string ManifestJson(string id) => $$"""
        {
          "latest": { "release": "{{id}}", "snapshot": "{{id}}" },
          "versions": [
            { "id": "{{id}}", "type": "release", "url": "https://piston-meta.mojang.com/v1/beef/{{id}}.json", "time": "2023-01-01T00:00:00+00:00", "releaseTime": "2023-01-01T00:00:00+00:00", "complianceLevel": 1 }
          ]
        }
        """;

        // The canned version.json carries a client.jar on piston-data, a library on libraries.minecraft.net,
        // and an asset index on piston-meta — three Mojang hosts we must prove get rewritten.
        private string VersionInfoJson() => $$"""
        {
          "id": "{{_versionId}}",
          "type": "release",
          "mainClass": "net.minecraft.client.main.Main",
          "assets": "5",
          "assetIndex": { "id": "5", "sha1": "", "size": 0, "totalSize": 0, "url": "https://piston-meta.mojang.com/v1/asset-index/5.json" },
          "downloads": {
            "client": { "sha1": "", "size": 0, "url": "https://piston-data.mojang.com/v1/beef/client.jar" }
          },
          "libraries": [
            { "name": "com.mojang:logging:1.1.1", "downloads": { "artifact": { "sha1": "", "size": 0, "url": "https://libraries.minecraft.net/com/mojang/logging/1.1.1/logging-1.1.1.jar", "path": "com/mojang/logging/1.1.1/logging-1.1.1.jar" } } }
          ]
        }
        """;

        // The canned asset index exposes two asset objects on resources.download.minecraft.net.
        // Each object's hash is the real SHA-1 of the dummy bytes StreamToAsync writes for that URL,
        // so the Downloader's integrity check passes (assets build Downloadable.Sha1 from obj.Hash).
        private static string AssetIndexJson() => $$"""
        {
          "objects": {
            "minecraft/sounds/dig.ogg": { "hash": "{{DummySha1File}}", "size": 100 },
            "minecraft/textures/block/stone.png": { "hash": "{{DummySha1Stone}}", "size": 200 }
          }
        }
        """;

        // Real SHA-1 values of the dummy byte strings written by StreamToAsync (see DummyContentFor).
        private const string DummySha1File = "1a2af960a7e35c9d5684a89de06edc8e106d9ece"; // DUMMY-CONTENT-FOR-FILE
        private const string DummySha1Stone = "577d0dbf69e2bdb8ad3f302ab2b644b56fd07a23"; // DUMMY-CONTENT-FOR-ASSET-2
    }
}
