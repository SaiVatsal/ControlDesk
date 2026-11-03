using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace ControlDesk.App.Converters;

/// <summary>
/// Converts boolean to a specified brush.
/// </summary>
[ValueConversion(typeof(bool), typeof(System.Windows.Media.Brush))]
public sealed class BooleanToBrushConverter : IValueConverter
{
    public System.Windows.Media.Brush TrueBrush { get; set; } = System.Windows.Media.Brushes.Green;
    public System.Windows.Media.Brush FalseBrush { get; set; } = System.Windows.Media.Brushes.Gray;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool b)
            return b ? TrueBrush : FalseBrush;

        if (parameter is System.Windows.Media.Brush p)
            return p;

        return FalseBrush;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Converts boolean to text (true|false).
/// </summary>
[ValueConversion(typeof(bool), typeof(string))]
public sealed class BooleanToTextConverter : IValueConverter
{
    public string TrueText { get; set; } = "True";
    public string FalseText { get; set; } = "False";

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool b)
            return b ? TrueText : FalseText;

        if (parameter is string s)
        {
            var parts = s.Split('|');
            if (parts.Length == 2)
            {
                TrueText = parts[0];
                FalseText = parts[1];
            }
        }

        return FalseText;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Inverts a boolean value.
/// </summary>
[ValueConversion(typeof(bool), typeof(bool))]
public sealed class InverseBooleanConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is bool b ? !b : true;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is bool b ? !b : true;
    }
}