using NML.Core.Instances;

namespace NML.Core.Tests;

/// <summary>
/// Verifies the per-instance launch-option persistence that backs the "Save launch options"
/// feature on the Home page: <see cref="InstanceStore.Clone"/> must carry the custom JVM/game
/// args forward (previously dropped silently), and <see cref="InstanceStore.SaveAll"/> must
/// round-trip edited options so they survive a restart.
/// </summary>
public class InstanceLaunchOptionsTests
{
    private static string TempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "nml-inst-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void Clone_Preserves_Custom_Jvm_And_Game_Args()
    {
        // Regression: Clone() previously omitted CustomJvmArgs/CustomGameArgs, so a cloned
        // instance silently lost its tuned launch options.
        string dir = TempDir();
        try
        {
            var store = new InstanceStore(dir);
            var source = new Instance
            {
                Name = "Tuned",
                VersionId = "1.20.1",
                CustomJvmArgs = "-XX:+UseG1GC -Xmx4096m",
                CustomGameArgs = "--fullscreen --server host",
            };
            store.Add(source);

            Instance clone = store.Clone(source, "Tuned (copy)");
            clone.CustomJvmArgs.Should().Be("-XX:+UseG1GC -Xmx4096m");
            clone.CustomGameArgs.Should().Be("--fullscreen --server host");
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void SaveAll_RoundTrips_Edited_Launch_Options()
    {
        // The Home page mutates the in-memory Instance, then calls SaveAll to persist. Verify
        // that memory, window, and args edits survive a fresh LoadAll (simulates a restart).
        string dir = TempDir();
        try
        {
            var store = new InstanceStore(dir);
            var inst = new Instance { Name = "Survival", VersionId = "1.20.1" };
            store.Add(inst);

            // Simulate the user editing options in the UI (in-memory mutation).
            var all = store.LoadAll();
            var edited = all[0];
            edited.MaxMemoryMb = 8192;
            edited.WindowWidth = 1920;
            edited.WindowHeight = 1080;
            edited.CustomJvmArgs = "-XX:+UseZGC";
            edited.CustomGameArgs = "--demo";
            store.SaveAll(all);

            // Reload from disk — edits must persist.
            var reloaded = store.LoadAll().Single();
            reloaded.MaxMemoryMb.Should().Be(8192);
            reloaded.WindowWidth.Should().Be(1920);
            reloaded.WindowHeight.Should().Be(1080);
            reloaded.CustomJvmArgs.Should().Be("-XX:+UseZGC");
            reloaded.CustomGameArgs.Should().Be("--demo");
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void SaveAll_Replaces_Only_The_Matching_Instance()
    {
        // The Home page's save path re-reads the list, swaps the matching entry, and saves — so a
        // save of one instance must not clobber a concurrently-added sibling.
        string dir = TempDir();
        try
        {
            var store = new InstanceStore(dir);
            store.Add(new Instance { Name = "A", VersionId = "1.20.1", MaxMemoryMb = 1024 });
            store.Add(new Instance { Name = "B", VersionId = "1.19.4", MaxMemoryMb = 2048 });

            // Simulate editing only A.
            var all = store.LoadAll();
            var a = all.Single(i => i.Name == "A");
            a.MaxMemoryMb = 4096;
            // Re-read + replace just A, mirroring SaveInstanceOptionsCommand exactly.
            var fresh = store.LoadAll();
            int idx = fresh.FindIndex(i => i.Name == "A");
            fresh[idx] = a;
            store.SaveAll(fresh);

            var reloaded = store.LoadAll();
            reloaded.Single(i => i.Name == "A").MaxMemoryMb.Should().Be(4096);
            reloaded.Single(i => i.Name == "B").MaxMemoryMb.Should().Be(2048); // untouched
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
