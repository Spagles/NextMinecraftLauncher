using System.Text.RegularExpressions;
using NML.Core.Logging;

namespace NML.Core.Tests;

/// <summary>
/// Verifies <see cref="LogSeverityClassifier"/> — the engine behind the launcher's
/// regex+level log viewer. The classifier must recognize Minecraft/Log4j's standard
/// severity markers so that errors and stack traces are colored red, warnings amber,
/// and routine INFO lines soft green, regardless of the surrounding timestamp/thread noise.
/// </summary>
public class LogSeverityClassifierTests
{
    [Theory]
    [InlineData("[12:34:56] [Render thread/ERROR]: Failed to load level", LogSeverityClassifier.Severity.Error)]
    [InlineData("java.lang.NullPointerException: Cannot invoke method on null", LogSeverityClassifier.Severity.Error)]
    [InlineData("\tat net.minecraft.server.MinecraftServer.run(MinecraftServer.java:120)", LogSeverityClassifier.Severity.Error)]
    [InlineData("Caused by: java.io.IOException: Stream closed", LogSeverityClassifier.Severity.Error)]
    [InlineData("FATAL: Out of memory", LogSeverityClassifier.Severity.Error)]
    [InlineData("SEVERE: shutting down", LogSeverityClassifier.Severity.Error)]
    [InlineData("[12:34:56] [Server thread/WARN]: Deprecated block used", LogSeverityClassifier.Severity.Warn)]
    [InlineData("Warning: low disk space", LogSeverityClassifier.Severity.Warn)]
    [InlineData("[12:34:56] [Render thread/INFO]: Loaded 42 chunks", LogSeverityClassifier.Severity.Info)]
    [InlineData("Starting minecraft server version 1.20.1", LogSeverityClassifier.Severity.Info)]
    [InlineData("[DEBUG] Buffer size = 1024", LogSeverityClassifier.Severity.Debug)]
    [InlineData("[TRACE] entered tick()", LogSeverityClassifier.Severity.Trace)]
    [InlineData("[FINE] detail line", LogSeverityClassifier.Severity.Trace)]
    [InlineData("", LogSeverityClassifier.Severity.Trace)]
    public void Classify_Recognizes_Standard_Severity_Markers(string line, LogSeverityClassifier.Severity expected)
    {
        LogSeverityClassifier.Classify(line).Should().Be(expected);
    }

    [Fact]
    public void Classify_Returns_Distinct_Colors_Per_Severity()
    {
        var colors = new[]
        {
            LogSeverityClassifier.ColorFor(LogSeverityClassifier.Severity.Error),
            LogSeverityClassifier.ColorFor(LogSeverityClassifier.Severity.Warn),
            LogSeverityClassifier.ColorFor(LogSeverityClassifier.Severity.Info),
            LogSeverityClassifier.ColorFor(LogSeverityClassifier.Severity.Debug),
            LogSeverityClassifier.ColorFor(LogSeverityClassifier.Severity.Trace),
        };
        // All colors are non-empty hex strings and mutually distinct.
        colors.Should().OnlyContain(c => !string.IsNullOrEmpty(c) && c.StartsWith('#'));
        colors.Distinct().Should().HaveCount(5);
    }

    [Fact]
    public void ClassifyAll_Preserves_Line_Order_And_Text()
    {
        string[] lines =
        {
            "INFO: hello",
            "ERROR: boom",
            "WARN: careful",
        };
        var classified = LogSeverityClassifier.ClassifyAll(lines);
        classified.Should().HaveCount(3);
        classified[0].Text.Should().Be("INFO: hello");
        classified[0].Severity.Should().Be(LogSeverityClassifier.Severity.Info);
        classified[1].Text.Should().Be("ERROR: boom");
        classified[1].Severity.Should().Be(LogSeverityClassifier.Severity.Error);
        classified[1].Color.Should().Be(LogSeverityClassifier.ColorFor(LogSeverityClassifier.Severity.Error));
        classified[2].Severity.Should().Be(LogSeverityClassifier.Severity.Warn);
    }

    [Fact]
    public void LogLine_Color_Matches_Its_Severity()
    {
        var line = new LogLine("ERROR: x", LogSeverityClassifier.Severity.Error);
        line.Color.Should().Be(LogSeverityClassifier.ColorFor(LogSeverityClassifier.Severity.Error));
        line.Text.Should().Be("ERROR: x");
    }

    /// <summary>
    /// Severity ordering invariant: Error(0) &lt; Warn(1) &lt; Info(2) &lt; Debug(3) &lt; Trace(4).
    /// The viewer's level-floor filter relies on this numeric ordering, so it is locked by test.
    /// </summary>
    [Fact]
    public void Severity_Values_Are_Ordered_Most_Severe_First()
    {
        ((int)LogSeverityClassifier.Severity.Error).Should().BeLessThan((int)LogSeverityClassifier.Severity.Warn);
        ((int)LogSeverityClassifier.Severity.Warn).Should().BeLessThan((int)LogSeverityClassifier.Severity.Info);
        ((int)LogSeverityClassifier.Severity.Info).Should().BeLessThan((int)LogSeverityClassifier.Severity.Debug);
        ((int)LogSeverityClassifier.Severity.Debug).Should().BeLessThan((int)LogSeverityClassifier.Severity.Trace);
    }
}

