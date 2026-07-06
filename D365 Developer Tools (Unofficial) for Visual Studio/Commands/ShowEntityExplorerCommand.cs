using System;
using System.Threading;
using System.Threading.Tasks;
using D365_Developer_Tools__Unofficial__for_Visual_Studio.Shared;
using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Commands;
using Microsoft.VisualStudio.Shell;

namespace D365_Developer_Tools__Unofficial__for_Visual_Studio.Commands
{
    /// <summary>Tools menu entry that opens the D365 Entity Explorer tool window (classic WPF ToolWindowPane, unchanged).</summary>
    [VisualStudioContribution]
    internal class ShowEntityExplorerCommand : Command
    {
        public ShowEntityExplorerCommand(VisualStudioExtensibility extensibility)
            : base(extensibility)
        {
        }

        public override CommandConfiguration CommandConfiguration => new("D365: Show Entity Explorer")
        {
            Placements = new[] { CommandPlacement.KnownPlacements.ToolsMenu },
        };

        public override async Task ExecuteCommandAsync(IClientContext context, CancellationToken cancellationToken)
        {
            try
            {
                var package = await D365DeveloperToolsPackage.GetReadyInstanceAsync().ConfigureAwait(true);
                await package.ShowEntityExplorerToolWindowAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                ActivityLog.LogError("D365DeveloperTools", "ShowEntityExplorerCommand FAILED: " + ex);
                DialogForegroundHelper.ShowMessage(
                    $"Failed to open the D365 Entity Explorer: {ex.Message}",
                    "D365 Developer Tools",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            }
        }
    }
}
