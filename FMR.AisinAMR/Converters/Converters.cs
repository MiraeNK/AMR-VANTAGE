using System;
using System.Globalization;
using System.Windows.Data;

namespace FMR.AisinAMR.Converters
{
    /// <summary>
    /// Konversi nilai persen (0-100) ke lebar pixel.
    /// ConverterParameter = lebar maksimum container (misal 180).
    /// </summary>
    public class PercentToWidthConverter : IValueConverter
    {
        public object Convert(object value, Type targetType,
                              object parameter, CultureInfo culture)
        {
            if (value is double percent &&
                parameter is string paramStr &&
                double.TryParse(paramStr, out double maxWidth))
            {
                return Math.Max(0, Math.Min(maxWidth, maxWidth * (percent / 100.0)));
            }
            return 0.0;
        }

        public object ConvertBack(object value, Type targetType,
                                  object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>
    /// Bool → SolidColorBrush
    /// True  = GreenBrush
    /// False = RedBrush
    /// </summary>
    public class BoolToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType,
                              object parameter, CultureInfo culture)
        {
            if (value is bool b)
                return b
                    ? new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(0, 230, 118))  // #00E676
                    : new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(255, 23, 68));  // #FF1744
            return System.Windows.Media.Brushes.Gray;
        }

        public object ConvertBack(object value, Type targetType,
                                  object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>
    /// Map coordinate converter for Canvas visualization.
    /// Assumes map range -10..10 in X and Y.
    /// </summary>
    public class MapCoordinateToCanvasConverter : IValueConverter
    {
        public object Convert(object value, Type targetType,
                              object parameter, CultureInfo culture)
        {
            if (value is double coordinate && parameter is string axis)
            {
                const double mapMin = -10.0;
                const double mapMax = 10.0;
                const double canvasWidth = 720.0;
                const double canvasHeight = 420.0;

                if (axis.Equals("Width", StringComparison.OrdinalIgnoreCase))
                {
                    var normalized = (coordinate - mapMin) / (mapMax - mapMin);
                    return Math.Max(0, Math.Min(canvasWidth - 6, normalized * canvasWidth));
                }
                if (axis.Equals("Height", StringComparison.OrdinalIgnoreCase))
                {
                    var normalized = (coordinate - mapMin) / (mapMax - mapMin);
                    return Math.Max(0, Math.Min(canvasHeight - 6, (1.0 - normalized) * canvasHeight));
                }
            }
            return 0.0;
        }

        public object ConvertBack(object value, Type targetType,
                                  object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    public class InvertBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b) return !b;
            return false;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
    public class NullToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value == null ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}
