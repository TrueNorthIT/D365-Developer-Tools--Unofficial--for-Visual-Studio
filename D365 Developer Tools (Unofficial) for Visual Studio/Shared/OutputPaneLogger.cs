using System;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace D365_Developer_Tools__Unofficial__for_Visual_Studio.Shared
{
    /// <summary>
    /// Writes diagnostic/trace messages to a dedicated "D365 Developer Tools" pane in VS's Output
    /// window (View > Output, then "Show output from:") — visible in any running session, unlike
    /// ActivityLog (needs devenv's /log switch and a file to open) or Debug.WriteLine (only reaches
    /// anywhere visible when a debugger is attached).
    /// </summary>
    internal static class OutputPaneLogger
    {
        private static readonly Guid PaneGuid = new Guid("9d1e2f3a-4b5c-4d6e-8f7a-1b2c3d4e5f6a");
        private static IVsOutputWindowPane _pane;

        public static async Task WriteLineAsync(string message)
        {
            var pane = _pane ?? await GetOrCreatePaneAsync().ConfigureAwait(true);

            // OutputStringThreadSafe is, per its name, safe to call off the main thread — only the
            // one-time pane creation below needs to happen there.
            pane?.OutputStringThreadSafe($"{DateTime.Now:HH:mm:ss.fff}  {message}{Environment.NewLine}");
        }

        private static async Task<IVsOutputWindowPane> GetOrCreatePaneAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            if (_pane != null) { return _pane; }

            // AsyncServiceProvider.GetServiceAsync returns the COM-flavored IVsTask (not System.Threading.Tasks.Task) — see D365DeveloperToolsPackage.GetReadyInstanceAsync's comment — so no ConfigureAwait here.
            if (!(await AsyncServiceProvider.GlobalProvider.GetServiceAsync(typeof(SVsOutputWindow)) is IVsOutputWindow outputWindow))
            {
                return null;
            }

            var paneGuid = PaneGuid;
            if (outputWindow.GetPane(ref paneGuid, out var pane) != Microsoft.VisualStudio.VSConstants.S_OK || pane == null)
            {
                outputWindow.CreatePane(ref paneGuid, "D365 Developer Tools", fInitVisible: 1, fClearWithSolution: 0);
                outputWindow.GetPane(ref paneGuid, out pane);
            }

            _pane = pane;
            return _pane;
        }
    }
}
