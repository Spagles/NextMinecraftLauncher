using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using NML.Core.Modloaders;

namespace NML.Core.Tests;

/// <summary>
/// End-to-end verification of the batch mod-upgrade ("upgrade all") file replacement loop — the
/// VM-side half of <c>GameContentPageViewModel.UpgradeAllModsAsync</c>. The pure planner is covered
/// by <see cref="ModUpdatePlannerTests"/>; here we exercise the real download→.part→replace→delete
/// pipeline against a local HTTP server (no live CDN, no flakiness), confirming that:
///  - each updatable mod's old jar is atomically replaced by the freshly downloaded one,
///  - the obsolete old file is removed when the new file has a different name,
///  - a failed download (404) leaves the existing jar intact and the upgrade continues,
///  - the mods dir ends up reflecting exactly the upgraded state.
/// </summary>
public class BatchModUpdateE2ETests
{
    /// <summary>
    /// Replicates the VM's per-item upgrade body (download to .part, swap, remove old) so we can
    /// exercise it off-line. This mirrors <c>UpgradeAllModsAsync</c>'s loop verbatim minus the
    /// surrounding status plumbing.
    /// </summary>
    private static async Task<int> RunUpgradeAsync(IReadOnlyList<ModUpdateItem> plan, string modsDir,
        HttpClient http, Action<string>? onFail = null)
    {
        int upgraded = 0;
        foreach (var item in plan)
        {
            try
            {
                string part = item.TargetPath + ".part";
                using (var resp = await http.GetStreamAsync(item.SourceUrl))
                using (var fs = File.Create(part))
                    await resp.CopyToAsync(fs);
                string oldPath = Path.Combine(modsDir, item.OldFileName);
                if (File.Exists(oldPath) && !string.Equals(oldPath, item.TargetPath, StringComparison.OrdinalIgnoreCase))
                    File.Delete(oldPath);
                if (File.Exists(item.TargetPath)) File.Delete(item.TargetPath);
                File.Move(part, item.TargetPath);
                upgraded++;
            }
            catch (Exception ex)
            {
                onFail?.Invoke($"{item.ModId}: {ex.Message}");
            }
        }
        return upgraded;
    }

