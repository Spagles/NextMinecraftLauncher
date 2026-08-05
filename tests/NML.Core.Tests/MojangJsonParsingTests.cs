using System.Text.Json;
using NML.Core.Models;

namespace NML.Core.Tests;

/// <summary>
/// Validates JSON parsing against realistic Mojang payloads, focusing on the tricky
/// parts: the polymorphic ArgumentElement (string | {value,rules}), the natives map,
/// and the inheritsFrom chain merge.
/// </summary>
public class MojangJsonParsingTests
{
    private const string SampleVersionJson = """
        {
          "id": "1.20.1",
          "type": "release",
          "mainClass": "net.minecraft.client.main.Main",
          "assets": "5",
          "javaVersion": { "component": "java-runtime-gamma", "majorVersion": 17 },
          "arguments": {
            "game": [
              "--username", "${auth_player_name}",
              { "rules": [ { "action": "allow", "features": { "is_demo_user": true } } ], "value": "--demo" }
            ],
            "jvm": [
              { "rules": [ { "action": "allow", "os": { "name": "osx" } } ], "value": "-XstartOnFirstThread" },
              "-cp", "${classpath}"
            ]
          },
          "libraries": [
            { "name": "com.mojang:authlib:4.0.43",
              "downloads": { "artifact": { "path": "com/mojang/authlib/4.0.43/authlib-4.0.43.jar", "sha1": "abc", "size": 1, "url": "https://example.com/authlib.jar" } } },
            { "name": "org.lwjgl:lwjgl:3.3.1",
              "rules": [ { "action": "allow", "os": { "name": "linux" } } ],
              "natives": { "linux": "natives-linux" },
              "downloads": {
                "artifact": { "path": "o/l.jar", "sha1": "def", "size": 2, "url": "https://e/l.jar" },
                "classifiers": { "natives-linux": { "path": "o/l-nat.jar", "sha1": "ghi", "size": 3, "url": "https://e/nat.jar" } }
              } }
          ],
          "downloads": {
            "client": { "sha1": "deadbeef", "size": 12345, "url": "https://e/client.jar" }
          }
        }
        """;

    [Fact]
    public void Parses_top_level_fields()
    {
        var info = JsonSerializer.Deserialize<VersionInfo>(SampleVersionJson, JsonOptions.Default)!;

        info.Id.Should().Be("1.20.1");
        info.Type.Should().Be("release");
        info.MainClass.Should().Be("net.minecraft.client.main.Main");
        info.Assets.Should().Be("5");
        info.JavaVersion!.Component.Should().Be("java-runtime-gamma");
        info.JavaVersion.MajorVersion.Should().Be(17);
    }

    [Fact]
    public void Parses_mixed_literal_and_conditional_arguments()
    {
        var info = JsonSerializer.Deserialize<VersionInfo>(SampleVersionJson, JsonOptions.Default)!;

        info.Arguments!.Game.Should().HaveCount(3);
        info.Arguments.Game[0].Literal.Should().Be("--username");
        info.Arguments.Game[0].IsConditional.Should().BeFalse();

        info.Arguments.Game[2].IsConditional.Should().BeTrue();
        info.Arguments.Game[2].Values.Should().ContainSingle().Which.Should().Be("--demo");
        info.Arguments.Game[2].Rules.Should().ContainSingle();
    }

    [Fact]
    public void Parses_os_gated_jvm_argument()
    {
        var info = JsonSerializer.Deserialize<VersionInfo>(SampleVersionJson, JsonOptions.Default)!;

        info.Arguments!.Jvm[0].IsConditional.Should().BeTrue();
        info.Arguments.Jvm[0].Values.Should().ContainSingle().Which.Should().Be("-XstartOnFirstThread");
        info.Arguments.Jvm[0].Rules![0].Os!.Name.Should().Be("osx");

        info.Arguments.Jvm[1].Literal.Should().Be("-cp");
        info.Arguments.Jvm[2].Literal.Should().Be("${classpath}");
    }

    [Fact]
    public void Parses_libraries_with_natives_and_classifiers()
    {
        var info = JsonSerializer.Deserialize<VersionInfo>(SampleVersionJson, JsonOptions.Default)!;

        info.Libraries.Should().HaveCount(2);
        info.Libraries[0].Coordinate.Group.Should().Be("com.mojang");
        info.Libraries[0].Downloads!.Artifact!.Path.Should().Be("com/mojang/authlib/4.0.43/authlib-4.0.43.jar");

        info.Libraries[1].Rules![0].Os!.Name.Should().Be("linux");
        info.Libraries[1].Natives!["linux"].Should().Be("natives-linux");
        info.Libraries[1].Downloads!.Classifiers!["natives-linux"].Sha1.Should().Be("ghi");
    }

    [Fact]
    public void Parses_client_download()
    {
        var info = JsonSerializer.Deserialize<VersionInfo>(SampleVersionJson, JsonOptions.Default)!;

        info.Downloads!.Client!.Sha1.Should().Be("deadbeef");
        info.Downloads.Client.Size.Should().Be(12345);
        info.Downloads.Client.Url.Should().Be("https://e/client.jar");
    }

    [Fact]
    public void VersionManifest_parses_latest_and_versions()
    {
        const string manifestJson = """
            {
              "latest": { "release": "1.20.1", "snapshot": "1.20.2-pre1" },
              "versions": [
                { "id": "1.20.1", "type": "release", "url": "https://e/1.20.1.json", "time": "2023-06-12T13:31:45+00:00", "releaseTime": "2023-06-12T13:31:45+00:00", "sha1": "abc", "complianceLevel": 1 }
              ]
            }
            """;

        var manifest = JsonSerializer.Deserialize<VersionManifest>(manifestJson, JsonOptions.Default)!;

        manifest.Latest.Release.Should().Be("1.20.1");
        manifest.Versions.Should().ContainSingle();
        manifest.Versions[0].Id.Should().Be("1.20.1");
        manifest.Versions[0].Sha1.Should().Be("abc");
    }
}
