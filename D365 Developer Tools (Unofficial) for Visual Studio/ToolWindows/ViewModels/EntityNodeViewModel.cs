using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using D365_Developer_Tools__Unofficial__for_Visual_Studio.Dataverse;
using D365_Developer_Tools__Unofficial__for_Visual_Studio.Shared.Mvvm;
using Microsoft.VisualStudio.Shell;

namespace D365_Developer_Tools__Unofficial__for_Visual_Studio.ToolWindows.ViewModels
{
    /// <summary>Ports the webview's toggleEntity() "load attributes once, cache after" behavior.</summary>
    internal sealed class EntityNodeViewModel : ObservableObject
    {
        private readonly DataverseClient _client;
        private bool _attributesLoaded;

        public EntityDefinition Entity { get; }
        public string LogicalName => Entity.LogicalName;
        public string DisplayName => Entity.DisplayName;

        public ObservableCollection<AttributeNodeViewModel> Attributes { get; } = new ObservableCollection<AttributeNodeViewModel>();

        private bool _isExpanded;
        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (SetProperty(ref _isExpanded, value) && value && !_attributesLoaded)
                {
                    LoadAttributesAsync().FileAndForget("D365DeveloperTools/LoadAttributes");
                }
            }
        }

        private bool _isLoading;
        public bool IsLoading { get => _isLoading; private set => SetProperty(ref _isLoading, value); }

        private string _error;
        public string Error { get => _error; private set => SetProperty(ref _error, value); }

        public EntityNodeViewModel(EntityDefinition entity, DataverseClient client)
        {
            Entity = entity;
            _client = client;
        }

        private async Task LoadAttributesAsync()
        {
            _attributesLoaded = true;
            IsLoading = true;
            Error = null;

            try
            {
                var attributes = await _client.GetAttributesAsync(Entity.LogicalName).ConfigureAwait(true);
                Attributes.Clear();
                foreach (var attribute in attributes)
                {
                    Attributes.Add(new AttributeNodeViewModel(attribute) { Owner = this });
                }
            }
            catch (Exception ex)
            {
                _attributesLoaded = false;
                Error = ex.Message;
            }
            finally
            {
                IsLoading = false;
            }
        }
    }
}
