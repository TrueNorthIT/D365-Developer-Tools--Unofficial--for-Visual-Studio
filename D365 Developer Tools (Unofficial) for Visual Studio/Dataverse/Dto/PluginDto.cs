using Newtonsoft.Json;

namespace D365_Developer_Tools__Unofficial__for_Visual_Studio.Dataverse.Dto
{
    // ── Dataverse OData response shapes for plugin assemblies/types/steps/images ────

    internal sealed class PluginAssemblyDto
    {
        public string PluginAssemblyId { get; set; }
        public string Name { get; set; }
        public string Version { get; set; }
        public int IsolationMode { get; set; }
        public int SourceType { get; set; }

        [JsonProperty("packageid")]
        public PluginPackageRefDto PackageId { get; set; }
    }

    internal sealed class PluginPackageRefDto
    {
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

        [JsonProperty("sdkmessageid")]
        public SdkMessageRefDto SdkMessageId { get; set; }

        [JsonProperty("sdkmessagefilterid")]
        public SdkMessageFilterRefDto SdkMessageFilterId { get; set; }
    }

    internal sealed class SdkMessageRefDto
    {
        public string Name { get; set; }
    }

    internal sealed class SdkMessageFilterRefDto
    {
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
