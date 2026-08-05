namespace NML.AICore.Features;

/// <summary>
/// Parsed view of a Minecraft crash report or log, extracting only the parts an LLM needs
/// to diagnose the cause. Sending the whole (often huge) report wastes tokens and distracts
/// the model; this keeps the prompt tight.
/// </summary>
public sealed class CrashReport
{
    /// <summary>The <c>Description:</c> line — the top-level cause (e.g. "Exception ticking world").</summary>
    public string? Description { get; init; }

    /// <summary>The head of the main stack trace (first N frames, skipping deep Mojang internals).</summary>
    public string StackTraceHead { get; init; } = string.Empty;

    /// <summary>The <c>Caused by:</c> chain, if present.</summary>
    public string CausedBy { get; init; } = string.Empty;

    /// <summary>System Details block: MC version, modloader, Java, OS, CPU, memory.</summary>
    public string SystemDetails { get; init; } = string.Empty;

    /// <summary>Loaded mods (parsed from the System Details "Mods" section), id → version.</summary>
    public IReadOnlyDictionary<string, string> Mods { get; init; } = new Dictionary<string, string>();

    /// <summary>Trailing lines of latest.log for runtime context just before the crash.</summary>
    public string LogTail { get; init; } = string.Empty;

    /// <summary>Did the source contain a recognizable crash report, or was it just a log?</summary>
    public bool LooksLikeCrashReport { get; init; }

    /// <summary>First 16 hex chars of the raw SHA-1 of the source — used as a cache key.</summary>
    public string SourceHash { get; init; } = string.Empty;
}

/// <summary>
/// Extracts a <see cref="CrashReport"/> from raw Minecraft crash-report or latest.log text.
/// Pure function, no I/O — fully unit-testable against canned inputs.
/// </summary>
public static class CrashReportParser
{
    private const int MaxStackHeadChars = 3000;
    private const int MaxLogTailLines = 60;
    private const int MaxSystemDetailsChars = 4000;

    /// <summary>Parse raw crash text into a structured <see cref="CrashReport"/>.</summary>
    public static CrashReport Parse(string raw, string? logTail = null)
    {
        bool isCrashReport = raw.Contains("//-- ") || raw.Contains("Description:");
        string sourceHash = ComputeShortHash(raw);

        string? description = ExtractDescription(raw);
        string stackHead = ExtractStackHead(raw);
        string causedBy = ExtractCausedBy(raw);
        string systemDetails = Truncate(ExtractSection(raw, "System Details"), MaxSystemDetailsChars);
        var mods = ExtractMods(systemDetails);
        string tail = logTail is null ? string.Empty : TailLines(logTail, MaxLogTailLines);

        return new CrashReport
        {
            Description = description,
            StackTraceHead = stackHead,
            CausedBy = causedBy,
            SystemDetails = systemDetails,
            Mods = mods,
            LogTail = tail,
            LooksLikeCrashReport = isCrashReport,
            SourceHash = sourceHash,
        };
    }

    private static string? ExtractDescription(string raw)
    {
        // "Description: <something>" appears on a line in crash reports.
        int idx = raw.IndexOf("Description:", StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return null;
        int eol = raw.IndexOfAny(new[] { '\r', '\n' }, idx);
        if (eol < 0) eol = raw.Length;
        string line = raw[idx..eol].Trim();
        return line.StartsWith("Description:", StringComparison.OrdinalIgnoreCase)
            ? line["Description:".Length..].Trim()
            : line;
    }

    private static string ExtractStackHead(string raw)
    {
        // The stack trace begins after the Description line; capture until the next
        // major section header ("// --") or a blank-line boundary.
        int descIdx = raw.IndexOf("Description:", StringComparison.OrdinalIgnoreCase);
        if (descIdx < 0) return string.Empty;

        int start = raw.IndexOfAny(new[] { '\r', '\n' }, descIdx);
        if (start < 0) return string.Empty;
        int endMarker = raw.IndexOf("// --", start, StringComparison.Ordinal);
        if (endMarker < 0) endMarker = raw.Length;
        return Truncate(raw[start..Math.Min(endMarker, raw.Length)].Trim(), MaxStackHeadChars);
    }

    private static string ExtractCausedBy(string raw)
    {
        var sb = new System.Text.StringBuilder();
        foreach (string line in SplitLines(raw))
        {
            string trimmed = line.TrimStart();
            if (!trimmed.StartsWith("Caused by:", StringComparison.OrdinalIgnoreCase)) continue;
            // Capture the Caused-by line plus the immediate stack frames that follow it.
            sb.AppendLine(trimmed);
        }
        return sb.ToString().Trim();
    }

    private static string ExtractSection(string raw, string sectionName)
    {
        // Section headers come in two real-world shapes:
        //   "// -- System Details --//"   and   "//-- System Details --//"
        // Match the section name between "//" markers, tolerant of internal spacing.
        int idx = raw.IndexOf(sectionName, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return string.Empty;

        // Find the closing "//" after the section name.
        int closingMarker = raw.IndexOf("//", idx + sectionName.Length, StringComparison.Ordinal);
        if (closingMarker < 0) closingMarker = idx + sectionName.Length;
        int start = closingMarker + 2;

        // The next section begins at the next "//" line; the body runs until then.
        int next = raw.IndexOf("//", start, StringComparison.Ordinal);
        if (next < 0) next = raw.Length;
        return raw[start..next].Trim();
    }

    private static IReadOnlyDictionary<string, string> ExtractMods(string systemDetails)
    {
        var mods = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        int idx = systemDetails.IndexOf("Mods:", StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return mods;
        string block = systemDetails[idx..];
        foreach (string line in SplitLines(block))
        {
            string t = line.Trim();
            if (string.IsNullOrEmpty(t) || t.StartsWith("Mods:", StringComparison.OrdinalIgnoreCase)) continue;

            // Two real formats:
            //   {id@version}        e.g. {sodium@0.5.3}      (modern, brace-wrapped, @ separator)
            //   id:version          e.g. sodium:0.5.3        (older, colon separator)
            string entry = t.Trim('{', '}', ' ');
            if (string.IsNullOrEmpty(entry)) continue;

            int sep = entry.IndexOfAny(new[] { '@', ':' });
            if (sep <= 0) continue;
            string id = entry[..sep].Trim();
            string ver = entry[(sep + 1)..].Trim();
            if (!string.IsNullOrEmpty(id)) mods[id] = ver;
        }
        return mods;
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "\n…[truncated]…";

    private static string TailLines(string s, int count)
    {
        string[] lines = SplitLines(s);
        return lines.Length <= count
            ? s
            : string.Join('\n', lines[^count..]);
    }

    private static string[] SplitLines(string s) =>
        s.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);

    private static string ComputeShortHash(string s)
    {
        byte[] hash = System.Security.Cryptography.SHA1.HashData(
            System.Text.Encoding.UTF8.GetBytes(s));
        return Convert.ToHexString(hash).ToLowerInvariant()[..16];
    }
}
