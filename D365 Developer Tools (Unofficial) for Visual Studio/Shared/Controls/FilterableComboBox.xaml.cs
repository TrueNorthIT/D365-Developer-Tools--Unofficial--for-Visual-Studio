using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace D365_Developer_Tools__Unofficial__for_Visual_Studio.Shared.Controls
{
    /// <summary>
    /// A typeable combobox: a text box with a dropdown of suggestions that narrows as you type.
    /// Deliberately not a stock WPF ComboBox with a filtered ItemsSource — WPF clears SelectedItem
    /// as soon as the current selection is filtered out, which fights a two-way SelectedItem binding
    /// mid-keystroke. Filtering here is entirely local to the control; the bound SelectedItem only
    /// changes when the user actually commits a choice (click, Enter, or an exact text match).
    /// </summary>
    internal partial class FilterableComboBox : UserControl
    {
        public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
            nameof(ItemsSource), typeof(IEnumerable), typeof(FilterableComboBox),
            new PropertyMetadata(null, OnItemsSourceChanged));

        public static readonly DependencyProperty DisplayMemberPathProperty = DependencyProperty.Register(
            nameof(DisplayMemberPath), typeof(string), typeof(FilterableComboBox), new PropertyMetadata(string.Empty));

        public static readonly DependencyProperty SelectedItemProperty = DependencyProperty.Register(
            nameof(SelectedItem), typeof(object), typeof(FilterableComboBox),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnSelectedItemChanged));

        public IEnumerable ItemsSource
        {
            get => (IEnumerable)GetValue(ItemsSourceProperty);
            set => SetValue(ItemsSourceProperty, value);
        }

        public string DisplayMemberPath
        {
            get => (string)GetValue(DisplayMemberPathProperty);
            set => SetValue(DisplayMemberPathProperty, value);
        }

        public object SelectedItem
        {
            get => GetValue(SelectedItemProperty);
            set => SetValue(SelectedItemProperty, value);
        }

        private List<object> _allItems = new List<object>();
        private bool _suppressTextChanged;

        public FilterableComboBox()
        {
            InitializeComponent();
        }

        private static void OnItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var control = (FilterableComboBox)d;
            if (e.OldValue is INotifyCollectionChanged oldIncc) { oldIncc.CollectionChanged -= control.OnSourceCollectionChanged; }
            if (e.NewValue is INotifyCollectionChanged newIncc) { newIncc.CollectionChanged += control.OnSourceCollectionChanged; }
            control.RefreshAllItems();
        }

        private void OnSourceCollectionChanged(object sender, NotifyCollectionChangedEventArgs e) => RefreshAllItems();

        private void RefreshAllItems()
        {
            _allItems = ItemsSource?.Cast<object>().ToList() ?? new List<object>();
            if (PART_Popup.IsOpen) { ApplyFilter(PART_TextBox.Text); }
        }

        private static void OnSelectedItemChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
            ((FilterableComboBox)d).SyncTextToSelection();

        private void SyncTextToSelection()
        {
            _suppressTextChanged = true;
            try
            {
                PART_TextBox.Text = GetDisplayText(SelectedItem);
                PART_TextBox.CaretIndex = PART_TextBox.Text.Length;
            }
            finally
            {
                _suppressTextChanged = false;
            }
        }

        private string GetDisplayText(object item)
        {
            if (item == null) { return string.Empty; }
            if (string.IsNullOrEmpty(DisplayMemberPath)) { return item.ToString(); }

            var property = item.GetType().GetProperty(DisplayMemberPath);
            return property?.GetValue(item)?.ToString() ?? item.ToString();
        }

        private void ApplyFilter(string text)
        {
            PART_ListBox.DisplayMemberPath = DisplayMemberPath;
            PART_ListBox.ItemsSource = string.IsNullOrEmpty(text)
                ? _allItems
                : _allItems.Where(i => GetDisplayText(i).IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            PART_ListBox.SelectedIndex = PART_ListBox.Items.Count > 0 ? 0 : -1;
        }

        private void OnTextBoxTextChanged(object sender, TextChangedEventArgs e)
        {
            if (_suppressTextChanged) { return; }

            ApplyFilter(PART_TextBox.Text);
            PART_Popup.IsOpen = true;
        }

        private void OnToggleButtonClick(object sender, MouseButtonEventArgs e)
        {
            if (PART_Popup.IsOpen)
            {
                PART_Popup.IsOpen = false;
                return;
            }

            ApplyFilter(string.Empty);
            PART_Popup.IsOpen = true;
            PART_TextBox.Focus();
        }

        private void OnTextBoxPreviewKeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Down:
                    if (!PART_Popup.IsOpen) { ApplyFilter(PART_TextBox.Text); PART_Popup.IsOpen = true; }
                    else { MoveListSelection(1); }
                    e.Handled = true;
                    break;

                case Key.Up:
                    if (PART_Popup.IsOpen) { MoveListSelection(-1); e.Handled = true; }
                    break;

                case Key.Enter:
                    if (PART_Popup.IsOpen)
                    {
                        if (PART_ListBox.SelectedItem != null) { SelectedItem = PART_ListBox.SelectedItem; }
                        PART_Popup.IsOpen = false;
                        e.Handled = true;
                    }
                    break;

                case Key.Escape:
                    if (PART_Popup.IsOpen) { PART_Popup.IsOpen = false; e.Handled = true; }
                    break;
            }
        }

        private void MoveListSelection(int delta)
        {
            if (PART_ListBox.Items.Count == 0) { return; }

            var next = Math.Max(0, Math.Min(PART_ListBox.Items.Count - 1, PART_ListBox.SelectedIndex + delta));
            PART_ListBox.SelectedIndex = next;
            PART_ListBox.ScrollIntoView(PART_ListBox.SelectedItem);
        }

        private void OnListBoxPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if ((e.OriginalSource as FrameworkElement)?.DataContext is object item && _allItems.Contains(item))
            {
                SelectedItem = item;
                PART_Popup.IsOpen = false;
                PART_TextBox.Focus();
            }
        }

        private void OnPopupClosed(object sender, EventArgs e)
        {
            if (!string.Equals(PART_TextBox.Text, GetDisplayText(SelectedItem), StringComparison.Ordinal))
            {
                SyncTextToSelection();
            }
        }
    }
}
