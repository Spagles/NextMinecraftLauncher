using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using NML.Core;
using NML.Core.Instances;
using NML.Core.Models;
using NML.Data;

namespace NML.App.Remote;

/// <summary>
/// A small local HTTP server that exposes the launcher to a remote-management mobile client
/// (browsing instances, triggering installs, fetching crash diagnoses — NOT on-device play).
/// Designed to be unit-tested via injectable handlers; the HTTP plumbing is thin.
/// </summary>
public sealed class RemoteApiServer : IDisposable
{
    private readonly RemoteApiHandlers _handlers;
    private readonly ILogger<RemoteApiServer> _logger;
    private HttpListener? _listener;
    private CancellationTokenSource? _cts;

    public int Port { get; }
    public bool IsRunning => _listener?.IsListening ?? false;

    public RemoteApiServer(RemoteApiHandlers handlers, int port, ILogger<RemoteApiServer> logger)
    {
        _handlers = handlers;
        Port = port;
        _logger = logger;
    }

    public void Start()
    {
        _cts = new CancellationTokenSource();
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://+:{Port}/");
        _listener.Start();
        _ = AcceptLoop(_cts.Token);
        _logger.LogInformation("Remote API listening on port {Port}.", Port);
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _listener?.Stop();
        _listener?.Close();
    }

    private async Task AcceptLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _listener!.IsListening)
        {
            HttpListenerContext ctx;
            try { ctx = await _listener.GetContextAsync(); }
            catch (HttpListenerException) { break; } // stopped

            try { await DispatchAsync(ctx, ct); }
            catch (Exception ex) { _logger.LogError(ex, "Remote API dispatch error."); }
        }
    }

    /// <summary>Route a single request to a handler and write the JSON response.</summary>
    public async Task DispatchAsync(HttpListenerContext ctx, CancellationToken ct)
    {
        var req = ctx.Request;
        var resp = ctx.Response;

        // Simple prefix routing. Real routing would be more elaborate; this covers the MVP.
        (int status, object? body) = await RouteAsync(req.Url?.AbsolutePath ?? "", req.HttpMethod, ct);

        byte[] bytes = body is null ? Array.Empty<byte>() :
            Encoding.UTF8.GetBytes(JsonSerializer.Serialize(body));
        resp.StatusCode = status;
        resp.ContentType = "application/json";
        resp.ContentLength64 = bytes.Length;
        await resp.OutputStream.WriteAsync(bytes.AsMemory(0, bytes.Length), ct);
        resp.Close();
    }

    public async Task<(int Status, object? Body)> RouteAsync(string path, string method, CancellationToken ct)
    {
        try
        {
            if (path == "/api/instances" && method == "GET")
                return (200, await _handlers.ListInstancesAsync(ct));
            if (path == "/api/versions" && method == "GET")
                return (200, await _handlers.ListAvailableVersionsAsync(ct));
            if (path.StartsWith("/api/install/") && method == "POST")
                return (202, new { status = "queued", versionId = path["/api/install/".Length..] });
            if (path.StartsWith("/api/diagnose/") && method == "GET")
            {
                string id = path["/api/diagnose/".Length..];
                return (200, await _handlers.DiagnoseAsync(id, ct));
            }
            if (path == "/api/mods/search" && method == "GET")
                return (400, new { error = "missing ?q=" });
            return (404, new { error = "not found" });
        }
        catch (Exception ex)
        {
            return (500, new { error = ex.Message });
        }
    }
}

/// <summary>
/// The actual business logic the HTTP server delegates to. Injected so unit tests can stub
/// the engine pieces (manifest, instance store, crash analyzer) without spinning up a server.
/// </summary>
public sealed class RemoteApiHandlers
{
    private readonly VersionManifestService _manifest;
    private readonly InstanceStore _instances;
    private readonly Func<string, Task<object?>> _diagnoseCrash;

    public RemoteApiHandlers(
        VersionManifestService manifest,
        InstanceStore instances,
        Func<string, Task<object?>> diagnoseCrash)
    {
        _manifest = manifest;
        _instances = instances;
        _diagnoseCrash = diagnoseCrash;
    }

    public Task<IReadOnlyList<Instance>> ListInstancesAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<Instance>>(_instances.LoadAll());

    public async Task<IReadOnlyList<object>> ListAvailableVersionsAsync(CancellationToken ct)
    {
        VersionManifest m = await _manifest.GetAsync(ct: ct);
        return m.Versions.Take(50)
            .Select(v => (object)new { v.Id, v.Type, v.ReleaseTime })
            .ToList();
    }

    public Task<object?> DiagnoseAsync(string instanceId, CancellationToken ct) =>
        _diagnoseCrash(instanceId);
}
