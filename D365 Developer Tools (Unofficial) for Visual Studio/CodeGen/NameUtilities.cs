using System.Linq;
using System.Text.RegularExpressions;

namespace D365_Developer_Tools__Unofficial__for_Visual_Studio.CodeGen
{
    /// <summary>Ports toPascalCase/toEnumKey from interfaceGenerator.ts.</summary>
    internal static class NameUtilities
    {
        private static readonly Regex NonAlphaNumeric = new Regex("[^a-zA-Z0-9]+", RegexOptions.Compiled);
        private static readonly Regex LeadingDigit = new Regex(@"^\d", RegexOptions.Compiled);

        public static string ToPascalCase(string s)
        {
            var sanitized = NonAlphaNumeric.Replace(s ?? string.Empty, " ").Trim();
            var pascal = string.Concat(sanitized
                .Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries)
                .Select(w => char.ToUpperInvariant(w[0]) + w.Substring(1)));

            return LeadingDigit.IsMatch(pascal) ? "_" + pascal : pascal;
        }

        /// <summary>Prefers the friendly display name over the logical name, falling back when the display name is missing.</summary>
        public static string ToPascalCase(string logicalName, string displayName) =>
            ToPascalCase(string.IsNullOrEmpty(displayName) ? logicalName : displayName);

        public static string ToEnumKey(string label)
        {
            var sanitized = Regex.Replace(label ?? string.Empty, "[^a-zA-Z0-9 _]", " ").Trim();
            var pascal = ToPascalCase(string.IsNullOrEmpty(sanitized) ? "Unknown" : sanitized);
            return LeadingDigit.IsMatch(pascal) ? "_" + pascal : pascal;
        }
    }
}
