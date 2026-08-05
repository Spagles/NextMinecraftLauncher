using NML.Core.Auth.AuthlibInjector;
using NML.Core.Java;
using NML.Core.Launch;
using NML.Core.Models;
using NML.Core.Rules;

namespace NML.Core.Tests;

/// <summary>
/// Verifies the authlib-injector agent is injected at the very front of the JVM args when an
/// external Yggdrasil server is configured — and absent otherwise. This is the closure of
/// HMCL's signature "外置登录" feature: the account can actually take effect in-game only if
/// the agent patches authlib before Minecraft loads it.
/// </summary>
public class AuthlibInjectorLaunchTests
{
    private static LaunchOptions Options(AuthlibInjectorServer? server, string? jar) => new()
    {
        Version = new VersionInfo { Id = "1.20.1", Type = "release", MainClass = "net.minecraft.client.main.Main" },
        Mc = new MinecraftDirectory(Path.GetTempPath()),
        Account = new Auth.Account
        {
            Username = "x", Uuid = "0123456789abcdef0123456789abcdef",
            AccessToken = "t", AccountType = server is null ? "legacy" : "authlib-injector",
        },
        Java = new JavaRuntime { BinDirectory = "/j", ExecutablePath = "/j/java", MajorVersion = 17 },
        AuthlibInjectorServer = server,
        AuthlibInjectorJarPath = jar,
    };

    private static AuthlibInjectorServer Server() => new()
    {
        Name = "LittleSkin",
        ApiUrl = "https://littleskin.cn/api/yggdrasil",
    };

    [Fact]
    public void Prepends_javaagent_argument_when_server_configured()
    {
        // Need a real (temp) file for the existence check to pass.
        string jar = Path.GetTempFileName();
        try
        {
            var argv = new LaunchCommandBuilder(new RuleContext { OsName = "windows", Arch = "x86_64" })
                .Build(Options(Server(), jar));

            // The FIRST arg must be -javaagent:<jar>=<server URL>.
            argv[0].Should().StartWith("-javaagent:");
            argv[0].Should().Contain(jar);
            argv[0].Should().Contain("https://littleskin.cn/api/yggdrasil");
            // And memory args still come after.
            argv.Should().Contain("-Xms512M");
        }
        finally { File.Delete(jar); }
    }

    [Fact]
    public void Omits_javaagent_when_no_server_configured()
    {
        var argv = new LaunchCommandBuilder(new RuleContext { OsName = "windows", Arch = "x86_64" })
            .Build(Options(null, null));

        argv.Should().NotContain(a => a.StartsWith("-javaagent:"));
        argv[0].Should().Be("-Xms512M");
    }

    [Fact]
    public void Throws_when_server_set_but_jar_missing()
    {
        // Point to a non-existent path; Build must refuse.
        Action act = () => new LaunchCommandBuilder(new RuleContext { OsName = "windows", Arch = "x86_64" })
            .Build(Options(Server(), "/nonexistent/authlib-injector.jar"));

        act.Should().Throw<FileNotFoundException>();
    }

    [Fact]
    public void BuildAgentArgument_format_is_correct()
    {
        string arg = AuthlibInjectorSetup.BuildAgentArgument("/cache/ai.jar", Server());
        arg.Should().Be("-javaagent:/cache/ai.jar=https://littleskin.cn/api/yggdrasil");
    }
}
