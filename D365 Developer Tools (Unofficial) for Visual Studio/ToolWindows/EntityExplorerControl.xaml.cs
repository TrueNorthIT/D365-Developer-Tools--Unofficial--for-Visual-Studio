using System.Windows;
using System.Windows.Controls;
using D365_Developer_Tools__Unofficial__for_Visual_Studio.ToolWindows.ViewModels;
using Microsoft.VisualStudio.Shell;

namespace D365_Developer_Tools__Unofficial__for_Visual_Studio.ToolWindows
{
    public partial class EntityExplorerControl : UserControl
    {
        internal EntityExplorerControl(EntityExplorerViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }

        private EntityExplorerViewModel ViewModel => (EntityExplorerViewModel)DataContext;

        private void OnGenerateClassClick(object sender, RoutedEventArgs e)
        {
            if (TryGetRowDataContext<EntityNodeViewModel>(sender, out var node))
            {
                ViewModel.GenerateClassAsync(node).FileAndForget("D365DeveloperTools/GenerateClass");
            }
        }

        private void OnGenerateEnumClick(object sender, RoutedEventArgs e)
        {
            if (TryGetRowDataContext<AttributeNodeViewModel>(sender, out var node))
            {
                ViewModel.GenerateEnumAsync(node).FileAndForget("D365DeveloperTools/GenerateEnum");
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
