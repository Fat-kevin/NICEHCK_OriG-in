using System.Globalization;
using System.Windows.Data;

namespace YuandaoTws.Desktop.Converters;

/// <summary>
/// 把枚举值与固定字符串参数比较，用于让 RadioButton 的 IsChecked 反映当前枚举状态。
/// 用法：IsChecked="{Binding AncMode, Mode=OneWay, Converter={StaticResource EnumToBoolean}, ConverterParameter=Off}"。
/// </summary>
public sealed class EnumToBooleanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is not null && string.Equals(value.ToString(), parameter?.ToString(), StringComparison.Ordinal);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true && parameter is not null ? parameter : System.Windows.Data.Binding.DoNothing;
}
