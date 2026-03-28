using Avalonia;
using Avalonia.Data.Converters;
using System.Globalization;

namespace AssetsAndMapEditor.App.Converters;

public class BoolToHighlightThicknessConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? new Thickness(2) : new Thickness(0);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
