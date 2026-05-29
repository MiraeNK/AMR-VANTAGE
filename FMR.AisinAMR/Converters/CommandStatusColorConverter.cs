using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace FMR.AisinAMR.Converters
{
    public class CommandStatusColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string status)
            {
                if (status.Contains("ACK")) return new SolidColorBrush(Color.FromRgb(57, 211, 83));
                if (status.Contains("Timeout") || status.Contains("Failed") || status.Contains("Error")) return new SolidColorBrush(Color.FromRgb(255, 23, 68));
                if (status.Contains("Pending")) return new SolidColorBrush(Color.FromRgb(210, 153, 34)); // Yellow
            }
            return new SolidColorBrush(Colors.White);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }
}
