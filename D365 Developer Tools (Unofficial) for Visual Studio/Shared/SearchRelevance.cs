using System;
using System.Collections.Generic;
using System.Linq;

namespace D365_Developer_Tools__Unofficial__for_Visual_Studio.Shared
{
    /// <summary>
    /// Ranks a search match by relevance rather than leaving matches in whatever order the underlying
    /// list happened to have them: an exact match beats a match at the start of the text, which beats a
    /// match at the start of a word, which beats a match buried mid-word. Without this, typing "update"
    /// could show "BulkUpdateOperation" above "Update" just because the list was sorted alphabetically.
    /// </summary>
    internal static class SearchRelevance
    {
        /// <summary>Lower is more relevant. int.MaxValue means "doesn't match at all".</summary>
        public static int Rank(string text, string query)
        {
            if (string.IsNullOrEmpty(query)) { return 0; }
            if (string.IsNullOrEmpty(text)) { return int.MaxValue; }

            if (string.Equals(text, query, StringComparison.OrdinalIgnoreCase)) { return 0; }
            if (text.StartsWith(query, StringComparison.OrdinalIgnoreCase)) { return 1; }

            var index = text.IndexOf(query, StringComparison.OrdinalIgnoreCase);
            if (index < 0) { return int.MaxValue; }

            var startsAWord = index == 0 || !char.IsLetterOrDigit(text[index - 1]);
            return startsAWord ? 2 : 3;
        }

        /// <summary>
        /// Orders already-filtered items by relevance against query (best match first). Ties keep their
        /// original relative order. Items with no match at all (Rank == int.MaxValue) sort last rather
        /// than being dropped, so callers that filter separately don't need to worry about this reordering
        /// hiding anything.
        /// </summary>
        public static IEnumerable<T> OrderByRelevance<T>(IEnumerable<T> items, string query, Func<T, string> textSelector)
        {
            if (string.IsNullOrEmpty(query)) { return items; }

            return items
                .Select((item, index) => (item, rank: Rank(textSelector(item), query), index))
                .OrderBy(x => x.rank)
                .ThenBy(x => textSelector(x.item)?.Length ?? int.MaxValue)
                .ThenBy(x => x.index)
                .Select(x => x.item);
        }
    }
}
