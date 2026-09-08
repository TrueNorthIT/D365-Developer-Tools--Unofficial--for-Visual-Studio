using System;
using System.Collections.Generic;

namespace D365DeveloperTools.DebugHost.Replay
{
    /// <summary>
    /// Classifies OrganizationRequest types as read (proxied live to the real environment) or write
    /// (sandboxed — no-op'd and recorded, never sent). No official Microsoft.Xrm.Sdk metadata
    /// distinguishes this (confirmed during the Plugin Debugging Phase 2 design pass), so classification
    /// is by request-type-name convention: default-deny (treat anything unrecognized as a write) is the
    /// safer direction — an unknown request wrongly sandboxed just means a plugin's write silently
    /// no-ops during a debug session; an unknown request wrongly let through live could mutate real data.
    /// </summary>
    internal static class WriteRequestClassifier
    {
        /// <summary>Request-type-name prefixes that are always reads, regardless of what follows.</summary>
        private static readonly string[] ReadPrefixes =
        {
            "Retrieve", // RetrieveRequest, RetrieveMultipleRequest, RetrieveEntityRequest, RetrieveAttributeRequest, RetrieveVersionRequest, etc.
        };

        /// <summary>Specific request-type names that are reads despite not matching a read prefix.</summary>
        private static readonly HashSet<string> ExplicitReadNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "WhoAmIRequest",
            "IsDataEncryptionActiveRequest",
            "CanBeReferencedRequest",
            "CanBeReferencingRequest",
        };

        /// <summary>
        /// Specific request-type names that DO match a read prefix (or otherwise look read-shaped) but
        /// actually write — checked first, so these are never misclassified as reads by the prefix rule.
        /// </summary>
        private static readonly HashSet<string> ExplicitWriteNames = new HashSet<string>(StringComparer.Ordinal)
        {
        };

        /// <summary>True if this request type should be sandboxed (no-op'd) rather than proxied live. ExecuteMultipleRequest is handled specially by the caller (recurse and classify each inner request) — this method is never asked to classify it directly.</summary>
        public static bool IsWrite(string requestTypeName)
        {
            if (ExplicitWriteNames.Contains(requestTypeName)) { return true; }
            if (ExplicitReadNames.Contains(requestTypeName)) { return false; }

            foreach (var prefix in ReadPrefixes)
            {
                if (requestTypeName.StartsWith(prefix, StringComparison.Ordinal)) { return false; }
            }

            // Default-deny: anything not explicitly recognized as a read is sandboxed.
            return true;
        }
    }
}
