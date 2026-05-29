using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace FMR.AisinAMR.Converters
{
    public class PageNameToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string currentPage && parameter is string expectedPage)
            {
                return string.Equals(currentPage, expectedPage, StringComparison.OrdinalIgnoreCase)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}
