using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using NML.Core.Models;

namespace NML.Core.Modloaders.Forge;

/// <summary>
/// Executes the <c>processors</c> array of a Forge <c>install_profile.json</c>. Each processor
/// is a small Java tool run in order, with arguments that reference install variables
/// (<c>{MINECRAFT_JAR}</c>, <c>{LIBRARY_DIR}</c>, <c>{PROCESSOR}</c>, <c>{SIDE}</c>, plus
/// library-coord refs like <c>[net.md-5:SpecialSource]</c>). This is the launcher-side
/// contract that turns a vanilla jar into a runnable Forge jar (deobf + binary patch + sign).
/// </summary>
public sealed class ForgeProcessorExecutor
{
    private readonly string _javaExecutable;
    private readonly ILogger<ForgeProcessorExecutor> _logger;

    public ForgeProcessorExecutor(string javaExecutable, ILogger<ForgeProcessorExecutor> logger)
    {
        _javaExecutable = javaExecutable;
        _logger = logger;
    }

    /// <summary>
    /// Run every applicable processor in <paramref name="profile"/> for the given side.
    /// Each processor is a JVM invocation: <c>java -cp &lt;processor jar&gt;:&lt;classpath&gt;
    /// &lt;main class&gt; &lt;args...&gt;</c>.
    /// </summary>
    public async Task ExecuteAsync(
        ForgeInstallProfile profile,
        ForgeProcessorContext ctx,
        string side = "client",
        CancellationToken ct = default)
    {
        if (profile.Processors is null || profile.Processors.Count == 0)
        {
            _logger.LogInformation("Forge profile has no processors; skipping processor execution.");
            return;
        }

        for (int i = 0; i < profile.Processors.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            ForgeProcessor p = profile.Processors[i];

            if (!AppliesToSide(p, side))
            {
                _logger.LogDebug("Skipping processor {Index} (side {Side} not in {Sides}).",
                    i, side, p.Sides is null ? "(none)" : string.Join(",", p.Sides));
                continue;
            }

            _logger.LogInformation("Running Forge processor {Index}/{Total} ({Jar})…",
                i + 1, profile.Processors.Count, p.Jar);

            // Build the classpath: the processor's own jar + its declared classpath libs.
            string processorJarPath = ResolveLibRef(p.Jar, ctx);
            List<string> cp = new();
            if (!string.IsNullOrEmpty(processorJarPath)) cp.Add(processorJarPath);
            if (p.Classpath is not null)
                foreach (string c in p.Classpath) cp.Add(ResolveLibRef(c, ctx));

            string classpath = string.Join(System.IO.Path.PathSeparator, cp);
            string mainClass = InferMainClass(processorJarPath);

            // Resolve each arg token (replace install variables + library refs).
            List<string> args = new();
            if (p.Args is not null)
                foreach (string a in p.Args) args.Add(ResolveArg(a, ctx));

            int exit = await RunJavaAsync(mainClass, classpath, args, ctx, ct);
            if (exit != 0)
                throw new InvalidOperationException(
                    $"Forge processor '{p.Jar}' exited with code {exit}. Forge install cannot continue.");
        }
    }

