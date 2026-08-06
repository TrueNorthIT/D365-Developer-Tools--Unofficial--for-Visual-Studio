using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace D365_Developer_Tools__Unofficial__for_Visual_Studio.PluginPublishing
{
    /// <summary>
    /// Best-effort text scan for IPlugin-implementing classes in a C# source file — used for the "Add
    /// Step" project-item context menu (both to decide whether to show it at all, and to work out the
    /// fully-qualified type name to look up in Dataverse). This is a lightweight regex scan rather than
    /// a real parse, so it's wrong for pathological cases (deeply nested classes, "IPlugin" appearing
    /// only in a comment/string, etc.) — good enough for a menu-visibility check and a starting point
    /// that the command can fail clearly on rather than something that needs to build the project.
    /// </summary>
    internal static class PluginTypeNameExtractor
    {
        private static readonly Regex NamespaceRegex = new Regex(
            @"^\s*namespace\s+([\w.]+)\s*[{;]",
            RegexOptions.Multiline | RegexOptions.Compiled);

        private static readonly Regex ClassRegex = new Regex(
            @"^\s*(?:\[[^\]]*\]\s*)*(?:public|internal)\s+(?:sealed\s+|abstract\s+|partial\s+)*class\s+(\w+)\s*(?::\s*([^{]+))?{",
            RegexOptions.Multiline | RegexOptions.Compiled);

        private static readonly Regex PluginInterfaceRegex = new Regex(@"\bIPlugin\b", RegexOptions.Compiled);

        public static bool MightContainPlugin(string sourceText) => sourceText != null && PluginInterfaceRegex.IsMatch(sourceText);

        public static bool FileMightContainPlugin(string filePath)
        {
            try
            {
                return MightContainPlugin(File.ReadAllText(filePath));
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Returns the fully-qualified names of classes in the file whose base list mentions IPlugin.</summary>
        public static List<string> FindPluginTypeNames(string sourceText)
        {
            var results = new List<string>();
            if (string.IsNullOrEmpty(sourceText)) { return results; }

            var namespaceMatches = NamespaceRegex.Matches(sourceText);

            foreach (Match classMatch in ClassRegex.Matches(sourceText))
            {
                var baseList = classMatch.Groups[2].Success ? classMatch.Groups[2].Value : string.Empty;
                if (!PluginInterfaceRegex.IsMatch(baseList)) { continue; }

                var className = classMatch.Groups[1].Value;
                var containingNamespace = FindContainingNamespace(namespaceMatches, classMatch.Index);

                results.Add(string.IsNullOrEmpty(containingNamespace) ? className : $"{containingNamespace}.{className}");
            }

            return results;
        }

        private static string FindContainingNamespace(MatchCollection namespaceMatches, int position)
        {
            string result = null;
            foreach (Match match in namespaceMatches)
            {
                if (match.Index > position) { break; }
                result = match.Groups[1].Value;
            }

            return result;
        }
    }
}
