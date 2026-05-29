using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace FMR.AisinAMR.Converters
{
    public class EqualityToVisibilityConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values == null || values.Length < 2)
                return Visibility.Collapsed;

            var val1 = values[0]?.ToString();
            var val2 = values[1]?.ToString();

            return string.Equals(val1, val2) ? Visibility.Visible : Visibility.Collapsed;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
