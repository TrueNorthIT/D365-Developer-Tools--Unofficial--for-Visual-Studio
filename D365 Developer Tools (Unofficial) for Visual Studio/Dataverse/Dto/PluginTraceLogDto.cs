namespace D365_Developer_Tools__Unofficial__for_Visual_Studio.Dataverse.Dto
{
    // ── Dataverse OData response shapes for plugin trace logs ───────────────

    internal sealed class PluginTraceLogDto
    {
        public string PluginTraceLogId { get; set; }
        public string TypeName { get; set; }
        public string MessageName { get; set; }
        public string PrimaryEntity { get; set; }
        public int? PerformanceExecutionDuration { get; set; }
        public string ExceptionDetails { get; set; }
        public string MessageBlock { get; set; }
        public System.DateTime? CreatedOn { get; set; }
        public string CorrelationId { get; set; }
        public int? Depth { get; set; }
        public int Mode { get; set; }
        public int OperationType { get; set; }

        /// <summary>Not a reliable signal for capture presence — see PluginTraceLogEntry.PersistenceKey's doc comment. Never $select the actual `profile` blob content here — this DTO backs the bounded list query, not the on-demand full-capture fetch.</summary>
        public string PersistenceKey { get; set; }
    }

    /// <summary>Just enough of the (singleton) organization record to read/write the org-wide tracing level.</summary>
    internal sealed class OrganizationTraceSettingDto
    {
        public string OrganizationId { get; set; }
        public int PluginTraceLogSetting { get; set; }
    }

    /// <summary>Wire shape for the on-demand full-capture fetch (GetPluginTraceLogCaptureAsync) — deliberately separate from PluginTraceLogDto, which never selects these two (potentially large) fields.</summary>
    internal sealed class PluginTraceLogCaptureDto
    {
        public string Profile { get; set; }
        public string SecureConfiguration { get; set; }
    }
}
