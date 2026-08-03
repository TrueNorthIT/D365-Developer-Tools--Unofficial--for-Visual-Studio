using System.Collections.Generic;
using System.Linq;
using D365_Developer_Tools__Unofficial__for_Visual_Studio.Dataverse;
using D365_Developer_Tools__Unofficial__for_Visual_Studio.Shared.Mvvm;

namespace D365_Developer_Tools__Unofficial__for_Visual_Studio.Shared.Dialogs
{
    /// <summary>Backs CustomApiParameterEditorDialog — registering or editing either a request parameter or a response property (identical shape apart from IsOptional, which only applies to request parameters).</summary>
    internal sealed class CustomApiParameterEditorViewModel : ObservableObject
    {
        public bool IsEditMode { get; }
        public bool IsRequestParameter { get; }

        public string DialogTitle =>
            (IsEditMode ? "D365: Edit " : "D365: Add ") + (IsRequestParameter ? "Request Parameter" : "Response Property");
        public string OkButtonLabel => IsEditMode ? "Update" : "Register";

        /// <summary>UniqueName/Type/LogicalEntityName/IsOptional can't be changed once the parameter/property exists.</summary>
        public bool CanEditImmutableFields => !IsEditMode;

        public IReadOnlyList<PluginOption> Types => CustomApiOptionLabels.RegisterableParameterTypes;

        private string _uniqueName;
        public string UniqueName { get => _uniqueName; set => SetProperty(ref _uniqueName, value); }

        private string _name;
        public string Name { get => _name; set => SetProperty(ref _name, value); }

        private string _displayName;
        public string DisplayName { get => _displayName; set => SetProperty(ref _displayName, value); }

        private string _description;
        public string Description { get => _description; set => SetProperty(ref _description, value); }

        private PluginOption _selectedType;
        public PluginOption SelectedType
        {
            get => _selectedType;
            set
            {
                if (SetProperty(ref _selectedType, value)) { OnPropertyChanged(nameof(IsEntityType)); }
            }
        }

        public bool IsEntityType => SelectedType != null && (SelectedType.Value == 3 || SelectedType.Value == 4 || SelectedType.Value == 5); // Entity / EntityCollection / EntityReference

        private string _logicalEntityName;
        public string LogicalEntityName { get => _logicalEntityName; set => SetProperty(ref _logicalEntityName, value); }

        private bool _isOptional = true;
        public bool IsOptional { get => _isOptional; set => SetProperty(ref _isOptional, value); }

        public CustomApiParameterEditorViewModel(bool isRequestParameter, bool isEditMode = false)
        {
            IsRequestParameter = isRequestParameter;
            IsEditMode = isEditMode;
        }

        public void LoadForAdd()
        {
            SelectedType = Types.FirstOrDefault(t => t.Value == 10); // String
        }

        public void LoadForEdit(CustomApiParameterDefinition existing)
        {
            UniqueName = existing.UniqueName;
            Name = existing.Name;
            DisplayName = existing.DisplayName;
            Description = existing.Description;
            SelectedType = Types.FirstOrDefault(t => t.Value == existing.TypeValue) ?? Types.FirstOrDefault();
            LogicalEntityName = existing.LogicalEntityName;
            IsOptional = existing.IsOptional;
        }

        /// <summary>Null if the user should fix something before this can be submitted.</summary>
        public string Validate()
        {
            if (string.IsNullOrWhiteSpace(Name)) { return "Enter a name."; }
            if (!IsEditMode && string.IsNullOrWhiteSpace(UniqueName)) { return "Enter a unique name."; }
            if (SelectedType == null) { return "Choose a type."; }
            if (IsEntityType && string.IsNullOrWhiteSpace(LogicalEntityName)) { return "Enter the bound entity's logical name."; }
            return null;
        }

        public CustomApiParameterRegistrationDetails ToRegistrationDetails() => new CustomApiParameterRegistrationDetails
        {
            UniqueName = UniqueName,
            Name = Name,
            DisplayName = DisplayName,
            Description = Description,
            Type = SelectedType.Value,
            LogicalEntityName = IsEntityType ? LogicalEntityName : null,
            IsOptional = IsRequestParameter && IsOptional,
        };
    }
}
