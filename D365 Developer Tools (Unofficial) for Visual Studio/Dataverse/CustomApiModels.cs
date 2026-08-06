using System.Collections.Generic;

namespace D365_Developer_Tools__Unofficial__for_Visual_Studio.Dataverse
{
    internal sealed class CustomApiDefinition
    {
        public string CustomApiId { get; set; }
        public string UniqueName { get; set; }
        public string Name { get; set; }
        public string DisplayName { get; set; }
        public string Description { get; set; }
        public int BindingTypeValue { get; set; }
        public string BindingType { get; set; }
        public string BoundEntityLogicalName { get; set; }
        public int AllowedCustomProcessingStepTypeValue { get; set; }
        public string AllowedCustomProcessingStepType { get; set; }
        public bool IsFunction { get; set; }
        public bool IsPrivate { get; set; }
        public string ExecutePrivilegeName { get; set; }

        /// <summary>Null until a plugin type is linked — Custom APIs can be registered before their implementation exists.</summary>
        public string PluginTypeId { get; set; }
    }

    /// <summary>A request parameter or response property — the two entities share an identical shape apart from IsOptional (request-only).</summary>
    internal sealed class CustomApiParameterDefinition
    {
        public string Id { get; set; }
        public string UniqueName { get; set; }
        public string Name { get; set; }
        public string DisplayName { get; set; }
        public string Description { get; set; }
        public int TypeValue { get; set; }
        public string Type { get; set; }
        public string LogicalEntityName { get; set; }
        public bool IsRequestParameter { get; set; }
        public bool IsOptional { get; set; }
    }

    /// <summary>Everything needed to create or update a Custom API. UniqueName/BindingType/BoundEntityLogicalName/AllowedCustomProcessingStepType/IsFunction can't be changed after creation (the Plugin Registration Tool enforces the same restriction) — callers should disable those fields' editors in edit mode rather than silently ignoring a change.</summary>
    internal sealed class CustomApiRegistrationDetails
    {
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
        public string PluginTypeId { get; set; }
        public string SolutionUniqueName { get; set; }
    }

    /// <summary>Everything needed to create or update a Custom API request parameter or response property. UniqueName/Type/LogicalEntityName/IsOptional can't be changed after creation.</summary>
    internal sealed class CustomApiParameterRegistrationDetails
    {
        public string UniqueName { get; set; }
        public string Name { get; set; }
        public string DisplayName { get; set; }
        public string Description { get; set; }
        public int Type { get; set; }
        public string LogicalEntityName { get; set; }
        public bool IsOptional { get; set; }
    }

    internal static class CustomApiOptionLabels
    {
        private static readonly Dictionary<int, string> BindingTypes = new Dictionary<int, string>
        {
            [0] = "Global",
            [1] = "Entity",
            [2] = "Entity Collection",
        };

        private static readonly Dictionary<int, string> ProcessingStepTypes = new Dictionary<int, string>
        {
            [0] = "None",
            [1] = "Async Only",
            [2] = "Sync and Async",
        };

        private static readonly Dictionary<int, string> ParameterTypes = new Dictionary<int, string>
        {
            [0] = "Boolean",
            [1] = "DateTime",
            [2] = "Decimal",
            [3] = "Entity",
            [4] = "EntityCollection",
            [5] = "EntityReference",
            [6] = "Float",
            [7] = "Integer",
            [8] = "Money",
            [9] = "Picklist",
            [10] = "String",
            [11] = "StringArray",
            [12] = "Guid",
        };

        public static string BindingType(int value) => BindingTypes.TryGetValue(value, out var label) ? label : value.ToString();
        public static string ProcessingStepType(int value) => ProcessingStepTypes.TryGetValue(value, out var label) ? label : value.ToString();
        public static string ParameterType(int value) => ParameterTypes.TryGetValue(value, out var label) ? label : value.ToString();

        public static readonly IReadOnlyList<PluginOption> RegisterableBindingTypes = new List<PluginOption>
        {
            new PluginOption(0, "Global"),
            new PluginOption(1, "Entity"),
            new PluginOption(2, "Entity Collection"),
        };

        public static readonly IReadOnlyList<PluginOption> RegisterableProcessingStepTypes = new List<PluginOption>
        {
            new PluginOption(0, "None"),
            new PluginOption(1, "Async Only"),
            new PluginOption(2, "Sync and Async"),
        };

        public static readonly IReadOnlyList<PluginOption> RegisterableParameterTypes = new List<PluginOption>
        {
            new PluginOption(0, "Boolean"),
            new PluginOption(1, "DateTime"),
            new PluginOption(2, "Decimal"),
            new PluginOption(3, "Entity"),
            new PluginOption(4, "EntityCollection"),
            new PluginOption(5, "EntityReference"),
            new PluginOption(6, "Float"),
            new PluginOption(7, "Integer"),
            new PluginOption(8, "Money"),
            new PluginOption(9, "Picklist"),
            new PluginOption(10, "String"),
            new PluginOption(11, "StringArray"),
            new PluginOption(12, "Guid"),
        };
    }
}
