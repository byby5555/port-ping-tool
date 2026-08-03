using Avalonia.Data.Converters;
using Avalonia.Media;
using System.Globalization;

namespace PortPingTool.Services;

/// <summary>Converts a hex color string like "#34C759" to a SolidColorBrush.</summary>
public sealed class HexToBrushConverter : IValueConverter
{
    public static readonly HexToBrushConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string hex && Color.TryParse(hex, out var c))
            return new SolidColorBrush(c);
        return new SolidColorBrush(Colors.Gray);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
