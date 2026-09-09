using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using D365_Developer_Tools__Unofficial__for_Visual_Studio.Commands;
using D365_Developer_Tools__Unofficial__for_Visual_Studio.Connection;
using D365_Developer_Tools__Unofficial__for_Visual_Studio.Dataverse;
using D365_Developer_Tools__Unofficial__for_Visual_Studio.PluginDebugging;
using D365_Developer_Tools__Unofficial__for_Visual_Studio.Shared;
using D365_Developer_Tools__Unofficial__for_Visual_Studio.Shared.Mvvm;
using Microsoft.VisualStudio.Shell;

namespace D365_Developer_Tools__Unofficial__for_Visual_Studio.ToolWindows.ViewModels
{
    /// <summary>
    /// Backs PluginDebuggingControl — two tabs: "Trace Logs" (the plain plugintracelog viewer) and
    /// "Profile Captures" (the same plugintracelog table, scoped to one step's Start/Stop Profiling
    /// session and narrowed to rows with a captured profile — where "Debug This" actually lives). Both
    /// tabs, and the header's trace-logging-level dropdown, sit on top of the one native Dataverse
    /// mechanism: plugintracelog.profile, populated automatically for every execution once
    /// plugintracelogsetting is "All" — no installable solution, no custom entity, no per-step
    /// registration change of any kind.
    /// </summary>
    internal sealed class PluginDebuggingViewModel : ObservableObject
    {
        private readonly ConnectionManager _connectionManager;
        private readonly DataverseClient _client;
        private readonly IUserPrompts _prompts;
        private readonly DispatcherTimer _autoRefreshTimer;

        public ObservableCollection<PluginTraceLogEntry> Entries { get; } = new ObservableCollection<PluginTraceLogEntry>();
        public ICollectionView EntriesView { get; }

        private bool _isConnected;
        public bool IsConnected { get => _isConnected; private set => SetProperty(ref _isConnected, value); }

        private bool _isRestoring;
        public bool IsRestoring { get => _isRestoring; private set => SetProperty(ref _isRestoring, value); }

        private string _statusText = "Not Connected";
        public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }

        private bool _isLoading;
        public bool IsLoading { get => _isLoading; private set => SetProperty(ref _isLoading, value); }

        private string _loadError;
        public string LoadError { get => _loadError; private set => SetProperty(ref _loadError, value); }

        /// <summary>
        /// Diagnostic aid while the plugintracelog schema's exact field shapes are still being verified
        /// against live environments — shows the filter actually sent and how many rows came back, so
        /// an unexpectedly-empty result can be checked without a debugger or VS's /log switch.
        /// </summary>
        private string _lastQueryDescription;
        public string LastQueryDescription { get => _lastQueryDescription; private set => SetProperty(ref _lastQueryDescription, value); }

        private int _selectedTabIndex;

        /// <summary>0 = Trace Logs, 1 = Profile Captures.</summary>
        public int SelectedTabIndex { get => _selectedTabIndex; set => SetProperty(ref _selectedTabIndex, value); }

        // ── Trace Logs tab ───────────────────────────────────────────────────────────────────────────

        // Full properties (not plain auto-properties) so LoadForStepAsync's programmatic prefill
        // below actually notifies the bound TextBoxes, not just user typing.
        private string _entityFilter = string.Empty;
        public string EntityFilter { get => _entityFilter; set => SetProperty(ref _entityFilter, value); }

        private string _messageFilter = string.Empty;
        public string MessageFilter { get => _messageFilter; set => SetProperty(ref _messageFilter, value); }

        private bool _exceptionsOnly;
        public bool ExceptionsOnly
        {
            get => _exceptionsOnly;
            set
            {
                if (SetProperty(ref _exceptionsOnly, value)) { RefreshAsync().FileAndForget("D365DeveloperTools/RefreshTraceLogsAfterExceptionsOnlyToggle"); }
            }
        }

        private DateTime? _fromDate;
        public DateTime? FromDate
        {
            get => _fromDate;
            set
            {
                if (SetProperty(ref _fromDate, value)) { RefreshAsync().FileAndForget("D365DeveloperTools/RefreshTraceLogsAfterFromDateChange"); }
            }
        }

