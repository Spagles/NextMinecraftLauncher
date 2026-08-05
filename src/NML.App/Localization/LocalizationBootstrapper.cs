using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace NML.App.Localization;

/// <summary>
/// Loads all embedded <c>Localization/*.json</c> files into <see cref="LocalizationService"/>
/// at startup, and applies the saved (or system-default) culture.
/// </summary>
public static class LocalizationBootstrapper
{
    public static void Initialize(string? savedCulture = null)
    {
        Assembly asm = Assembly.GetExecutingAssembly();
        var svc = LocalizationService.Instance;

        // Find embedded resources matching "NML.App.Localization.<culture>.json".
        foreach (string name in asm.GetManifestResourceNames())
        {
            const string prefix = "NML.App.Localization.";
            const string suffix = ".json";
            if (!name.StartsWith(prefix, StringComparison.Ordinal) ||
                !name.EndsWith(suffix, StringComparison.Ordinal)) continue;

            string culture = name[prefix.Length..^suffix.Length];
            using var stream = asm.GetManifestResourceStream(name);
            if (stream is null) continue;

            using var doc = JsonDocument.Parse(stream);
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.Value.ValueKind == JsonValueKind.String)
                    map[prop.Name] = prop.Value.GetString() ?? string.Empty;
            }
            svc.RegisterCulture(culture, map);
        }

        // Apply saved culture, else fall back to system UI culture if supported, else en-US.
        string chosen = savedCulture
                        ?? (svc.Supports(CultureInfo.CurrentUICulture.Name)
                                ? CultureInfo.CurrentUICulture.Name
                                : "en-US");
        try { svc.CurrentCulture = CultureInfo.GetCultureInfo(chosen); }
        catch { svc.CurrentCulture = new CultureInfo("en-US"); }
    }
}
