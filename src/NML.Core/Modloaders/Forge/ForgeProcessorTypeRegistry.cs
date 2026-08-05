namespace NML.Core.Modloaders.Forge;

/// <summary>
/// Identifies the well-known processor types Forge installers use, so the executor can apply
/// type-specific argument conventions. Modern Forge install_profile.json processors are
/// generic (jar + args), but several have stable, well-documented invocation patterns that
/// differ from the generic "main-class from manifest" path.
/// </summary>
public enum ForgeProcessorKind
{
    /// <summary>Generic — run the jar's main class with the declared args (default).</summary>
    Generic,

    /// <summary>net.md_5.specialsource: SpecialSource — deobfuscation/remapping.
    /// Invoked as: SpecialSource --in-jar {input} --out-jar {output} -m {mappings} --srg-in-map {srg}</summary>
    SpecialSource,

    /// <summary>Jar-signing via a signing processor (e.g. net.minecraftforge:jarjar / SigningFix).
    /// Invoked as: &lt;signer&gt; --signedJar {output} --jar {input} --alias forge --keypass ... --storepass ...</summary>
    JarSigner,

    /// <summary>Binary patch applier (e.g. net.minecraftforge:binarypatcher). Pattern:
    /// --output {output} --input {input} --patches {patch file}</summary>
    BinaryPatch,

    /// <summary>A processor that only copies a jar (Identity/ExtractProcessor) — no transform.</summary>
    Copy,
}

/// <summary>
/// Recognizes the processor type from a ForgeProcessor's jar coordinate + args. This is the
/// seam that lets the executor apply type-specific conventions without parsing each jar.
/// </summary>
public static class ForgeProcessorTypeRegistry
{
    /// <summary>Classify a processor by inspecting its jar coordinate and args.</summary>
    public static ForgeProcessorKind Classify(ForgeProcessor p)
    {
        string jar = (p.Jar ?? string.Empty).ToLowerInvariant();
        var args = p.Args ?? new();

        // SpecialSource deobfuscation tool.
        if (jar.Contains("specialsource") || jar.Contains("net.md-5"))
            return ForgeProcessorKind.SpecialSource;

        // Binary patcher.
        if (jar.Contains("binarypatcher") || jar.Contains("binarypatch"))
            return ForgeProcessorKind.BinaryPatch;

        // Jar signing — any processor whose args mention signing tokens or whose jar is a signer.
        if (jar.Contains("jarsigner") || jar.Contains("signingfix") || jar.Contains("jarjar")
            || args.Any(a => a.Contains("--signedjar", StringComparison.OrdinalIgnoreCase)
                          || a.Contains("--keypass", StringComparison.OrdinalIgnoreCase)
                          || a.Contains("--storepass", StringComparison.OrdinalIgnoreCase)))
            return ForgeProcessorKind.JarSigner;

        // Copy-only processors.
        if (jar.Contains("extractprocessor") || jar.Contains("identity"))
            return ForgeProcessorKind.Copy;

        return ForgeProcessorKind.Generic;
    }

    /// <summary>
    /// The expected arg-count sanity check for a kind. Returns null when there's no fixed
    /// expectation (the processor can take any number of args).
    /// </summary>
    public static int? ExpectedMinArgs(ForgeProcessorKind kind) => kind switch
    {
        ForgeProcessorKind.SpecialSource => 4, // --in-jar --out-jar -m
        ForgeProcessorKind.JarSigner => 3,     // --signedJar --jar --alias
        ForgeProcessorKind.BinaryPatch => 3,   // --output --input --patches
        _ => null,
    };

    /// <summary>Human-readable description (for logging).</summary>
    public static string Describe(ForgeProcessorKind kind) => kind switch
    {
        ForgeProcessorKind.SpecialSource => "deobfuscation (SpecialSource)",
        ForgeProcessorKind.JarSigner => "jar signing",
        ForgeProcessorKind.BinaryPatch => "binary patching",
        ForgeProcessorKind.Copy => "copy/extract",
        _ => "generic processor",
    };
}
