using System.Windows;
using System.Windows.Controls;
using D365_Developer_Tools__Unofficial__for_Visual_Studio.ToolWindows.ViewModels;
using Microsoft.VisualStudio.Shell;

namespace D365_Developer_Tools__Unofficial__for_Visual_Studio.ToolWindows
{
    public partial class PluginExplorerControl : UserControl
    {
        internal PluginExplorerControl(PluginExplorerViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }

        private PluginExplorerViewModel ViewModel => (PluginExplorerViewModel)DataContext;

        private void OnAddStepClick(object sender, RoutedEventArgs e)
        {
            if (TryGetRowDataContext<PluginTypeNodeViewModel>(sender, out var node))
            {
                ViewModel.AddStepAsync(node).FileAndForget("D365DeveloperTools/PluginExplorerAddStep");
            }
        }

        private void OnEditStepClick(object sender, RoutedEventArgs e)
        {
            if (TryGetRowDataContext<SdkMessageStepNodeViewModel>(sender, out var node))
            {
                ViewModel.EditStepAsync(node).FileAndForget("D365DeveloperTools/PluginExplorerEditStep");
            }
        }

        private void OnEnableStepClick(object sender, RoutedEventArgs e)
        {
            if (TryGetRowDataContext<SdkMessageStepNodeViewModel>(sender, out var node))
            {
                ViewModel.SetStepEnabledAsync(node, enabled: true).FileAndForget("D365DeveloperTools/PluginExplorerEnableStep");
            }
        }

        private void OnDisableStepClick(object sender, RoutedEventArgs e)
        {
            if (TryGetRowDataContext<SdkMessageStepNodeViewModel>(sender, out var node))
            {
                ViewModel.SetStepEnabledAsync(node, enabled: false).FileAndForget("D365DeveloperTools/PluginExplorerDisableStep");
            }
        }

        private static bool TryGetRowDataContext<T>(object sender, out T value) where T : class
        {
            value = null;
            if (!(sender is MenuItem menuItem)) { return false; }
            if (!(menuItem.Parent is ContextMenu contextMenu)) { return false; }
            if (!(contextMenu.PlacementTarget is FrameworkElement placementTarget)) { return false; }

            value = placementTarget.DataContext as T;
            return value != null;
        }
    }
}
