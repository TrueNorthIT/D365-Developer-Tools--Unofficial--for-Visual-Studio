using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel.Composition;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion.Data;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;

namespace D365_Developer_Tools__Unofficial__for_Visual_Studio.Completion
{
    /// <summary>
    /// Ports d365CodeActionProvider.ts's D365CompletionProvider: typing "d365" anywhere in a .cs file
    /// offers two completion items. Accepting one (handled by D365CompletionCommitManager) prompts for
    /// an entity, optionally lets the user pick fields, and inserts a generated early-bound C# class.
    /// </summary>
    [Export(typeof(IAsyncCompletionSourceProvider))]
    [Name("D365CompletionSourceProvider")]
    [ContentType("CSharp")]
    internal sealed class D365CompletionSourceProvider : IAsyncCompletionSourceProvider
    {
        private readonly D365CompletionSource _source = new D365CompletionSource();

        public IAsyncCompletionSource GetOrCreate(ITextView textView) => _source;
    }

    internal sealed class D365CompletionSource : IAsyncCompletionSource
    {
        public const string GenerateAllDisplayText = "D365: Generate interface…";
        public const string GenerateSelectDisplayText = "D365: Generate interface (select fields…)";

        private static readonly Regex WordPattern = new Regex(@"d365\w*", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public CompletionStartData InitializeCompletion(CompletionTrigger trigger, SnapshotPoint triggerLocation, CancellationToken token)
        {
            var line = triggerLocation.GetContainingLine();
            var lineText = line.GetText();
            var offsetInLine = triggerLocation.Position - line.Start.Position;

            foreach (Match match in WordPattern.Matches(lineText))
            {
                if (offsetInLine >= match.Index && offsetInLine <= match.Index + match.Length)
                {
                    var span = new SnapshotSpan(line.Start + match.Index, match.Length);
                    return new CompletionStartData(CompletionParticipation.ProvidesItems, span);
                }
            }

            return CompletionStartData.DoesNotParticipateInCompletion;
        }

        public Task<CompletionContext> GetCompletionContextAsync(
            IAsyncCompletionSession session,
            CompletionTrigger trigger,
            SnapshotPoint triggerLocation,
            SnapshotSpan applicableToSpan,
            CancellationToken token)
        {
            var connected = D365DeveloperToolsPackage.Instance?.ConnectionManager?.IsConnected ?? false;
            var suffix = connected ? string.Empty : " — connect first";

            var items = ImmutableArray.Create(
                MakeItem(GenerateAllDisplayText + suffix),
                MakeItem(GenerateSelectDisplayText + suffix));

            return Task.FromResult(new CompletionContext(items));
        }

        public Task<object> GetDescriptionAsync(IAsyncCompletionSession session, CompletionItem item, CancellationToken token) =>
            Task.FromResult<object>("Generates an early-bound C# class (and any option-set enums) for a Dataverse entity.");

        // The two-arg constructor is enough: FilterText/SortText default to displayText, which is
        // fine since both our items literally start with "D365:" and so still match while the user
        // is typing "d365". InsertText doesn't matter either — D365CompletionCommitManager intercepts
        // and handles the commit itself, so the editor's own default-insert path never runs.
        private CompletionItem MakeItem(string displayText) => new CompletionItem(displayText, this);
    }
}
