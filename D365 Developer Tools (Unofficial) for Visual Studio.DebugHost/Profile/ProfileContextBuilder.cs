using System;
using System.Collections.Generic;
using Microsoft.Xrm.Sdk;

namespace D365DeveloperTools.DebugHost.Profile
{
    /// <summary>Result of building a RemoteExecutionContext from a decoded profile — the context itself plus diagnostics for the two-tier graceful-degradation policy (see the Plugin Debugging Phase 2 plan).</summary>
    internal sealed class ProfileContextResult
    {
        public RemoteExecutionContext Context { get; set; }
        public List<string> TraceNotes { get; } = new List<string>();
        public List<string> UndecodedProfileEntries { get; } = new List<string>();
        public string FailureReason { get; set; }
        public bool Succeeded => FailureReason == null;
    }

    /// <summary>
    /// Turns the flat list of decoded profile entries into a populated RemoteExecutionContext. Only
    /// InputParameters (specifically Target — the one hard requirement of the degradation policy) and
    /// entity images are mapped with real confidence, both keyed off content rather than a type-code or
    /// declared name (the envelope has neither — see ProfileEnvelopeReader/NbfxEntryDecoder). Everything
    /// else (Stage, Depth, CorrelationId, OwningExtension, ...) has no confirmed mapping yet and is left
    /// at its default with a trace note — an incremental, empirical task per the plan, not blocking since
    /// replay only hard-requires Target.
    /// </summary>
    internal static class ProfileContextBuilder
    {
        public static ProfileContextResult Build(byte[] rawProfileBytes, string messageName, string primaryEntityName)
        {
            var result = new ProfileContextResult();
            var entries = ProfileEnvelopeReader.SplitEntries(rawProfileBytes, out var resyncNotes);
            result.TraceNotes.AddRange(resyncNotes);

            var context = new RemoteExecutionContext
            {
                MessageName = messageName,
                PrimaryEntityName = primaryEntityName,
            };

            var targetFound = false;
            var sharedVariablesMerged = false;

            foreach (var entry in entries)
            {
                var decoded = NbfxEntryDecoder.TryDecode(entry);
                var entryLabel = entry.IsBare ? $"bare@{entry.EntryStart}" : entry.Id.ToString();

                if (!decoded.Succeeded)
                {
                    result.UndecodedProfileEntries.Add(entryLabel);
                    result.TraceNotes.Add($"Entry {entryLabel}: {decoded.FailureReason}");
                    continue;
                }

                switch (decoded.Value)
                {
                    case ParameterCollection parameters:
                        if (parameters.Contains("Target"))
                        {
                            MergeInto(context.InputParameters, parameters);
                            targetFound = true;
                        }
                        else if (!sharedVariablesMerged)
                        {
                            MergeInto(context.SharedVariables, parameters);
                            sharedVariablesMerged = true;
                        }
                        else
                        {
                            // A third ParameterCollection with no Target — no confirmed slot for it yet
                            // (OutputParameters is unlikely to be present in a pre-execution capture).
                            result.UndecodedProfileEntries.Add(entryLabel);
                            result.TraceNotes.Add($"Entry {entryLabel}: an extra ParameterCollection (no 'Target' key, and SharedVariables already assigned) — left unmapped.");
                        }

                        break;

                    case EntityImageCollection images:
                        foreach (var image in images)
                        {
                            if (image.Key.IndexOf("post", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                context.PostEntityImages[image.Key] = image.Value;
                            }
                            else if (image.Key.IndexOf("pre", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                context.PreEntityImages[image.Key] = image.Value;
                            }
                            else
                            {
                                // Naming convention heuristic (no other signal available) — pre-images are
                                // by far the more common capture, so default there rather than drop it.
                                context.PreEntityImages[image.Key] = image.Value;
                                result.TraceNotes.Add($"Entry {entryLabel}: image '{image.Key}' didn't match a pre/post naming convention — defaulted to PreEntityImages.");
                            }
                        }

                        break;

                    default:
                        // EntityReference, ArrayOfEntityImageCollection, and anything not yet in
                        // NbfxEntryDecoder's KnownTypes — no confirmed RemoteExecutionContext slot yet.
                        result.UndecodedProfileEntries.Add(entryLabel);
                        result.TraceNotes.Add($"Entry {entryLabel}: decoded as '{decoded.TypeName}' but has no confirmed mapping onto RemoteExecutionContext yet — left unmapped.");
                        break;
                }
            }

            if (!targetFound)
            {
                result.FailureReason = "Could not find InputParameters[\"Target\"] in the captured profile — replay requires it (see the two-tier graceful-degradation policy in the Plugin Debugging Phase 2 plan).";
                return result;
            }

            result.Context = context;
            return result;
        }

        private static void MergeInto(ParameterCollection destination, ParameterCollection source)
        {
            foreach (var pair in source)
            {
                destination[pair.Key] = pair.Value;
            }
        }
    }
}
