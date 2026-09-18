using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace AiPet.ToolWindow;

public static class ThemeManager
{
    private static readonly IReadOnlyDictionary<string, string> LightPalette =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Paper"] = "#FFFFFCF8",
            ["Canvas"] = "#FFFAF7F2",
            ["Card"] = "#FFFFFFFF",
            ["Soft"] = "#FFF3EDE6",
            ["Line"] = "#FFE6DBCF",
            ["Muted"] = "#FF786F67",
            ["Ink"] = "#FF33302E",
            ["AccentTint"] = "#FFFCEADE",
            ["AccentSoft"] = "#FFF7DCC9",
            ["Accent"] = "#FFB45E32",
            ["AccentHover"] = "#FFA95129",
            ["AccentPressed"] = "#FF934622",
            ["AccentForeground"] = "#FFFFFFFF",
            ["FieldHoverLine"] = "#FFD6C4B5",
            ["ErrorTint"] = "#FFFFF3F0",
            ["SuccessTint"] = "#FFF2F7F3",
            ["Shadow"] = "#FF6A5140",
            ["Success"] = "#FF4F7A60",
            ["Error"] = "#FFAE4F4F",
        };

    private static readonly IReadOnlyDictionary<string, string> DarkPalette =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Paper"] = "#FF24201E",
            ["Canvas"] = "#FF1D1A18",
            ["Card"] = "#FF302A27",
            ["Soft"] = "#FF3B3430",
            ["Line"] = "#FF5D514A",
            ["Muted"] = "#FFD0C3BA",
            ["Ink"] = "#FFFFF8F2",
            ["AccentTint"] = "#FF493126",
            ["AccentSoft"] = "#FF66402E",
            ["Accent"] = "#FFFF9A68",
            ["AccentHover"] = "#FFFFB087",
            ["AccentPressed"] = "#FFFFC4A7",
            ["AccentForeground"] = "#FF2A1710",
            ["FieldHoverLine"] = "#FF78685F",
            ["ErrorTint"] = "#FF402826",
            ["SuccessTint"] = "#FF26362C",
            ["Shadow"] = "#FF000000",
            ["Success"] = "#FF86CCA0",
            ["Error"] = "#FFFF9B9B",
        };

    private static readonly IReadOnlyDictionary<string, string> HighContrastPalette =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Paper"] = "#FF000000",
            ["Canvas"] = "#FF000000",
            ["Card"] = "#FF000000",
            ["Soft"] = "#FF1A1A1A",
            ["Line"] = "#FFFFFFFF",
            ["Muted"] = "#FFFFFFFF",
            ["Ink"] = "#FFFFFFFF",
            ["AccentTint"] = "#FF000000",
            ["AccentSoft"] = "#FF1A1A1A",
            ["Accent"] = "#FF0066CC",
            ["AccentHover"] = "#FF00509E",
            ["AccentPressed"] = "#FF003B75",
            ["AccentForeground"] = "#FFFFFFFF",
            ["FieldHoverLine"] = "#FFFFFFFF",
            ["ErrorTint"] = "#FF000000",
            ["SuccessTint"] = "#FF000000",
            ["Shadow"] = "#FF000000",
            ["Success"] = "#FF00CC66",
            ["Error"] = "#FFFF8080",
        };

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
            "dark" => DarkPalette,
            "high-contrast" => HighContrastPalette,
            _ => LightPalette,
        };
        foreach (var pair in colors)
        {
            var color = (Color)ColorConverter.ConvertFromString(pair.Value);
            var colorKey = pair.Key + "Color";
            if (target.Contains(colorKey))
            {
                // Theme brushes bind to these Color resources dynamically. This
                // keeps every StaticResource brush reference alive while its
                // color changes across the complete visual tree.
                target[colorKey] = color;
                continue;
            }

            // Compatibility path for small test/host dictionaries that expose
            // the brushes directly rather than the color indirection.
            if (target[pair.Key] is SolidColorBrush brush && !brush.IsFrozen)
                brush.Color = color;
            else
                target[pair.Key] = new SolidColorBrush(color);
        }
    }
}
