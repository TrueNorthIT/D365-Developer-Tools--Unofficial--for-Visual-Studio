using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using D365_Developer_Tools__Unofficial__for_Visual_Studio.Dataverse;
using D365_Developer_Tools__Unofficial__for_Visual_Studio.Shared.Mvvm;
using Microsoft.VisualStudio.Shell;

namespace D365_Developer_Tools__Unofficial__for_Visual_Studio.ToolWindows.ViewModels
{
    /// <summary>A plugin type within an assembly; lazy-loads its registered steps once on first expand.</summary>
    internal sealed class PluginTypeNodeViewModel : ObservableObject
    {
        private readonly DataverseClient _client;
        private bool _stepsLoaded;

        public PluginTypeDefinition PluginType { get; }

        public string Name => PluginType.TypeName;
        public string FriendlyName => PluginType.FriendlyName;
        public bool IsWorkflowActivity => PluginType.IsWorkflowActivity;

        public ObservableCollection<SdkMessageStepNodeViewModel> Steps { get; } = new ObservableCollection<SdkMessageStepNodeViewModel>();

        private bool _isExpanded;
        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (SetProperty(ref _isExpanded, value) && value && !_stepsLoaded)
                {
                    LoadStepsAsync().FileAndForget("D365DeveloperTools/LoadSteps");
                }
            }
        }

        private bool _isLoading;
        public bool IsLoading { get => _isLoading; private set => SetProperty(ref _isLoading, value); }

        private string _error;
        public string Error { get => _error; private set => SetProperty(ref _error, value); }

        public PluginTypeNodeViewModel(PluginTypeDefinition pluginType, DataverseClient client)
        {
            PluginType = pluginType;
            _client = client;

            // A placeholder child so the TreeViewItem shows its expand chevron before the real
            // steps are lazy-loaded (WPF hides the chevron whenever HasItems is false).
            // LoadStepsAsync replaces this the moment the node is actually expanded.
            Steps.Add(null);
        }

        private async Task LoadStepsAsync()
        {
            _stepsLoaded = true;
            IsLoading = true;
            Error = null;

            try
            {
                var steps = await _client.GetSdkMessageStepsAsync(PluginType.PluginTypeId).ConfigureAwait(true);
                Steps.Clear();
                foreach (var step in steps) { Steps.Add(new SdkMessageStepNodeViewModel(step, _client)); }
            }
            catch (Exception ex)
            {
                _stepsLoaded = false;
                Error = ex.Message;
            }
            finally
            {
                IsLoading = false;
            }
        }
    }
}
