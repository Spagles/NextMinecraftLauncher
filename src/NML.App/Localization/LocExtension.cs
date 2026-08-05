using Avalonia.Markup.Xaml;

namespace NML.App.Localization;

/// <summary>
/// XAML markup extension for localized strings. Usage in .axaml:
/// <code>Title="{Loc 'nav.home'}"</code>
/// Resolves the key through <see cref="LocalizationService.Instance"/> at bind time.
/// </summary>
public sealed class LocExtension : MarkupExtension
{
    public LocExtension() { }
    public LocExtension(string key) => Key = key;

    /// <summary>The localization key (e.g. <c>nav.home</c>).</summary>
    public string Key { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider)
        => LocalizationService.Instance[Key];
}
