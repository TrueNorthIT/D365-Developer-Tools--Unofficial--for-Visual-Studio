using System.Windows;
using Microsoft.VisualStudio.PlatformUI;

namespace D365_Developer_Tools__Unofficial__for_Visual_Studio.Shared.Dialogs
{
    /// <summary>Registers or edits a Custom API, from Plugin Explorer's "Add Custom API..."/"Edit Custom API..." commands.</summary>
    internal partial class CustomApiEditorDialog : DialogWindow
    {
        public CustomApiEditorViewModel ViewModel { get; }

        public CustomApiEditorDialog(CustomApiEditorViewModel viewModel)
        {
            InitializeComponent();
            ViewModel = viewModel;
            DataContext = viewModel;
            Loaded += (_, __) => DialogForegroundHelper.BringToFront(this);
        }

        private void OnOk(object sender, RoutedEventArgs e)
        {
            var error = ViewModel.Validate();
            if (error != null)
            {
                DialogForegroundHelper.ShowMessage(error, "D365 Developer Tools", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            DialogResult = true;
        }
    }
}
