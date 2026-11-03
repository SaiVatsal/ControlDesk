using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace ControlDesk.Setup.Converters;

public class StepToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush ActiveBrush = new(Color.FromRgb(0x00, 0x66, 0xCC));      // #0066CC (BrushPrimary)
    private static readonly SolidColorBrush InactiveBrush = new(Color.FromRgb(0xD0, 0xD0, 0xD0));    // #D0D0D0 (BrushBorder)

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int currentStepIndex && parameter is string targetStepStr && int.TryParse(targetStepStr, out int targetStepIndex))
        {
            return currentStepIndex >= targetStepIndex ? ActiveBrush : InactiveBrush;
        }

        return InactiveBrush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
