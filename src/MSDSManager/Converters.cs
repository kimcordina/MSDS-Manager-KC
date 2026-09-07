using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using MSDSManager.Models;

namespace MSDSManager;

public sealed class StatusToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not DocumentStatus status)
            return new SolidColorBrush(System.Windows.Media.Color.FromRgb(91, 102, 122));

        return status switch
        {
            DocumentStatus.Current => new SolidColorBrush(System.Windows.Media.Color.FromRgb(31, 122, 77)),
            DocumentStatus.ReviewRecommended => new SolidColorBrush(System.Windows.Media.Color.FromRgb(178, 106, 0)),
            DocumentStatus.Superseded => new SolidColorBrush(System.Windows.Media.Color.FromRgb(161, 40, 40)),
            DocumentStatus.Incomplete => new SolidColorBrush(System.Windows.Media.Color.FromRgb(91, 102, 122)),
            _ => new SolidColorBrush(System.Windows.Media.Color.FromRgb(91, 102, 122))
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is null ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;
}
