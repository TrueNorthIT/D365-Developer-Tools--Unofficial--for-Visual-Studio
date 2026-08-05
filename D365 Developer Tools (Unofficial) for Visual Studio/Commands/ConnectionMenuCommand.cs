using System;
using System.Threading;
using System.Threading.Tasks;
using D365_Developer_Tools__Unofficial__for_Visual_Studio.Shared;
using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Commands;
using Microsoft.VisualStudio.Shell;

namespace D365_Developer_Tools__Unofficial__for_Visual_Studio.Commands
{
    /// <summary>
    /// Tools menu entry that pops the same connect/disconnect/switch-account/recent-environments
    /// picker as the tool window header's "…" button (see ConnectionMenu). One always-visible command
    /// that branches internally on connection state, rather than several commands with dynamic
    /// visibility — simpler, and avoids relying on this SDK's dynamic-visibility APIs for now.
    /// </summary>
    [VisualStudioContribution]
    internal class ConnectionMenuCommand : Command
    {
        public ConnectionMenuCommand(VisualStudioExtensibility extensibility)
            : base(extensibility)
        {
        }

        public override CommandConfiguration CommandConfiguration => new("Connect / Manage Connection…");

        public override async Task ExecuteCommandAsync(IClientContext context, CancellationToken cancellationToken)
        {
            try
            {
                var package = await D365DeveloperToolsPackage.GetReadyInstanceAsync().ConfigureAwait(true);
                await ConnectionMenu.ShowAsync(package.ConnectionManager, package.UserPrompts).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                ActivityLog.LogError("D365DeveloperTools", "ConnectionMenuCommand FAILED: " + ex);
                DialogForegroundHelper.ShowMessage(
                    $"D365 connection command failed: {ex.Message}",
                    "D365 Developer Tools",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            }
        }
    }
}
