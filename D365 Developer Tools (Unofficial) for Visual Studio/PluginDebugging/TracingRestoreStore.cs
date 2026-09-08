using System.IO;
using D365_Developer_Tools__Unofficial__for_Visual_Studio.Dataverse;
using D365_Developer_Tools__Unofficial__for_Visual_Studio.Persistence;

namespace D365_Developer_Tools__Unofficial__for_Visual_Studio.PluginDebugging
{
    internal sealed class TracingRestoreEntry
    {
        public PluginTraceLogSetting PriorSetting { get; set; }
    }

    /// <summary>
    /// Remembers, per environment, the org's tracing level from just before "Debug This Step..."
    /// switched it on to arm a capture — so it can be offered back afterward rather than silently left
    /// on. Keyed by environment URL, matching ConnectionManager's GetDefaultSolution/SetDefaultSolution
    /// convention. A stored entry is cleared once it's been offered back (accepted or declined) so a
    /// stale prior value doesn't linger and get re-offered on an unrelated later session.
    /// </summary>
    internal static class TracingRestoreStore
    {
        private static string PathFor(string environmentUrl) =>
            Path.Combine(JsonFileStore.RootDirectory, "tracing-restore", JsonFileStore.HashKey(environmentUrl) + ".json");

        public static PluginTraceLogSetting? TryGetPriorSetting(string environmentUrl) =>
            JsonFileStore.Load<TracingRestoreEntry>(PathFor(environmentUrl))?.PriorSetting;

        public static void SetPriorSetting(string environmentUrl, PluginTraceLogSetting setting) =>
            JsonFileStore.Save(PathFor(environmentUrl), new TracingRestoreEntry { PriorSetting = setting });

        public static void Clear(string environmentUrl) =>
            JsonFileStore.Delete(PathFor(environmentUrl));
    }
}
