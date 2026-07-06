using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using Microsoft.VisualStudio.Imaging;

namespace D365_Developer_Tools__Unofficial__for_Visual_Studio.Shared.Converters
{
    internal sealed class BoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            (value is bool b && b) ? Visibility.Visible : Visibility.Collapsed;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }

    /// <summary>Dims a row (e.g. a disabled plugin step) without hiding it.</summary>
    internal sealed class BoolToOpacityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            (value is bool b && b) ? 1.0 : 0.5;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }

    /// <summary>Picks between the "plugin package" and plain "assembly" tree icons.</summary>
    internal sealed class BoolToAssemblyIconConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            (value is bool b && b) ? KnownMonikers.Package : KnownMonikers.Assembly;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
