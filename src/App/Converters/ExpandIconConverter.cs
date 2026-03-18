using Avalonia.Data.Converters;
using System.Globalization;

namespace POriginsItemEditor.App.Converters;

/// <summary>
/// Converts a bool (IsExpanded) to a FontAwesome chevron icon string.
/// </summary>
public class ExpandIconConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? "fa-solid fa-chevron-down" : "fa-solid fa-chevron-right";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
