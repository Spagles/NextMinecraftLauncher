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

            // 1. Try the current culture.
            if (_cultures.TryGetValue(_current.Name, out var cur) && cur.TryGetValue(key, out var s))
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

    /// <summary>Whether a given culture has been registered.</summary>
    public bool Supports(string cultureName) => _cultures.ContainsKey(cultureName);
}
