using System;
using System.IO;
using D365_Developer_Tools__Unofficial__for_Visual_Studio.Persistence;

namespace D365_Developer_Tools__Unofficial__for_Visual_Studio.PluginDebugging
{
    internal sealed class ProfilingSessionEntry
    {
        public DateTime StartedAtUtc { get; set; }
    }

    /// <summary>
    /// Remembers, per original step id, that "Start Profiling..." has been used on it and when. The real
    /// arming is server-side (DataverseClient.SetStepProfilingEnabledAsync flips the step's own genuine
    /// "enablepluginprofiler" flag) — this store is a local mirror of that, so the Start/Stop menu toggle
    /// and tree indicator don't need a live server round-trip on every context menu open, and so the
    /// Profile Captures tab knows "since when" to scope its query (filtered by TypeName + this
    /// timestamp). Same JSON-file-per-key pattern as DebugTargetStore. Can go stale if the flag is
    /// toggled from somewhere else (e.g. the Plugin Registration Tool) — refreshed from
    /// StartProfilingCommand/StopProfilingCommand's own round-trips, not polled independently.
    /// </summary>
    internal static class ProfilingSessionStore
    {
        private static string PathFor(string originalStepId) =>
            Path.Combine(JsonFileStore.RootDirectory, "profiling-sessions", JsonFileStore.HashKey(originalStepId) + ".json");

        public static ProfilingSessionEntry TryGet(string originalStepId) =>
            JsonFileStore.Load<ProfilingSessionEntry>(PathFor(originalStepId));

        public static void Set(string originalStepId, ProfilingSessionEntry entry) =>
            JsonFileStore.Save(PathFor(originalStepId), entry);

        public static void Clear(string originalStepId) =>
            JsonFileStore.Delete(PathFor(originalStepId));
    }
}
