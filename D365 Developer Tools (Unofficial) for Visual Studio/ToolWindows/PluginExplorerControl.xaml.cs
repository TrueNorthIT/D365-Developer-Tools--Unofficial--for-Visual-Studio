using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using D365_Developer_Tools__Unofficial__for_Visual_Studio.Commands;
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

        private void OnAddImageClick(object sender, RoutedEventArgs e)
        {
            if (TryGetRowDataContext<SdkMessageStepNodeViewModel>(sender, out var node))
            {
                ViewModel.AddImageAsync(node).FileAndForget("D365DeveloperTools/PluginExplorerAddImage");
            }
        }

        private void OnEditImageClick(object sender, RoutedEventArgs e)
        {
            if (TryGetRowDataContext<SdkMessageStepImageNodeViewModel>(sender, out var node))
            {
                ViewModel.EditImageAsync(node).FileAndForget("D365DeveloperTools/PluginExplorerEditImage");
            }
        }

        private void OnViewStepTraceLogsClick(object sender, RoutedEventArgs e)
        {
            if (TryGetRowDataContext<SdkMessageStepNodeViewModel>(sender, out var node))
            {
                // node.Owner.Name is the owning PluginTypeNodeViewModel's fully-qualified type name
                // (PluginTypeDefinition.TypeName) — see ViewStepTraceLogsCommand for why plugintracelog
                // needs that instead of the step's own ID.
                ViewStepTraceLogsCommand.ExecuteAsync(node.Step, node.Owner?.Name, node.PluginTypeFriendlyName).FileAndForget("D365DeveloperTools/ViewStepTraceLogs");
            }
        }

        private void OnStartProfilingClick(object sender, RoutedEventArgs e)
        {
            if (TryGetRowDataContext<SdkMessageStepNodeViewModel>(sender, out var node))
            {
                StartProfilingAndRefreshAsync(node).FileAndForget("D365DeveloperTools/StartProfiling");
            }
        }

        private async Task StartProfilingAndRefreshAsync(SdkMessageStepNodeViewModel node)
        {
            // See OnViewStepTraceLogsClick above for why node.Owner?.Name (the plugin type's bare
            // typename) is what's needed here — same key space PluginTraceLogFilter.TypeName uses
            // (matched against plugintracelog.typename via startswith, since that column is
            // assembly-qualified — see PluginTraceLogModels.cs).
            await StartProfilingCommand.ExecuteAsync(node.Step, node.Owner?.Name, node.PluginTypeFriendlyName).ConfigureAwait(true);

            // Re-checks ProfilingSessionStore rather than assuming success — the command returns early
            // (without arming anything) in several cases, e.g. the Profiler solution isn't installed.
            node.RefreshProfilingState();
        }

        private void OnStopProfilingClick(object sender, RoutedEventArgs e)
        {
            if (TryGetRowDataContext<SdkMessageStepNodeViewModel>(sender, out var node))
            {
                StopProfilingAndRefreshAsync(node).FileAndForget("D365DeveloperTools/StopProfiling");
            }
        }

        private async Task StopProfilingAndRefreshAsync(SdkMessageStepNodeViewModel node)
        {
            await StopProfilingCommand.ExecuteAsync(node.Step.StepId).ConfigureAwait(true);
            node.RefreshProfilingState();
        }

        /// <summary>
        /// Toggles the Start/Stop Profiling menu items so only the one matching this step's actual state
        /// is shown — a ContextMenu is a separate visual-tree root, so its items can't just bind straight
        /// to the row's own IsProfiling property (see TryGetRowDataContext's own doc comment for the same
        /// PlacementTarget limitation elsewhere in this file). Also re-checks (not just trusts the node's
        /// possibly-stale cached value) since profiling could have been started/stopped from a different
        /// VS session since this tree was last built.
        /// </summary>
        private void OnStepContextMenuOpened(object sender, RoutedEventArgs e)
        {
            if (!(sender is ContextMenu contextMenu)) { return; }
            if (!(contextMenu.PlacementTarget is FrameworkElement placementTarget) || !(placementTarget.DataContext is SdkMessageStepNodeViewModel node)) { return; }

            node.RefreshProfilingState();

            // Matched by Header text, not x:Name — x:Name on an element declared inside a resource (this
            // ContextMenu lives in UserControl.Resources, not the main content tree) doesn't register a
            // code-behind field the way a normally-placed named element would (confirmed: nameof() on
            // these failed to compile at all when tried).
            foreach (var item in contextMenu.Items.OfType<MenuItem>())
            {
                if (Equals(item.Header, "Start Profiling...")) { item.Visibility = node.IsProfiling ? Visibility.Collapsed : Visibility.Visible; }
                if (Equals(item.Header, "Stop Profiling...")) { item.Visibility = node.IsProfiling ? Visibility.Visible : Visibility.Collapsed; }
            }
        }

        private void OnEditAssemblyDescriptionClick(object sender, RoutedEventArgs e)
        {
            if (TryGetRowDataContext<PluginAssemblyNodeViewModel>(sender, out var node))
            {
                ViewModel.EditAssemblyDescriptionAsync(node).FileAndForget("D365DeveloperTools/PluginExplorerEditAssemblyDescription");
            }
        }

        private void OnUnregisterAssemblyClick(object sender, RoutedEventArgs e)
        {
            if (TryGetRowDataContext<PluginAssemblyNodeViewModel>(sender, out var node))
            {
                ViewModel.DeleteAssemblyAsync(node).FileAndForget("D365DeveloperTools/PluginExplorerUnregisterAssembly");
            }
        }

        private void OnUnregisterTypeClick(object sender, RoutedEventArgs e)
        {
            if (TryGetRowDataContext<PluginTypeNodeViewModel>(sender, out var node))
            {
                ViewModel.DeleteTypeAsync(node).FileAndForget("D365DeveloperTools/PluginExplorerUnregisterType");
            }
        }

        private void OnUnregisterStepClick(object sender, RoutedEventArgs e)
        {
            if (TryGetRowDataContext<SdkMessageStepNodeViewModel>(sender, out var node))
            {
                ViewModel.DeleteStepAsync(node).FileAndForget("D365DeveloperTools/PluginExplorerUnregisterStep");
            }
        }

        private void OnUnregisterImageClick(object sender, RoutedEventArgs e)
        {
            if (TryGetRowDataContext<SdkMessageStepImageNodeViewModel>(sender, out var node))
            {
                ViewModel.DeleteImageAsync(node).FileAndForget("D365DeveloperTools/PluginExplorerUnregisterImage");
            }
        }

        private void OnEditCustomApiClick(object sender, RoutedEventArgs e)
        {
            if (TryGetRowDataContext<CustomApiNodeViewModel>(sender, out var node))
            {
                ViewModel.EditCustomApiAsync(node).FileAndForget("D365DeveloperTools/PluginExplorerEditCustomApi");
            }
        }

        private void OnAddCustomApiRequestParameterClick(object sender, RoutedEventArgs e)
        {
            if (TryGetRowDataContext<CustomApiNodeViewModel>(sender, out var node))
            {
                ViewModel.AddCustomApiParameterAsync(node, isRequestParameter: true).FileAndForget("D365DeveloperTools/PluginExplorerAddCustomApiRequestParameter");
            }
        }

        private void OnAddCustomApiResponsePropertyClick(object sender, RoutedEventArgs e)
        {
            if (TryGetRowDataContext<CustomApiNodeViewModel>(sender, out var node))
            {
                ViewModel.AddCustomApiParameterAsync(node, isRequestParameter: false).FileAndForget("D365DeveloperTools/PluginExplorerAddCustomApiResponseProperty");
            }
        }

        private void OnUnregisterCustomApiClick(object sender, RoutedEventArgs e)
        {
            if (TryGetRowDataContext<CustomApiNodeViewModel>(sender, out var node))
            {
                ViewModel.DeleteCustomApiAsync(node).FileAndForget("D365DeveloperTools/PluginExplorerUnregisterCustomApi");
            }
        }

        private void OnEditCustomApiParameterClick(object sender, RoutedEventArgs e)
        {
            if (TryGetRowDataContext<CustomApiParameterNodeViewModel>(sender, out var node))
            {
                ViewModel.EditCustomApiParameterAsync(node).FileAndForget("D365DeveloperTools/PluginExplorerEditCustomApiParameter");
            }
        }

        private void OnUnregisterCustomApiParameterClick(object sender, RoutedEventArgs e)
        {
            if (TryGetRowDataContext<CustomApiParameterNodeViewModel>(sender, out var node))
            {
                ViewModel.DeleteCustomApiParameterAsync(node).FileAndForget("D365DeveloperTools/PluginExplorerUnregisterCustomApiParameter");
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
