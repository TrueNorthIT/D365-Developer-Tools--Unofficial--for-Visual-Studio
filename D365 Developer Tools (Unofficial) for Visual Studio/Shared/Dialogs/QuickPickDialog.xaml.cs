using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using Microsoft.VisualStudio.PlatformUI;

namespace D365_Developer_Tools__Unofficial__for_Visual_Studio.Shared.Dialogs
{
    /// <summary>Modal replacement for vscode.window.showQuickPick (single- and multi-select modes).</summary>
    internal partial class QuickPickDialog : DialogWindow
    {
        private readonly ObservableCollection<PickRowViewModel> _allRows = new ObservableCollection<PickRowViewModel>();
        private readonly ICollectionView _view;
        private readonly bool _isMultiSelect;

        private QuickPickDialog(string title, string placeholder, bool isMultiSelect, IEnumerable<PickRowViewModel> rows)
        {
            InitializeComponent();
            Title = title;
            SearchBox.ToolTip = placeholder;
            _isMultiSelect = isMultiSelect;

            foreach (var r in rows) { _allRows.Add(r); }

            _view = CollectionViewSource.GetDefaultView(_allRows);
            _view.Filter = FilterPredicate;
            ItemsList.ItemsSource = _view;
            ItemsList.ItemTemplate = (DataTemplate)FindResource(isMultiSelect ? "MultiTemplate" : "SingleTemplate");
            ItemsList.SelectionMode = SelectionMode.Single;

            Loaded += (_, __) =>
            {
                DialogForegroundHelper.BringToFront(this);
                SearchBox.Focus();
            };
        }

        private bool FilterPredicate(object obj)
        {
            var text = SearchBox.Text?.Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(text)) { return true; }
            return ((PickRowViewModel)obj).SearchText.Contains(text);
        }

        private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
        {
            if (_view is ListCollectionView listView)
            {
                var query = SearchBox.Text?.Trim() ?? string.Empty;
                listView.CustomSort = string.IsNullOrEmpty(query) ? null : new RelevanceComparer(query);
            }

            _view.Refresh();
        }

        /// <summary>Ranks rows by their best match across Label/Description/Detail, so a match in the primary label always outranks a match found only in the (less prominent) detail text.</summary>
        private sealed class RelevanceComparer : System.Collections.IComparer
        {
            private readonly string _query;
            public RelevanceComparer(string query) => _query = query;

            public int Compare(object x, object y) => RankOf((PickRowViewModel)x).CompareTo(RankOf((PickRowViewModel)y));

            private int RankOf(PickRowViewModel row) => new[]
            {
                SearchRelevance.Rank(row.Label, _query),
                SearchRelevance.Rank(row.Description, _query),
                SearchRelevance.Rank(row.Detail, _query),
            }.Min();
        }

        private void OnRowClicked(object sender, MouseButtonEventArgs e)
        {
            if (_isMultiSelect) { return; }
            if ((sender as FrameworkElement)?.DataContext is PickRowViewModel row)
            {
                ItemsList.SelectedItem = row;
            }
        }

        private void OnRowDoubleClicked(object sender, MouseButtonEventArgs e)
        {
            if (!_isMultiSelect && ItemsList.SelectedItem != null) { Confirm(); }
        }

        private void OnListKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && !_isMultiSelect && ItemsList.SelectedItem != null) { Confirm(); }
        }

        private void OnOk(object sender, RoutedEventArgs e) => Confirm();

        private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;

        private void Confirm() => DialogResult = true;

        private IReadOnlyList<PickRowViewModel> GetCheckedOrSelected()
        {
            if (_isMultiSelect) { return _allRows.Where(r => r.IsChecked).ToList(); }
            return ItemsList.SelectedItem is PickRowViewModel row
                ? new List<PickRowViewModel> { row }
                : new List<PickRowViewModel>();
        }

        // ── Public generic API ──────────────────────────────────────────────

        internal static PickItem<T> ShowSingle<T>(IntPtr ownerHwnd, string title, string placeholder, IReadOnlyList<PickItem<T>> items)
        {
            var rows = items.Select(ToRow).ToList();
            var dialog = new QuickPickDialog(title, placeholder, false, rows);
            if (ownerHwnd != IntPtr.Zero) { new WindowInteropHelper(dialog).Owner = ownerHwnd; }
            if (dialog.ShowDialog() != true) { return null; }

            var picked = dialog.GetCheckedOrSelected().FirstOrDefault();
            return picked == null ? null : items[rows.IndexOf(picked)];
        }

        internal static IReadOnlyList<PickItem<T>> ShowMulti<T>(IntPtr ownerHwnd, string title, string placeholder, IReadOnlyList<PickItem<T>> items)
        {
            var rows = items.Select(ToRow).ToList();
            var dialog = new QuickPickDialog(title, placeholder, true, rows);
            if (ownerHwnd != IntPtr.Zero) { new WindowInteropHelper(dialog).Owner = ownerHwnd; }
            if (dialog.ShowDialog() != true) { return null; }

            var checkedRows = dialog.GetCheckedOrSelected();
            return checkedRows.Select(r => items[rows.IndexOf(r)]).ToList();
        }

        private static PickRowViewModel ToRow<T>(PickItem<T> item) => new PickRowViewModel
        {
            Label = item.Label,
            Description = item.Description,
            Detail = item.Detail,
            IsChecked = item.Checked,
            Value = item.Value,
        };
    }
}
