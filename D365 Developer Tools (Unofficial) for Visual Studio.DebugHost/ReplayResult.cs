using System.Collections.Generic;

namespace D365DeveloperTools.DebugHost
{
    internal sealed class SandboxedWriteRecord
    {
        public string Operation { get; set; }
        public string EntityOrRequestName { get; set; }
        public string TimestampUtc { get; set; }
    }

    /// <summary>
    /// Written to job.json + ".result.json" once this process is done (success or failure) — the main
    /// extension reads it after detecting the process has exited and surfaces it in the Plugin
    /// Debugging tool window.
    /// </summary>
    internal sealed class ReplayResult
    {
        public bool Succeeded { get; set; }
        public string FailureMessage { get; set; }
        public List<string> TraceMessages { get; set; } = new List<string>();
        public List<SandboxedWriteRecord> SandboxedWrites { get; set; } = new List<SandboxedWriteRecord>();

        /// <summary>GUID ids of profile entries that could not be decoded/mapped — see the two-tier graceful-degradation policy in the Plugin Debugging Phase 2 plan.</summary>
        public List<string> UndecodedProfileEntries { get; set; } = new List<string>();

        public static ReplayResult Failed(string message) => new ReplayResult { Succeeded = false, FailureMessage = message };
    }
}
