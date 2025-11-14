// Converters/StringListToStringConverter.cs
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Data;

namespace LaserCuttingDetector.Converters // 请确保命名空间与你的项目匹配
{
    /// <summary>
    /// 在 List<string> 和以逗号分隔的 string 之间进行转换。
    /// </summary>
    public class StringListToStringConverter : IValueConverter
    {
        // 从 ViewModel (List<string>) 到 View (string)
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // 如果值是 List<string> 类型
            if (value is List<string> list)
            {
                // 使用 string.Join 将列表合并成一个用逗号分隔的字符串
                return string.Join(",", list);
            }
            // 如果不是，返回空字符串
            return string.Empty;
        }

        // 从 View (string) 回到 ViewModel (List<string>)
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // 如果值是 string 类型
            if (value is string str)
            {
                // 如果字符串为空或只包含空白，则返回一个空的列表
                if (string.IsNullOrWhiteSpace(str))
                {
                    return new List<string>();
                }
                // 使用 Split 分割字符串，并去除每个元素前后的空格，最后过滤掉空元素
                return str.Split(',')
                    .Select(s => s.Trim())
                    .Where(s => !string.IsNullOrEmpty(s))
                    .ToList();
            }
            // 如果不是，返回一个空的列表
            return new List<string>();
        }
    }
}