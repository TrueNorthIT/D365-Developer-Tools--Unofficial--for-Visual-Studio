using System;
using System.Collections.Generic;

namespace D365_Developer_Tools__Unofficial__for_Visual_Studio.Dataverse
{
    /// <summary>A single plugintracelog record — one execution of a plugin/workflow step.</summary>
    internal sealed class PluginTraceLogEntry
    {
        public string TraceLogId { get; set; }
        public string TypeName { get; set; }
        public string MessageName { get; set; }
        public string PrimaryEntity { get; set; }
        public int? PerformanceExecutionDuration { get; set; }
        public string ExceptionDetails { get; set; }
        public string MessageBlock { get; set; }
        public DateTime? CreatedOn { get; set; }
        public string CorrelationId { get; set; }
        public int? Depth { get; set; }
        public int ModeValue { get; set; }
        public string Mode { get; set; }
        public int OperationTypeValue { get; set; }
        public string OperationType { get; set; }

        public bool HasException => !string.IsNullOrEmpty(ExceptionDetails);

        /// <summary>
        /// PersistenceKey ties together the pre/post trace rows the official Plugin Registration Tool's
        /// separate Profiler solution produces, when that solution is installed and used. It is NOT a
        /// reliable signal for "does this row have a replayable capture" — confirmed against a live
        /// environment where real, fully-populated `profile` captures had a null PersistenceKey (that
        /// environment has no Profiler solution installed at all; Dataverse populates `profile`
        /// automatically for every execution once plugintracelogsetting is "All", independent of the
        /// Profiler solution). Use PluginTraceLogFilter.HasCapturedProfile / DataverseClient's
        /// $filter=profile ne null to find rows with a capture — never this property for that purpose.
        /// HasProfilingData is kept only as a display hint, not a gate on any "Debug This" action.
        /// </summary>
        public string PersistenceKey { get; set; }
        public bool HasProfilingData { get; set; }
    }

    /// <summary>
    /// Structured filter for querying plugintracelogs — sent to Dataverse as an OData $filter, unlike
    /// free-text search which only narrows the already-loaded page client-side.
    ///
    /// plugintracelog has no lookup back to the sdkmessageprocessingstep that produced it (confirmed
    /// against a live environment: Web API 400s with "Could not find a property named
    /// '_pluginstepid_value'" — there is no such attribute). Plugin Explorer's "View Trace Logs..."
    /// therefore scopes by TypeName alone (see PluginDebuggingViewModel.LoadForStepAsync) — AND-ing in
    /// MessageName/PrimaryEntity too turned out too strict in practice.
    ///
    /// TypeName itself is matched against plugintracelog.typename by DataverseClient using startswith,
    /// not eq — also confirmed against a live environment: plugintracelog.typename holds the
    /// *assembly-qualified* type name ("Namespace.Class, AssemblyName, Version=..., Culture=...,
    /// PublicKeyToken=..."), while this property (and plugintype.typename, what Plugin Explorer
    /// surfaces) is just the bare class name — an eq match against the bare name can never succeed.
    ///
    /// MessageName/PrimaryEntity remain available as ordinary, independent, user-typed filters.
    /// </summary>
    internal sealed class PluginTraceLogFilter
    {
        public string TypeName { get; set; }
        public string PrimaryEntity { get; set; }
        public string MessageName { get; set; }
        public string CorrelationId { get; set; }
        public DateTime? From { get; set; }
        public DateTime? To { get; set; }
        public bool ExceptionsOnly { get; set; }

        /// <summary>Narrows to rows with a replayable capture (plugintracelog.profile ne null) — the corrected, verified signal for "can this row be debugged," used instead of PersistenceKey/HasProfilingData.</summary>
        public bool HasCapturedProfile { get; set; }

        /// <summary>Bounded page size — trace log tables can be huge, so this is never followed via unbounded @odata.nextLink paging (see DataverseClient.GetPluginTraceLogsAsync).</summary>
        public int Top { get; set; } = 200;
    }

    /// <summary>One bounded, newest-first page of trace logs. NextLink is retained for a future "Load more" — not wired into the v1 UI.</summary>
    internal sealed class PluginTraceLogPage
    {
        public List<PluginTraceLogEntry> Items { get; set; }
        public string NextLink { get; set; }
    }

    /// <summary>Dataverse's plugintracelogsetting org-level option set — controls whether plugin executions are recorded at all.</summary>
    internal enum PluginTraceLogSetting
    {
        Off = 0,
        Exception = 1,
        All = 2,
    }

    internal sealed class PluginTraceLogSettingsInfo
    {
        public string OrganizationId { get; set; }
        public PluginTraceLogSetting Setting { get; set; }
    }

    /// <summary>
    /// The full captured execution data for one trace log row — fetched only on demand (e.g. when the
    /// user picks "Debug This"), never as part of the bounded list query in GetPluginTraceLogsAsync or
    /// its auto-refresh polling, since `profile` blobs run several KB each and pulling that for every
    /// row on every poll would reintroduce the exact jitter/bandwidth problem already fixed once there.
    /// </summary>
    internal sealed class PluginTraceLogCapture
    {
        /// <summary>Base64, MC-NBFX-encoded (a public Microsoft Open Specification, the WCF binary-XML wire format) — decoded by the debug host process, never by the main extension.</summary>
        public string ProfileBase64 { get; set; }

        /// <summary>Null when this capture has no secure configuration recorded.</summary>
        public string SecureConfiguration { get; set; }
    }
}
