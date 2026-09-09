using System.IO;
using D365_Developer_Tools__Unofficial__for_Visual_Studio.Persistence;

namespace D365_Developer_Tools__Unofficial__for_Visual_Studio.PluginDebugging
{
    internal sealed class DebugTargetEntry
    {
        public string ProjectFilePath { get; set; }
    }

    /// <summary>
    /// Remembers, per plugin type name (the bare class name — same key space plugin type nodes use, the
    /// reverse direction from PublishSettingsStore which keys by project path), which local project's
    /// Debug-configuration build output "Debug This" should load. Auto-populated by
    /// PublishToDataverseCommand whenever it discovers plugin types in a project it just published, so
    /// anything already published through this extension needs no extra prompting; anything else falls
    /// back to a one-time project picker that gets remembered here afterward.
    /// </summary>
    internal static class DebugTargetStore
    {
        private static string PathFor(string pluginTypeName) =>
            Path.Combine(JsonFileStore.RootDirectory, "debug-targets", JsonFileStore.HashKey(pluginTypeName) + ".json");

        public static string TryGetProjectPath(string pluginTypeName) =>
            JsonFileStore.Load<DebugTargetEntry>(PathFor(pluginTypeName))?.ProjectFilePath;

        public static void SetProjectPath(string pluginTypeName, string projectFilePath) =>
            JsonFileStore.Save(PathFor(pluginTypeName), new DebugTargetEntry { ProjectFilePath = projectFilePath });
    }
}
