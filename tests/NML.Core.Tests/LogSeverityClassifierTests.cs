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
