using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using D365_Developer_Tools__Unofficial__for_Visual_Studio.Dataverse;
using D365_Developer_Tools__Unofficial__for_Visual_Studio.Shared.Mvvm;

namespace D365_Developer_Tools__Unofficial__for_Visual_Studio.Shared.Dialogs
{
    /// <summary>Backs ImageEditorDialog — registering or editing a pre-/post-image on a step.</summary>
    internal sealed class ImageEditorViewModel : ObservableObject
    {
        private readonly DataverseClient _client;
        private readonly IUserPrompts _prompts;
        private readonly string _primaryEntity;
        private string _attributes;

        public bool IsEditMode { get; }
        public string DialogTitle => IsEditMode ? "D365: Edit Image" : "D365: Add Image";
        public string OkButtonLabel => IsEditMode ? "Update" : "Register";

        /// <summary>False when the step isn't registered against a specific entity — there's nothing to offer a field picker for, so Attributes falls back to free text.</summary>
        public bool CanChooseAttributes => !string.IsNullOrEmpty(_primaryEntity);

        public IReadOnlyList<PluginOption> ImageTypes => PluginOptionLabels.RegisterableImageTypes;

        private string _name;
        public string Name { get => _name; set => SetProperty(ref _name, value); }

        private string _entityAlias;
        public string EntityAlias { get => _entityAlias; set => SetProperty(ref _entityAlias, value); }

        private PluginOption _selectedImageType;
        public PluginOption SelectedImageType { get => _selectedImageType; set => SetProperty(ref _selectedImageType, value); }

        public string AttributesSummary => string.IsNullOrEmpty(_attributes) ? "(none selected)" : _attributes;
        public string Attributes
        {
            get => _attributes;
            set
            {
                if (SetProperty(ref _attributes, value)) { OnPropertyChanged(nameof(AttributesSummary)); }
            }
        }

        public ICommand ChooseAttributesCommand { get; }

        public ImageEditorViewModel(DataverseClient client, IUserPrompts prompts, string primaryEntity, bool isEditMode = false)
        {
            _client = client;
            _prompts = prompts;
            _primaryEntity = primaryEntity;
            IsEditMode = isEditMode;
            ChooseAttributesCommand = new AsyncRelayCommand(ChooseAttributesAsync);
        }

        public void LoadForAdd()
        {
            SelectedImageType = ImageTypes.FirstOrDefault(t => t.Value == 0);
            Name = SelectedImageType?.Label;
            EntityAlias = SelectedImageType?.Label.Replace(" ", string.Empty);
        }

        public void LoadForEdit(SdkMessageStepImageDefinition existingImage)
        {
            Name = existingImage.Name;
            EntityAlias = existingImage.EntityAlias;
            SelectedImageType = ImageTypes.FirstOrDefault(t => t.Value == existingImage.ImageTypeValue) ?? ImageTypes.FirstOrDefault();
            Attributes = existingImage.Attributes;
        }

        private async Task ChooseAttributesAsync()
        {
            if (!CanChooseAttributes) { return; }

            List<AttributeDefinition> attributes;
            try
            {
                attributes = await _prompts.RunWithProgressAsync(
                    $"D365: Loading fields for '{_primaryEntity}'…",
                    () => _client.GetAttributesAsync(_primaryEntity)).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                _prompts.ShowError($"Failed to load fields: {ex.Message}");
                return;
            }

            var currentlyChecked = new HashSet<string>((_attributes ?? string.Empty).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
            var items = attributes
                .Select(a => new PickItem<AttributeDefinition>(a.DisplayName, a.LogicalName, a, @checked: currentlyChecked.Contains(a.LogicalName)))
                .ToList();

            var picked = await _prompts.PickManyAsync(
                "D365: Image attributes",
                "Choose the attributes to include in this image…",
                items).ConfigureAwait(true);
            if (picked == null) { return; }

            Attributes = string.Join(",", picked.Select(p => p.Value.LogicalName));
        }

        /// <summary>Null if the user should fix something before this can be submitted.</summary>
        public string Validate()
        {
            if (string.IsNullOrWhiteSpace(Name)) { return "Enter a name for the image."; }
            if (string.IsNullOrWhiteSpace(EntityAlias)) { return "Enter an entity alias."; }
            if (SelectedImageType == null) { return "Choose an image type."; }
            return null;
        }

        public ImageRegistrationDetails ToImageRegistrationDetails() => new ImageRegistrationDetails
        {
            Name = Name,
            EntityAlias = EntityAlias,
            ImageType = SelectedImageType.Value,
            Attributes = Attributes,
        };
    }
}
