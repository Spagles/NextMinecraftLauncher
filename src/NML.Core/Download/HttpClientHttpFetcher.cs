using System.Net;
using System.Net.Http.Headers;

namespace NML.Core.Download;

/// <summary>
/// Default <see cref="IHttpFetcher"/> backed by a shared <see cref="HttpClient"/>.
/// The HttpClient should be supplied via DI (HttpClientFactory) so connection
/// pooling and timeouts are configured once.
/// </summary>
public sealed class HttpClientHttpFetcher : IHttpFetcher
{
    private readonly HttpClient _client;

    public HttpClientHttpFetcher(HttpClient client)
    {
        _client = client;
    }

    public async Task<byte[]> GetByteArrayAsync(string url, CancellationToken ct = default)
    {
        using var resp = await _client.GetAsync(url, HttpCompletionOption.ResponseContentRead, ct);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadAsByteArrayAsync(ct);
    }

    public async Task<string> GetStringAsync(string url, CancellationToken ct = default)
    {
        using var resp = await _client.GetAsync(url, HttpCompletionOption.ResponseContentRead, ct);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadAsStringAsync(ct);
    }

    public async Task StreamToAsync(
        string url,
        Stream destination,
        IProgress<long>? bytesReceived = null,
        CancellationToken ct = default)
    {
        using var resp = await _client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();

        await using Stream source = await resp.Content.ReadAsStreamAsync(ct);
        var buffer = new byte[81920];
        long total = 0;
        int read;

        while ((read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), ct)) > 0)
        {
            await destination.WriteAsync(buffer.AsMemory(0, read), ct);
            total += read;
            bytesReceived?.Report(total);
        }
    }

    public async Task<RangeResponse?> TryRangeDownloadAsync(
        string url, long from, long? to, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        var range = new RangeHeaderValue(from, to);
        req.Headers.Range = range;

        using var resp = await _client.SendAsync(
            req, HttpCompletionOption.ResponseHeadersRead, ct);

        // 206 = Partial Content means the server honored the range request.
        // 200 = full body (server doesn't support ranges) — caller should treat as "no resume".
        if (resp.StatusCode != HttpStatusCode.PartialContent)
            return null;

        Stream stream = await resp.Content.ReadAsStreamAsync(ct);
        return new RangeResponse
        {
            Stream = stream,
            ContentLength = resp.Content.Headers.ContentLength ?? 0,
        };
    }
}
