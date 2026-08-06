using System;
using System.Threading;
using System.Threading.Tasks;
using D365_Developer_Tools__Unofficial__for_Visual_Studio.Shared;
using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Commands;
using Microsoft.VisualStudio.Shell;

namespace D365_Developer_Tools__Unofficial__for_Visual_Studio.Commands
{
    /// <summary>Tools menu entry that opens the D365 Plugin Explorer tool window (classic WPF ToolWindowPane, unchanged).</summary>
    [VisualStudioContribution]
    internal class ShowPluginExplorerCommand : Command
    {
        public ShowPluginExplorerCommand(VisualStudioExtensibility extensibility)
            : base(extensibility)
        {
        }

        public override CommandConfiguration CommandConfiguration => new("Show Plugin Explorer");

        public override async Task ExecuteCommandAsync(IClientContext context, CancellationToken cancellationToken)
        {
            try
            {
                var package = await D365DeveloperToolsPackage.GetReadyInstanceAsync().ConfigureAwait(true);
                await package.ShowPluginExplorerToolWindowAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                ActivityLog.LogError("D365DeveloperTools", "ShowPluginExplorerCommand FAILED: " + ex);
                DialogForegroundHelper.ShowMessage(
                    $"Failed to open the D365 Plugin Explorer: {ex.Message}",
                    "D365 Developer Tools",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            }
        }
    }
}
