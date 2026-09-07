using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;

namespace D365_Developer_Tools__Unofficial__for_Visual_Studio.ToolWindows
{
    /// <summary>
    /// Hosts PluginDebuggingControl — views/filters plugintracelog records.
    ///
    /// Unlike PluginExplorerToolWindow, this doesn't build its own ViewModel: it consumes the
    /// package's singleton PluginDebuggingViewModel, so ViewStepTraceLogsCommand can push a step
    /// filter into it whether or not the window is already open (see PluginDebuggingViewModel.LoadForStepAsync).
    /// </summary>
    [Guid(PackageGuids.PluginDebuggingToolWindowString)]
    public sealed class PluginDebuggingToolWindow : ToolWindowPane
    {
        public PluginDebuggingToolWindow() : base(null)
        {
            Caption = "D365 Plugin Debugging";
        }

        protected override void Initialize()
        {
            base.Initialize();

            var package = (D365DeveloperToolsPackage)Package;
            Content = new PluginDebuggingControl(package.PluginDebuggingViewModel);
        }

        protected override void Dispose(bool disposing)
        {
            // The auto-refresh timer is the first disposable resource any tool window in this
            // extension has owned — stop it here so it doesn't keep polling after the window closes.
            if (disposing && Content is PluginDebuggingControl control)
            {
                control.ViewModel.StopAutoRefresh();
            }

            base.Dispose(disposing);
        }
    }
}
