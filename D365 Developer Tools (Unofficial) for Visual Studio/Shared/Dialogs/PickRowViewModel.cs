using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace D365_Developer_Tools__Unofficial__for_Visual_Studio.Shared.Dialogs
{
    /// <summary>Non-generic row backing QuickPickDialog's list — wraps a boxed PickItem&lt;T&gt;.Value.</summary>
    internal sealed class PickRowViewModel : INotifyPropertyChanged
    {
        public string Label { get; set; }
        public string Description { get; set; }
        public string Detail { get; set; }
        public object Value { get; set; }

        private bool _isChecked;
        public bool IsChecked
        {
            get => _isChecked;
            set { _isChecked = value; OnPropertyChanged(); }
        }

        public string SearchText => $"{Label} {Description} {Detail}".ToLowerInvariant();

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