        private DateTime? _toDate;
        public DateTime? ToDate
        {
            get => _toDate;
            set
            {
                if (SetProperty(ref _toDate, value)) { RefreshAsync().FileAndForget("D365DeveloperTools/RefreshTraceLogsAfterToDateChange"); }
            }
        }

        /// <summary>
        /// Free-text "HH:mm:ss" companion to FromDate/ToDate — DatePicker.SelectedDate only ever carries
        /// a calendar date (time defaults to midnight), so without this the actual query's From/To bound
        /// would silently always be start-of-day, with no way to narrow to a specific moment. Blank falls
        /// back to start of day (From) / end of day (To) — see CombineDateAndTime.
        /// </summary>
        private string _fromTimeText = string.Empty;
        public string FromTimeText
        {
            get => _fromTimeText;
            set
            {
                if (SetProperty(ref _fromTimeText, value)) { RefreshAsync().FileAndForget("D365DeveloperTools/RefreshTraceLogsAfterFromTimeChange"); }
            }
        }

        private string _toTimeText = string.Empty;
        public string ToTimeText
        {
            get => _toTimeText;
            set
            {
                if (SetProperty(ref _toTimeText, value)) { RefreshAsync().FileAndForget("D365DeveloperTools/RefreshTraceLogsAfterToTimeChange"); }
            }
        }

