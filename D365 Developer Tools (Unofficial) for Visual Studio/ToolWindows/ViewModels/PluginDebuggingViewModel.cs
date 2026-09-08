using System;
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
    /// Backs PluginDebuggingControl — views/filters plugintracelog records, similar to the Plugin
    /// Registration Tool's Profiler log grid, but view/filter only (no capture/replay in v1).
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

        // ── Structured filters — sent to Dataverse as an OData $filter on RefreshCommand, unlike
        // SearchText below which only narrows the already-loaded page client-side. ─────────────────

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

        private bool _hasCapturedProfileOnly;

        /// <summary>Narrows to rows with a replayable capture (plugintracelog.profile ne null) — see PluginTraceLogFilter.HasCapturedProfile's doc comment for why this, not PersistenceKey/HasProfilingData, is the correct signal.</summary>
        public bool HasCapturedProfileOnly
        {
            get => _hasCapturedProfileOnly;
            set
            {
                if (SetProperty(ref _hasCapturedProfileOnly, value)) { RefreshAsync().FileAndForget("D365DeveloperTools/RefreshTraceLogsAfterHasCapturedProfileToggle"); }
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

        private DebugSessionInfo _debugSession;

        /// <summary>Non-null for the lifetime of one "Debug This" run — backs PluginDebuggingControl's non-modal session status strip.</summary>
        public DebugSessionInfo DebugSession { get => _debugSession; private set => SetProperty(ref _debugSession, value); }

        private System.Diagnostics.Process _debugProcess;

        public ICommand ConnectCommand { get; }
        public ICommand RefreshCommand { get; }
        public ICommand ShowMenuCommand { get; }
        public ICommand ClearStepFilterCommand { get; }
        public ICommand EnableTracingCommand { get; }
        public ICommand DebugCommand { get; }
        public ICommand StopDebugSessionCommand { get; }

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
            EnableTracingCommand = new AsyncRelayCommand(EnableTracingAsync);
            DebugCommand = new AsyncRelayCommand(
                () => DebugPluginCaptureCommand.ExecuteAsync(SelectedEntry),
                () => SelectedEntry != null && DebugSession == null);
            StopDebugSessionCommand = new RelayCommand(_ => StopDebugSession(), _ => DebugSession != null);

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
            }
            else
            {
                Entries.Clear();
                TracingSetting = null;
                StopAutoRefresh();
            }
        }

        /// <summary>
        /// Scopes the grid to one plugin type (e.g. from Plugin Explorer's "View Trace Logs..."
        /// command) and refreshes. Only TypeName is applied to the query — messageName/primaryEntity
        /// are shown in stepLabel for context but deliberately NOT written into EntityFilter/
        /// MessageFilter, since AND-ing all three turned out too strict in practice (a single
        /// mismatched field, e.g. a primaryentity that isn't recorded exactly the way the step's own
        /// entity filter is, silently zeroes the whole result). TypeName alone is the field this
        /// feature has the most confidence in, and the user can still narrow further by hand using the
        /// regular Entity/Message boxes plus Refresh if they want tighter matching.
        /// </summary>
        public async Task LoadForStepAsync(string pluginTypeName, string stepLabel, bool requireCapturedProfile = false)
        {
            _stepFilterTypeName = pluginTypeName;
            StepFilterLabel = stepLabel;
            OnPropertyChanged(nameof(HasStepFilter));

            // Setting this (rather than passing it straight into RefreshAsync's own filter) so the
            // checkbox in PluginDebuggingControl reflects it too — "Debug This Step..." arms a capture
            // and this filter is exactly what finds it once triggered.
            if (requireCapturedProfile) { _hasCapturedProfileOnly = true; OnPropertyChanged(nameof(HasCapturedProfileOnly)); }

            await RefreshAsync().ConfigureAwait(true);
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
                    HasCapturedProfile = HasCapturedProfileOnly,
                    From = FromDate,
                    To = ToDate,
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
        }

        private async Task EnableTracingAsync()
        {
            if (TracingSetting == null) { return; }

            try
            {
                await _client.SetPluginTraceLogSettingAsync(TracingSetting.OrganizationId, PluginTraceLogSetting.All).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                _prompts.ShowError($"D365: Failed to enable plugin trace logging: {ex.Message}");
                return;
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
