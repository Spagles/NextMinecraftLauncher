using System.Threading;
using Microsoft.Extensions.Logging.Abstractions;
using NML.App.Remote;
using NML.Core;
using NML.Core.Instances;
using NML.Core.Models;

namespace NML.App.Tests;

/// <summary>
/// Validates the remote-API routing (the part that must work without a real network).
/// Uses stubbed engine pieces; the HTTP server itself is covered by the runtime smoke test.
/// </summary>
public class RemoteApiRoutingTests
{
    private static RemoteApiServer MakeServer(RemoteApiHandlers? handlers = null)
    {
        var manifest = new VersionManifestService(new FakeFetcher(), NullLogger<VersionManifestService>.Instance);
        var instances = new InstanceStore(Path.Combine(Path.GetTempPath(), "nml-test-" + Guid.NewGuid().ToString("N")[..8]));
        handlers ??= new RemoteApiHandlers(manifest, instances, _ => Task.FromResult<object?>(new { ok = true })!);
        return new RemoteApiServer(handlers, port: 0, logger: NullLogger<RemoteApiServer>.Instance);
    }

    [Fact]
    public async Task Unknown_path_returns_404()
    {
        RemoteApiServer server = MakeServer();
        var (status, _) = await server.RouteAsync("/api/nonexistent", "GET", default);
        status.Should().Be(404);
    }

    [Fact]
    public async Task Get_instances_returns_200()
    {
        RemoteApiServer server = MakeServer();
        var (status, body) = await server.RouteAsync("/api/instances", "GET", default);
        status.Should().Be(200);
        body.Should().NotBeNull();
    }

    [Fact]
    public async Task Get_versions_returns_200()
    {
        RemoteApiServer server = MakeServer();
        var (status, body) = await server.RouteAsync("/api/versions", "GET", default);
        status.Should().Be(200);
        body.Should().NotBeNull();
    }

    [Fact]
    public async Task Install_endpoint_returns_202_queued()
    {
        RemoteApiServer server = MakeServer();
        var (status, body) = await server.RouteAsync("/api/install/1.20.1", "POST", default);
        status.Should().Be(202);
        body!.GetType().GetProperty("versionId")!.GetValue(body).Should().Be("1.20.1");
    }

    [Fact]
    public async Task Diagnose_endpoint_delegates_to_handler()
    {
        RemoteApiServer server = MakeServer();
        (int status, object? _) = await server.RouteAsync("/api/diagnose/myinstance", "GET", default);
        status.Should().Be(200);
    }
}

/// <summary>Minimal fake IHttpFetcher returning canned manifest JSON.</summary>
internal sealed class FakeFetcher : NML.Core.Download.IHttpFetcher
{
    public Task<byte[]> GetByteArrayAsync(string url, CancellationToken ct = default) =>
        Task.FromResult(Array.Empty<byte>());
    public Task<string> GetStringAsync(string url, CancellationToken ct = default)
    {
        string manifest = "{\"latest\":{\"release\":\"1.20.1\",\"snapshot\":\"1.20.1\"},\"versions\":[" +
            "{\"id\":\"1.20.1\",\"type\":\"release\",\"url\":\"https://x/1.20.1.json\",\"time\":\"2023-01-01T00:00:00Z\",\"releaseTime\":\"2023-01-01T00:00:00Z\",\"sha1\":\"a\",\"complianceLevel\":1}" +
            "]}";
        return Task.FromResult(manifest);
    }
    public Task StreamToAsync(string url, Stream destination, IProgress<long>? bytesReceived = null, CancellationToken ct = default) =>
        Task.CompletedTask;
    public Task<NML.Core.Download.RangeResponse?> TryRangeDownloadAsync(string url, long from, long? to, CancellationToken ct = default) =>
        Task.FromResult<NML.Core.Download.RangeResponse?>(null);
}
