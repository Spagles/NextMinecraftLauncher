using System.Collections.Concurrent;
using System.Globalization;

namespace NML.App.Localization;

/// <summary>
/// The i18n core. Loads culture-keyed string tables (JSON resource files), exposes a
/// <see cref="this[string]"/> indexer for XAML binding, and raises <see cref="LanguageChanged"/>
/// so views can refresh when the user switches languages at runtime.
///
/// String keys are grouped by dotted namespace, e.g. <c>nav.home</c>, <c>home.launch</c>,
/// <c>settings.language</c>. Missing keys fall back to the key itself (never throw) so the
/// UI always renders even if a translation is incomplete.
/// </summary>
public sealed class LocalizationService
{
    private readonly ConcurrentDictionary<string, Dictionary<string, string>> _cultures = new();
    private CultureInfo _current = CultureInfo.CurrentUICulture;

    public static LocalizationService Instance { get; } = new();

    /// <summary>Currently active culture.</summary>
    public CultureInfo CurrentCulture
    {
        get => _current;
        set
        {
            if (_current.Name == value.Name) return;
            _current = value;
            CultureInfo.DefaultThreadCurrentUICulture = value;
            // Also set CurrentCulture so RTL detection and date/number formatting follow the user's choice.
            CultureInfo.DefaultThreadCurrentCulture = value;
            LanguageChanged?.Invoke(this, value);
        }
    }

    /// <summary>Fired when the active culture changes (XAML bindings refresh on this).</summary>
    public event EventHandler<CultureInfo>? LanguageChanged;

    /// <summary>Register a string table for a culture (e.g. "zh-CN" → {key:text} map).</summary>
    public void RegisterCulture(string cultureName, Dictionary<string, string> strings)
    {
        _cultures[cultureName] = strings;
        // Make the culture available even if it was registered after CurrentCulture was set.
        if (_current.Name.Equals(cultureName, StringComparison.OrdinalIgnoreCase))
            LanguageChanged?.Invoke(this, _current);
    }

    /// <summary>The list of cultures with a registered string table.</summary>
    public IReadOnlyList<CultureInfo> AvailableCultures =>
        _cultures.Keys.Select(k =>
        {
            try { return CultureInfo.GetCultureInfo(k); }
            catch { return new CultureInfo(k); }
        }).ToList();

    /// <summary>
    /// Resolve a key to the current culture's text. Falls back to English, then to the key
    /// itself — never throws, so a missing translation can't break the UI.
    /// </summary>
    public string this[string key]
    {
        get
        {
            if (string.IsNullOrEmpty(key)) return string.Empty;

            // 1. Try the current culture's exact key.
            string resolvedKey = ResolveCultureKey(_current.Name) ?? _current.Name;
            if (_cultures.TryGetValue(resolvedKey, out var cur) && cur.TryGetValue(key, out var s))
                return s;

            // 2. Fall back to English.
            if (_cultures.TryGetValue("en-US", out var en) && en.TryGetValue(key, out var e))
                return e;

            // 3. Fall back to the raw key (visible — encourages completing the translation).
            return key;
        }
    }

    /// <summary>Convenience for code-behind.</summary>
    public string Get(string key) => this[key];

    /// <summary>Whether a given culture has been registered. Tries exact match, then the
    /// two-letter neutral prefix (e.g. "zh-Hans-CN" → "zh"), so system cultures that don't
    /// exactly match a file key still resolve.</summary>
    public bool Supports(string cultureName)
    {
        if (string.IsNullOrEmpty(cultureName)) return false;
        if (_cultures.ContainsKey(cultureName)) return true;
        // Try the neutral two-letter prefix: "zh-Hans-CN" → "zh", "fr-FR" → "fr".
        if (cultureName.Length >= 2)
        {
            string neutral = cultureName[..2];
            // Check if any registered key starts with this prefix.
            foreach (var key in _cultures.Keys)
                if (key.StartsWith(neutral + "-", StringComparison.OrdinalIgnoreCase) ||
                    key.Equals(neutral, StringComparison.OrdinalIgnoreCase))
                    return true;
        }
        return false;
    }

    /// <summary>Resolve a culture name to the best matching registered key, or null.</summary>
    public string? ResolveCultureKey(string cultureName)
    {
        if (string.IsNullOrEmpty(cultureName)) return null;
        if (_cultures.ContainsKey(cultureName)) return cultureName;
        // Try the neutral two-letter prefix.
        if (cultureName.Length >= 2)
        {
            string neutral = cultureName[..2];
            foreach (var key in _cultures.Keys)
                if (key.StartsWith(neutral + "-", StringComparison.OrdinalIgnoreCase) ||
                    key.Equals(neutral, StringComparison.OrdinalIgnoreCase))
                    return key;
        }
        return null;
    }
}
