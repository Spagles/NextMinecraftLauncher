using NML.Core.Instances;

namespace NML.Core.Tests;

/// <summary>
/// Verifies the IsFavorite field on Instance: defaults false, round-trips through persistence,
/// and is preserved by Clone().
/// </summary>
public class InstanceFavoriteTests
{
    private static string TempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "nml-fav-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void IsFavorite_Defaults_False()
    {
        new Instance().IsFavorite.Should().BeFalse();
    }

    [Fact]
    public void IsFavorite_RoundTrips_Through_Persistence()
    {
        string dir = TempDir();
        try
        {
            var store = new InstanceStore(dir);
            store.Add(new Instance { Name = "Fav", VersionId = "1.20.1", IsFavorite = true });
            store.Add(new Instance { Name = "Regular", VersionId = "1.20.1", IsFavorite = false });

            var loaded = store.LoadAll();
            loaded.Single(i => i.Name == "Fav").IsFavorite.Should().BeTrue();
            loaded.Single(i => i.Name == "Regular").IsFavorite.Should().BeFalse();
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Clone_Preserves_IsFavorite()
    {
        string dir = TempDir();
        try
        {
            var store = new InstanceStore(dir);
            store.Add(new Instance { Name = "Starred", VersionId = "1.20.1", IsFavorite = true });
            var source = store.LoadAll().Single();

            Instance clone = store.Clone(source, "Starred (copy)");
            clone.IsFavorite.Should().BeTrue();
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Toggle_IsFavorite_RoundTrips()
    {
        string dir = TempDir();
        try
        {
            var store = new InstanceStore(dir);
            var inst = new Instance { Name = "Toggle", VersionId = "1.20.1", IsFavorite = true };
            store.Add(inst);

            // Toggle off (Instance is a class, not a record).
            var all = store.LoadAll();
            all[0].IsFavorite = false;
            store.SaveAll(all);

            store.LoadAll().Single().IsFavorite.Should().BeFalse();
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
