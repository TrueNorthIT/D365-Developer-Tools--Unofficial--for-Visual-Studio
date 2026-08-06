using System.Windows;
using Microsoft.VisualStudio.PlatformUI;

namespace D365_Developer_Tools__Unofficial__for_Visual_Studio.Shared.Dialogs
{
    /// <summary>Registers a new SdkMessageProcessingStep for a plugin type found via "D365: Add Step...".</summary>
    internal partial class StepEditorDialog : DialogWindow
    {
        public StepEditorViewModel ViewModel { get; }

        public StepEditorDialog(StepEditorViewModel viewModel)
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
