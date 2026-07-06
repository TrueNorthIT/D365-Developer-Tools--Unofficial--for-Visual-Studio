using EnvDTE;
using Microsoft.VisualStudio.Shell;

namespace D365_Developer_Tools__Unofficial__for_Visual_Studio.CodeGen
{
    /// <summary>
    /// Opens generated text as an unsaved editor buffer — the analog of VS Code's
    /// vscode.workspace.openTextDocument({ content, language }), rather than writing a throwaway file.
    /// </summary>
    internal static class DocumentOpener
    {
        public static void OpenAsCSharp(string content)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (!(ServiceProvider.GlobalProvider.GetService(typeof(DTE)) is DTE dte)) { return; }

            var window = dte.ItemOperations.NewFile(@"General\Text File");
            var textDocument = (TextDocument)window.Document.Object("TextDocument");
            textDocument.StartPoint.CreateEditPoint().Insert(content);

            try { window.Document.Language = "CSharp"; }
            catch { /* language service not registered under this exact name in some SKUs; text still opens fine */ }
        }
    }
}
