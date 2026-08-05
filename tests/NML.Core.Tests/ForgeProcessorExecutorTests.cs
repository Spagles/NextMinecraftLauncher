using NML.Core.Modloaders.Forge;

namespace NML.Core.Tests;

/// <summary>
/// Validates the deterministic parts of Forge processor execution (variable substitution,
/// library resolution, side filtering). The actual JVM invocation is covered by the runtime
/// smoke; these tests pin the parts that must work without running java.
/// </summary>
public class ForgeProcessorExecutorTests
{
    private static ForgeProcessorContext Ctx() => new()
    {
        RootDir = "/root",
        LibraryDir = "/root/libraries",
        MinecraftJar = "/root/versions/1.18.1/1.18.1.jar",
        ProcessorDir = "/root/processors",
        InstallerJar = "/root/forge-installer.jar",
        Side = "client",
    };

    [Fact]
    public void Resolves_builtin_variables()
    {
        var ctx = Ctx();
        ForgeProcessorExecutor.ResolveVariable("MINECRAFT_JAR", ctx).Should().Be("/root/versions/1.18.1/1.18.1.jar");
        ForgeProcessorExecutor.ResolveVariable("LIBRARY_DIR", ctx).Should().Be("/root/libraries");
        ForgeProcessorExecutor.ResolveVariable("SIDE", ctx).Should().Be("client");
    }

    [Fact]
    public void Unknown_variable_falls_back_to_braces()
    {
        var ctx = Ctx();
        ForgeProcessorExecutor.ResolveVariable("UNKNOWN", ctx).Should().Be("{UNKNOWN}");
    }

    [Fact]
    public void Extra_variables_take_effect()
    {
        var ctx = new ForgeProcessorContext
        {
            RootDir = "/root",
            LibraryDir = "/root/libraries",
            MinecraftJar = "/root/versions/1.18.1/1.18.1.jar",
            ProcessorDir = "/root/processors",
            InstallerJar = "/root/forge-installer.jar",
            Side = "client",
            ExtraVariables = new() { ["MAP"] = "/root/mapped.jar" },
        };
        ForgeProcessorExecutor.ResolveVariable("MAP", ctx).Should().Be("/root/mapped.jar");
    }

    [Fact]
    public void Resolves_library_coord_in_brackets_to_maven_path()
    {
        var ctx = Ctx();
        string path = ForgeProcessorExecutor.ResolveLibRef("[net.md-5:SpecialSource:1.8.5]", ctx);
        path.Should().Be(Path.Combine("/root/libraries", "net/md-5/SpecialSource/1.8.5/SpecialSource-1.8.5.jar"));
    }

    [Fact]
    public void ResolveArg_substitutes_braces_and_brackets()
    {
        var ctx = Ctx();
        string arg = ForgeProcessorExecutor.ResolveArg("--in={MINECRAFT_JAR}", ctx);
        arg.Should().Be("--in=/root/versions/1.18.1/1.18.1.jar");

        string libArg = ForgeProcessorExecutor.ResolveArg("[net.md-5:SpecialSource:1.8.5]", ctx);
        libArg.Should().Contain("SpecialSource-1.8.5.jar");
    }

    [Fact]
    public void Empty_or_null_sides_means_runs_on_both()
    {
        var p = new ForgeProcessor { Jar = "x" };
        ForgeProcessorExecutor.AppliesToSide(p, "client").Should().BeTrue();
        ForgeProcessorExecutor.AppliesToSide(p, "server").Should().BeTrue();
    }

    [Fact]
    public void Sides_filter_correctly()
    {
        var p = new ForgeProcessor { Jar = "x", Sides = new() { "client" } };
        ForgeProcessorExecutor.AppliesToSide(p, "client").Should().BeTrue();
        ForgeProcessorExecutor.AppliesToSide(p, "server").Should().BeFalse();
    }

    [Fact]
    public async Task No_processors_is_a_noop()
    {
        var exec = new ForgeProcessorExecutor(
            "/fake/java", Microsoft.Extensions.Logging.Abstractions.NullLogger<ForgeProcessorExecutor>.Instance);
        var profile = new ForgeInstallProfile { Processors = new() };
        // Should complete without invoking java (no throw, no process spawn).
        await exec.ExecuteAsync(profile, Ctx());
    }

    [Fact]
    public async Task Skips_processors_that_dont_apply_to_side()
    {
        // A processor restricted to "server" must be skipped on a "client" install — no java
        // invocation happens. We verify by completing without throwing (java path is fake,
        // so if it ran it would fail to spawn and throw).
        var exec = new ForgeProcessorExecutor(
            "/nonexistent/java", Microsoft.Extensions.Logging.Abstractions.NullLogger<ForgeProcessorExecutor>.Instance);
        var profile = new ForgeInstallProfile
        {
            Processors = new()
            {
                new() { Jar = "[x:y:z]", Sides = new() { "server" } },
            },
        };
        // Completing successfully proves the server-only processor was skipped on client side.
        await exec.ExecuteAsync(profile, Ctx(), side: "client");
    }
}
