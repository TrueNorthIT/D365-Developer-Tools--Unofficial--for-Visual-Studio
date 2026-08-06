using System;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace D365_Developer_Tools__Unofficial__for_Visual_Studio.Shared
{
    /// <summary>Wraps IVsThreadedWaitDialog2 — the analog of vscode.window.withProgress.</summary>
    internal static class ProgressDialog
    {
        public static async Task<T> RunAsync<T>(string title, string message, Func<Task<T>> work)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            IVsThreadedWaitDialog2 dialog = null;
            if (ServiceProvider.GlobalProvider.GetService(typeof(SVsThreadedWaitDialogFactory)) is IVsThreadedWaitDialogFactory factory)
            {
                factory.CreateInstance(out dialog);
                dialog?.StartWaitDialog(title, message, null, null, null, 0, false, true);
            }

            try
            {
                return await work().ConfigureAwait(true);
            }
            finally
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                dialog?.EndWaitDialog(out _);
            }
        }

        public static Task RunAsync(string title, string message, Func<Task> work) =>
            RunAsync<object>(title, message, async () => { await work().ConfigureAwait(true); return null; });
    }
}
