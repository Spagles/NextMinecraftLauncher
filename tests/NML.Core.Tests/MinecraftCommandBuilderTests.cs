using NML.Core.Game;

namespace NML.Core.Tests;

/// <summary>
/// Verifies <see cref="MinecraftCommandBuilder"/> — generates syntactically correct Minecraft
/// commands from structured params (/give, /tp, /effect, /gamemode, /time, /weather).
/// </summary>
public class MinecraftCommandBuilderTests
{
    [Fact]
    public void Give_Basic_Item()
    {
        MinecraftCommandBuilder.Give("@p", "diamond_sword").Should().Be("/give @p minecraft:diamond_sword");
    }

    [Fact]
    public void Give_With_Count()
    {
        MinecraftCommandBuilder.Give("@p", "bread", 64).Should().Be("/give @p minecraft:bread 64");
    }

    [Fact]
    public void Give_With_Enchantment()
    {
        var cmd = MinecraftCommandBuilder.Give("@s", "diamond_sword", 1, enchantLevel: 5, enchantId: "sharpness");
        cmd.Should().StartWith("/give @s minecraft:diamond_sword");
        cmd.Should().Contain("Enchantments");
        cmd.Should().Contain("sharpness");
        cmd.Should().Contain("lvl:5");
    }

    [Fact]
    public void Give_Preserves_Custom_Namespace()
    {
        MinecraftCommandBuilder.Give("@p", "create:cogwheel").Should().Contain("create:cogwheel");
    }

    [Fact]
    public void Teleport_To_Coordinates()
    {
        MinecraftCommandBuilder.Teleport("@p", 100, 64, -200).Should().Be("/tp @p 100.0 64.0 -200.0");
    }

    [Fact]
    public void Teleport_To_Player()
    {
        MinecraftCommandBuilder.TeleportTo("@a", "Steve").Should().Be("/tp @a Steve");
    }

    [Fact]
    public void EffectGive_Standard()
    {
        MinecraftCommandBuilder.EffectGive("@p", "speed", 60, 1).Should().Be("/effect give @p minecraft:speed 60 1 true");
    }

    [Fact]
    public void EffectGive_No_Particles()
    {
        var cmd = MinecraftCommandBuilder.EffectGive("@p", "invisibility", 30, 0, particles: false);
        cmd.Should().EndWith("false");
    }

    [Theory]
    [InlineData("creative", "/gamemode creative @p")]
    [InlineData("survival", "/gamemode survival @p")]
    [InlineData("spectator", "/gamemode spectator @p")]
    [InlineData("invalid", "/gamemode survival @p")] // falls back to survival
    public void Gamemode_Generates_Correct_Command(string mode, string expected)
    {
        MinecraftCommandBuilder.Gamemode("@p", mode).Should().Be(expected);
    }

    [Fact]
    public void TimeSet_Day()
    {
        MinecraftCommandBuilder.TimeSet("day").Should().Be("/time set day");
    }

    [Fact]
    public void TimeSet_Number()
    {
        MinecraftCommandBuilder.TimeSet("1000").Should().Be("/time set 1000");
    }

    [Theory]
    [InlineData("clear", null, "/weather clear")]
    [InlineData("rain", 600, "/weather rain 600")]
    [InlineData("thunder", null, "/weather thunder")]
    [InlineData("invalid", null, "/weather clear")] // falls back to clear
    public void Weather_Generates_Correct_Command(string type, int? duration, string expected)
    {
        MinecraftCommandBuilder.Weather(type, duration).Should().Be(expected);
    }

    [Fact]
    public void SanitizeTarget_Replaces_Spaces_In_Player_Names()
    {
        MinecraftCommandBuilder.SanitizeTarget("Bad Name").Should().Be("Bad_Name");
    }

    [Fact]
    public void SanitizeTarget_Preserves_Selectors()
    {
        MinecraftCommandBuilder.SanitizeTarget("@p").Should().Be("@p");
        MinecraftCommandBuilder.SanitizeTarget("@a[distance=..10]").Should().Be("@a[distance=..10]");
    }
}
