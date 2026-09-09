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

    /// <summary>Shows an element only once a lazily-loaded value (e.g. an entity's real icon) is available.</summary>
    internal sealed class NullToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            value != null ? Visibility.Visible : Visibility.Collapsed;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }

    /// <summary>
    /// The inverse of Dialogs\EmptyToCollapsedConverter — shows an element only while its paired value is
    /// empty. Used for a ghost-text placeholder overlaid on a filter TextBox (e.g. Plugin Debugging's
    /// trace log filters): a real, hit-test-invisible TextBlock bound to the same property this converts,
    /// rather than a VisualBrush-based watermark style, since a VisualBrush.Visual subtree is painted,
    /// not part of the real visual tree — a RelativeSource binding inside one can't see back out to the
    /// TextBox to read a per-instance placeholder (e.g. a Tag), which is why this is a plain overlay
    /// instead. Saves the vertical space a separate label row would need.
    /// </summary>
    internal sealed class EmptyToVisibleConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            string.IsNullOrEmpty(value as string) ? Visibility.Visible : Visibility.Collapsed;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }

    /// <summary>Flips a bool — used to enable the "Enable"/"Disable" step commands opposite each other.</summary>
    internal sealed class InverseBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            !(value is bool b && b);

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
