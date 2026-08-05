using NML.AICore.Features;

namespace NML.AICore.Tests;

/// <summary>
/// Validates the deterministic crash-report parser against realistic inputs. The parser
/// is the part that must work without any network — the LLM only sees what it extracts.
/// </summary>
public class CrashReportParserTests
{
    private const string SampleCrashReport = """
        // -----------------------------------------
        // Minecraft Crash Report
        // -----------------------------------------

        Time: 2024-01-15 12:00:00
        Description: Exception ticking world

        java.lang.NoClassDefFoundError: net/fabricmc/fabric/api/event/Event
            at com.example.somemod.EventHandler.onTick(EventHandler.java:42)
            at net.minecraft.server.level.ServerLevel.tick(ServerLevel.java:300)
            at net.minecraft.server.MinecraftServer.tickChildren(MinecraftServer.java:900)
        Caused by: java.lang.ClassNotFoundException: net.fabricmc.fabric.api.event.Event
            at java.base/java.lang.ClassLoader.loadClass(ClassLoader.java:522)

        //-- System Details --
        Details:
            Minecraft Version: 1.20.1
            Fabric Version: 0.15.7
            ModLauncher: 10.0.9
            Java Version: 17.0.9
            Operating System: Windows 11
            Mods:
                {minecraft@1.20.1}
                {fabricloader@0.15.7}
                {somemod@1.0.0}
                {sodium@0.5.3}
        """;

    [Fact]
    public void Extracts_description()
    {
        var r = CrashReportParser.Parse(SampleCrashReport);
        r.Description.Should().Be("Exception ticking world");
        r.LooksLikeCrashReport.Should().BeTrue();
    }

    [Fact]
    public void Extracts_caused_by_chain()
    {
        var r = CrashReportParser.Parse(SampleCrashReport);
        r.CausedBy.Should().Contain("ClassNotFoundException");
        r.CausedBy.Should().Contain("net.fabricmc.fabric.api.event.Event");
    }

    [Fact]
    public void Extracts_system_details_block()
    {
        var r = CrashReportParser.Parse(SampleCrashReport);
        r.SystemDetails.Should().Contain("Minecraft Version: 1.20.1");
        r.SystemDetails.Should().Contain("Java Version: 17.0.9");
    }

    [Fact]
    public void Extracts_mod_list()
    {
        var r = CrashReportParser.Parse(SampleCrashReport);
        r.Mods.Should().NotBeEmpty();
        r.Mods.Should().ContainKey("sodium");
    }

    [Fact]
    public void Produces_stable_source_hash()
    {
        var a = CrashReportParser.Parse(SampleCrashReport);
        var b = CrashReportParser.Parse(SampleCrashReport);
        a.SourceHash.Should().Be(b.SourceHash);
        a.SourceHash.Should().HaveLength(16);
    }

    [Fact]
    public void Handles_plain_log_without_crash_report_markers()
    {
        const string log = "[12:00:00] [Server thread/INFO]: Starting minecraft server version 1.20.1\n" +
                           "[12:00:01] [Server thread/ERROR]: Encountered an unexpected exception";

        var r = CrashReportParser.Parse(log);
        r.LooksLikeCrashReport.Should().BeFalse();
        r.Description.Should().BeNull();
    }

    [Fact]
    public void Log_tail_is_truncated_to_last_n_lines()
    {
        string tail = string.Join('\n', Enumerable.Range(0, 200).Select(i => $"line {i}"));
        var r = CrashReportParser.Parse("Description: test", tail);
        var tailLines = r.LogTail.Split('\n');
        tailLines.Length.Should().BeLessThanOrEqualTo(60);
        tailLines.Last().Should().Be("line 199");
    }

    [Fact]
    public void Stack_head_is_capped()
    {
        string huge = "Description: x\n" + string.Join('\n', Enumerable.Range(0, 1000).Select(_ => "    at some.Frame(frame.java:1)"));
        var r = CrashReportParser.Parse(huge);
        r.StackTraceHead.Length.Should().BeLessThanOrEqualTo(3000 + 30); // cap + truncation marker
    }
}
