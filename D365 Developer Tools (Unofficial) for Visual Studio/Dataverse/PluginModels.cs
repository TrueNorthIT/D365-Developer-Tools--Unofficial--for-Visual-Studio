using System.Collections.Generic;

namespace D365_Developer_Tools__Unofficial__for_Visual_Studio.Dataverse
{
    internal sealed class PluginAssemblyDefinition
    {
        public string PluginAssemblyId { get; set; }
        public string Name { get; set; }
        public string Version { get; set; }
        public string IsolationMode { get; set; }
        public string SourceType { get; set; }

        /// <summary>Null when the assembly wasn't deployed as part of a NuGet-style plugin package.</summary>
        public string PackageName { get; set; }
        public string PackageVersion { get; set; }
        public string Description { get; set; }
    }

    internal sealed class PluginTypeDefinition
    {
        public string PluginTypeId { get; set; }
        public string Name { get; set; }
        public string FriendlyName { get; set; }
        public string TypeName { get; set; }
        public bool IsWorkflowActivity { get; set; }
    }

    internal sealed class SdkMessageStepDefinition
    {
        public string StepId { get; set; }
        public string Name { get; set; }
        public string SdkMessageId { get; set; }
        public string MessageName { get; set; }

        /// <summary>Null for steps registered against every entity (no message filter).</summary>
        public string SdkMessageFilterId { get; set; }
        public string PrimaryEntity { get; set; }

        public int StageValue { get; set; }
        public string Stage { get; set; }
        public int ModeValue { get; set; }
        public string Mode { get; set; }
        public int Rank { get; set; }
        public bool IsEnabled { get; set; }
        public string FilteringAttributes { get; set; }
        public string Description { get; set; }
        public string UnsecureConfiguration { get; set; }

        /// <summary>Null when the step runs as the calling user (the default) rather than an impersonated one.</summary>
        public string ImpersonatingUserId { get; set; }

        /// <summary>Null when the step has no secure configuration record yet. Dataverse never returns the secure value itself, only whether one exists.</summary>
        public string SecureConfigId { get; set; }
    }

    internal sealed class SystemUserOption
    {
        public string UserId { get; set; }
        public string FullName { get; set; }
    }

    /// <summary>Everything needed to create or update an SdkMessageProcessingStep — grouped here rather than as a long positional parameter list.</summary>
    internal sealed class StepRegistrationDetails
    {
        public string SdkMessageId { get; set; }

        /// <summary>Null for "(all entities)".</summary>
        public string SdkMessageFilterId { get; set; }
        public string Name { get; set; }
        public int Stage { get; set; }
        public int Mode { get; set; }
        public int Rank { get; set; }
        public string FilteringAttributes { get; set; }
        public string SolutionUniqueName { get; set; }
        public string Description { get; set; }
        public string UnsecureConfiguration { get; set; }

        /// <summary>Null to run as the calling user (the default).</summary>
        public string ImpersonatingUserId { get; set; }

        /// <summary>Null/empty means "leave the secure configuration as-is" on update (Dataverse never returns its value, so a blank field isn't a request to clear it).</summary>
        public string SecureConfiguration { get; set; }

        /// <summary>Update only: the step's current secure-config record ID, if it already has one.</summary>
        public string ExistingSecureConfigId { get; set; }
    }

    internal sealed class SdkMessageStepImageDefinition
    {
        public string ImageId { get; set; }
        public string Name { get; set; }
        public string EntityAlias { get; set; }
        public int ImageTypeValue { get; set; }
        public string ImageType { get; set; }
        public string Attributes { get; set; }
    }

    /// <summary>Everything needed to create or update an SdkMessageProcessingStepImage.</summary>
    internal sealed class ImageRegistrationDetails
    {
        public string Name { get; set; }
        public string EntityAlias { get; set; }
        public int ImageType { get; set; }

        /// <summary>Comma-separated attribute logical names.</summary>
        public string Attributes { get; set; }

        /// <summary>The Request message's target parameter name. "Target" covers every message images are realistically used with (Create/Update/Delete/Assign) — see the Message/Request Class Property table in Microsoft's entity-images docs for the handful of messages that differ.</summary>
        public string MessagePropertyName { get; set; } = "Target";
    }

