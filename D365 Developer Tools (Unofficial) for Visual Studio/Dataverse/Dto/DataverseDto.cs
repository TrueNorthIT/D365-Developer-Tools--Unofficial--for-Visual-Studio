using System.Collections.Generic;
using Newtonsoft.Json;

namespace D365_Developer_Tools__Unofficial__for_Visual_Studio.Dataverse.Dto
{
    // ── Dataverse OData response shapes ─────────────────────────────────────

    internal sealed class ODataResponse<T>
    {
        public List<T> Value { get; set; }

        [JsonProperty("@odata.nextLink")]
        public string NextLink { get; set; }
    }

    internal sealed class LocalizedLabelDto
    {
        public string Label { get; set; }
        public int LanguageCode { get; set; }
    }

    internal sealed class DataverseLabelDto
    {
        public List<LocalizedLabelDto> LocalizedLabels { get; set; } = new List<LocalizedLabelDto>();
        public LocalizedLabelDto UserLocalizedLabel { get; set; }
    }

    internal sealed class EntityDefinitionDto
    {
        public string MetadataId { get; set; }
        public string LogicalName { get; set; }
        public string SchemaName { get; set; }
        public DataverseLabelDto DisplayName { get; set; }
        public bool IsCustomEntity { get; set; }
        public string IconVectorName { get; set; }
    }

    internal sealed class WebResourceContentDto
    {
        public string Name { get; set; }
        public string Content { get; set; }
    }

    internal sealed class AttributeDefinitionDto
    {
        public string LogicalName { get; set; }
        public string SchemaName { get; set; }
        public DataverseLabelDto DisplayName { get; set; }
        public string AttributeType { get; set; }
        public bool IsPrimaryId { get; set; }
        public bool IsPrimaryName { get; set; }
    }

    internal sealed class SolutionDto
    {
        public string SolutionId { get; set; }
        public string UniqueName { get; set; }
        public string FriendlyName { get; set; }
    }

    internal sealed class SolutionComponentDto
    {
        public string ObjectId { get; set; }
    }

    internal sealed class OptionSetItemsDto
    {
        public List<OptionDto> Options { get; set; } = new List<OptionDto>();
    }

    internal sealed class OptionDto
    {
        public int Value { get; set; }
        public DataverseLabelDto Label { get; set; }
    }

    internal sealed class AttributeOptionsDto
    {
        public OptionSetItemsDto OptionSet { get; set; }
        public OptionSetItemsDto GlobalOptionSet { get; set; }
    }
}
