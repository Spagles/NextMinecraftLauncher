using NML.Core.Auth;
using NML.Core.Java;
using NML.Core.Launch;
using NML.Core.Models;
using NML.Core.Rules;

namespace NML.Core.Tests;

/// <summary>
/// Verifies the launch command is assembled correctly: memory, classpath from libraries,
/// placeholder substitution, and OS/feature rule filtering of version.json arguments.
/// Uses a fake working directory so no real files are needed.
/// </summary>
public class LaunchCommandBuilderTests
{
    private static LaunchOptions Options(VersionInfo version, MinecraftDirectory mc) => new()
    {
        Version = version,
        Mc = mc,
        Account = new Account
        {
            Username = "Steve",
            Uuid = "abcdef0123456789abcdef0123456789",
            AccessToken = "token123",
            AccountType = "legacy",
        },
        Java = new JavaRuntime
        {
            BinDirectory = "/fake/bin",
            ExecutablePath = "/fake/bin/java",
            MajorVersion = 17,
        },
        MinMemoryMb = 1024,
        MaxMemoryMb = 4096,
        WindowWidth = 1920,
        WindowHeight = 1080,
    };

    private static VersionInfo SampleVersion() => new()
    {
        Id = "1.20.1",
        Type = "release",
        MainClass = "net.minecraft.client.main.Main",
        Arguments = new Arguments
        {
            Game = new()
            {
                ArgumentElement.FromLiteral("--username"),
                ArgumentElement.FromLiteral("${auth_player_name}"),
                ArgumentElement.FromLiteral("--version"),
                ArgumentElement.FromLiteral("${version_name}"),
                ArgumentElement.FromLiteral("--width"),
                ArgumentElement.FromLiteral("${resolution_width}"),
                ArgumentElement.FromLiteral("--height"),
                ArgumentElement.FromLiteral("${resolution_height}"),
                ArgumentElement.FromConditional(
                    new[] { "--demo" },
                    new List<Rule> { new() { Action = "allow", Features = new Dictionary<string, bool> { ["is_demo_user"] = true } } }),
            },
            Jvm = new()
            {
                ArgumentElement.FromConditional(
                    new[] { "-XstartOnFirstThread" },
                    new List<Rule> { new() { Action = "allow", Os = new OsRule { Name = "osx" } } }),
                ArgumentElement.FromLiteral("-Dminecraft.launcher.brand=${launcher_name}"),
            },
        },
        Libraries = new()
        {
            new()
            {
                Name = "com.mojang:authlib:4.0.43",
                Downloads = new LibraryDownloads
                {
                    Artifact = new Downloadable
                    {
                        Path = "com/mojang/authlib/4.0.43/authlib-4.0.43.jar",
                        Sha1 = "a", Size = 1, Url = "https://e/a.jar",
                    },
                },
            },
        },
    };

    [Fact]
    public void Includes_memory_and_executable_path_is_separate()
    {
        // The builder returns argv *without* the executable path (the launcher prepends it).
        var mc = new MinecraftDirectory(Path.GetTempPath());
        var argv = new LaunchCommandBuilder(new RuleContext { OsName = "windows", Arch = "x86_64" })
            .Build(Options(SampleVersion(), mc));

        argv.Should().Contain("-Xms1024M");
        argv.Should().Contain("-Xmx4096M");
    }

    [Fact]
    public void Substitutes_placeholders_in_game_arguments()
    {
        var mc = new MinecraftDirectory(Path.GetTempPath());
        var argv = new LaunchCommandBuilder(new RuleContext { OsName = "windows", Arch = "x86_64" })
            .Build(Options(SampleVersion(), mc));

        argv.Should().Contain("--username");
        argv.Should().Contain("Steve");
        argv.Should().Contain("--version");
        argv.Should().Contain("1.20.1");
        argv.Should().Contain("--width");
        argv.Should().Contain("1920");
        argv.Should().Contain("--height");
        argv.Should().Contain("1080");
    }

    [Fact]
    public void Excludes_feature_gated_args_when_feature_not_set()
    {
        var mc = new MinecraftDirectory(Path.GetTempPath());
        var argv = new LaunchCommandBuilder(new RuleContext { OsName = "windows", Arch = "x86_64" })
            .Build(Options(SampleVersion(), mc));

        // is_demo_user is not set → --demo must not appear.
        argv.Should().NotContain("--demo");
    }

    [Fact]
    public void Includes_feature_gated_args_when_feature_set()
    {
        var mc = new MinecraftDirectory(Path.GetTempPath());
        var opts = new LaunchOptions
        {
            Version = SampleVersion(),
            Mc = mc,
            Account = new Account
            {
                Username = "Steve",
                Uuid = "abcdef0123456789abcdef0123456789",
                AccessToken = "token123",
                AccountType = "legacy",
            },
            Java = new JavaRuntime
            {
                BinDirectory = "/fake/bin",
                ExecutablePath = "/fake/bin/java",
                MajorVersion = 17,
            },
            Features = new Dictionary<string, bool> { ["is_demo_user"] = true },
        };

        var argv = new LaunchCommandBuilder(new RuleContext { OsName = "windows", Arch = "x86_64" })
            .Build(opts);

        argv.Should().Contain("--demo");
    }

    [Fact]
    public void Excludes_osx_only_jvm_arg_on_windows()
    {
        var mc = new MinecraftDirectory(Path.GetTempPath());
        var argv = new LaunchCommandBuilder(new RuleContext { OsName = "windows", Arch = "x86_64" })
            .Build(Options(SampleVersion(), mc));

        argv.Should().NotContain("-XstartOnFirstThread");
    }

    [Fact]
    public void Includes_osx_only_jvm_arg_on_osx()
    {
        var mc = new MinecraftDirectory(Path.GetTempPath());
        var argv = new LaunchCommandBuilder(new RuleContext { OsName = "osx", Arch = "arm64" })
            .Build(Options(SampleVersion(), mc));

        argv.Should().Contain("-XstartOnFirstThread");
    }

    [Fact]
    public void Replaces_launcher_brand_placeholder()
    {
        var mc = new MinecraftDirectory(Path.GetTempPath());
        var argv = new LaunchCommandBuilder(new RuleContext { OsName = "windows", Arch = "x86_64" })
            .Build(Options(SampleVersion(), mc));

        argv.Should().Contain("-Dminecraft.launcher.brand=NextMinecraftLauncher");
    }

    [Fact]
    public void Appends_main_class_after_classpath()
    {
        var mc = new MinecraftDirectory(Path.GetTempPath());
        var argv = new LaunchCommandBuilder(new RuleContext { OsName = "windows", Arch = "x86_64" })
            .Build(Options(SampleVersion(), mc));

        int cpIndex = argv.IndexOf("-cp");
        cpIndex.Should().BeGreaterThanOrEqualTo(0);
        argv[cpIndex + 2].Should().Be("net.minecraft.client.main.Main");
    }
}
