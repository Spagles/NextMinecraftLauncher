namespace NML.Core.Java;

/// <summary>
/// Pre-launch Java compatibility check: compares the Java runtime the launcher is about to use
/// against the version's required major version, so a mismatched runtime (e.g. Java 8 for 1.17+,
/// which needs Java 16/17) is caught before the game launches and crashes instantly. Pure + unit
/// tested; the launch path calls it and surfaces the result as a status.
/// <para>
/// The rule matches the launcher's existing runtime-selection logic: a runtime is acceptable when
/// its major version is greater-than-or-equal to the required major version (Minecraft is forward-
/// compatible with newer Java within reason; 1.17+ runs on 17/18/19/20/21).
/// </para>
/// </summary>
public static class JavaVersionValidator
{
    /// <summary>Validate that <paramref name="actualMajor"/> satisfies <paramref name="requiredMajor"/>.
    /// Returns a result carrying OK/failure + a human-readable reason.</summary>
    public static JavaCompatibility Validate(int requiredMajor, int actualMajor)
    {
        if (actualMajor < requiredMajor)
        {
            return new JavaCompatibility(
                Ok: false,
                Reason: JavaIncompatibilityReason.TooOld,
                Message: $"Java {actualMajor} is too old for this version (requires Java {requiredMajor}+). " +
                         $"Select a newer Java runtime in the instance settings or Settings.");
        }
        return new JavaCompatibility(Ok: true, Reason: JavaIncompatibilityReason.None, Message: string.Empty);
    }

    /// <summary>Convenience: validate the runtime an instance is about to launch with against a
    /// version's required major. A null runtime is reported as missing (not incompatible).</summary>
    public static JavaCompatibility Validate(int requiredMajor, JavaRuntime? runtime)
        => runtime is null
            ? new JavaCompatibility(Ok: false, Reason: JavaIncompatibilityReason.Missing,
                Message: "No Java runtime selected. Install Java or pick a runtime in Settings.")
            : Validate(requiredMajor, runtime.MajorVersion);
}

/// <summary>The outcome of a pre-launch Java check.</summary>
public sealed record JavaCompatibility(bool Ok, JavaIncompatibilityReason Reason, string Message);

/// <summary>Why a runtime was rejected (or None when OK).</summary>
public enum JavaIncompatibilityReason
{
    /// <summary>The runtime is compatible.</summary>
    None,
    /// <summary>The runtime's major version is below the required major (would crash at launch).</summary>
    TooOld,
    /// <summary>No Java runtime was selected/detected.</summary>
    Missing,
}
