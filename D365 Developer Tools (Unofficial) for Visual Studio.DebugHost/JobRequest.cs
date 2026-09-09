using System;

namespace D365DeveloperTools.DebugHost
{
    /// <summary>
    /// The handoff contract from the main extension — read from job.json, whose path is this process's
    /// one command-line argument. Not command-line args themselves: a bearer token and a multi-KB
    /// profile blob don't belong on a command line visible to every other process on the box.
    /// </summary>
    internal sealed class JobRequest
    {
        public int SchemaVersion { get; set; }
        public string SessionId { get; set; }
        public string PluginTypeName { get; set; }
        public string MessageName { get; set; }
        public string PrimaryEntity { get; set; }

        /// <summary>The developer's local Debug-configuration build output — not the assembly deployed to Dataverse.</summary>
        public string AssemblyPath { get; set; }

        /// <summary>Raw (still MC-NBFX-encoded) bytes of the captured plugintracelog.profile field.</summary>
        public string ProfileFilePath { get; set; }

        /// <summary>Null when this capture had no secure configuration recorded.</summary>
        public string SecureConfigFilePath { get; set; }

        public string EnvironmentUrl { get; set; }

        /// <summary>Short-lived — a fresh process per debug session means no refresh channel is needed for v1 (see the Plugin Debugging Phase 2 plan's "Deferred" section).</summary>
        public string AccessToken { get; set; }
        public DateTime AccessTokenExpiresOnUtc { get; set; }
    }
}
