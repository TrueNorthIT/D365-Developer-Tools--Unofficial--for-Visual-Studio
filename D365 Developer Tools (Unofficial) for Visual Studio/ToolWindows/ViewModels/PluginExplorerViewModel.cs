using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Windows.Input;
using D365_Developer_Tools__Unofficial__for_Visual_Studio.Commands;
using D365_Developer_Tools__Unofficial__for_Visual_Studio.Connection;
using D365_Developer_Tools__Unofficial__for_Visual_Studio.Dataverse;
using D365_Developer_Tools__Unofficial__for_Visual_Studio.Shared;
using D365_Developer_Tools__Unofficial__for_Visual_Studio.Shared.Mvvm;
using Microsoft.VisualStudio.Shell;

namespace D365_Developer_Tools__Unofficial__for_Visual_Studio.ToolWindows.ViewModels
{
    /// <summary>Backs PluginExplorerControl — browses plugin assemblies/types/steps/images, similar to the Plugin Registration Tool.</summary>
    internal sealed class PluginExplorerViewModel : ObservableObject
    {
        private readonly ConnectionManager _connectionManager;
        private readonly DataverseClient _client;
        private readonly IUserPrompts _prompts;

        private List<PluginAssemblyNodeViewModel> _allAssemblies = new List<PluginAssemblyNodeViewModel>();
        private HashSet<string> _solutionFilterIds;

        public ObservableCollection<PluginAssemblyNodeViewModel> Assemblies { get; } = new ObservableCollection<PluginAssemblyNodeViewModel>();
        public ICollectionView AssembliesView { get; }

