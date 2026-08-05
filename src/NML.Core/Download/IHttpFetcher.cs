namespace NML.Core.Download;

/// <summary>
/// Minimal async byte-fetching abstraction over HTTP, designed so that the
/// downloader can be unit-tested with a fake instead of a real network round-trip.
/// Implementations wrap <c>HttpClient</c> in production (via DI) but can return
/// canned bytes in tests.
/// </summary>
public interface IHttpFetcher
{
    /// <summary>Fetch the entire body of <paramref name="url"/> as bytes.</summary>
    Task<byte[]> GetByteArrayAsync(string url, CancellationToken ct = default);

    /// <summary>Fetch the body of <paramref name="url"/> as a UTF-8 string.</summary>
    Task<string> GetStringAsync(string url, CancellationToken ct = default);

    /// <summary>
    /// Stream the body of <paramref name="url"/> into <paramref name="destination"/>,
    /// reporting bytes received. Used for large downloads (client.jar, libraries, assets).
    /// </summary>
    Task StreamToAsync(
        string url,
        Stream destination,
        IProgress<long>? bytesReceived = null,
        CancellationToken ct = default);

    /// <summary>Optional range-download (resume) support. Implementations may return null.</summary>
    Task<RangeResponse?> TryRangeDownloadAsync(
        string url, long from, long? to, CancellationToken ct = default);
}

/// <summary>Result of a ranged (resume) download attempt. Null when the server refused ranges.</summary>
public sealed class RangeResponse
{
    public required Stream Stream { get; init; }
    public required long ContentLength { get; init; }
}
