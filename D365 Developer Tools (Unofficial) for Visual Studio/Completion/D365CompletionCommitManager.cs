using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion.Data;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;

namespace D365_Developer_Tools__Unofficial__for_Visual_Studio.Completion
{
    [Export(typeof(IAsyncCompletionCommitManagerProvider))]
    [Name("D365CompletionCommitManagerProvider")]
    [ContentType("CSharp")]
    internal sealed class D365CompletionCommitManagerProvider : IAsyncCompletionCommitManagerProvider
    {
        private readonly D365CompletionCommitManager _manager = new D365CompletionCommitManager();

        public IAsyncCompletionCommitManager GetOrCreate(ITextView textView) => _manager;
    }

    /// <summary>
    /// Intercepts acceptance of our own completion items (identified by display text) and, instead of
    /// letting the editor insert plain text, clears the "d365" word immediately and kicks off the
    /// entity/field picker + generation flow — mirroring the completion item's `command` callback in
    /// the VS Code version.
    /// </summary>
    internal sealed class D365CompletionCommitManager : IAsyncCompletionCommitManager
    {
        public IEnumerable<char> PotentialCommitCharacters => Array.Empty<char>();

        public bool ShouldCommitCompletion(IAsyncCompletionSession session, SnapshotPoint location, char typedChar, System.Threading.CancellationToken token) =>
            false;

        public CommitResult TryCommit(IAsyncCompletionSession session, ITextBuffer buffer, CompletionItem item, char typedChar, System.Threading.CancellationToken token)
        {
            bool selectFields;
            if (item.DisplayText.StartsWith(D365CompletionSource.GenerateAllDisplayText, StringComparison.Ordinal))
            {
                selectFields = false;
            }
            else if (item.DisplayText.StartsWith(D365CompletionSource.GenerateSelectDisplayText, StringComparison.Ordinal))
            {
                selectFields = true;
            }
            else
            {
                return CommitResult.Unhandled;
            }

            var applicableSpan = session.ApplicableToSpan;
            ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                await D365CompletionInsertionService.GenerateAndInsertAsync(buffer, applicableSpan, selectFields).ConfigureAwait(true);
            }).Task.FileAndForget("D365DeveloperTools/CompletionCommit");

            return CommitResult.Handled;
        }
    }
}
