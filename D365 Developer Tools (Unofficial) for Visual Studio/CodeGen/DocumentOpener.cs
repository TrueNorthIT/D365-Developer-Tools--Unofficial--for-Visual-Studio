using EnvDTE;
using Microsoft.VisualStudio.Shell;

namespace D365_Developer_Tools__Unofficial__for_Visual_Studio.CodeGen
{
    /// <summary>
    /// Opens generated text as an unsaved editor buffer — the analog of VS Code's
    /// vscode.workspace.openTextDocument({ content, language }), rather than writing a throwaway file.
    /// The suggested name is given a .cs extension so VS's language service picks C# up from the
    /// file name (and the eventual Save As defaults to .cs instead of .txt).
    /// </summary>
    internal static class DocumentOpener
    {
        public static void OpenAsCSharp(string content, string suggestedFileName)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (!(ServiceProvider.GlobalProvider.GetService(typeof(DTE)) is DTE dte)) { return; }

            var fileName = string.IsNullOrEmpty(suggestedFileName) ? "Class1.cs" : suggestedFileName;
            var window = dte.ItemOperations.NewFile(@"General\Text File", fileName);
            var textDocument = (TextDocument)window.Document.Object("TextDocument");
            textDocument.StartPoint.CreateEditPoint().Insert(content);

            try { window.Document.Language = "CSharp"; }
            catch { /* language service not registered under this exact name in some SKUs; text still opens fine */ }
        }
    }
}
