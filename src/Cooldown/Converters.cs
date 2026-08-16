using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Cooldown;

internal sealed class BoolVis : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var flag = value is true;
        if (Invert) flag = !flag;
        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

internal sealed class AnyTrueVis : IMultiValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        var flag = false;
        foreach (var value in values)
        {
            if (value is true)
            {
                flag = true;
                break;
            }
        }
        if (Invert) flag = !flag;
        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

internal sealed class GrayCover : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not BitmapSource source) return value;
        try
        {
            var gray = new FormatConvertedBitmap();
            gray.BeginInit();
            gray.Source = source;
            gray.DestinationFormat = PixelFormats.Gray8;
            gray.DestinationPalette = BitmapPalettes.Gray256;
            gray.EndInit();
            gray.Freeze();
            return gray;
        }
        catch
        {
            return value;
        }
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