    [Fact]
    public async Task Upgrade_All_Replaces_Jars_And_Removes_Old_Files()
    {
        using var server = new LocalFileServer();
        // Serve the "new" jars from the local server.
        server.Add("/sodium-0.6.jar", "NEW-SODIUM-0.6");
        server.Add("/iris-1.7.jar", "NEW-IRIS-1.7");
        // A 404 for one mod — its existing jar must survive.
        server.Add("/missing.jar", null, status: 404);

        string modsDir = Path.Combine(Path.GetTempPath(), "nml-upgrade-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(modsDir);
        try
        {
            // Lay down the "old" installed mods.
            await File.WriteAllTextAsync(Path.Combine(modsDir, "sodium-0.5.jar"), "OLD-SODIUM");
            await File.WriteAllTextAsync(Path.Combine(modsDir, "iris-1.6.jar"), "OLD-IRIS");
            await File.WriteAllTextAsync(Path.Combine(modsDir, "legacy-3.0.jar"), "OLD-LEGACY");

            var installed = new[]
            {
                new InstalledModInfo { ModId = "sodium", FileName = "sodium-0.5.jar", Version = "0.5",
                    UpdateAvailable = true, LatestFileUrl = server.Url + "/sodium-0.6.jar", LatestVersion = "sodium-0.6.jar" },
                new InstalledModInfo { ModId = "iris", FileName = "iris-1.6.jar", Version = "1.6",
                    UpdateAvailable = true, LatestFileUrl = server.Url + "/iris-1.7.jar", LatestVersion = "iris-1.7.jar" },
                // Updatable but the new jar 404s — its old file must remain untouched.
                new InstalledModInfo { ModId = "legacy", FileName = "legacy-3.0.jar", Version = "3.0",
                    UpdateAvailable = true, LatestFileUrl = server.Url + "/missing.jar", LatestVersion = "legacy-3.1.jar" },
            };

            var plan = ModUpdatePlanner.Plan(installed, modsDir);
            plan.Should().HaveCount(3, "all three mods are flagged updatable with jar URLs");

            int upgraded;
            using (var http = new HttpClient())
                upgraded = await RunUpgradeAsync(plan, modsDir, http);

            // Two of three succeeded (the 404 one failed and is left alone).
            upgraded.Should().Be(2);

            // New jars are present with the new contents.
            File.Exists(Path.Combine(modsDir, "sodium-0.6.jar")).Should().BeTrue();
            (await File.ReadAllTextAsync(Path.Combine(modsDir, "sodium-0.6.jar"))).Should().Be("NEW-SODIUM-0.6");
            File.Exists(Path.Combine(modsDir, "iris-1.7.jar")).Should().BeTrue();
            (await File.ReadAllTextAsync(Path.Combine(modsDir, "iris-1.7.jar"))).Should().Be("NEW-IRIS-1.7");

            // Old jars were removed (different file names from the new ones).
            File.Exists(Path.Combine(modsDir, "sodium-0.5.jar")).Should().BeFalse("the old jar is deleted after upgrade");
            File.Exists(Path.Combine(modsDir, "iris-1.6.jar")).Should().BeFalse("the old jar is deleted after upgrade");

            // The mod whose download failed keeps its original jar untouched.
            File.Exists(Path.Combine(modsDir, "legacy-3.0.jar")).Should().BeTrue("a failed download must not delete the existing mod");
            (await File.ReadAllTextAsync(Path.Combine(modsDir, "legacy-3.0.jar"))).Should().Be("OLD-LEGACY");
        }
        finally { Directory.Delete(modsDir, recursive: true); }
    }

    [Fact]
    public async Task Upgrade_In_Place_Overwrites_Same_Name_Jar()
    {
        // When LatestVersion is a version string (not a file name), the plan keeps the original
        // file name — so the upgrade overwrites the same jar in place (no old-file deletion).
        using var server = new LocalFileServer();
        server.Add("/sodium-new.jar", "NEWER-SODIUM");

        string modsDir = Path.Combine(Path.GetTempPath(), "nml-upgrade-inplace-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(modsDir);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(modsDir, "sodium.jar"), "OLD");
            var installed = new[]
            {
                new InstalledModInfo { ModId = "sodium", FileName = "sodium.jar", Version = "0.5",
                    UpdateAvailable = true, LatestFileUrl = server.Url + "/sodium-new.jar", LatestVersion = "1.2.3" },
            };
            var plan = ModUpdatePlanner.Plan(installed, modsDir);
            plan.Single().TargetPath.Should().EndWith("sodium.jar", "version string is not a file name → keep original name");

            using var http = new HttpClient();
            int upgraded = await RunUpgradeAsync(plan, modsDir, http);
            upgraded.Should().Be(1);

            // Same file name, but content replaced.
            (await File.ReadAllTextAsync(Path.Combine(modsDir, "sodium.jar"))).Should().Be("NEWER-SODIUM");
        }
        finally { Directory.Delete(modsDir, recursive: true); }
    }

    /// <summary>
    /// Minimal in-process HTTP server that serves canned bytes for registered paths. Avoids any
    /// dependency on live CDNs so the E2E upgrade test is deterministic and offline.
    /// </summary>
    private sealed class LocalFileServer : IDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly Dictionary<string, (string? Body, int Status)> _routes = new();
        public string Url { get; }

        public LocalFileServer()
        {
            // Try a few random high ports until one is free (avoids HttpListener port conflicts
            // with other test runs or OS-reserved prefixes).
            const int maxAttempts = 10;
            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                int port = 5000 + Random.Shared.Next(0, 5000);
                Url = $"http://localhost:{port}";
                _listener.Prefixes.Add(Url + "/");
                try { _listener.Start(); _ = ServeAsync(); return; }
                catch (HttpListenerException) { _listener.Prefixes.Remove(Url + "/"); }
            }
            // Last resort: let the exception propagate with context.
            throw new InvalidOperationException("Could not bind a free port for LocalFileServer after retries.");
        }

        public void Add(string path, string? body, int status = 200)
            => _routes[path] = (body, status);

        private async Task ServeAsync()
        {
            while (_listener.IsListening)
            {
                HttpListenerContext ctx;
                try { ctx = await _listener.GetContextAsync(); }
                catch { break; }
                try
                {
                    string path = ctx.Request.Url!.AbsolutePath;
                    if (_routes.TryGetValue(path, out var route))
                    {
                        ctx.Response.StatusCode = route.Status;
                        if (route.Body is not null)
                        {
                            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(route.Body);
                            ctx.Response.ContentLength64 = bytes.Length;
                            await ctx.Response.OutputStream.WriteAsync(bytes);
                        }
                    }
                    else
                    {
                        ctx.Response.StatusCode = 404;
                    }
                }
                catch { /* ignore */ }
                finally { try { ctx.Response.Close(); } catch { } }
            }
        }

        public void Dispose()
        {
            try { _listener.Stop(); _listener.Close(); } catch { }
        }
    }
}
