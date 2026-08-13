using System;
using System.Globalization;
using System.Windows.Data;
using BetterGenshinImpact.Service.Interface;

namespace BetterGenshinImpact.View.Converters;

/// <summary>
/// Display-only translation for data-bound values: the UI shows the localized
/// text while SelectedItem/SelectedValue keep the internal (Chinese) value.
/// Values missing from the dictionary fall through unchanged.
/// </summary>
[ValueConversion(typeof(string), typeof(string))]
public sealed class LocalizedDisplayConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string s || string.IsNullOrEmpty(s))
        {
            return value;
        }

        var translator = App.GetService<ITranslationService>();
        return translator?.Translate(s, TranslationSourceInfo.From(MissingTextSource.UiDynamicBinding)) ?? s;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return Binding.DoNothing;
    }
}
