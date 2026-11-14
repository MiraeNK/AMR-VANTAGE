using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Vantage.Studio.ViewModels;

/// <summary>Small converters used from XAML via x:Static.</summary>
public static class Percent
{
    public static IValueConverter ToRatio { get; } = new Lambda(v => v is double d ? d / 100.0 : 0.0);
    public static IValueConverter NonZeroVis { get; } = new Lambda(v => v is int i && i > 0 ? Visibility.Visible : Visibility.Collapsed);
    public static IMultiValueConverter Same { get; } = new SameConverter();

    private sealed class Lambda(Func<object?, object> f) : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => f(value);
        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
    }

    private sealed class SameConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
            values.Length == 2 && ReferenceEquals(values[0], values[1]);
        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }
}
