using Avalonia.Data.Converters;
using Avalonia.Media;
using System.Globalization;

namespace AssetsAndMapEditor.App.Converters;

public class BoolToForegroundConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is true
            ? new SolidColorBrush(Color.Parse("#89b4fa"))
            : new SolidColorBrush(Color.Parse("#585b70"));
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