    /// <summary>True if the processor applies to <paramref name="side"/> (no sides = both).</summary>
    public static bool AppliesToSide(ForgeProcessor p, string side) =>
        p.Sides is null || p.Sides.Count == 0 || p.Sides.Contains(side, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Resolve a library-coordinate token (<c>[group:artifact:version]</c>) to its absolute path
    /// under <see cref="ForgeProcessorContext.LibraryDir"/>. Non-token args pass through unchanged.
    /// </summary>
    public static string ResolveLibRef(string? token, ForgeProcessorContext ctx)
    {
        if (string.IsNullOrEmpty(token)) return string.Empty;
        string t = token.Trim();
        if (t.StartsWith('[') && t.EndsWith(']'))
        {
            string coord = t[1..^1];
            try
            {
                var c = MavenCoordinate.Parse(coord);
                return System.IO.Path.Combine(ctx.LibraryDir, c.RelativePath);
            }
            catch { return t; }
        }
        if (t.StartsWith('{') && t.EndsWith('}'))
            return ResolveVariable(t[1..^1], ctx);
        return t;
    }

    /// <summary>Resolve a variable like <c>MINECRAFT_JAR</c> or <c>SIDE</c> to its concrete value.</summary>
    public static string ResolveVariable(string name, ForgeProcessorContext ctx) => name.ToUpperInvariant() switch
    {
        "MINECRAFT_JAR" => ctx.MinecraftJar,
        "LIBRARY_DIR" => ctx.LibraryDir,
        "PROCESSOR" => ctx.ProcessorDir,
        "SIDE" => ctx.Side,
        "ROOT" => ctx.RootDir,
        "INSTALLER" => ctx.InstallerJar,
        _ => ctx.ExtraVariables.TryGetValue(name, out var v) ? v : "{" + name + "}",
    };

    /// <summary>Resolve a single argument: substitute {variables} and [library] refs.</summary>
    public static string ResolveArg(string arg, ForgeProcessorContext ctx)
    {
        string result = arg;

        // [library:coord] → absolute path.
        if (result.StartsWith('[') && result.EndsWith(']'))
            return ResolveLibRef(result, ctx);

        // {VARIABLE} → concrete value.
        var matches = System.Text.RegularExpressions.Regex.Matches(result, @"\{([A-Z0-9_]+)\}");
        foreach (System.Text.RegularExpressions.Match m in matches)
            result = result.Replace("{" + m.Groups[1].Value + "}", ResolveVariable(m.Groups[1].Value, ctx));

        // {artifact:data} style — the modern format also wraps a maven coord in {…}; detect by ':'.
        if (result.StartsWith('{') && result.EndsWith('}'))
            return ResolveLibRef(result, ctx);

        return result;
    }

    /// <summary>
    /// Best-effort main-class inference from the processor jar path. Forge processors follow
    /// the convention that the main class is the processor jar's main manifest Main-Class; a
    /// full implementation would unzip+read it. Here we use the convention-based fallback the
    /// legacy installer used: many processors ship with a known main class.
    /// </summary>
    private static string InferMainClass(string processorJarPath)
    {
        // The convention for Forge's deobf tools is to declare the main class in the jar
        // manifest. We read it from the jar; if missing, fall back to the legacy default.
        try
        {
            using var zip = System.IO.Compression.ZipFile.OpenRead(processorJarPath);
            var entry = zip.GetEntry("META-INF/MANIFEST.MF");
            if (entry is not null)
            {
                using var s = entry.Open();
                using var r = new StreamReader(s);
                string manifest = r.ReadToEnd();
                foreach (string line in manifest.Split('\n'))
                {
                    string trimmed = line.Trim();
                    if (trimmed.StartsWith("Main-Class:", StringComparison.OrdinalIgnoreCase))
                        return trimmed["Main-Class:".Length..].Trim();
                }
            }
        }
        catch { /* fall through to default */ }
        return "net.minecraftforge.installer.actions processors"; // legacy fallback (modern jars carry a manifest)
    }

    private async Task<int> RunJavaAsync(
        string mainClass, string classpath, List<string> args, ForgeProcessorContext ctx, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(_javaExecutable)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = ctx.RootDir,
        };
        psi.ArgumentList.Add("-cp");
        psi.ArgumentList.Add(classpath);
        psi.ArgumentList.Add(mainClass);
        foreach (string a in args) psi.ArgumentList.Add(a);

        _logger.LogDebug("Forge processor: {Exe} -cp {Cp} {Main} {Args}",
            _javaExecutable, classpath, mainClass, string.Join(' ', args));

        using var p = new Process { StartInfo = psi };
        p.Start();
        await p.WaitForExitAsync(ct);
        string stderr = await p.StandardError.ReadToEndAsync(ct);
        if (p.ExitCode != 0)
            _logger.LogWarning("Processor stderr:\n{Stderr}", stderr);
        return p.ExitCode;
    }
}

/// <summary>
/// Resolved paths passed to the processor executor. All install variables derive from these.
/// </summary>
public sealed class ForgeProcessorContext
{
    public string RootDir { get; init; } = string.Empty;
    public string LibraryDir { get; init; } = string.Empty;
    public string MinecraftJar { get; init; } = string.Empty;
    public string ProcessorDir { get; init; } = string.Empty;
    public string InstallerJar { get; init; } = string.Empty;
    public string Side { get; init; } = "client";

    /// <summary>Additional named variables (e.g. modloader-specific tokens) beyond the built-ins.</summary>
    public Dictionary<string, string> ExtraVariables { get; init; } = new();
}
