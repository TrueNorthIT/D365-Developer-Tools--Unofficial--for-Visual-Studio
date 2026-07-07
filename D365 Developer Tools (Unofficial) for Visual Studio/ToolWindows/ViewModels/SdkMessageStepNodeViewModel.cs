using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using D365_Developer_Tools__Unofficial__for_Visual_Studio.Dataverse;
using D365_Developer_Tools__Unofficial__for_Visual_Studio.Shared.Mvvm;
using Microsoft.VisualStudio.Shell;

namespace D365_Developer_Tools__Unofficial__for_Visual_Studio.ToolWindows.ViewModels
{
    /// <summary>A registered SDK message processing step; lazy-loads its images once on first expand.</summary>
    internal sealed class SdkMessageStepNodeViewModel : ObservableObject
    {
        private readonly DataverseClient _client;
        private bool _imagesLoaded;
        private SdkMessageStepDefinition _step;

        public SdkMessageStepDefinition Step => _step;

        /// <summary>The owning plugin type's friendly name, for the "Edit Step..." dialog header.</summary>
        public string PluginTypeFriendlyName { get; }

        public string Name => Step.Name;
        public string MessageName => Step.MessageName;
        public string PrimaryEntity => Step.PrimaryEntity;
        public string Stage => Step.Stage;
        public string Mode => Step.Mode;
        public int Rank => Step.Rank;
        public bool IsEnabled => Step.IsEnabled;
        public string FilteringAttributes => Step.FilteringAttributes;

        public string Summary =>
            PrimaryEntity == null ? $"{MessageName}  ({Stage})" : $"{MessageName}: {PrimaryEntity}  ({Stage})";

        public ObservableCollection<SdkMessageStepImageNodeViewModel> Images { get; } = new ObservableCollection<SdkMessageStepImageNodeViewModel>();

        private bool _isExpanded;
        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (SetProperty(ref _isExpanded, value) && value && !_imagesLoaded)
                {
                    LoadImagesAsync().FileAndForget("D365DeveloperTools/LoadStepImages");
                }
            }
        }

        private bool _isLoading;
        public bool IsLoading { get => _isLoading; private set => SetProperty(ref _isLoading, value); }

        private string _error;
        public string Error { get => _error; private set => SetProperty(ref _error, value); }

        public SdkMessageStepNodeViewModel(SdkMessageStepDefinition step, DataverseClient client, string pluginTypeFriendlyName)
        {
            _step = step;
            _client = client;
            PluginTypeFriendlyName = pluginTypeFriendlyName;

            // A placeholder child so the TreeViewItem shows its expand chevron before the real
            // images are lazy-loaded (WPF hides the chevron whenever HasItems is false).
            // LoadImagesAsync replaces this the moment the node is actually expanded.
            Images.Add(null);
        }

        /// <summary>Re-fetches this step's own fields after an edit, without disturbing sibling steps or its loaded images.</summary>
        public async Task ReloadAsync()
        {
            var updated = await _client.GetSdkMessageStepAsync(Step.StepId).ConfigureAwait(true);
            if (updated == null) { return; }

            _step = updated;
            OnPropertyChanged(nameof(Name));
            OnPropertyChanged(nameof(MessageName));
            OnPropertyChanged(nameof(PrimaryEntity));
            OnPropertyChanged(nameof(Stage));
            OnPropertyChanged(nameof(Mode));
            OnPropertyChanged(nameof(Rank));
            OnPropertyChanged(nameof(IsEnabled));
            OnPropertyChanged(nameof(FilteringAttributes));
            OnPropertyChanged(nameof(Summary));
        }

        private async Task LoadImagesAsync()
        {
            _imagesLoaded = true;
            IsLoading = true;
            Error = null;

            try
            {
                var images = await _client.GetSdkMessageStepImagesAsync(Step.StepId).ConfigureAwait(true);
                Images.Clear();
                foreach (var image in images) { Images.Add(new SdkMessageStepImageNodeViewModel(image)); }
            }
            catch (Exception ex)
            {
                _imagesLoaded = false;
                Error = ex.Message;
            }
            finally
            {
                IsLoading = false;
            }
        }
    }
}
