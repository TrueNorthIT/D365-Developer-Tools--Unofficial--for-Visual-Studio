using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace D365_Developer_Tools__Unofficial__for_Visual_Studio.Shared
{
    /// <summary>Small helpers for interacting with the Visual Studio shell from anywhere in the extension.</summary>
    internal static class VsShellHelper
    {
        [DllImport("user32.dll")]
        private static extern bool IsWindow(IntPtr hWnd);

        /// <summary>
        /// Returns the current HWND of the main Visual Studio window, or IntPtr.Zero if it can't be
        /// determined. Deliberately fetched fresh on every call rather than cached at startup — a
        /// handle captured once during package initialization can go stale (observed as "Invalid
        /// window handle" when later used to parent a dialog), and Process.MainWindowHandle is an
        /// unreliable, cached-at-first-access snapshot that has the same problem.
        /// </summary>
        public static async Task<IntPtr> GetMainWindowHandleAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            if (await AsyncServiceProvider.GlobalProvider.GetServiceAsync(typeof(SVsUIShell)) is IVsUIShell uiShell &&
                uiShell.GetDialogOwnerHwnd(out var hwnd) == Microsoft.VisualStudio.VSConstants.S_OK &&
                hwnd != IntPtr.Zero &&
                IsWindow(hwnd))
            {
                return hwnd;
            }

            // No valid handle — callers treat IntPtr.Zero as "show without an explicit owner", which
            // is far safer than risking a stale/invalid one.
            return IntPtr.Zero;
        }
    }
}
