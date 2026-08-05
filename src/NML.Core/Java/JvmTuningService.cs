namespace NML.Core.Java;

/// <summary>Recommended JVM tuning settings for the current hardware.</summary>
public sealed class JvmTuningRecommendation
{
    /// <summary>Recommended GC strategy string (e.g. "-XX:+UseG1GC" or "-XX:+UseZGC").</summary>
    public string GcArgs { get; init; } = string.Empty;

    /// <summary>Recommended memory in MB.</summary>
    public int RecommendedMemoryMb { get; init; }

    /// <summary>Full recommended JVM args line (GC + performance flags).</summary>
    public string FullArgs { get; init; } = string.Empty;

    /// <summary>Human-readable summary of why these settings were chosen.</summary>
    public string Explanation { get; init; } = string.Empty;
}

/// <summary>
/// Recommends JVM tuning arguments based on the current system's CPU core count and RAM.
/// Mirrors HMCL/PCL's auto-tuning: picks G1GC for most systems, ZGC for high-RAM modern
/// setups, and sets AlwaysPreTouch + StringDeduplication for smoother gameplay.
/// </summary>
public static class JvmTuningService
{
    /// <summary>Generate a recommendation for the current machine.</summary>
    public static JvmTuningRecommendation Recommend(int javaMajorVersion = 17)
    {
        int cores = Environment.ProcessorCount;
        long ramMb = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (1024 * 1024);

        // Memory: recommend 2/3 of system RAM, clamped 1024..32768.
        int recMem = ramMb > 0
            ? Math.Clamp((int)(ramMb * 0.66), 1024, 32768)
            : 4096;

        // GC strategy: ZGC for Java 17+ with ≥8GB RAM and ≥4 cores; G1GC otherwise.
        bool useZgc = javaMajorVersion >= 15 && recMem >= 6144 && cores >= 4;

        var args = new List<string>();
        string explanation;

        if (useZgc)
        {
            args.Add("-XX:+UseZGC");
            explanation = $"ZGC recommended ({cores} cores, {ramMb / 1024.0:F1} GB RAM, Java {javaMajorVersion}+).";
        }
        else
        {
            args.Add("-XX:+UseG1GC");
            args.Add($"-XX:MaxGCPauseMillis=50");
            explanation = $"G1GC recommended ({cores} cores, {ramMb / 1024.0:F1} GB RAM).";
        }

        // Common performance flags.
        args.Add("-XX:+AlwaysPreTouch");
        if (javaMajorVersion >= 8) args.Add("-XX:+UseStringDeduplication");

        // Aikar's flags (popular in the Minecraft community) for G1GC tuning.
        if (!useZgc)
        {
            args.Add("-XX:G1NewSizePercent=20");
            args.Add("-XX:G1ReservePercent=20");
            args.Add("-XX:MaxGCPauseMillis=50");
            args.Add("-XX:G1HeapRegionSize=32M");
        }

        return new JvmTuningRecommendation
        {
            GcArgs = useZgc ? "-XX:+UseZGC" : "-XX:+UseG1GC",
            RecommendedMemoryMb = recMem,
            FullArgs = string.Join(" ", args),
            Explanation = explanation,
        };
    }

    /// <summary>The CPU core count.</summary>
    public static int CpuCores => Environment.ProcessorCount;
}
