using NML.Core;

namespace NML.Core.Tests;

/// <summary>
/// Verifies <see cref="ManifestDiskCache"/> — the disk-persistence layer behind the version
/// manifest service. Saves/loads the manifest JSON, reports freshness via TTL, and clears cleanly.
/// </summary>
public class ManifestDiskCacheTests
{
    private static string TempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "nml-mdc-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void Save_Load_RoundTrips_Json()
    {
        string dir = TempDir();
        try
        {
            var cache = new ManifestDiskCache(dir);
            cache.Save("""{"latest":{"release":"1.20.1","snapshot":"1.20.2"},"versions":[]}""");
            cache.Load().Should().NotBeNull();
            cache.Load()!.Should().Contain("1.20.1");
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void IsFresh_True_After_Save()
    {
        string dir = TempDir();
        try
        {
            var cache = new ManifestDiskCache(dir);
            cache.IsFresh().Should().BeFalse(); // no file yet
            cache.Save("data");
            cache.IsFresh().Should().BeTrue();
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void IsFresh_False_When_Older_Than_Ttl()
    {
        string dir = TempDir();
        try
        {
            var cache = new ManifestDiskCache(dir);
            cache.Save("data");
            // Set the file's last-write time to 7 hours ago (past the 6h default TTL).
            File.SetLastWriteTimeUtc(cache.CacheFilePath, DateTime.UtcNow.AddHours(-7));
            cache.IsFresh().Should().BeFalse();
            // With a custom TTL of 12h, it would be fresh.
            cache.IsFresh(TimeSpan.FromHours(12)).Should().BeTrue();
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Load_Returns_Null_When_No_File()
    {
        string dir = TempDir();
        try
        {
            var cache = new ManifestDiskCache(dir);
            cache.Load().Should().BeNull();
            cache.IsFresh().Should().BeFalse();
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Clear_Removes_File()
    {
        string dir = TempDir();
        try
        {
            var cache = new ManifestDiskCache(dir);
            cache.Save("data");
            File.Exists(cache.CacheFilePath).Should().BeTrue();
            cache.Clear();
            File.Exists(cache.CacheFilePath).Should().BeFalse();
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Save_Empty_String_Does_Not_Write()
    {
        string dir = TempDir();
        try
        {
            var cache = new ManifestDiskCache(dir);
            cache.Save("");
            File.Exists(cache.CacheFilePath).Should().BeFalse();
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