        private string _searchText = string.Empty;
        public string SearchText
        {
            get => _searchText;
            set { if (SetProperty(ref _searchText, value)) { AssembliesView.Refresh(); } }
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

        private bool _isLoadingAssemblies;
        public bool IsLoadingAssemblies { get => _isLoadingAssemblies; private set => SetProperty(ref _isLoadingAssemblies, value); }

        private string _loadError;
        public string LoadError { get => _loadError; private set => SetProperty(ref _loadError, value); }

        public ICommand ConnectCommand { get; }
        public ICommand RefreshCommand { get; }
        public ICommand ShowMenuCommand { get; }
        public ICommand ShowSolutionPickerCommand { get; }
        public ICommand ClearSolutionFilterCommand { get; }
        public ICommand PublishProjectCommand { get; }

        public PluginExplorerViewModel(ConnectionManager connectionManager, DataverseClient client, IUserPrompts prompts)
        {
            _connectionManager = connectionManager;
            _client = client;
            _prompts = prompts;

            AssembliesView = CollectionViewSource.GetDefaultView(Assemblies);
            AssembliesView.Filter = FilterAssembly;

            ConnectCommand = new AsyncRelayCommand(() => _connectionManager.ConnectAsync());
            RefreshCommand = new AsyncRelayCommand(RefreshAsync);
            ShowMenuCommand = new AsyncRelayCommand(() => ConnectionMenu.ShowAsync(_connectionManager, _prompts));
            ShowSolutionPickerCommand = new AsyncRelayCommand(ShowSolutionPickerAsync);
            ClearSolutionFilterCommand = new RelayCommand(_ => ClearSolutionFilter());
            PublishProjectCommand = new AsyncRelayCommand(PublishProjectAsync);

            // See EntityExplorerViewModel's constructor for why this hops to the UI thread first.
            _connectionManager.ConnectionChanged += (_, __) =>
                ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    UpdateConnectionState();
                }).Task.FileAndForget("D365DeveloperTools/OnPluginConnectionChanged");

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
                RefreshAsync().FileAndForget("D365DeveloperTools/RefreshPluginAssemblies");
            }
            else
            {
                Assemblies.Clear();
                _allAssemblies.Clear();
                _solutionFilterIds = null;
                SolutionFilterName = null;
            }
        }

        public async Task RefreshAsync()
        {
            IsLoadingAssemblies = true;
            LoadError = null;
            try
            {
                var assemblies = await _client.GetPluginAssembliesAsync().ConfigureAwait(true);
                _allAssemblies = assemblies.Select(a => new PluginAssemblyNodeViewModel(a, _client)).ToList();
                Assemblies.Clear();
                foreach (var assembly in _allAssemblies) { Assemblies.Add(assembly); }
            }
            catch (Exception ex)
            {
                LoadError = ex.Message;
            }
            finally
            {
                IsLoadingAssemblies = false;
            }
        }

        private bool FilterAssembly(object obj)
        {
            var node = (PluginAssemblyNodeViewModel)obj;

            if (_solutionFilterIds != null && !_solutionFilterIds.Contains(node.Assembly.PluginAssemblyId)) { return false; }

            if (string.IsNullOrWhiteSpace(SearchText)) { return true; }

            return node.SearchText.Contains(SearchText.Trim().ToLowerInvariant());
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
                ids = await _prompts.RunWithProgressAsync("D365: Loading solution components…", () => _client.GetSolutionPluginAssemblyIdsAsync(pick.Value.SolutionId)).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                _prompts.ShowError($"Failed to load solution components: {ex.Message}");
                return;
            }

            _solutionFilterIds = ids;
            SolutionFilterName = pick.Value.FriendlyName;
            OnPropertyChanged(nameof(HasSolutionFilter));
            AssembliesView.Refresh();
        }

        public void ClearSolutionFilter()
        {
            _solutionFilterIds = null;
            SolutionFilterName = null;
            OnPropertyChanged(nameof(HasSolutionFilter));
            AssembliesView.Refresh();
        }

        /// <summary>Builds a project from the open solution and publishes it, same as the Solution Explorer project context menu command.</summary>
        private async Task PublishProjectAsync()
        {
            var package = await D365DeveloperToolsPackage.GetReadyInstanceAsync().ConfigureAwait(true);
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            var dte = package.GetDte();
            if (dte?.Solution == null || !dte.Solution.IsOpen)
            {
                _prompts.ShowError("D365: Open a solution with a plugin project first.");
                return;
            }

            var projects = VsShellHelper.GetAllProjects(dte);
            if (projects.Count == 0)
            {
                _prompts.ShowError("D365: No projects found in the open solution.");
                return;
            }

            EnvDTE.Project project;
            if (projects.Count == 1)
            {
                project = projects[0];
            }
            else
            {
                var items = projects.Select(p => new PickItem<EnvDTE.Project>(p.Name, null, p)).ToList();
                var pick = await _prompts.PickOneAsync("D365: Choose a project", "Select the project to publish…", items).ConfigureAwait(true);
                if (pick == null) { return; }
                project = pick.Value;
            }

            await PublishToDataverseCommand.ExecuteAsync(dte, project).ConfigureAwait(true);
            await RefreshAsync().ConfigureAwait(true);
        }

        /// <summary>Registers a new step on a plugin type found in this tree, reusing the same dialog as the .cs file "Add Step..." command.</summary>
        public async Task AddStepAsync(PluginTypeNodeViewModel node)
        {
            var succeeded = await AddStepToPluginCommand.RunAddStepDialogAsync(
                _client, _prompts, node.PluginType.PluginTypeId, node.PluginAssemblyId, node.FriendlyName).ConfigureAwait(true);

            if (succeeded) { await node.ReloadStepsAsync().ConfigureAwait(true); }
        }

        /// <summary>Edits an already-registered step found in this tree.</summary>
        public async Task EditStepAsync(SdkMessageStepNodeViewModel node)
        {
            var succeeded = await EditStepCommand.ExecuteAsync(_client, _prompts, node.Step, node.PluginTypeFriendlyName).ConfigureAwait(true);
            if (succeeded) { await node.ReloadAsync().ConfigureAwait(true); }
        }

        /// <summary>Activates or deactivates a step in place, mirroring the Plugin Registration Tool's Enable/Disable commands.</summary>
        public async Task SetStepEnabledAsync(SdkMessageStepNodeViewModel node, bool enabled)
        {
            try
            {
                await _client.SetSdkMessageStepEnabledAsync(node.Step.StepId, enabled).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                _prompts.ShowError($"D365: Failed to {(enabled ? "enable" : "disable")} the step: {ex.Message}");
                return;
            }

            await node.ReloadAsync().ConfigureAwait(true);
        }

        /// <summary>Unregisters a plugin assembly. Fails (surfaced as an error) if any of its types still have registered steps.</summary>
        public async Task DeleteAssemblyAsync(PluginAssemblyNodeViewModel node)
        {
            var confirmed = await _prompts.ConfirmAsync(
                "D365: Unregister Assembly",
                $"Unregister '{node.Name}'? This can't be undone, and will fail if any of its plugin types still have registered steps.").ConfigureAwait(true);
            if (!confirmed) { return; }

            try
            {
                await _client.DeletePluginAssemblyAsync(node.Assembly.PluginAssemblyId).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                _prompts.ShowError($"D365: Failed to unregister the assembly: {ex.Message}");
                return;
            }

            _allAssemblies.Remove(node);
            Assemblies.Remove(node);
            _prompts.ShowInfo($"D365: Unregistered '{node.Name}'.");
        }

        /// <summary>Unregisters a plugin type. Fails (surfaced as an error) if it still has registered steps.</summary>
        public async Task DeleteTypeAsync(PluginTypeNodeViewModel node)
        {
            var confirmed = await _prompts.ConfirmAsync(
                "D365: Unregister Plugin Type",
                $"Unregister '{node.FriendlyName}'? This can't be undone, and will fail if it still has registered steps.").ConfigureAwait(true);
            if (!confirmed) { return; }

            try
            {
                await _client.DeletePluginTypeAsync(node.PluginType.PluginTypeId).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                _prompts.ShowError($"D365: Failed to unregister the plugin type: {ex.Message}");
                return;
            }

            if (node.Owner != null) { await node.Owner.ReloadTypesAsync().ConfigureAwait(true); }
            _prompts.ShowInfo($"D365: Unregistered '{node.FriendlyName}'.");
        }

        /// <summary>Unregisters a step (and its images, which Dataverse deletes along with it).</summary>
        public async Task DeleteStepAsync(SdkMessageStepNodeViewModel node)
        {
            var confirmed = await _prompts.ConfirmAsync("D365: Unregister Step", $"Unregister step '{node.Name}'? This can't be undone.").ConfigureAwait(true);
            if (!confirmed) { return; }

            try
            {
                await _client.DeleteSdkMessageStepAsync(node.Step.StepId).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                _prompts.ShowError($"D365: Failed to unregister the step: {ex.Message}");
                return;
            }

            if (node.Owner != null) { await node.Owner.ReloadStepsAsync().ConfigureAwait(true); }
            _prompts.ShowInfo($"D365: Unregistered '{node.Name}'.");
        }

        /// <summary>Unregisters a pre-/post-image.</summary>
        public async Task DeleteImageAsync(SdkMessageStepImageNodeViewModel node)
        {
            var confirmed = await _prompts.ConfirmAsync("D365: Unregister Image", $"Unregister image '{node.Name}'? This can't be undone.").ConfigureAwait(true);
            if (!confirmed) { return; }

            try
            {
                await _client.DeleteSdkMessageStepImageAsync(node.Image.ImageId).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                _prompts.ShowError($"D365: Failed to unregister the image: {ex.Message}");
                return;
            }

            if (node.Owner != null) { await node.Owner.ReloadImagesAsync().ConfigureAwait(true); }
            _prompts.ShowInfo($"D365: Unregistered '{node.Name}'.");
        }
    }
}
