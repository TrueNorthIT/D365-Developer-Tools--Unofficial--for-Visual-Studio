using Newtonsoft.Json;

namespace D365_Developer_Tools__Unofficial__for_Visual_Studio.Dataverse.Dto
{
    // ── Dataverse OData response shapes for Custom APIs ─────────────────────
    //
    // Custom API's own lookups use unusually capitalized navigation property names
    // (PluginTypeId, CustomAPIId) rather than the lowercase convention most other
    // entities in this file use — that's a Dataverse platform quirk for this
    // particular entity family, not a typo.

    internal sealed class CustomApiDto
    {
        public string CustomApiId { get; set; }
        public string UniqueName { get; set; }
        public string Name { get; set; }
        public string DisplayName { get; set; }
        public string Description { get; set; }
        public int BindingType { get; set; }
        public string BoundEntityLogicalName { get; set; }
        public int AllowedCustomProcessingStepType { get; set; }
        public bool IsFunction { get; set; }
        public bool IsPrivate { get; set; }
        public string ExecutePrivilegeName { get; set; }

        [JsonProperty("_plugintypeid_value")]
        public string PluginTypeIdValue { get; set; }
    }

    internal sealed class CustomApiRequestParameterDto
    {
        public string CustomApiRequestParameterId { get; set; }
        public string UniqueName { get; set; }
        public string Name { get; set; }
        public string DisplayName { get; set; }
        public string Description { get; set; }
        public int Type { get; set; }
        public string LogicalEntityName { get; set; }
        public bool IsOptional { get; set; }
    }

    internal sealed class CustomApiResponsePropertyDto
    {
        public string CustomApiResponsePropertyId { get; set; }
        public string UniqueName { get; set; }
        public string Name { get; set; }
        public string DisplayName { get; set; }
        public string Description { get; set; }
        public int Type { get; set; }
        public string LogicalEntityName { get; set; }
    }
}
