using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using D365_Developer_Tools__Unofficial__for_Visual_Studio.Dataverse;
using D365_Developer_Tools__Unofficial__for_Visual_Studio.Shared.Mvvm;
using Microsoft.VisualStudio.Shell;

namespace D365_Developer_Tools__Unofficial__for_Visual_Studio.ToolWindows.ViewModels
{
    /// <summary>A registered Custom API; lazy-loads its request parameters + response properties (combined into one list, tagged by Kind) once on first expand.</summary>
    internal sealed class CustomApiNodeViewModel : ObservableObject
    {
        private readonly DataverseClient _client;
        private bool _parametersLoaded;
        private CustomApiDefinition _api;

        public CustomApiDefinition Api => _api;

        public string Name => Api.Name;
        public string DisplayName => Api.DisplayName;
        public string UniqueName => Api.UniqueName;
        public string BindingType => Api.BindingType;
        public bool IsFunction => Api.IsFunction;
        public bool IsPrivate => Api.IsPrivate;

        public ObservableCollection<CustomApiParameterNodeViewModel> Parameters { get; } = new ObservableCollection<CustomApiParameterNodeViewModel>();

        private bool _isExpanded;
        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (SetProperty(ref _isExpanded, value) && value && !_parametersLoaded)
                {
                    LoadParametersAsync().FileAndForget("D365DeveloperTools/LoadCustomApiParameters");
                }
            }
        }

        private bool _isLoading;
        public bool IsLoading { get => _isLoading; private set => SetProperty(ref _isLoading, value); }

        private string _error;
        public string Error { get => _error; private set => SetProperty(ref _error, value); }

        public CustomApiNodeViewModel(CustomApiDefinition api, DataverseClient client)
        {
            _api = api;
            _client = client;

            // A placeholder child so the TreeViewItem shows its expand chevron before the real
            // parameters/properties are lazy-loaded (WPF hides the chevron whenever HasItems is false).
            Parameters.Add(null);
        }

        /// <summary>Re-fetches this API's own fields after an edit, without disturbing its loaded parameters.</summary>
        public void UpdateApi(CustomApiDefinition updated)
        {
            _api = updated;
            OnPropertyChanged(nameof(Name));
            OnPropertyChanged(nameof(DisplayName));
            OnPropertyChanged(nameof(UniqueName));
            OnPropertyChanged(nameof(BindingType));
            OnPropertyChanged(nameof(IsFunction));
            OnPropertyChanged(nameof(IsPrivate));
        }

        /// <summary>Forces a reload of this API's parameters/properties, e.g. after adding, editing, or unregistering one.</summary>
        public Task ReloadParametersAsync()
        {
            _parametersLoaded = false;
            return LoadParametersAsync();
        }

        private async Task LoadParametersAsync()
        {
            _parametersLoaded = true;
            IsLoading = true;
            Error = null;

            try
            {
                var requestParameters = await _client.GetCustomApiRequestParametersAsync(Api.CustomApiId).ConfigureAwait(true);
                var responseProperties = await _client.GetCustomApiResponsePropertiesAsync(Api.CustomApiId).ConfigureAwait(true);

                Parameters.Clear();
                foreach (var p in requestParameters) { Parameters.Add(new CustomApiParameterNodeViewModel(p) { Owner = this }); }
                foreach (var p in responseProperties) { Parameters.Add(new CustomApiParameterNodeViewModel(p) { Owner = this }); }
            }
            catch (Exception ex)
            {
                _parametersLoaded = false;
                Error = ex.Message;
            }
            finally
            {
                IsLoading = false;
            }
        }
    }
}
