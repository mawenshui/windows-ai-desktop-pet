using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace AiPet.ToolWindow;

public static class ThemeManager
{
    public static string Resolve(string preference)
    {
        if (SystemParameters.HighContrast || preference == "high-contrast") return "high-contrast";
        if (preference is "light" or "dark") return preference;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return Convert.ToInt32(key?.GetValue("AppsUseLightTheme", 1)) == 0 ? "dark" : "light";
        }
        catch { return "light"; }
    }

    public static void Apply(ResourceDictionary resources, string preference)
    {
        var target = resources.MergedDictionaries.FirstOrDefault() ?? resources;
        var theme = Resolve(preference);
        var colors = theme switch
        {
            "dark" => new Dictionary<string, string> { ["Paper"]="#FF24201E", ["Canvas"]="#FF1D1A18", ["Card"]="#FF302A27", ["Soft"]="#FF3B3430", ["Line"]="#FF5D514A", ["Muted"]="#FFD0C3BA", ["Ink"]="#FFFFF8F2", ["AccentTint"]="#FF493126", ["AccentSoft"]="#FF66402E", ["Accent"]="#FFFF9A68", ["AccentHover"]="#FFFFB087", ["AccentPressed"]="#FFFFC4A7", ["Success"]="#FF86CCA0", ["Error"]="#FFFF9B9B" },
            "high-contrast" => new Dictionary<string, string> { ["Paper"]="#FF000000", ["Canvas"]="#FF000000", ["Card"]="#FF000000", ["Soft"]="#FF1A1A1A", ["Line"]="#FFFFFFFF", ["Muted"]="#FFFFFFFF", ["Ink"]="#FFFFFFFF", ["AccentTint"]="#FF000000", ["AccentSoft"]="#FF1A1A1A", ["Accent"]="#FF0066CC", ["AccentHover"]="#FF00509E", ["AccentPressed"]="#FF003B75", ["Success"]="#FF00CC66", ["Error"]="#FFFF8080" },
            _ => new Dictionary<string, string>(),
        };
        foreach (var pair in colors)
        {
            var color = (Color)ColorConverter.ConvertFromString(pair.Value);
            if (target[pair.Key] is SolidColorBrush brush && !brush.IsFrozen) brush.Color = color;
            else target[pair.Key] = new SolidColorBrush(color);
        }
    }
}
