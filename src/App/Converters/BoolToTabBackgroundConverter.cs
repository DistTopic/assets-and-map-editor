using Avalonia.Data.Converters;
using Avalonia.Media;
using System.Globalization;

namespace AssetsAndMapEditor.App.Converters;

public class BoolToTabBackgroundConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is true
            ? new SolidColorBrush(Color.Parse("#313244"))   // active: slightly lighter
            : new SolidColorBrush(Color.Parse("#1e1e2e"));  // inactive: dark, blends with bar
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
