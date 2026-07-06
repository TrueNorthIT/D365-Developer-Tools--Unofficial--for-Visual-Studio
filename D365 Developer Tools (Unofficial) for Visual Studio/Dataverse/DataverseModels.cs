namespace D365_Developer_Tools__Unofficial__for_Visual_Studio.Dataverse
{
    internal sealed class EntityDefinition
    {
        public string MetadataId { get; set; }
        public string LogicalName { get; set; }
        public string SchemaName { get; set; }
        public string DisplayName { get; set; }
        public bool IsCustom { get; set; }
    }

    internal sealed class AttributeDefinition
    {
        public string LogicalName { get; set; }
        public string SchemaName { get; set; }
        public string DisplayName { get; set; }
        public string AttributeType { get; set; }
        public bool IsPrimaryId { get; set; }
        public bool IsPrimaryName { get; set; }
    }

    internal sealed class DataverseSolution
    {
        public string SolutionId { get; set; }
        public string UniqueName { get; set; }
        public string FriendlyName { get; set; }
    }

    internal sealed class OptionValue
    {
        public int Value { get; set; }
        public string Label { get; set; }
    }
}
