using System.Globalization;

namespace MonitoringSystem.Maui.Converters;

/// <summary>
/// Конвертер: порожній рядок → false, непорожній → true.
/// Використовується для IsVisible прив'язки до string-властивостей.
/// </summary>
public class StringToBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => !string.IsNullOrEmpty(value?.ToString());

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
