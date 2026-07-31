using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Windows.Input;
using D365_Developer_Tools__Unofficial__for_Visual_Studio.CodeGen;
using D365_Developer_Tools__Unofficial__for_Visual_Studio.Commands;
using D365_Developer_Tools__Unofficial__for_Visual_Studio.Connection;
using D365_Developer_Tools__Unofficial__for_Visual_Studio.Dataverse;
using D365_Developer_Tools__Unofficial__for_Visual_Studio.Shared;
using D365_Developer_Tools__Unofficial__for_Visual_Studio.Shared.Mvvm;
using Microsoft.VisualStudio.Shell;

namespace D365_Developer_Tools__Unofficial__for_Visual_Studio.ToolWindows.ViewModels
{
    /// <summary>Backs EntityExplorerControl — ports entityExplorerWebview.ts's state and message handlers.</summary>
    internal sealed class EntityExplorerViewModel : ObservableObject
    {
        private readonly ConnectionManager _connectionManager;
        private readonly DataverseClient _client;
        private readonly IUserPrompts _prompts;

        private List<EntityNodeViewModel> _allEntities = new List<EntityNodeViewModel>();
        private HashSet<string> _solutionFilterIds;

        public ObservableCollection<EntityNodeViewModel> Entities { get; } = new ObservableCollection<EntityNodeViewModel>();
        public ICollectionView EntitiesView { get; }

        private string _searchText = string.Empty;
        public string SearchText
        {
            get => _searchText;
            set { if (SetProperty(ref _searchText, value)) { EntitiesView.Refresh(); } }
        }

        private string _solutionFilterName;
        public string SolutionFilterName { get => _solutionFilterName; private set => SetProperty(ref _solutionFilterName, value); }

        public bool HasSolutionFilter => SolutionFilterName != null;

        private bool _isConnected;
        public bool IsConnected { get => _isConnected; private set => SetProperty(ref _isConnected, value); }

        private bool _isRestoring;
        public bool IsRestoring { get => _isRestoring; private set => SetProperty(ref _isRestoring, value); }

        private string _statusText = "Not Connected";
        public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }

        private bool _isLoadingEntities;
        public bool IsLoadingEntities { get => _isLoadingEntities; private set => SetProperty(ref _isLoadingEntities, value); }

        private string _loadError;
        public string LoadError { get => _loadError; private set => SetProperty(ref _loadError, value); }

        public ICommand ConnectCommand { get; }
        public ICommand RefreshCommand { get; }
        public ICommand ShowMenuCommand { get; }
        public ICommand ShowSolutionPickerCommand { get; }
        public ICommand ClearSolutionFilterCommand { get; }

