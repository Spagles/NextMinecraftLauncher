using NML.Core.Instances;

namespace NML.Core.Tests;

/// <summary>
/// Verifies <see cref="InstanceStore.Rename"/> — the instance-rename feature (HMCL parity:
/// rename an instance in the store and move its isolated game directory on disk).
/// </summary>
public class InstanceRenameTests
{
    private static (string settingsDir, InstanceStore store) MakeStore()
    {
        string dir = Path.Combine(Path.GetTempPath(), "nml-inst-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        return (dir, new InstanceStore(dir));
    }

    private static Instance AddInstance(InstanceStore store, string name, bool isolated = true,
        string? fileContent = null)
    {
        var inst = new Instance { Name = name, VersionId = "1.20.1", IsIsolated = isolated };
        store.Add(inst);
        if (fileContent is not null)
        {
            string gameDir = store.GameDirFor(name);
            Directory.CreateDirectory(gameDir);
            File.WriteAllText(Path.Combine(gameDir, "marker.txt"), fileContent);
        }
        return inst;
    }

    [Fact]
    public void Rename_Updates_Store_And_Moves_Game_Dir()
    {
        var (dir, store) = MakeStore();
        try
        {
            AddInstance(store, "Old", fileContent: "hello");
            string oldDir = store.GameDirFor("Old");

            var renamed = store.Rename("Old", "New");

            renamed.Name.Should().Be("New");
            store.LoadAll().Should().ContainSingle(i => i.Name == "New");
            store.LoadAll().Should().NotContain(i => i.Name == "Old");
            // The isolated game directory moved with the rename.
            Directory.Exists(oldDir).Should().BeFalse("the old directory is gone");
            string newDir = store.GameDirFor("New");
            Directory.Exists(newDir).Should().BeTrue();
            File.ReadAllText(Path.Combine(newDir, "marker.txt")).Should().Be("hello");
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Rename_Same_Name_Is_Noop()
    {
        var (dir, store) = MakeStore();
        try
        {
            AddInstance(store, "Same");
            var renamed = store.Rename("Same", "Same");
            renamed.Name.Should().Be("Same");
            store.LoadAll().Should().ContainSingle(i => i.Name == "Same");
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Rename_Rejects_Existing_Target_Name()
    {
        var (dir, store) = MakeStore();
        try
        {
            AddInstance(store, "A");
            AddInstance(store, "B");
            var act = () => store.Rename("A", "B");
            act.Should().Throw<InvalidOperationException>("renaming onto an existing instance must be rejected");
            // Nothing changed.
            store.LoadAll().Select(i => i.Name).Should().BeEquivalentTo(new[] { "A", "B" });
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Rename_Rejects_Empty_Name()
    {
        var (dir, store) = MakeStore();
        try
        {
            AddInstance(store, "X");
            var act = () => store.Rename("X", "  ");
            act.Should().Throw<ArgumentException>();
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Rename_Unknown_Instance_Throws()
    {
        var (dir, store) = MakeStore();
        try
        {
            var act = () => store.Rename("Ghost", "Whatever");
            act.Should().Throw<KeyNotFoundException>();
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Rename_NonIsolated_Only_Changes_Store()
    {
        // Non-isolated instances share the common .minecraft — renaming must not move anything.
        var (dir, store) = MakeStore();
        try
        {
            AddInstance(store, "Shared", isolated: false);
            var renamed = store.Rename("Shared", "Shared2");
            renamed.Name.Should().Be("Shared2");
            renamed.IsIsolated.Should().BeFalse();
            // No isolated directory should have been created for either name.
            Directory.Exists(store.GameDirFor("Shared")).Should().BeFalse();
            Directory.Exists(store.GameDirFor("Shared2")).Should().BeFalse();
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Rename_Preserves_Instance_Settings()
    {
        var (dir, store) = MakeStore();
        try
        {
            var inst = AddInstance(store, "Cfg");
            inst.MaxMemoryMb = 4096;
            inst.CustomJvmArgs = "-XX:+UseG1GC";
            inst.IsFavorite = true;
            store.Add(inst);

            var renamed = store.Rename("Cfg", "Cfg2");
            renamed.MaxMemoryMb.Should().Be(4096);
            renamed.CustomJvmArgs.Should().Be("-XX:+UseG1GC");
            renamed.IsFavorite.Should().BeTrue();
            renamed.VersionId.Should().Be("1.20.1");
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