/// <summary>
/// Verifies the live-console filter pipeline: classify each streamed line, then apply a severity
/// floor + substring/regex search — exactly what <c>HomePageViewModel.RebuildFilteredConsole</c>
/// does at runtime (mirroring the GameContent logs-tab flow). Keeping this logic exercised in a
/// pure Core test means the console filtering contract is locked even though the VM is hard to
/// construct in a test.
/// </summary>
public class ConsoleFilterPipelineTests
{
    /// <summary>
    /// Reproduce the VM's filter body against a seeded line buffer. Lower ordinal = more verbose
    /// (Error=0 … Trace=4); a floor of Info keeps Error+Warn+Info, drops Debug+Trace.
    /// </summary>
    private static List<LogLine> Filter(
        IReadOnlyList<string> lines, LogSeverityClassifier.Severity floor,
        string? search = null, bool regex = false)
    {
        var classified = LogSeverityClassifier.ClassifyAll(lines);
        var result = new List<LogLine>();
        Regex? rx = null;
        bool hasSearch = !string.IsNullOrWhiteSpace(search);
        if (hasSearch && regex)
        {
            try { rx = new Regex(search!, RegexOptions.IgnoreCase); }
            catch (ArgumentException) { return result; } // invalid pattern → empty (matches the VM)
        }
        foreach (var line in classified)
        {
            if ((int)line.Severity > (int)floor) continue;
            if (hasSearch)
            {
                bool match = rx is not null
                    ? rx.IsMatch(line.Text)
                    : line.Text.Contains(search!, StringComparison.OrdinalIgnoreCase);
                if (!match) continue;
            }
            result.Add(line);
        }
        return result;
    }

    private static readonly string[] MixedLines =
    {
        "INFO: Starting minecraft server version 1.20.1",
        "DEBUG: Buffer size = 1024",
        "ERROR: java.lang.NullPointerException: Cannot invoke method on null",
        "WARN: Deprecated block used",
        "TRACE: entered tick()",
        "INFO: Loaded 42 chunks",
    };

    [Fact]
    public void No_Filter_Shows_All_Lines()
    {
        var filtered = Filter(MixedLines, LogSeverityClassifier.Severity.Trace);
        filtered.Should().HaveCount(MixedLines.Length);
    }

    [Fact]
    public void Severity_Floor_Drops_Less_Severe_Lines()
    {
        // Floor=Warn keeps Error + Warn only; drops Info/Debug/Trace.
        var filtered = Filter(MixedLines, LogSeverityClassifier.Severity.Warn);
        filtered.Should().HaveCount(2);
        filtered.All(l => l.Severity <= LogSeverityClassifier.Severity.Warn).Should().BeTrue();
        filtered.Should().OnlyContain(l =>
            l.Severity == LogSeverityClassifier.Severity.Error ||
            l.Severity == LogSeverityClassifier.Severity.Warn);
    }

    [Fact]
    public void Severity_Floor_Error_Shows_Only_Errors()
    {
        var filtered = Filter(MixedLines, LogSeverityClassifier.Severity.Error);
        filtered.Should().HaveCount(1);
        filtered[0].Severity.Should().Be(LogSeverityClassifier.Severity.Error);
        filtered[0].Color.Should().Be(LogSeverityClassifier.ColorFor(LogSeverityClassifier.Severity.Error));
    }

    [Fact]
    public void Substring_Search_Filters_By_Text_Ignore_Case()
    {
        // Searching "null" should find only the NullPointerException error line.
        var filtered = Filter(MixedLines, LogSeverityClassifier.Severity.Trace, search: "NULL");
        filtered.Should().HaveCount(1);
        filtered[0].Text.Should().Contain("NullPointerException");
    }

    [Fact]
    public void Regex_Search_Filters_By_Pattern()
    {
        // A regex matching lines containing a number (the chunks/buffer/tick lines).
        var filtered = Filter(MixedLines, LogSeverityClassifier.Severity.Trace, search: @"\d+", regex: true);
        filtered.Should().NotBeEmpty();
        filtered.All(l => Regex.IsMatch(l.Text, @"\d+")).Should().BeTrue();
    }

    [Fact]
    public void Invalid_Regex_Returns_Empty_Matches_VM_Behavior()
    {
        var filtered = Filter(MixedLines, LogSeverityClassifier.Severity.Trace, search: "[invalid", regex: true);
        filtered.Should().BeEmpty("an invalid regex must clear the console (matching the VM's guard)");
    }

    [Fact]
    public void Search_And_Severity_Floor_Combine()
    {
        // Floor=Info + search "loaded" → only the INFO chunks line.
        var filtered = Filter(MixedLines, LogSeverityClassifier.Severity.Info, search: "loaded");
        filtered.Should().HaveCount(1);
        filtered[0].Severity.Should().Be(LogSeverityClassifier.Severity.Info);
        filtered[0].Text.Should().Contain("Loaded 42 chunks");
    }

    [Fact]
    public void Empty_Input_Yields_Empty_Filtered()
        => Filter(Array.Empty<string>(), LogSeverityClassifier.Severity.Trace).Should().BeEmpty();
}
