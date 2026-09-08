using System;

namespace D365_Developer_Tools__Unofficial__for_Visual_Studio.PluginDebugging
{
    /// <summary>
    /// Everything PluginCaptureHostLauncher needs to start a replay session — assembled by
    /// DebugPluginCaptureCommand/DebugStepCommand from a built project, a fetched PluginTraceLogCapture,
    /// and the active connection, before the launcher ever touches disk or a process.
    /// </summary>
    internal sealed class ResolvedPluginCapture
    {
        public string PluginTypeName { get; set; }
        public string MessageName { get; set; }
        public string PrimaryEntity { get; set; }

        /// <summary>The developer's local Debug-configuration build output.</summary>
        public string AssemblyPath { get; set; }

        /// <summary>Base64, MC-NBFX-encoded plugintracelog.profile — written to profile.bin in the session folder, decoded only by the debug host process.</summary>
        public string ProfileBase64 { get; set; }

        /// <summary>Null when this capture had no secure configuration recorded.</summary>
        public string SecureConfiguration { get; set; }

        public string EnvironmentUrl { get; set; }
        public string AccessToken { get; set; }
        public DateTime AccessTokenExpiresOnUtc { get; set; }
    }
}
