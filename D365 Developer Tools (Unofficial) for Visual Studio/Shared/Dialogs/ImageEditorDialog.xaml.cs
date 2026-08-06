using System.Windows;
using Microsoft.VisualStudio.PlatformUI;

namespace D365_Developer_Tools__Unofficial__for_Visual_Studio.Shared.Dialogs
{
    /// <summary>Registers or edits a pre-/post-image on a step, from Plugin Explorer's "Add Image..."/"Edit Image..." commands.</summary>
    internal partial class ImageEditorDialog : DialogWindow
    {
        public ImageEditorViewModel ViewModel { get; }

        public ImageEditorDialog(ImageEditorViewModel viewModel)
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
