using System.Windows;

namespace SurfaceMedic.App.Infrastructure;

public static class ThemeManager
{
    public static void Apply(string theme)
    {
        var source = theme.Equals("Light", StringComparison.OrdinalIgnoreCase)
            ? new Uri("Themes/LightTheme.xaml", UriKind.Relative)
            : new Uri("Themes/DarkTheme.xaml", UriKind.Relative);

        var dictionaries = Application.Current.Resources.MergedDictionaries;
        var existing = dictionaries.FirstOrDefault(dictionary =>
            dictionary.Source is not null &&
            (dictionary.Source.OriginalString.EndsWith("DarkTheme.xaml", StringComparison.OrdinalIgnoreCase) ||
             dictionary.Source.OriginalString.EndsWith("LightTheme.xaml", StringComparison.OrdinalIgnoreCase)));

        var replacement = new ResourceDictionary { Source = source };
        if (existing is null)
        {
            dictionaries.Insert(0, replacement);
            return;
        }

        var index = dictionaries.IndexOf(existing);
        dictionaries[index] = replacement;
    }
}
