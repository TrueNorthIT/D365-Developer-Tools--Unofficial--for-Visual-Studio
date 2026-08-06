using System;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.VisualStudio.Shell;

namespace D365_Developer_Tools__Unofficial__for_Visual_Studio.Shared.Mvvm
{
    /// <summary>
    /// ICommand over an async delegate, with a re-entrancy guard so double-clicks/rapid invocation
    /// can't start the same operation twice while it's already running.
    /// </summary>
    internal sealed class AsyncRelayCommand : ICommand
    {
        private readonly Func<Task> _execute;
        private readonly Func<bool> _canExecute;
        private bool _isRunning;

        public AsyncRelayCommand(Func<Task> execute, Func<bool> canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public event EventHandler CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }

        public bool CanExecute(object parameter) => !_isRunning && (_canExecute?.Invoke() ?? true);

        public void Execute(object parameter)
        {
            // Fire-and-forget onto the JoinableTaskFactory: commands are invoked from the UI thread,
            // and the work itself is expected to switch off it for any I/O.
            ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                _isRunning = true;
                CommandManager.InvalidateRequerySuggested();
                try
                {
                    await _execute().ConfigureAwait(true);
                }
                finally
                {
                    _isRunning = false;
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    CommandManager.InvalidateRequerySuggested();
                }
            }).Task.FileAndForget("D365DeveloperTools/AsyncRelayCommand");
        }
    }
}
