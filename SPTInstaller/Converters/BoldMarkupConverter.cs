using System.Globalization;
using Avalonia.Controls.Documents;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace SPTInstaller.Converters;

/// <summary>
/// Text wrapped in ** is bold, the rest is plain
/// </summary>
public class BoldMarkupConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var inlines = new InlineCollection();
        var parts = (value as string ?? "").Split("**");

        for (var i = 0; i < parts.Length; i++)
        {
            if (parts[i].Length > 0)
            {
                inlines.Add(new Run(parts[i]) { FontWeight = i % 2 == 1 ? FontWeight.Bold : FontWeight.Normal });
            }
        }

        return inlines;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
