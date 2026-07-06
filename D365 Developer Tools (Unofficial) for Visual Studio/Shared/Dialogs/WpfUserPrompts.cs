using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace D365_Developer_Tools__Unofficial__for_Visual_Studio.Shared.Dialogs
{
    /// <summary>Default IUserPrompts implementation backed by the WPF dialogs in this folder.</summary>
    internal sealed class WpfUserPrompts : IUserPrompts
    {
        public async Task<string> PromptTextAsync(string title, string placeholder, bool isPassword = false, string defaultValue = null)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var hwnd = await VsShellHelper.GetMainWindowHandleAsync().ConfigureAwait(true);
            return TextInputDialog.Show(hwnd, title, placeholder, placeholder, isPassword, defaultValue);
        }

        public async Task<PickItem<T>> PickOneAsync<T>(string title, string placeholder, IReadOnlyList<PickItem<T>> items)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var hwnd = await VsShellHelper.GetMainWindowHandleAsync().ConfigureAwait(true);
            return QuickPickDialog.ShowSingle(hwnd, title, placeholder, items);
        }

        public async Task<IReadOnlyList<PickItem<T>>> PickManyAsync<T>(string title, string placeholder, IReadOnlyList<PickItem<T>> items)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var hwnd = await VsShellHelper.GetMainWindowHandleAsync().ConfigureAwait(true);
            return QuickPickDialog.ShowMulti(hwnd, title, placeholder, items);
        }

        public void ShowInfo(string message)
        {
            ThreadHelper.JoinableTaskFactory.Run(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                // AsyncServiceProvider, not the legacy ServiceProvider.GlobalProvider — the latter can
                // throw "Cannot access a closed registry key" when invoked from a
                // VisualStudio.Extensibility command's execution context. GetServiceAsync returns the
                // COM-flavored IVsTask, not System.Threading.Tasks.Task, so no ConfigureAwait here.
                if (await AsyncServiceProvider.GlobalProvider.GetServiceAsync(typeof(SVsStatusbar)) is IVsStatusbar statusBar)
                {
                    statusBar.SetText(message);
                }
            });
        }

        public void ShowError(string message)
        {
            // Plain WPF MessageBox, not VsShellUtilities.ShowMessageBox — that requires a VS service
            // provider (ServiceProvider.GlobalProvider), which can throw "Cannot access a closed
            // registry key" when invoked from a VisualStudio.Extensibility command's execution context.
            // Routed through DialogForegroundHelper so it doesn't open behind/minimized (see its docs).
            ThreadHelper.JoinableTaskFactory.Run(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                DialogForegroundHelper.ShowMessage(message, "D365 Developer Tools", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            });
        }

        public Task<T> RunWithProgressAsync<T>(string title, Func<Task<T>> work) =>
            ProgressDialog.RunAsync(title, title, work);

        public Task RunWithProgressAsync(string title, Func<Task> work) =>
            ProgressDialog.RunAsync(title, title, work);
    }
}
