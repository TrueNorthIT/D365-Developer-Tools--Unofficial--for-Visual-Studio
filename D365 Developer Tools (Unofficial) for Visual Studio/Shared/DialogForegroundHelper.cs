using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace D365_Developer_Tools__Unofficial__for_Visual_Studio.Shared
{
    /// <summary>
    /// Windows enforces a "foreground lock" that blocks a window from stealing focus unless the
    /// showing process/thread currently owns the foreground. Windows created from a
    /// VisualStudio.Extensibility command's execution context routinely lose that permission, so our
    /// dialogs and error message boxes were opening minimized or behind other windows instead of on
    /// top. This forces them to the front.
    /// </summary>
    internal static class DialogForegroundHelper
    {
        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        public static void BringToFront(Window window)
        {
            window.Topmost = true;
            window.Activate();
            window.Topmost = false;
            window.Focus();

            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd != IntPtr.Zero)
            {
                SetForegroundWindow(hwnd);
            }
        }

        /// <summary>
        /// Shows a message box that reliably comes to the foreground. Plain MessageBox.Show with no
        /// owner has no window of its own to force forward, so this hosts it under a temporary,
        /// invisible, always-on-top owner window instead.
        /// </summary>
        public static MessageBoxResult ShowMessage(string message, string title, MessageBoxButton button, MessageBoxImage icon)
        {
            var owner = new Window
            {
                Width = 0,
                Height = 0,
                WindowStyle = WindowStyle.None,
                ShowInTaskbar = false,
                AllowsTransparency = true,
                Background = System.Windows.Media.Brushes.Transparent,
                Topmost = true,
            };

            owner.Show();
            BringToFront(owner);
            try
            {
                return MessageBox.Show(owner, message, title, button, icon);
            }
            finally
            {
                owner.Close();
            }
        }
    }
}
