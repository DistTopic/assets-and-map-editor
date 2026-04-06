using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using System.Globalization;

namespace AssetsAndMapEditor.App.Converters;

public class BoolToHighlightBoxShadowConverter : IValueConverter
{
    private static readonly BoxShadows Active = BoxShadows.Parse("0 0 0 1 #11111b, 0 0 8 0 #ff44ff");
    private static readonly BoxShadows None = default;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? Active : None;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
