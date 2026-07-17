using System.Globalization;
using System.Windows;
using System.Windows.Data;
using HardwareModularWorkflow.Lang.Strings;

namespace HardwareModularWorkflow.Wpf.Converters;

public class ResourceFormatConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (parameter is not string resourceKey || value is null)
            return DependencyProperty.UnsetValue;

        var format = Resources.ResourceManager.GetString(resourceKey, Resources.Culture);
        if (string.IsNullOrEmpty(format))
            return DependencyProperty.UnsetValue;

        return string.Format(Resources.Culture, format, value);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
