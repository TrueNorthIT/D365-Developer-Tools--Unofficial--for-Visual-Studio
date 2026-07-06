using System;
using System.Windows;
using System.Windows.Interop;

namespace D365_Developer_Tools__Unofficial__for_Visual_Studio.Shared.Dialogs
{
    /// <summary>Modal replacement for vscode.window.showInputBox.</summary>
    internal partial class TextInputDialog : Window
    {
        public string Value { get; private set; }

        private readonly bool _isPassword;

        public TextInputDialog(string title, string prompt, string placeholder, bool isPassword, string defaultValue)
        {
            InitializeComponent();
            Loaded += (_, __) => DialogForegroundHelper.BringToFront(this);

            Title = title;
            PromptText.Text = prompt;
            _isPassword = isPassword;

            if (isPassword)
            {
                PlainTextBox.Visibility = Visibility.Collapsed;
                SecretTextBox.Visibility = Visibility.Visible;
                SecretTextBox.Focus();
            }
            else
            {
                PlainTextBox.Text = defaultValue ?? string.Empty;
                PlainTextBox.ToolTip = placeholder;
                PlainTextBox.Focus();
                PlainTextBox.SelectAll();
            }
        }

        private void OnOk(object sender, RoutedEventArgs e)
        {
            var text = (_isPassword ? SecretTextBox.Password : PlainTextBox.Text)?.Trim();
            if (string.IsNullOrEmpty(text))
            {
                DialogResult = false;
                return;
            }

            Value = text;
            DialogResult = true;
        }

        private void OnCancel(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        /// <summary>Shows the dialog and returns the entered text, or null if cancelled/empty.</summary>
        public static string Show(IntPtr ownerHwnd, string title, string prompt, string placeholder = null, bool isPassword = false, string defaultValue = null)
        {
            var dialog = new TextInputDialog(title, prompt, placeholder, isPassword, defaultValue);
            if (ownerHwnd != IntPtr.Zero) { new WindowInteropHelper(dialog).Owner = ownerHwnd; }
            return dialog.ShowDialog() == true ? dialog.Value : null;
        }
    }
}
