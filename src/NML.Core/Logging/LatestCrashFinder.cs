using System.IO;
using System.Linq;

namespace NML.Core.Logging;

/// <summary>
/// Locates the newest Minecraft crash report under an instance's <c>crash-reports/</c> folder and
/// the trailing lines of <c>logs/latest.log</c> for runtime context — the inputs the crash analyzer
/// needs. Pure file operations + unit-tested; used by the one-click "diagnose crash" button.
/// <para>
/// Minecraft writes crash reports as <c>crash-reports/crash-&lt;timestamp&gt;-client.txt</c>; the
/// newest by last-write time is the relevant one. The analyzer also wants a log tail so it sees the
/// final error lines even when no formal crash report exists.
/// </para>
/// </summary>
public static class LatestCrashFinder
{
    /// <summary>Number of trailing <c>latest.log</c> lines to capture as runtime context.</summary>
    public const int LogTailLines = 60;

    /// <summary>Find the newest crash-report .txt under <c>{gameDir}/crash-reports/</c>, or null
    /// when the folder is absent/empty. Newest = highest last-write time (ties broken by name).</summary>
    public static string? FindNewestCrashReport(string gameDir)
    {
        string dir = Path.Combine(gameDir, "crash-reports");
        if (!Directory.Exists(dir)) return null;
        var fi = new DirectoryInfo(dir)
            .EnumerateFiles("crash-*.txt")
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .ThenByDescending(f => f.Name)
            .FirstOrDefault();
        return fi?.FullName;
    }

    /// <summary>Read the trailing <see cref="LogTailLines"/> lines of <c>{gameDir}/logs/latest.log</c>,
    /// or null when the file is absent. The tail gives the analyzer runtime context just before a
    /// crash even when no formal crash report was produced.</summary>
    public static string? ReadLatestLogTail(string gameDir, int tailLines = LogTailLines)
    {
        if (tailLines <= 0) return null;
        string log = Path.Combine(gameDir, "logs", "latest.log");
        if (!File.Exists(log)) return null;
        // Read only the tail to avoid loading a multi-MB log into memory.
        var lines = new Queue<string>(tailLines + 1);
        foreach (string line in File.ReadLines(log))
        {
            lines.Enqueue(line);
            if (lines.Count > tailLines) lines.Dequeue();
        }
        return lines.Count == 0 ? null : string.Join('\n', lines);
    }

    /// <summary>Locate the crash inputs for a game dir: the newest crash report (if any) and the
    /// latest.log tail (if any). At least one is null only when the corresponding source is absent.</summary>
    public static CrashInputs Find(string gameDir, int tailLines = LogTailLines)
        => new(CrashReportPath: FindNewestCrashReport(gameDir), LogTail: ReadLatestLogTail(gameDir, tailLines));
}

/// <summary>The two inputs the crash analyzer consumes for a game dir.</summary>
public sealed record CrashInputs(string? CrashReportPath, string? LogTail)
{
    /// <summary>True when there's any crash material to analyze (a report or a log tail).</summary>
    public bool HasAny => CrashReportPath is not null || LogTail is not null;
}
