using Avalonia.Data.Converters;
using Avalonia.Media;
using System.Globalization;

namespace AssetsAndMapEditor.App.Converters;

public class MismatchBorderConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is true
            ? new SolidColorBrush(Color.Parse("#f38ba8"))
            : new SolidColorBrush(Color.Parse("#313244"));
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