    /// <summary>Identifies an existing PluginAssembly or PluginPackage record found by name.</summary>
    internal sealed class PluginRecordRef
    {
        public string Id { get; set; }
        public string Version { get; set; }
    }

    internal sealed class SdkMessageOption
    {
        public string SdkMessageId { get; set; }
        public string Name { get; set; }
    }

    internal sealed class SdkMessageFilterOption
    {
        public string SdkMessageFilterId { get; set; }
        public string EntityLogicalName { get; set; }
    }

    /// <summary>A PluginType record found by its fully-qualified type name — possibly ambiguous if more than one assembly registers a type with that name.</summary>
    internal sealed class PluginTypeMatch
    {
        public string PluginTypeId { get; set; }
        public string PluginAssemblyId { get; set; }
        public string TypeName { get; set; }
        public string FriendlyName { get; set; }
        public string AssemblyName { get; set; }
    }

    /// <summary>Maps the raw numeric option-set values Dataverse returns for plugin metadata to their labels.</summary>
    internal static class PluginOptionLabels
    {
        private static readonly Dictionary<int, string> IsolationModes = new Dictionary<int, string>
        {
            [1] = "None",
            [2] = "Sandbox",
            [3] = "External",
        };

        private static readonly Dictionary<int, string> SourceTypes = new Dictionary<int, string>
        {
            [0] = "Database",
            [1] = "Disk",
            [2] = "Normal",
            [3] = "Azure Web App",
            [4] = "File Store",
        };

        private static readonly Dictionary<int, string> Stages = new Dictionary<int, string>
        {
            [10] = "Pre-validation",
            [20] = "Pre-operation",
            [40] = "Post-operation",
            [50] = "Post-operation (deprecated)",
        };

        private static readonly Dictionary<int, string> Modes = new Dictionary<int, string>
        {
            [0] = "Synchronous",
            [1] = "Asynchronous",
        };

        private static readonly Dictionary<int, string> ImageTypes = new Dictionary<int, string>
        {
            [0] = "Pre Image",
            [1] = "Post Image",
            [2] = "Both",
        };

        /// <summary>plugintracelog's operationtype option set — which kind of registered component produced the trace.</summary>
        private static readonly Dictionary<int, string> OperationTypes = new Dictionary<int, string>
        {
            [1] = "Plugin",
            [2] = "Classic Workflow",
            [3] = "Workflow Activity",
        };

        public static string IsolationMode(int value) => IsolationModes.TryGetValue(value, out var label) ? label : value.ToString();
        public static string SourceType(int value) => SourceTypes.TryGetValue(value, out var label) ? label : value.ToString();
        public static string Stage(int value) => Stages.TryGetValue(value, out var label) ? label : value.ToString();
        public static string Mode(int value) => Modes.TryGetValue(value, out var label) ? label : value.ToString();
        public static string ImageType(int value) => ImageTypes.TryGetValue(value, out var label) ? label : value.ToString();
        public static string OperationType(int value) => OperationTypes.TryGetValue(value, out var label) ? label : value.ToString();

        /// <summary>The three stages a step can actually be registered against (the rest are internal-only/deprecated).</summary>
        public static readonly IReadOnlyList<PluginOption> RegisterableStages = new List<PluginOption>
        {
            new PluginOption(10, "Pre-validation"),
            new PluginOption(20, "Pre-operation"),
            new PluginOption(40, "Post-operation"),
        };

        public static readonly IReadOnlyList<PluginOption> RegisterableModes = new List<PluginOption>
        {
            new PluginOption(0, "Synchronous"),
            new PluginOption(1, "Asynchronous"),
        };

        public static readonly IReadOnlyList<PluginOption> RegisterableImageTypes = new List<PluginOption>
        {
            new PluginOption(0, "Pre Image"),
            new PluginOption(1, "Post Image"),
            new PluginOption(2, "Both"),
        };
    }

    internal sealed class PluginOption
    {
        public int Value { get; }
        public string Label { get; }

        public PluginOption(int value, string label)
        {
            Value = value;
            Label = label;
        }
    }
}
