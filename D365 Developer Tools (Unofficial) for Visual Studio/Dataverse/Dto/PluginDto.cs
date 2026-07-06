using Newtonsoft.Json;

namespace D365_Developer_Tools__Unofficial__for_Visual_Studio.Dataverse.Dto
{
    // ── Dataverse OData response shapes for plugin assemblies/types/steps/images ────
    //
    // Lookups are read via their raw "_x_value" GUID and resolved with a separate, small
    // client-side join instead of $expand: the Web API's $expand navigation property name for a
    // single-valued lookup is the lookup's schema name, which doesn't always match its logical
    // name's casing (e.g. pluginassembly's "packageid" attribute expands as "PackageId", not
    // "packageid") and isn't worth guessing at per lookup.

    internal sealed class PluginAssemblyDto
    {
        public string PluginAssemblyId { get; set; }
        public string Name { get; set; }
        public string Version { get; set; }
        public int IsolationMode { get; set; }
        public int SourceType { get; set; }

        [JsonProperty("_packageid_value")]
        public string PackageIdValue { get; set; }
    }

    internal sealed class PluginPackageDto
    {
        public string PluginPackageId { get; set; }
        public string Name { get; set; }
        public string Version { get; set; }
    }

    internal sealed class PluginTypeDto
    {
        public string PluginTypeId { get; set; }
        public string Name { get; set; }
        public string FriendlyName { get; set; }
        public string TypeName { get; set; }
        public bool IsWorkflowActivity { get; set; }
    }

    internal sealed class SdkMessageStepDto
    {
        public string SdkMessageProcessingStepId { get; set; }
        public string Name { get; set; }
        public int Stage { get; set; }
        public int Mode { get; set; }
        public int Rank { get; set; }
        public int StateCode { get; set; }
        public string FilteringAttributes { get; set; }

        [JsonProperty("_sdkmessageid_value")]
        public string SdkMessageIdValue { get; set; }

        [JsonProperty("_sdkmessagefilterid_value")]
        public string SdkMessageFilterIdValue { get; set; }
    }

    internal sealed class SdkMessageDto
    {
        public string SdkMessageId { get; set; }
        public string Name { get; set; }
    }

    internal sealed class SdkMessageFilterDto
    {
        public string SdkMessageFilterId { get; set; }
        public string PrimaryObjectTypeCode { get; set; }
    }

    internal sealed class SdkMessageStepImageDto
    {
        public string SdkMessageProcessingStepImageId { get; set; }
        public string Name { get; set; }
        public string EntityAlias { get; set; }
        public int ImageType { get; set; }
        public string Attributes { get; set; }
    }
}
