using System;
using System.Globalization;
using System.Windows.Data;

namespace ControlDesk.App.Converters;

/// <summary>
/// Converts an enum value to a boolean for RadioButton IsChecked binding.
/// Returns true when the enum value matches the parameter.
/// </summary>
[ValueConversion(typeof(Enum), typeof(bool))]
public sealed class EnumToBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value == null || parameter == null)
            return false;

        var enumValue = value.ToString();
        var targetValue = parameter.ToString();

        return string.Equals(enumValue, targetValue, StringComparison.OrdinalIgnoreCase);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isChecked && isChecked && parameter != null)
        {
            return Enum.Parse(targetType, parameter.ToString()!, true);
        }

        return Binding.DoNothing;
    }
}