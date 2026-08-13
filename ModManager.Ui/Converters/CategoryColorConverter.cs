using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace ModManager.Ui.Converters;

/// <summary>
/// Maps a category name to one of a fixed set of chip colors. The mapping is a stable hash so the
/// same category always renders the same color across rows and app restarts, without needing to
/// track color assignments anywhere.
/// </summary>
public sealed class CategoryColorConverter : IValueConverter
{
    public static readonly CategoryColorConverter Instance = new();

    // Evenly spaced around the hue wheel (~30° apart) so adjacent entries never read as "the same
    // color", and each is dark/saturated enough to stay legible under white chip text.
    private static readonly IReadOnlyList<Color> Palette =
    [
        Color.Parse("#DC2626"), // red
        Color.Parse("#EA580C"), // orange
        Color.Parse("#A16207"), // dark gold
        Color.Parse("#65A30D"), // lime
        Color.Parse("#16A34A"), // green
        Color.Parse("#059669"), // emerald
        Color.Parse("#0D9488"), // teal
        Color.Parse("#0891B2"), // cyan
        Color.Parse("#2563EB"), // blue
        Color.Parse("#7C3AED"), // violet
        Color.Parse("#C026D3"), // fuchsia
        Color.Parse("#DB2777"), // pink
    ];

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string category || string.IsNullOrWhiteSpace(category))
        {
            return Brushes.Transparent;
        }

        int index = (int)((uint)StableHash(category) % (uint)Palette.Count);
        return new SolidColorBrush(Palette[index]);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();

    private static int StableHash(string text)
    {
        unchecked
        {
            int hash = 17;
            foreach (char character in text)
            {
                hash = (hash * 31) + character;
            }

            return hash;
        }
    }
}
