using System.IO;

namespace NML.Core.Theming;

/// <summary>
/// Loads, validates, and persists a user-supplied custom CSS stylesheet that the launcher injects
/// into the live Avalonia theme at runtime (PCL-style "import custom CSS"). Pure file/string
/// operations + a defensive validation pass, unit-tested without spinning up the UI.
/// <para>
/// The validation is intentionally light: it strips empty input, caps the size, rejects inputs
/// that look like binary, and trims a leading BOM. It does not parse CSS (Avalonia will report
/// parse errors at apply time); the goal is to refuse obviously broken input early so the apply
/// path receives clean text.
/// </para>
/// </summary>
public sealed class CustomCssManager
{
    /// <summary>Max accepted stylesheet size (1 MiB) — defends against pasting a multi-MB blob.</summary>
    public const int MaxBytes = 1 * 1024 * 1024;

    /// <summary>Default file name the launcher stores the active user CSS under.</summary>
    public const string FileName = "custom.css";

    private readonly string _file;

    public CustomCssManager(string settingsDir)
    {
        Directory.CreateDirectory(settingsDir);
        _file = Path.Combine(settingsDir, FileName);
    }

    /// <summary>Absolute path of the persisted stylesheet.</summary>
    public string FilePath => _file;

    /// <summary>
    /// Validate raw user input, returning either the cleaned-up CSS or null when it should be
    /// rejected. Empty/whitespace → null (treated as "no custom CSS"). Binary or oversized → null.
    /// A leading UTF-8 BOM is stripped.
    /// </summary>
    public static string? Validate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        // Strip a UTF-8 BOM (EF BB BF) if the user pasted from a file that included one.
        string s = raw;
        if (s.Length > 0 && s[0] == '\uFEFF') s = s[1..];
        if (string.IsNullOrWhiteSpace(s)) return null;
        // Reject inputs containing NULs (binary) — a hand-written CSS file never contains one.
        if (s.IndexOf('\0') >= 0) return null;
        // Size cap by UTF-8 byte length (comments may use multibyte chars).
        if (System.Text.Encoding.UTF8.GetByteCount(s) > MaxBytes) return null;
        return s;
    }

    /// <summary>True when <paramref name="raw"/> passes <see cref="Validate"/> (i.e. would be applied).</summary>
    public static bool IsValid(string? raw) => Validate(raw) is not null;

    /// <summary>Persist the validated CSS to the settings dir. Returns false when the input is
    /// rejected (nothing written); true when saved (or cleared when input is empty/invalid).</summary>
    public bool Save(string? raw)
    {
        string? cleaned = Validate(raw);
        if (cleaned is null)
        {
            if (File.Exists(_file)) File.Delete(_file);
            return false;
        }
        File.WriteAllText(_file, cleaned);
        return true;
    }

    /// <summary>Load the persisted CSS, or null when none saved / unreadable.</summary>
    public string? Load()
    {
        if (!File.Exists(_file)) return null;
        try { return File.ReadAllText(_file); }
        catch { return null; }
    }

    /// <summary>True when a persisted stylesheet exists and is non-empty.</summary>
    public bool HasCustomCss() => !string.IsNullOrWhiteSpace(Load());

    /// <summary>Remove the persisted stylesheet (clear custom CSS).</summary>
    public void Clear()
    {
        if (File.Exists(_file)) File.Delete(_file);
    }
}
