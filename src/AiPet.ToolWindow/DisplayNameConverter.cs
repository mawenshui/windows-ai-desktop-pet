using System;
using System.Globalization;
using System.Reflection;
using System.Windows.Data;

namespace AiPet.ToolWindow;

public sealed class DisplayNameConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null) return string.Empty;
        var displayName = value.GetType().GetProperty("DisplayName", BindingFlags.Instance | BindingFlags.Public);
        return displayName?.GetValue(value)?.ToString() ?? value.ToString() ?? string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
