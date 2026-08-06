using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace D365_Developer_Tools__Unofficial__for_Visual_Studio.Shared
{
    /// <summary>
    /// Abstracts user interaction (text prompts, single/multi-select pickers, progress, notifications)
    /// so ConnectionManager/DataverseClient-consuming code isn't hard-wired to WPF. This is the direct
    /// analog of vscode.window's showInputBox / showQuickPick / withProgress / showInformationMessage.
    /// </summary>
    internal interface IUserPrompts
    {
        /// <summary>Returns null if the user cancels.</summary>
        Task<string> PromptTextAsync(string title, string placeholder, bool isPassword = false, string defaultValue = null);

        /// <summary>Returns null (default(T) for value types, wrapped) if the user cancels.</summary>
        Task<PickItem<T>> PickOneAsync<T>(string title, string placeholder, IReadOnlyList<PickItem<T>> items);

        /// <summary>Returns null if the user cancels; empty list if they confirm with nothing checked.</summary>
        Task<IReadOnlyList<PickItem<T>>> PickManyAsync<T>(string title, string placeholder, IReadOnlyList<PickItem<T>> items);

        void ShowInfo(string message);
        void ShowError(string message);

        /// <summary>Yes/No confirmation for destructive actions (e.g. unregistering a record). True if the user chose Yes.</summary>
        Task<bool> ConfirmAsync(string title, string message);

        Task<T> RunWithProgressAsync<T>(string title, Func<Task<T>> work);
        Task RunWithProgressAsync(string title, Func<Task> work);
    }
}
