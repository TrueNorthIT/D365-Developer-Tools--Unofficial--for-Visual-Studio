using System.Collections.Generic;

namespace D365_Developer_Tools__Unofficial__for_Visual_Studio.PluginDebugging
{
    /// <summary>Mirrors DebugHost's SandboxedWriteRecord — the two projects don't share a reference, so the wire shape (job.json + ".result.json") is duplicated deliberately on both sides rather than taking on a cross-project dependency for one small DTO.</summary>
    internal sealed class SandboxedWriteInfo
    {
        public string Operation { get; set; }
        public string EntityOrRequestName { get; set; }
        public string TimestampUtc { get; set; }
    }

    /// <summary>Mirrors DebugHost's ReplayResult — read back from "&lt;job.json&gt;.result.json" once PluginCaptureHostLauncher detects the host process has exited.</summary>
    internal sealed class ReplaySessionResult
    {
        public bool Succeeded { get; set; }
        public string FailureMessage { get; set; }
        public List<string> TraceMessages { get; set; } = new List<string>();
        public List<SandboxedWriteInfo> SandboxedWrites { get; set; } = new List<SandboxedWriteInfo>();
        public List<string> UndecodedProfileEntries { get; set; } = new List<string>();

        public static ReplaySessionResult Failed(string message) => new ReplaySessionResult { Succeeded = false, FailureMessage = message };
    }
}