        private string _searchText = string.Empty;
        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value)) { EntriesView.Refresh(); }
            }
        }

        /// <summary>
        /// The plugin type name (fully qualified) to scope to when opened via Plugin Explorer's "View
        /// Trace Logs..." — not user-editable (there's no textbox for it), applied silently alongside
        /// EntityFilter/MessageFilter in RefreshAsync. See PluginTraceLogFilter's doc comment for why
        /// this trio, rather than a step ID, is how plugintracelog can be scoped to one step at all.
        /// </summary>
        private string _stepFilterTypeName;

        private string _stepFilterLabel;
        public string StepFilterLabel { get => _stepFilterLabel; private set => SetProperty(ref _stepFilterLabel, value); }

        public bool HasStepFilter => _stepFilterTypeName != null;

        private PluginTraceLogEntry _selectedEntry;
        public PluginTraceLogEntry SelectedEntry { get => _selectedEntry; set => SetProperty(ref _selectedEntry, value); }

        private PluginTraceLogSettingsInfo _tracingSetting;
        public PluginTraceLogSettingsInfo TracingSetting { get => _tracingSetting; private set => SetProperty(ref _tracingSetting, value); }

        public bool IsTracingOff => TracingSetting?.Setting == PluginTraceLogSetting.Off;

        /// <summary>The three raw plugintracelogsetting values, for the header's tracing-level dropdown — bound directly, no separate label wrapper needed since the enum's own names ("Off"/"Exception"/"All") are exactly what should be shown.</summary>
        public IReadOnlyList<PluginTraceLogSetting> TracingLevelOptions { get; } = new[] { PluginTraceLogSetting.Off, PluginTraceLogSetting.Exception, PluginTraceLogSetting.All };

        /// <summary>
        /// The org's current trace-logging level, settable directly from the dropdown — a manual control
        /// the user drives themselves. Deliberately never changed automatically by Start Profiling: an
        /// earlier iteration auto-set it to All and auto-restored the prior value afterward, but that
        /// turned out more surprising than useful (Exception is a legitimate, deliberate choice — e.g.
        /// only capturing executions that throw — not something to silently override).
        /// </summary>
        public PluginTraceLogSetting? SelectedTracingLevel
        {
            get => TracingSetting?.Setting;
            set
            {
                if (value == null || TracingSetting == null || value == TracingSetting.Setting) { return; }
                SetTracingLevelAsync(value.Value).FileAndForget("D365DeveloperTools/SetTracingLevel");
            }
        }

        private bool _isAutoRefreshEnabled;
        public bool IsAutoRefreshEnabled
        {
            get => _isAutoRefreshEnabled;
            set
            {
                if (!SetProperty(ref _isAutoRefreshEnabled, value)) { return; }
                if (value) { _autoRefreshTimer.Start(); } else { _autoRefreshTimer.Stop(); }
            }
        }

        // ── Profile Captures tab ─────────────────────────────────────────────────────────────────────

        public ObservableCollection<PluginTraceLogEntry> ProfileEntries { get; } = new ObservableCollection<PluginTraceLogEntry>();

        private PluginTraceLogEntry _selectedProfileEntry;
        public PluginTraceLogEntry SelectedProfileEntry { get => _selectedProfileEntry; set => SetProperty(ref _selectedProfileEntry, value); }

        /// <summary>The original step id currently scoped, if profiling was armed from Plugin Explorer — needed so "Stop Profiling" from this tab knows which step to stop.</summary>
        private string _profiledStepId;
        private string _profiledStepTypeName;

        private DateTime? _profileFromDate;

        /// <summary>Defaults to the moment profiling was armed (see LoadForProfiledStepAsync), but freely adjustable afterward — e.g. to widen the window and see captures from before this session, or narrow it. Not a hard floor like the old fixed session-start cutoff was.</summary>
        public DateTime? ProfileFromDate
        {
            get => _profileFromDate;
            set
            {
                if (SetProperty(ref _profileFromDate, value)) { RefreshProfileCapturesAsync().FileAndForget("D365DeveloperTools/RefreshProfileCapturesAfterFromDateChange"); }
            }
        }

        private DateTime? _profileToDate;
        public DateTime? ProfileToDate
        {
            get => _profileToDate;
            set
            {
                if (SetProperty(ref _profileToDate, value)) { RefreshProfileCapturesAsync().FileAndForget("D365DeveloperTools/RefreshProfileCapturesAfterToDateChange"); }
            }
        }

        /// <summary>
        /// Free-text "HH:mm:ss" companion to ProfileFromDate/ProfileToDate — LoadForProfiledStepAsync
        /// seeds this with the exact time-of-day profiling was armed, which a bare DatePicker would
        /// otherwise carry silently without ever showing it. Blank falls back to start of day (From) /
        /// end of day (To) — see CombineDateAndTime.
        /// </summary>
        private string _profileFromTimeText = string.Empty;
        public string ProfileFromTimeText
        {
            get => _profileFromTimeText;
            set
            {
                if (SetProperty(ref _profileFromTimeText, value)) { RefreshProfileCapturesAsync().FileAndForget("D365DeveloperTools/RefreshProfileCapturesAfterFromTimeChange"); }
            }
        }

        private string _profileToTimeText = string.Empty;
        public string ProfileToTimeText
        {
            get => _profileToTimeText;
            set
            {
                if (SetProperty(ref _profileToTimeText, value)) { RefreshProfileCapturesAsync().FileAndForget("D365DeveloperTools/RefreshProfileCapturesAfterToTimeChange"); }
            }
        }

        private bool _hasActiveProfilingSession;
        public bool HasActiveProfilingSession { get => _hasActiveProfilingSession; private set => SetProperty(ref _hasActiveProfilingSession, value); }

        private string _activeProfilingSessionLabel;
        public string ActiveProfilingSessionLabel { get => _activeProfilingSessionLabel; private set => SetProperty(ref _activeProfilingSessionLabel, value); }

        private DebugSessionInfo _debugSession;

        /// <summary>Non-null for the lifetime of one "Debug This" run — backs PluginDebuggingControl's non-modal session status strip.</summary>
        public DebugSessionInfo DebugSession { get => _debugSession; private set => SetProperty(ref _debugSession, value); }

        private System.Diagnostics.Process _debugProcess;

        public ICommand ConnectCommand { get; }
        public ICommand RefreshCommand { get; }
        public ICommand ShowMenuCommand { get; }
        public ICommand ClearStepFilterCommand { get; }
        public ICommand DebugCommand { get; }
        public ICommand StopDebugSessionCommand { get; }
        public ICommand RefreshProfileCapturesCommand { get; }
        public ICommand StopProfilingSessionCommand { get; }

        public PluginDebuggingViewModel(ConnectionManager connectionManager, DataverseClient client, IUserPrompts prompts)
        {
            _connectionManager = connectionManager;
            _client = client;
            _prompts = prompts;

            EntriesView = CollectionViewSource.GetDefaultView(Entries);
            EntriesView.Filter = FilterEntry;

            // First DispatcherTimer used anywhere in this extension — must be stopped explicitly
            // (disconnect below, and PluginDebuggingToolWindow.Dispose) since nothing else here does
            // that automatically.
            _autoRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _autoRefreshTimer.Tick += (_, __) => RefreshAsync(showLoadingIndicator: false).FileAndForget("D365DeveloperTools/AutoRefreshTraceLogs");

            ConnectCommand = new AsyncRelayCommand(() => _connectionManager.ConnectAsync());
            RefreshCommand = new AsyncRelayCommand(() => RefreshAsync());
            ShowMenuCommand = new AsyncRelayCommand(() => ConnectionMenu.ShowAsync(_connectionManager, _prompts));
            ClearStepFilterCommand = new RelayCommand(_ => ClearStepFilter());
            DebugCommand = new AsyncRelayCommand(
                () => DebugPluginCaptureCommand.ExecuteAsync(SelectedProfileEntry),
                () => SelectedProfileEntry != null && DebugSession == null);
            StopDebugSessionCommand = new RelayCommand(_ => StopDebugSession(), _ => DebugSession != null);
            RefreshProfileCapturesCommand = new AsyncRelayCommand(() => RefreshProfileCapturesAsync());
            StopProfilingSessionCommand = new AsyncRelayCommand(
                () => StopProfilingCommand.ExecuteAsync(_profiledStepId),
                () => HasActiveProfilingSession);

            // See EntityExplorerViewModel's constructor for why this hops to the UI thread first.
            _connectionManager.ConnectionChanged += (_, __) =>
                ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    UpdateConnectionState();
                }).Task.FileAndForget("D365DeveloperTools/OnPluginDebuggingConnectionChanged");

            UpdateConnectionState();
        }

        private void UpdateConnectionState()
        {
            IsRestoring = _connectionManager.IsRestoring;
            IsConnected = _connectionManager.IsConnected;
            StatusText = IsRestoring
                ? "Reconnecting…"
                : IsConnected
                    ? $"Connected: {ConnectionMenu.FriendlyName(_connectionManager.Connection.EnvironmentUrl)}"
                    : "Not Connected";

            if (IsConnected)
            {
                CheckTracingSettingAsync().FileAndForget("D365DeveloperTools/CheckPluginTraceLogSetting");
                RefreshAsync().FileAndForget("D365DeveloperTools/RefreshPluginTraceLogs");
                if (_profiledStepTypeName != null) { RefreshProfileCapturesAsync().FileAndForget("D365DeveloperTools/RefreshProfileCapturesOnConnect"); }
            }
            else
            {
                Entries.Clear();
                ProfileEntries.Clear();
                TracingSetting = null;
                StopAutoRefresh();
            }
        }

        /// <summary>
        /// Scopes the Trace Logs tab to one plugin type (e.g. from Plugin Explorer's "View Trace
        /// Logs..." command) and refreshes. Only TypeName is applied to the query — messageName/
        /// primaryEntity are shown in stepLabel for context but deliberately NOT written into
        /// EntityFilter/MessageFilter, since AND-ing all three turned out too strict in practice (a
        /// single mismatched field, e.g. a primaryentity that isn't recorded exactly the way the step's
        /// own entity filter is, silently zeroes the whole result). TypeName alone is the field this
        /// feature has the most confidence in, and the user can still narrow further by hand using the
        /// regular Entity/Message boxes plus Refresh if they want tighter matching.
        /// </summary>
        public async Task LoadForStepAsync(string pluginTypeName, string stepLabel)
        {
            _stepFilterTypeName = pluginTypeName;
            StepFilterLabel = stepLabel;
            OnPropertyChanged(nameof(HasStepFilter));
            SelectedTabIndex = 0;
            await RefreshAsync().ConfigureAwait(true);
        }

        /// <summary>
        /// Scopes the Profile Captures tab to one just-armed profiling session and switches to it —
        /// called by StartProfilingCommand right after arming. sessionStartedAtUtc seeds ProfileFromDate
        /// as a sensible default (so the tab starts out showing "since I armed this"), but it's just a
        /// starting value the user can freely widen or narrow afterward via the From/To pickers — not a
        /// hard floor baked into every query the way a fixed session-start cutoff used to be.
        /// </summary>
        public async Task LoadForProfiledStepAsync(string originalStepId, string pluginTypeName, DateTime sessionStartedAtUtc, string stepLabel)
        {
            _profiledStepId = originalStepId;
            _profiledStepTypeName = pluginTypeName;
            ActiveProfilingSessionLabel = stepLabel;
            HasActiveProfilingSession = true;
            SelectedTabIndex = 1;

            // Set the backing fields directly (not the ProfileFromDate/ProfileToDate/*TimeText properties)
            // so this doesn't trigger several redundant refreshes back to back — the explicit call below
            // covers it. The time text is seeded from the exact same instant as the date, so the user
            // sees the real armed-at moment (down to the second) rather than it silently defaulting to
            // midnight the way a bare DatePicker-bound DateTime would otherwise display.
            _profileFromDate = sessionStartedAtUtc;
            _profileFromTimeText = sessionStartedAtUtc.ToString("HH:mm:ss");
            _profileToDate = null;
            _profileToTimeText = string.Empty;
            OnPropertyChanged(nameof(ProfileFromDate));
            OnPropertyChanged(nameof(ProfileFromTimeText));
            OnPropertyChanged(nameof(ProfileToDate));
            OnPropertyChanged(nameof(ProfileToTimeText));

            await RefreshProfileCapturesAsync().ConfigureAwait(true);
        }

        /// <summary>Called by Commands.StopProfilingCommand once a step's session is forgotten — already-captured rows stay listed and debuggable.</summary>
        public void OnProfilingStopped()
        {
            HasActiveProfilingSession = false;
            ActiveProfilingSessionLabel = null;
        }

        public void ClearStepFilter()
        {
            _stepFilterTypeName = null;
            StepFilterLabel = null;
            OnPropertyChanged(nameof(HasStepFilter));
            RefreshAsync().FileAndForget("D365DeveloperTools/RefreshTraceLogsAfterClearStepFilter");
        }

        /// <summary>
        /// showLoadingIndicator is false for auto-refresh's silent background polls — toggling
        /// IsLoading (and so the "Loading…" text) on every 5s tick, even when nothing changed, shifted
        /// the DataGrid up and down every time (that text row is Auto-height, sitting above it), which
        /// is exactly the jitter auto-refresh was supposed to avoid causing. Manual refreshes/filter
        /// changes still show it, since those are deliberate user actions worth acknowledging.
        /// </summary>
        public async Task RefreshAsync(bool showLoadingIndicator = true)
        {
            if (!IsConnected) { return; }

            if (showLoadingIndicator) { IsLoading = true; }
            LoadError = null;
            try
            {
                var filter = new PluginTraceLogFilter
                {
                    TypeName = _stepFilterTypeName,
                    PrimaryEntity = string.IsNullOrWhiteSpace(EntityFilter) ? null : EntityFilter.Trim(),
                    MessageName = string.IsNullOrWhiteSpace(MessageFilter) ? null : MessageFilter.Trim(),
                    ExceptionsOnly = ExceptionsOnly,
                    From = CombineDateAndTime(FromDate, FromTimeText, endOfDayIfBlank: false),
                    To = CombineDateAndTime(ToDate, ToTimeText, endOfDayIfBlank: true),
                };

                LastQueryDescription =
                    $"Type={filter.TypeName ?? "(any)"}, Entity={filter.PrimaryEntity ?? "(any)"}, Message={filter.MessageName ?? "(any)"}, ExceptionsOnly={filter.ExceptionsOnly}";

                var page = await _client.GetPluginTraceLogsAsync(filter).ConfigureAwait(true);
                LastQueryDescription += $" → {page.Items.Count} row(s)";
                OutputPaneLogger.WriteLineAsync(LastQueryDescription).FileAndForget("D365DeveloperTools/LogPluginDebuggingQuery");

                // Auto-refresh polls every 5s, and most ticks find nothing new — rebuilding Entries
                // unconditionally would reset the DataGrid's scroll position and drop SelectedEntry
                // (closing the detail pane) on every single tick, even when nothing changed. Skip the
                // rebuild entirely when the result is identical to what's already shown, and when it
                // isn't, restore the selection by TraceLogId (a fresh page is all new object instances,
                // so SelectedEntry itself would otherwise no longer be reference-equal to anything).
                var newIds = page.Items.Select(e => e.TraceLogId);
                var currentIds = Entries.Select(e => e.TraceLogId);
                if (!newIds.SequenceEqual(currentIds))
                {
                    var previousSelectedId = SelectedEntry?.TraceLogId;

                    Entries.Clear();
                    foreach (var entry in page.Items) { Entries.Add(entry); }

                    SelectedEntry = previousSelectedId == null ? null : Entries.FirstOrDefault(e => e.TraceLogId == previousSelectedId);
                }
            }
            catch (Exception ex)
            {
                LoadError = ex.Message;
                OutputPaneLogger.WriteLineAsync($"{LastQueryDescription} → ERROR: {ex.Message}").FileAndForget("D365DeveloperTools/LogPluginDebuggingQueryError");
            }
            finally
            {
                // Guarded by showLoadingIndicator too: a silent auto-refresh tick completing shouldn't
                // clear an unrelated in-flight manual refresh's own IsLoading=true.
                if (showLoadingIndicator) { IsLoading = false; }
            }
        }

        private string _lastProfileQueryDescription;

        /// <summary>Mirrors LastQueryDescription for the Profile Captures tab — RefreshProfileCapturesAsync used to fail completely silently on an empty result, with no way to tell TypeName/From/HasCapturedProfile apart as the cause.</summary>
        public string LastProfileQueryDescription { get => _lastProfileQueryDescription; private set => SetProperty(ref _lastProfileQueryDescription, value); }

        /// <summary>Manual only — no auto-poll. Profile captures are deliberate, bounded (the user triggers the action a handful of times), not a firehose like plugintracelog, so a 5s timer would just be extra load for no benefit.</summary>
        public async Task RefreshProfileCapturesAsync()
        {
            if (!IsConnected || _profiledStepTypeName == null) { return; }

            try
            {
                var filter = new PluginTraceLogFilter
                {
                    TypeName = _profiledStepTypeName,
                    HasCapturedProfile = true,
                    From = CombineDateAndTime(ProfileFromDate, ProfileFromTimeText, endOfDayIfBlank: false),
                    To = CombineDateAndTime(ProfileToDate, ProfileToTimeText, endOfDayIfBlank: true),
                    Top = 50,
                };

                LastProfileQueryDescription = $"Type={filter.TypeName}, From={filter.From:o}, To={filter.To:o}, HasCapturedProfile=true";

                var page = await _client.GetPluginTraceLogsAsync(filter).ConfigureAwait(true);
                LastProfileQueryDescription += $" → {page.Items.Count} row(s)";

                var previousSelectedId = SelectedProfileEntry?.TraceLogId;
                ProfileEntries.Clear();
                foreach (var capture in page.Items) { ProfileEntries.Add(capture); }
                SelectedProfileEntry = previousSelectedId == null ? null : ProfileEntries.FirstOrDefault(e => e.TraceLogId == previousSelectedId);
            }
            catch (Exception ex)
            {
                LastProfileQueryDescription += $" → ERROR: {ex.Message}";
                _prompts.ShowError($"D365: Failed to load profile captures: {ex.Message}");
            }
        }

        private async Task CheckTracingSettingAsync()
        {
            try
            {
                TracingSetting = await _client.GetPluginTraceLogSettingAsync().ConfigureAwait(true);
            }
            catch
            {
                // Best-effort — the banner just won't show if this fails; it shouldn't block the log grid.
                TracingSetting = null;
            }

            // TracingSetting's own SetProperty only notifies "TracingSetting" itself — these two derive
            // from it but aren't automatically re-evaluated by bound controls without an explicit nudge.
            OnPropertyChanged(nameof(IsTracingOff));
            OnPropertyChanged(nameof(SelectedTracingLevel));
        }

        private async Task SetTracingLevelAsync(PluginTraceLogSetting setting)
        {
            if (TracingSetting == null) { return; }

            try
            {
                await _client.SetPluginTraceLogSettingAsync(TracingSetting.OrganizationId, setting).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                _prompts.ShowError($"D365: Failed to change plugin trace logging: {ex.Message}");
            }

            await CheckTracingSettingAsync().ConfigureAwait(true);
        }

        /// <summary>Free-text search over the already-loaded page only — structured filters (entity/message/date/exceptions-only/step) are server-side via RefreshAsync.</summary>
        private bool FilterEntry(object obj)
        {
            if (string.IsNullOrWhiteSpace(SearchText)) { return true; }

            var entry = (PluginTraceLogEntry)obj;
            var query = SearchText.Trim();
            return Contains(entry.TypeName, query) || Contains(entry.MessageBlock, query) || Contains(entry.ExceptionDetails, query);
        }

        private static bool Contains(string haystack, string query) =>
            !string.IsNullOrEmpty(haystack) && haystack.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>
        /// Merges a DatePicker-bound calendar date with its free-text "HH:mm:ss" companion into the
        /// actual DateTime sent to the server — DatePicker.SelectedDate only ever carries a date (time
        /// always midnight), so without this every From/To bound would silently be start-of-day
        /// regardless of what the user actually wants. A blank or unparseable time falls back to start
        /// of day for a "From" bound (endOfDayIfBlank: false) or end of day for a "To" bound
        /// (endOfDayIfBlank: true) — matching ordinary date-range-filter conventions (a bare "To" date
        /// should include that whole day, not exclude everything past midnight of it).
        /// </summary>
        private static DateTime? CombineDateAndTime(DateTime? date, string timeText, bool endOfDayIfBlank)
        {
            if (date == null) { return null; }

            if (!string.IsNullOrWhiteSpace(timeText) && TimeSpan.TryParse(timeText.Trim(), out var time))
            {
                return date.Value.Date + time;
            }

            return endOfDayIfBlank ? date.Value.Date.AddDays(1).AddTicks(-1) : date.Value.Date;
        }

        /// <summary>
        /// Stops the auto-refresh timer — called on disconnect and from PluginDebuggingToolWindow.Dispose
        /// so it never keeps polling after the window closes. Goes through the IsAutoRefreshEnabled
        /// property (rather than stopping _autoRefreshTimer directly) so the checkbox reflects reality
        /// if the window is reopened later against the same, still-live singleton ViewModel.
        /// </summary>
        public void StopAutoRefresh()
        {
            IsAutoRefreshEnabled = false;
        }

        // ── Debug session tracking — DebugPluginCaptureCommand drives these; this ViewModel only
        // tracks state for the status strip and the Stop button, it never launches anything itself. ──

        public void BeginDebugSession(string label)
        {
            _debugProcess = null;
            DebugSession = new DebugSessionInfo { Label = label, StartedAtUtc = DateTime.UtcNow, StatusText = "Starting…" };
        }

        public void OnDebugProcessAttached(System.Diagnostics.Process process)
        {
            _debugProcess = process;
            if (DebugSession != null) { DebugSession.StatusText = $"Attached (PID {process.Id}) — debugging"; }
        }

        public void EndDebugSession()
        {
            _debugProcess = null;
            DebugSession = null;
        }

        private void StopDebugSession()
        {
            try
            {
                if (_debugProcess != null && !_debugProcess.HasExited) { _debugProcess.Kill(); }
            }
            catch
            {
                // Best-effort — the process may already have exited between the check above and the kill.
            }
        }
    }
}
