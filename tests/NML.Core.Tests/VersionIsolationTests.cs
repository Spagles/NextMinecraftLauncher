using NML.Core.Instances;

namespace NML.Core.Tests;

/// <summary>
/// Verifies the version-isolation feature: each <see cref="Instance"/> carries an
/// <see cref="Instance.IsIsolated"/> flag (default true), and <see cref="InstanceStore.GameDirFor(Instance)"/>
/// resolves to the instance's own <c>{root}/{name}/.minecraft</c> when isolated, or the shared
/// common root when not — so non-isolated instances reuse one set of saves/mods (HMCL/PCL's
/// "non-isolated" mode). Pure persistence + path logic, no network/UI.
/// </summary>
public class VersionIsolationTests
{
    private static string TempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "nml-iso-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void Instance_IsIsolated_Defaults_True()
    {
        // Backward compat: existing instances (which never set the flag) stay isolated, matching
        // the launcher's prior always-isolated behavior.
        new Instance().IsIsolated.Should().BeTrue();
    }

    [Fact]
    public void GameDirFor_Instance_Isolated_Returns_Instance_Specific_Dir()
    {
        string dir = TempDir();
        try
        {
            var store = new InstanceStore(dir);
            var isolated = new Instance { Name = "Modded", IsIsolated = true };
            store.GameDirFor(isolated)
                .Should().Be(Path.Combine(store.InstancesRoot, "Modded", ".minecraft"));
            // The name-based overload still matches (used by older call sites).
            store.GameDirFor("Modded").Should().Be(store.GameDirFor(isolated));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void GameDirFor_Instance_NonIsolated_Returns_SharedRoot()
    {
        string dir = TempDir();
        try
        {
            var store = new InstanceStore(dir);
            // A non-isolated instance shares the common .minecraft regardless of its own name.
            store.SharedRoot = Path.Combine(dir, "shared", ".minecraft");
            var shared = new Instance { Name = "Shared", IsIsolated = false };
            store.GameDirFor(shared).Should().Be(store.SharedRoot);

            // Two different non-isolated instances must resolve to the SAME directory (that's the
            // whole point of non-isolation — one set of saves/mods reused).
            var other = new Instance { Name = "Other", IsIsolated = false };
            store.GameDirFor(shared).Should().Be(store.GameDirFor(other));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void SharedRoot_Falls_Back_To_OS_Default_When_Unset()
    {
        string dir = TempDir();
        try
        {
            var store = new InstanceStore(dir);
            // When SharedRoot is never assigned, it resolves to the OS-standard .minecraft.
            store.SharedRoot.Should().Be(InstanceStore.DefaultSharedRoot());
            // DefaultSharedRoot lives under the OS app-data dir and ends with .minecraft.
            InstanceStore.DefaultSharedRoot().Should().EndWith(".minecraft");
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Clone_Preserves_IsIsolated_Flag()
    {
        // A cloned instance must inherit the source's isolation mode (regression: Clone() previously
        // dropped custom args; isolation should be copied too).
        string dir = TempDir();
        try
        {
            var store = new InstanceStore(dir);
            store.Add(new Instance { Name = "Shared", VersionId = "1.20.1", IsIsolated = false });
            var source = store.LoadAll().Single(i => i.Name == "Shared");

            Instance clone = store.Clone(source, "Shared (copy)");
            clone.IsIsolated.Should().BeFalse();
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void IsIsolated_RoundTrips_Through_Persistence()
    {
        string dir = TempDir();
        try
        {
            var store = new InstanceStore(dir);
            store.Add(new Instance { Name = "Shared", VersionId = "1.20.1", IsIsolated = false });
            store.Add(new Instance { Name = "Isolated", VersionId = "1.20.1", IsIsolated = true });

            var loaded = store.LoadAll();
            loaded.Single(i => i.Name == "Shared").IsIsolated.Should().BeFalse();
            loaded.Single(i => i.Name == "Isolated").IsIsolated.Should().BeTrue();
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
