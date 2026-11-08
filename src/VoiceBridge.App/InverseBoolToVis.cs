using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace VoiceBridge.App;

/// <summary>
/// Shows an element while the bound flag is false. The word book needs both
/// halves: the plain text of a row and the boxes that edit it.
/// </summary>
public sealed class InverseBoolToVis : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is bool flag && flag ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is Visibility visible && visible == Visibility.Collapsed;
}
