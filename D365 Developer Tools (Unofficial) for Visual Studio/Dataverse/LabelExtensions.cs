using System.Linq;
using D365_Developer_Tools__Unofficial__for_Visual_Studio.Dataverse.Dto;

namespace D365_Developer_Tools__Unofficial__for_Visual_Studio.Dataverse
{
    internal static class LabelExtensions
    {
        private const int EnUsLanguageCode = 1033;

        public static string ExtractLabel(this DataverseLabelDto label)
        {
            if (label == null) { return string.Empty; }

            return label.UserLocalizedLabel?.Label
                ?? label.LocalizedLabels?.FirstOrDefault(l => l.LanguageCode == EnUsLanguageCode)?.Label
                ?? label.LocalizedLabels?.FirstOrDefault()?.Label
                ?? string.Empty;
        }
    }
}
