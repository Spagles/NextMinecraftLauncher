using System.Security.Cryptography;

namespace NML.Core.Download;

/// <summary>SHA-1 hashing helpers (Mojang uses SHA-1 throughout for integrity).</summary>
public static class Sha1
{
    /// <summary>Compute the hex SHA-1 of a byte slice (e.g. for offline-mode UUID/profile hashing).</summary>
    public static string OfBytes(ReadOnlySpan<byte> bytes)
    {
        Span<byte> hash = stackalloc byte[20];
        using var sha = SHA1.Create();
        sha.TryComputeHash(bytes, hash, out _);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>Stream the file at <paramref name="path"/> and return its hex SHA-1.</summary>
    public static async Task<string> OfFileAsync(string path, CancellationToken ct = default)
    {
        await using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 81920, options: FileOptions.Asynchronous | FileOptions.SequentialScan);

        using var sha = SHA1.Create();
        byte[] hash = await sha.ComputeHashAsync(stream, ct);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>True if the file's SHA-1 matches the expected value. Missing file → false.</summary>
    public static async Task<bool> FileMatchesAsync(string path, string expectedSha1, CancellationToken ct = default)
    {
        if (!File.Exists(path)) return false;
        string actual = await OfFileAsync(path, ct);
        return string.Equals(actual, expectedSha1, StringComparison.OrdinalIgnoreCase);
    }
}
