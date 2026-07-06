using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using D365_Developer_Tools__Unofficial__for_Visual_Studio.Dataverse;
using D365_Developer_Tools__Unofficial__for_Visual_Studio.Shared.Mvvm;
using Microsoft.VisualStudio.Shell;

namespace D365_Developer_Tools__Unofficial__for_Visual_Studio.ToolWindows.ViewModels
{
    /// <summary>A plugin assembly; lazy-loads its plugin types once on first expand.</summary>
    internal sealed class PluginAssemblyNodeViewModel : ObservableObject
    {
        private readonly DataverseClient _client;
        private bool _typesLoaded;

        public PluginAssemblyDefinition Assembly { get; }

        public string Name => Assembly.Name;
        public string Version => Assembly.Version;
        public string IsolationMode => Assembly.IsolationMode;
        public string SourceType => Assembly.SourceType;
        public string PackageName => Assembly.PackageName;
        public bool HasPackage => !string.IsNullOrEmpty(Assembly.PackageName);

        public string SearchText => Name?.ToLowerInvariant() ?? string.Empty;

        public ObservableCollection<PluginTypeNodeViewModel> Types { get; } = new ObservableCollection<PluginTypeNodeViewModel>();

        private bool _isExpanded;
        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (SetProperty(ref _isExpanded, value) && value && !_typesLoaded)
                {
                    LoadTypesAsync().FileAndForget("D365DeveloperTools/LoadPluginTypes");
                }
            }
        }

        private bool _isLoading;
        public bool IsLoading { get => _isLoading; private set => SetProperty(ref _isLoading, value); }

        private string _error;
        public string Error { get => _error; private set => SetProperty(ref _error, value); }

        public PluginAssemblyNodeViewModel(PluginAssemblyDefinition assembly, DataverseClient client)
        {
            Assembly = assembly;
            _client = client;
        }

        private async Task LoadTypesAsync()
        {
            _typesLoaded = true;
            IsLoading = true;
            Error = null;

            try
            {
                var types = await _client.GetPluginTypesAsync(Assembly.PluginAssemblyId).ConfigureAwait(true);
                Types.Clear();
                foreach (var type in types) { Types.Add(new PluginTypeNodeViewModel(type, _client)); }
            }
            catch (Exception ex)
            {
                _typesLoaded = false;
                Error = ex.Message;
            }
            finally
            {
                IsLoading = false;
            }
        }
    }
}
