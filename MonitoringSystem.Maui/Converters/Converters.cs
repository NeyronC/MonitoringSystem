using System.Globalization;

namespace MonitoringSystem.Maui.Converters;

/// <summary>Bool → Color. TrueColor/FalseColor або через ConverterParameter "true,false"</summary>
public class BoolToColorConverter : IValueConverter
{
    public Color TrueColor  { get; set; } = Colors.Green;
    public Color FalseColor { get; set; } = Colors.Gray;

    public object Convert(object? value, Type targetType,
        object? parameter, CultureInfo culture)
    {
        // Підтримуємо параметр "#22c55e,#d1d5db"
        if (parameter is string p)
        {
            var parts = p.Split(',');
            if (parts.Length == 2)
            {
                return value is true
                    ? Color.FromArgb(parts[0].Trim())
                    : Color.FromArgb(parts[1].Trim());
            }
        }
        return value is true ? TrueColor : FalseColor;
    }

    public object ConvertBack(object? value, Type targetType,
        object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>Null-check → bool</summary>
public class NotNullConverter : IValueConverter
{
    public object Convert(object? value, Type targetType,
        object? parameter, CultureInfo culture)
        => value != null;

    public object ConvertBack(object? value, Type targetType,
        object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>Bool → один з двох рядків через ConverterParameter "ТакText|НіText"</summary>
public class BoolToStringConverter : IValueConverter
{
    public object Convert(object? value, Type targetType,
        object? parameter, CultureInfo culture)
    {
        var parts = parameter?.ToString()?.Split('|');
        if (parts?.Length == 2)
            return value is true ? parts[0] : parts[1];
        return value?.ToString() ?? "";
    }

    public object ConvertBack(object? value, Type targetType,
        object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>Severity → фоновий колір бейджа</summary>
public class SeverityColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType,
        object? parameter, CultureInfo culture)
        => value?.ToString() switch
        {
            "Critical" => Color.FromArgb("#FCEBEB"),
            "Warning"  => Color.FromArgb("#FAEEDA"),
            _          => Color.FromArgb("#E6F1FB")
        };

    public object ConvertBack(object? value, Type targetType,
        object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>Username → ініціали (перша літера). "john.doe" → "J"</summary>
public class InitialsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType,
        object? parameter, CultureInfo culture)
    {
        var name = value?.ToString() ?? "";
        if (string.IsNullOrEmpty(name)) return "?";
        return name[0].ToString().ToUpper();
    }

    public object ConvertBack(object? value, Type targetType,
        object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>Role → колір фону аватара. "Admin" → фіолетовий, "User" → синій</summary>
public class RoleColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType,
        object? parameter, CultureInfo culture)
        => value?.ToString() switch
        {
            "Admin" => Color.FromArgb("#EEEDFE"),
            _       => Color.FromArgb("#E6F1FB")
        };

    public object ConvertBack(object? value, Type targetType,
        object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Role string == ConverterParameter → true (для IsVisible).
/// ConverterParameter="User" → true тільки для юзерів.
/// ConverterParameter="Admin" → true тільки для адмінів.
/// </summary>
public class IsRoleConverter : IValueConverter
{
    public object Convert(object? value, Type targetType,
        object? parameter, CultureInfo culture)
        => string.Equals(
            value?.ToString(),
            parameter?.ToString(),
            StringComparison.OrdinalIgnoreCase);

    public object ConvertBack(object? value, Type targetType,
        object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
