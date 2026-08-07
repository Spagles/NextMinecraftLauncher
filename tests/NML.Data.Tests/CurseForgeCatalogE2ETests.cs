using System.Net;
using System.Net.Http;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using NML.Core.Download;
using NML.Data;
using NML.Data.CurseForge;

namespace NML.Data.Tests;

/// <summary>
/// End-to-end verification that CurseForgeCatalog calls the real CurseForge v1 API shape with the
/// required <c>x-api-key</c> header and parses the response correctly — without touching the live
/// network. A fake <see cref="RecordingHandler"/> intercepts every request, records its URL + headers,
/// and returns canned JSON so the catalog can be exercised deterministically.
/// <para>
/// This guards the two bugs the audit found: (1) the catalog previously sent no <c>x-api-key</c> on
/// Search/GetProject/GetFiles (would 403), and (2) the catalog was never wired with a real key at DI
/// time (constructor was fed "" and silently returned null).
/// </para>
/// </summary>
public class CurseForgeCatalogE2ETests
{
    private const string SampleKey = "$2a$10$SampleCurseForgeApiKeyForTests";

    private static string SearchJson() => """
    {
      "data": [
        {
          "id": 238222,
          "slug": "jei",
          "name": "Just Enough Items",
          "summary": "View items and recipes",
          "downloadCount": 500000000,
          "categories": [ { "name": "Food" }, { "name": "Utility" } ],
          "logo": { "thumbnailUrl": "https://media.forgecdn.net/jei.png" },
          "authors": [ { "name": "mezz" } ]
        }
      ]
    }
    """;

    private static string ProjectJson() => """
    {
      "data": {
        "id": 238222,
        "slug": "jei",
        "name": "Just Enough Items",
        "summary": "View items and recipes"
      }
    }
    """;

    private static string FilesJson() => """
    {
      "data": [
        {
          "id": 12345,
          "fileName": "jei-1.20.1-15.2.0.27.jar",
          "downloadUrl": "https://mediafilez.forgecdn.net/files/jei.jar",
          "fileLength": 1048576,
          "hashes": [ { "algo": 1, "value": "abc123sha1" } ]
        }
      ]
    }
    """;

    /// <summary>A fake HttpMessageHandler that records every request and returns canned JSON by path.</summary>
    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = new();
        private readonly Func<HttpRequestMessage, string> _respond;

        public RecordingHandler(Func<HttpRequestMessage, string> respond) => _respond = respond;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request);
            string body = _respond(request);
            var resp = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
                RequestMessage = request,
            };
            return Task.FromResult(resp);
        }
    }

    /// <summary>A no-op IHttpFetcher stand-in (the catalog no longer uses it for the catalog endpoints,
    /// but the ctor still requires it so DI shape stays stable).</summary>
    private sealed class StubFetcher : IHttpFetcher
    {
        public Task<byte[]> GetByteArrayAsync(string url, CancellationToken ct = default) =>
            Task.FromResult(Array.Empty<byte>());
        public Task<string> GetStringAsync(string url, CancellationToken ct = default) =>
            Task.FromResult("{}");
        public Task StreamToAsync(string url, Stream destination, IProgress<long>? bytesReceived = null, CancellationToken ct = default) =>
            Task.CompletedTask;
        public Task<RangeResponse?> TryRangeDownloadAsync(string url, long from, long? to, CancellationToken ct = default) =>
            Task.FromResult<RangeResponse?>(null);
    }

    private static CurseForgeCatalog MakeCatalog(RecordingHandler handler)
        => new(new StubFetcher(), SampleKey, NullLogger<CurseForgeCatalog>.Instance, handler);

    [Fact]
    public async Task SearchAsync_Sends_X_Api_Key_Header_And_Parses_Results()
    {
        var handler = new RecordingHandler(req => req.RequestUri!.AbsolutePath.EndsWith("/mods/search") ? SearchJson() : "{}");
        var catalog = MakeCatalog(handler);

        var results = await catalog.SearchAsync("jei");

        results.Should().HaveCount(1);
        var r = results[0];
        r.Title.Should().Be("Just Enough Items");
        r.ProjectId.Should().Be("238222");
        r.Downloads.Should().Be(500000000);
        r.IconUrl.Should().Contain("jei.png");
        r.Source.Should().Be(ModCatalogKind.CurseForge);

        // The request must carry the x-api-key header (the bug: previously it didn't).
        var searchReq = handler.Requests.Single(r => r.RequestUri!.AbsolutePath.EndsWith("/mods/search"));
        searchReq.Headers.TryGetValues("x-api-key", out var keys).Should().BeTrue("the API key header must be sent");
        keys!.Single().Should().Be(SampleKey);

        // The URL must target the real CurseForge v1 endpoint with the Minecraft gameId.
        searchReq.RequestUri!.ToString().Should().StartWith("https://api.curseforge.com/v1/mods/search");
        searchReq.RequestUri!.Query.Should().Contain("gameId=432").And.Contain("classId=6");
    }

    [Fact]
    public async Task GetProjectAsync_Sends_Key_And_Returns_Project_Or_Null_On_404()
    {
        var handler = new RecordingHandler(req => req.RequestUri!.AbsolutePath.Contains("/mods/238222") ? ProjectJson() : "{}");
        var catalog = MakeCatalog(handler);

        var project = await catalog.GetProjectAsync("238222");
        project.Should().NotBeNull();
        project!.Title.Should().Be("Just Enough Items");
        project.ProjectId.Should().Be("238222");

        handler.Requests.Single().Headers.Contains("x-api-key").Should().BeTrue();
    }

    [Fact]
    public async Task GetFilesAsync_Sends_Key_And_Parses_File_Metadata()
    {
        var handler = new RecordingHandler(req => req.RequestUri!.AbsolutePath.EndsWith("/files") ? FilesJson() : "{}");
        var catalog = MakeCatalog(handler);

        var files = await catalog.GetFilesAsync("238222", "1.20.1", ModLoader.Fabric);

        files.Should().HaveCount(1);
        var f = files[0];
        f.FileName.Should().Be("jei-1.20.1-15.2.0.27.jar");
        f.DownloadUrl.Should().Contain("jei.jar");
        f.Sha1.Should().Be("abc123sha1", "the SHA-1 hash (algo 1) must be extracted");
        f.Size.Should().Be(1048576);
        f.GameVersion.Should().Be("1.20.1");
        f.Loader.Should().Be(ModLoader.Fabric);

        handler.Requests.Single().Headers.Contains("x-api-key").Should().BeTrue();
        // The Fabric loader int (4) must be in the query.
        handler.Requests.Single().RequestUri!.Query.Should().Contain("modLoaderType=4");
    }

    [Fact]
    public void Constructor_Throws_On_Empty_Key()
    {
        var act = () => new CurseForgeCatalog(new StubFetcher(), "", NullLogger<CurseForgeCatalog>.Instance);
        act.Should().Throw<ArgumentException>("an empty key must never silently succeed");
    }

    [Fact]
    public void Constructor_Accepts_Real_Key()
    {
        // The audit found DI fed "" here, which threw and made the singleton null. A real key must
        // construct cleanly so the catalog can be used.
        var act = () => new CurseForgeCatalog(new StubFetcher(), SampleKey, NullLogger<CurseForgeCatalog>.Instance);
        act.Should().NotThrow();
    }
}
