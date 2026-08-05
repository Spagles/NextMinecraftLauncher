using NML.App.Services;
using NML.Core.Auth.AuthlibInjector;

namespace NML.App.Tests;

/// <summary>
/// Validates the authlib-injector server list persistence (the JSON store behind the
/// server-management panel). The UI commands are thin wrappers around this store.
/// </summary>
public class AuthlibInjectorServerStoreTests
{
    private static AuthlibInjectorServerStore NewStore()
    {
        string dir = Path.Combine(Path.GetTempPath(), "nml-authlib-" + Guid.NewGuid().ToString("N")[..8]);
        return new AuthlibInjectorServerStore(dir);
    }

    [Fact]
    public void Empty_store_returns_empty_list()
    {
        var store = NewStore();
        store.LoadAll().Should().BeEmpty();
        store.GetActiveApiUrl().Should().BeNull();
    }

    [Fact]
    public void Add_then_load_round_trips()
    {
        var store = NewStore();
        store.Add(new AuthlibInjectorServer { Name = "LittleSkin", ApiUrl = "https://littleskin.cn/api/yggdrasil" });

        var loaded = store.LoadAll();
        loaded.Should().ContainSingle();
        loaded[0].Name.Should().Be("LittleSkin");
        loaded[0].ApiUrl.Should().Be("https://littleskin.cn/api/yggdrasil");
    }

    [Fact]
    public void Add_replaces_existing_server_with_same_url()
    {
        var store = NewStore();
        store.Add(new AuthlibInjectorServer { Name = "Old", ApiUrl = "https://example.com/api" });
        store.Add(new AuthlibInjectorServer { Name = "New", ApiUrl = "https://example.com/api" });

        var loaded = store.LoadAll();
        loaded.Should().ContainSingle();
        loaded[0].Name.Should().Be("New"); // replaced, not duplicated
    }

    [Fact]
    public void Remove_deletes_by_url()
    {
        var store = NewStore();
        store.Add(new AuthlibInjectorServer { Name = "A", ApiUrl = "https://a.example/api" });
        store.Add(new AuthlibInjectorServer { Name = "B", ApiUrl = "https://b.example/api" });

        store.Remove("https://a.example/api");

        var loaded = store.LoadAll();
        loaded.Should().ContainSingle();
        loaded[0].Name.Should().Be("B");
    }

    [Fact]
    public void Active_server_url_persists_and_clears()
    {
        var store = NewStore();
        store.SetActiveApiUrl("https://x.example/api");
        store.GetActiveApiUrl().Should().Be("https://x.example/api");

        store.SetActiveApiUrl(null);
        store.GetActiveApiUrl().Should().BeNull();
    }

    [Fact]
    public void Reload_from_disk_keeps_all_servers()
    {
        string dir = Path.Combine(Path.GetTempPath(), "nml-authlib-" + Guid.NewGuid().ToString("N")[..8]);
        var store1 = new AuthlibInjectorServerStore(dir);
        store1.Add(new AuthlibInjectorServer { Name = "S1", ApiUrl = "https://1.example/api" });
        store1.Add(new AuthlibInjectorServer { Name = "S2", ApiUrl = "https://2.example/api" });

        // New store instance pointing at the same dir must see both.
        var store2 = new AuthlibInjectorServerStore(dir);
        store2.LoadAll().Should().HaveCount(2);
    }
}