        public EntityExplorerViewModel(ConnectionManager connectionManager, DataverseClient client, IUserPrompts prompts)
        {
            _connectionManager = connectionManager;
            _client = client;
            _prompts = prompts;

            EntitiesView = CollectionViewSource.GetDefaultView(Entities);
            EntitiesView.Filter = FilterEntity;

            ConnectCommand = new AsyncRelayCommand(() => _connectionManager.ConnectAsync());
            RefreshCommand = new AsyncRelayCommand(RefreshAsync);
            ShowMenuCommand = new AsyncRelayCommand(() => ConnectionMenu.ShowAsync(_connectionManager, _prompts));
            ShowSolutionPickerCommand = new AsyncRelayCommand(ShowSolutionPickerAsync);
            ClearSolutionFilterCommand = new RelayCommand(_ => ClearSolutionFilter());

            // ConnectionManager deliberately uses ConfigureAwait(false) internally and can raise this
            // event from a background thread — hop back to the UI thread before touching Entities
            // (an ObservableCollection bound to a CollectionView, which throws if mutated off the
            // Dispatcher thread) or any other UI-bound state.
            _connectionManager.ConnectionChanged += (_, __) =>
                ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    UpdateConnectionState();
                }).Task.FileAndForget("D365DeveloperTools/OnConnectionChanged");

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
                LoadEntitiesAsync().FileAndForget("D365DeveloperTools/RefreshEntities");
            }
            else
            {
                Entities.Clear();
                _allEntities.Clear();
                _solutionFilterIds = null;
                SolutionFilterName = null;
            }
        }

        private async Task LoadEntitiesAsync()
        {
            await RefreshAsync().ConfigureAwait(true);
            await ApplyDefaultSolutionAsync().ConfigureAwait(true);
        }

        public async Task RefreshAsync()
        {
            IsLoadingEntities = true;
            LoadError = null;
            try
            {
                var entities = await _client.GetEntitiesAsync().ConfigureAwait(true);
                _allEntities = entities.Select(e => new EntityNodeViewModel(e, _client)).ToList();
                Entities.Clear();
                foreach (var entity in _allEntities) { Entities.Add(entity); }
            }
            catch (Exception ex)
            {
                LoadError = ex.Message;
            }
            finally
            {
                IsLoadingEntities = false;
            }
        }

        /// <summary>Re-applies the solution filter last picked for this environment (see ShowSolutionPickerAsync), if any.</summary>
        private async Task ApplyDefaultSolutionAsync()
        {
            var environmentUrl = _connectionManager.Connection?.EnvironmentUrl;
            var defaultSolution = environmentUrl == null ? null : _connectionManager.GetDefaultSolution(environmentUrl);
            if (defaultSolution == null) { return; }

            try
            {
                _solutionFilterIds = await _client.GetSolutionEntityIdsAsync(defaultSolution.SolutionId).ConfigureAwait(true);
                SolutionFilterName = defaultSolution.FriendlyName;
                OnPropertyChanged(nameof(HasSolutionFilter));
                EntitiesView.Refresh();
            }
            catch
            {
                // The remembered solution may have been deleted/renamed since — leave unfiltered and
                // let the user re-pick one manually.
            }
        }

        private bool FilterEntity(object obj)
        {
            var node = (EntityNodeViewModel)obj;

            if (_solutionFilterIds != null && !_solutionFilterIds.Contains(node.Entity.MetadataId)) { return false; }

            if (string.IsNullOrWhiteSpace(SearchText)) { return true; }

            var text = SearchText.Trim().ToLowerInvariant();
            return node.LogicalName.ToLowerInvariant().Contains(text) || node.DisplayName.ToLowerInvariant().Contains(text);
        }

        private async Task ShowSolutionPickerAsync()
        {
            List<DataverseSolution> solutions;
            try
            {
                solutions = await _prompts.RunWithProgressAsync("D365: Loading solutions…", () => _client.GetSolutionsAsync()).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                _prompts.ShowError($"Failed to load solutions: {ex.Message}");
                return;
            }

            var items = solutions.Select(s => new PickItem<DataverseSolution>(s.FriendlyName, s.UniqueName, s)).ToList();
            var pick = await _prompts.PickOneAsync("D365: Filter by solution", "Select a solution…", items).ConfigureAwait(true);
            if (pick == null) { return; }

            HashSet<string> ids;
            try
            {
                ids = await _prompts.RunWithProgressAsync("D365: Loading solution components…", () => _client.GetSolutionEntityIdsAsync(pick.Value.SolutionId)).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                _prompts.ShowError($"Failed to load solution components: {ex.Message}");
                return;
            }

            _solutionFilterIds = ids;
            SolutionFilterName = pick.Value.FriendlyName;
            OnPropertyChanged(nameof(HasSolutionFilter));
            EntitiesView.Refresh();

            var environmentUrl = _connectionManager.Connection?.EnvironmentUrl;
            if (environmentUrl != null) { _connectionManager.SetDefaultSolution(environmentUrl, pick.Value); }
        }

        public void ClearSolutionFilter()
        {
            _solutionFilterIds = null;
            SolutionFilterName = null;
            OnPropertyChanged(nameof(HasSolutionFilter));
            EntitiesView.Refresh();

            var environmentUrl = _connectionManager.Connection?.EnvironmentUrl;
            if (environmentUrl != null) { _connectionManager.ClearDefaultSolution(environmentUrl); }
        }

        // ── Codegen actions (invoked from the tree's context menus) ─────────

        public async Task GenerateClassAsync(EntityNodeViewModel node)
        {
            List<AttributeDefinition> attributes;
            try
            {
                attributes = await _prompts.RunWithProgressAsync(
                    $"D365: Loading fields for '{node.LogicalName}'…",
                    () => _client.GetAttributesAsync(node.LogicalName)).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                _prompts.ShowError($"Failed to load fields: {ex.Message}");
                return;
            }

            var items = attributes.Select(a => new PickItem<AttributeDefinition>(
                a.DisplayName,
                a.LogicalName,
                a,
                detail: string.Join("  ·  ", new[] { a.AttributeType, a.IsPrimaryId ? "Primary ID" : null, a.IsPrimaryName ? "Primary Name" : null }.Where(s => s != null)),
                @checked: a.IsPrimaryId || a.IsPrimaryName)).ToList();

            var picked = await _prompts.PickManyAsync($"D365: Select fields — {node.DisplayName} ({node.LogicalName})", "Choose fields to include in the class…", items).ConfigureAwait(true);
            if (picked == null || picked.Count == 0) { return; }

            var selectedAttrs = picked.Select(p => p.Value).ToList();
            var optionSetAttrs = selectedAttrs.Where(a => OptionSetCasts.OptionSetTypes.Contains(a.AttributeType)).ToList();

            var enumBlocks = new List<string>();
            var enumNames = new Dictionary<string, string>();

            if (optionSetAttrs.Count > 0)
            {
                try
                {
                    await _prompts.RunWithProgressAsync("D365: Loading option sets…", async () =>
                    {
                        foreach (var attr in optionSetAttrs)
                        {
                            var options = await _client.GetAttributeOptionsAsync(node.LogicalName, attr.LogicalName, attr.AttributeType).ConfigureAwait(true);
                            var enumName = EnumGenerator.GetEnumName(attr.LogicalName, attr.DisplayName);
                            enumNames[attr.LogicalName] = enumName;
                            enumBlocks.Add(EnumGenerator.GenerateEnum(attr.LogicalName, attr.DisplayName, options));
                        }
                    }).ConfigureAwait(true);
                }
                catch (Exception ex)
                {
                    _prompts.ShowError($"Failed to load option sets: {ex.Message}");
                    return;
                }
            }

            var primaryId = attributes.FirstOrDefault(a => a.IsPrimaryId);
            var fileContent = EarlyBoundClassGenerator.GenerateFile(node.LogicalName, node.DisplayName, selectedAttrs, primaryId, enumNames, enumBlocks);
            var className = NameUtilities.ToPascalCase(node.LogicalName, node.DisplayName);

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            DocumentOpener.OpenAsCSharp(fileContent, $"{className}.cs");
        }

        public async Task GenerateEnumAsync(AttributeNodeViewModel attrNode)
        {
            var entityNode = attrNode.Owner;
            List<OptionValue> options;
            try
            {
                options = await _prompts.RunWithProgressAsync(
                    $"D365: Loading options for '{attrNode.DisplayName}'…",
                    () => _client.GetAttributeOptionsAsync(entityNode.LogicalName, attrNode.LogicalName, attrNode.AttributeType)).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                _prompts.ShowError($"Failed to load option set: {ex.Message}");
                return;
            }

            var text = "using System;\n\n" + EnumGenerator.GenerateEnum(attrNode.LogicalName, attrNode.DisplayName, options);
            var enumName = EnumGenerator.GetEnumName(attrNode.LogicalName, attrNode.DisplayName);

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            DocumentOpener.OpenAsCSharp(text, $"{enumName}.cs");
        }
    }
}
