using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace MiniPdm.Wpf.Converters;

// bool → Visibility. Показывает и скрывает элементы в XAML.
public sealed class BooleanToVisibilityConverter : IValueConverter
{
    // Параметр "invert" меняет поведение на обратное
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        // value может быть bool или bool? — берём true только если точно true
        bool flag = false;
        if (value is bool b)
        {
            flag = b;
        }

        // Если передан параметр "invert" — инвертируем
        string? text = parameter as string;
        if (text == "invert")
        {
            flag = !flag;
        }

        // true → видимый, false → скрытый
        if (flag)
        {
            return Visibility.Visible;
        }

        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        // Обратное преобразование: видимый → true, иначе → false
        if (value is Visibility.Visible)
        {
            return true;
        }

        return false;
    }
}