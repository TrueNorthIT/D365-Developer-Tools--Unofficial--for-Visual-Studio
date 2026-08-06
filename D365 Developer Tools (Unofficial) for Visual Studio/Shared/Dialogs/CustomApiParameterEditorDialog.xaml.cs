using System.Windows;
using Microsoft.VisualStudio.PlatformUI;

namespace D365_Developer_Tools__Unofficial__for_Visual_Studio.Shared.Dialogs
{
    /// <summary>Registers or edits a Custom API request parameter or response property.</summary>
    internal partial class CustomApiParameterEditorDialog : DialogWindow
    {
        public CustomApiParameterEditorViewModel ViewModel { get; }

        public CustomApiParameterEditorDialog(CustomApiParameterEditorViewModel viewModel)
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
