using System.Windows.Markup;

namespace CrashScope.Desktop.Services;

[MarkupExtensionReturnType(typeof(string))]
public class LocaleExtension : MarkupExtension
{
    public string Key { get; set; } = string.Empty;

    public LocaleExtension() { }

    public LocaleExtension(string key)
    {
        Key = key;
    }

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        if (App.Current is App app && app.Localization is not null)
            return app.Localization.T(Key);
        return Key;
    }
}