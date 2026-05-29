using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace FMR.AisinAMR.Converters
{
    public class NodeStatusColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isOk)
            {
                return isOk ? new SolidColorBrush(Color.FromRgb(57, 211, 83)) : new SolidColorBrush(Color.FromRgb(255, 23, 68)); // Green / Red
            }
            return new SolidColorBrush(Colors.Gray);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }
}
