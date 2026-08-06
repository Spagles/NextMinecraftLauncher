using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace NML.Core.Logging;

/// <summary>
/// Categorizes a single log line into one of five severity bands by pattern-matching the
/// standard prefixes used by Minecraft's Log4j output and the Java stack-trace conventions.
/// Pure and allocation-free; safe to call from a tight UI-bindable loop or a background map
/// over thousands of lines.
/// </summary>
public static class LogSeverityClassifier
{
    /// <summary>The severity bands, ordered from most to least severe.</summary>
    public enum Severity
    {
        /// <summary>Errors, exceptions, fatal messages, Java ERROR-level Log4j lines.</summary>
        Error,
        /// <summary>Warnings, deprecations, Log4j WARN lines.</summary>
        Warn,
        /// <summary>Informational / normal operation.</summary>
        Info,
        /// <summary>Debug-level output.</summary>
        Debug,
        /// <summary>Trace / finest output, or lines that carry no recognizable marker.</summary>
        Trace,
    }

    /// <summary>
    /// Map a severity band to a display color (hex string) suitable for an Avalonia
    /// <c>Foreground</c> binding. Centralizing the palette here keeps the XAML and any
    /// future theme overrides consistent.
    /// </summary>
    public static string ColorFor(Severity s) => s switch
    {
        Severity.Error => "#ef5350", // bright red — exceptions jump out
        Severity.Warn  => "#ffb74d", // amber
        Severity.Info  => "#c8e6c9", // soft green (default readable)
        Severity.Debug => "#90caf9", // muted blue
        Severity.Trace => "#757575", // gray — deemphasize
        _              => "#c8e6c9",
    };

    // Pre-compiled matchers, ordered most-specific → least. Log4j layout looks like:
    //   [12:34:56] [Render thread/ERROR]: ...
    //   [12:34:56] [Server thread/WARN]: ...
    // Java exception lines begin with a leading tab/space + "at " or "Caused by:".
    private static readonly Regex _errorPattern = new(
        @"\b(ERROR|FATAL|SEVERE)\b|^\s*(Caused by:|at\s+\S+\.\S+\()|Exception|Error|Stacktrace",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex _warnPattern = new(
        @"\b(WARN(ING)?)\b|deprecat", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex _debugPattern = new(
        @"\bDEBUG\b", RegexOptions.Compiled);
    private static readonly Regex _tracePattern = new(
        @"\bTRACE\b|FINE(ST)?\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Classify a single raw log line into a severity band.</summary>
    public static Severity Classify(string line)
    {
        if (string.IsNullOrEmpty(line)) return Severity.Trace;
        // Order matters: an "Exception" in a WARN line is still an error.
        if (_errorPattern.IsMatch(line)) return Severity.Error;
        if (_warnPattern.IsMatch(line)) return Severity.Warn;
        if (_debugPattern.IsMatch(line)) return Severity.Debug;
        if (_tracePattern.IsMatch(line)) return Severity.Trace;
        return Severity.Info;
    }

    /// <summary>
    /// Classify every line in <paramref name="lines"/> and return them as
    /// <see cref="LogLine"/> records. Eagerly evaluated so the result is cheap to bind.
    /// </summary>
    public static IReadOnlyList<LogLine> ClassifyAll(IEnumerable<string> lines)
    {
        var result = new List<LogLine>();
        foreach (var l in lines)
            result.Add(new LogLine(l, Classify(l)));
        return result;
    }
}

/// <summary>A single classified log line: the raw text plus its severity-derived color.</summary>
public sealed record LogLine(string Text, LogSeverityClassifier.Severity Severity)
{
    /// <summary>Hex color for this line (resolves via <see cref="LogSeverityClassifier.ColorFor"/>).</summary>
    public string Color => LogSeverityClassifier.ColorFor(Severity);
}
