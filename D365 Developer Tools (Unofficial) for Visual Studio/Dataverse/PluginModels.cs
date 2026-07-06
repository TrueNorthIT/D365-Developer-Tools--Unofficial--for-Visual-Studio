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
        public string MessageName { get; set; }

        /// <summary>Null for steps registered against every entity (no message filter).</summary>
        public string PrimaryEntity { get; set; }

        public string Stage { get; set; }
        public string Mode { get; set; }
        public int Rank { get; set; }
        public bool IsEnabled { get; set; }
        public string FilteringAttributes { get; set; }
    }

    internal sealed class SdkMessageStepImageDefinition
    {
        public string ImageId { get; set; }
        public string Name { get; set; }
        public string EntityAlias { get; set; }
        public string ImageType { get; set; }
        public string Attributes { get; set; }
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

        public static string IsolationMode(int value) => IsolationModes.TryGetValue(value, out var label) ? label : value.ToString();
        public static string SourceType(int value) => SourceTypes.TryGetValue(value, out var label) ? label : value.ToString();
        public static string Stage(int value) => Stages.TryGetValue(value, out var label) ? label : value.ToString();
        public static string Mode(int value) => Modes.TryGetValue(value, out var label) ? label : value.ToString();
        public static string ImageType(int value) => ImageTypes.TryGetValue(value, out var label) ? label : value.ToString();
    }
}
