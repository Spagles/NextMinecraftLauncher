using NML.Core.Models;

namespace NML.Core.Tests;

public class VersionInfoMergeTests
{
    private static VersionInfo Parent() => new()
    {
        Id = "1.20.1",
        Type = "release",
        MainClass = "net.minecraft.client.main.Main",
        Assets = "5",
        Arguments = new Arguments
        {
            Game = new() { ArgumentElement.FromLiteral("--username"), ArgumentElement.FromLiteral("${auth_player_name}") },
            Jvm = new() { ArgumentElement.FromLiteral("-cp"), ArgumentElement.FromLiteral("${classpath}") },
        },
        Libraries = new()
        {
            new() { Name = "com.mojang:authlib:4.0.43" },
            new() { Name = "org.lwjgl:lwjgl:3.3.1" },
        },
        JavaVersion = new JavaVersionRef { Component = "java-runtime-gamma", MajorVersion = 17 },
    };

    private static VersionInfo Child() => new()
    {
        Id = "fabric-loader-0.15.7-1.20.1",
        InheritsFrom = "1.20.1",
        MainClass = "net.fabricmc.loader.launch.knot.KnotClient",
        Libraries = new()
        {
            new() { Name = "net.fabricmc:fabric-loader:0.15.7" },
            new() { Name = "org.lwjgl:lwjgl:3.3.1" }, // duplicate — should be deduped
        },
        Arguments = new Arguments
        {
            Game = new() { ArgumentElement.FromLiteral("--fabric") },
            Jvm = new() { ArgumentElement.FromLiteral("-Dfabric") },
        },
    };

    [Fact]
    public void Child_overrides_mainClass_and_assets()
    {
        var merged = VersionInfoService.Merge(Parent(), Child());

        merged.MainClass.Should().Be("net.fabricmc.loader.launch.knot.KnotClient");
        merged.Assets.Should().Be("5"); // inherited from parent (child has none)
        merged.JavaVersion!.MajorVersion.Should().Be(17); // inherited
    }

    [Fact]
    public void Libraries_are_concatenated_and_deduplicated_by_name()
    {
        var merged = VersionInfoService.Merge(Parent(), Child());

        merged.Libraries.Should().HaveCount(3); // authlib, lwjgl, fabric-loader
        merged.Libraries.Select(l => l.Name).Should().BeEquivalentTo(new[]
        {
            "com.mojang:authlib:4.0.43",
            "org.lwjgl:lwjgl:3.3.1",
            "net.fabricmc:fabric-loader:0.15.7",
        });
    }

    [Fact]
    public void Arguments_are_concatenated_parent_first()
    {
        var merged = VersionInfoService.Merge(Parent(), Child());

        // Parent's game args first, then child's.
        merged.Arguments!.Game.Should().HaveCount(3);
        merged.Arguments.Game[2].Literal.Should().Be("--fabric");
        // Parent's jvm args first, then child's.
        merged.Arguments.Jvm.Should().HaveCount(3);
        merged.Arguments.Jvm[2].Literal.Should().Be("-Dfabric");
    }

    [Fact]
    public void Merged_result_has_no_inheritsFrom()
    {
        var merged = VersionInfoService.Merge(Parent(), Child());
        merged.InheritsFrom.Should().BeNull();
    }
}
