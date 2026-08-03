using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using D365_Developer_Tools__Unofficial__for_Visual_Studio.Dataverse;
using D365_Developer_Tools__Unofficial__for_Visual_Studio.Shared.Mvvm;
using Microsoft.VisualStudio.Shell;

namespace D365_Developer_Tools__Unofficial__for_Visual_Studio.Shared.Dialogs
{
    /// <summary>Backs CustomApiEditorDialog — registering or editing a Custom API.</summary>
    internal sealed class CustomApiEditorViewModel : ObservableObject
    {
        private static readonly DataverseSolution NoSolutionOption = new DataverseSolution { SolutionId = null, FriendlyName = "(none)" };
        private static readonly PluginAssemblyDefinition NoAssemblyOption = new PluginAssemblyDefinition { PluginAssemblyId = null, Name = "(none)" };
        private static readonly PluginTypeDefinition NoPluginOption = new PluginTypeDefinition { PluginTypeId = null, FriendlyName = "(none)" };
        private static readonly EntityDefinition NoEntityOption = new EntityDefinition { LogicalName = null, DisplayName = "(none)" };

        private readonly DataverseClient _client;
        private readonly IUserPrompts _prompts;

        public bool IsEditMode { get; }
        public string DialogTitle => IsEditMode ? "D365: Edit Custom API" : "D365: Add Custom API";
        public string OkButtonLabel => IsEditMode ? "Update" : "Register";

        /// <summary>Binding Type, Allowed Custom Processing Step Type, Bound Entity, and Function can't be changed once a Custom API exists.</summary>
        public bool CanEditImmutableFields => !IsEditMode;

        public IReadOnlyList<PluginOption> BindingTypes => CustomApiOptionLabels.RegisterableBindingTypes;
        public IReadOnlyList<PluginOption> ProcessingStepTypes => CustomApiOptionLabels.RegisterableProcessingStepTypes;

        public ObservableCollection<DataverseSolution> Solutions { get; } = new ObservableCollection<DataverseSolution>();
        public ObservableCollection<PluginAssemblyDefinition> Assemblies { get; } = new ObservableCollection<PluginAssemblyDefinition>();
        public ObservableCollection<PluginTypeDefinition> PluginTypes { get; } = new ObservableCollection<PluginTypeDefinition>();
        public ObservableCollection<EntityDefinition> Entities { get; } = new ObservableCollection<EntityDefinition>();

        private string _uniqueName;
        public string UniqueName { get => _uniqueName; set => SetProperty(ref _uniqueName, value); }

        private string _name;
        public string Name { get => _name; set => SetProperty(ref _name, value); }

        private string _displayName;
        public string DisplayName { get => _displayName; set => SetProperty(ref _displayName, value); }

        private string _description;
        public string Description { get => _description; set => SetProperty(ref _description, value); }

        private PluginOption _selectedBindingType;
        public PluginOption SelectedBindingType
        {
            get => _selectedBindingType;
            set
            {
                if (SetProperty(ref _selectedBindingType, value))
                {
                    OnPropertyChanged(nameof(IsEntityBound));
                    if (IsEntityBound && Entities.Count == 0) { LoadEntitiesAsync().FileAndForget("D365DeveloperTools/LoadCustomApiEntities"); }
                }
            }
        }

        public bool IsEntityBound => SelectedBindingType != null && SelectedBindingType.Value != 0;

        private EntityDefinition _selectedEntity;
        public EntityDefinition SelectedEntity { get => _selectedEntity; set => SetProperty(ref _selectedEntity, value); }

        private PluginOption _selectedProcessingStepType;
        public PluginOption SelectedProcessingStepType { get => _selectedProcessingStepType; set => SetProperty(ref _selectedProcessingStepType, value); }

        private bool _isFunction;
        public bool IsFunction { get => _isFunction; set => SetProperty(ref _isFunction, value); }

        private bool _isPrivate;
        public bool IsPrivate { get => _isPrivate; set => SetProperty(ref _isPrivate, value); }

        private string _executePrivilegeName;
        public string ExecutePrivilegeName { get => _executePrivilegeName; set => SetProperty(ref _executePrivilegeName, value); }

        private PluginAssemblyDefinition _selectedAssembly;
        public PluginAssemblyDefinition SelectedAssembly
        {
            get => _selectedAssembly;
            set
            {
                if (SetProperty(ref _selectedAssembly, value))
                {
                    LoadPluginTypesAsync().FileAndForget("D365DeveloperTools/LoadCustomApiPluginTypes");
                }
            }
        }

        private PluginTypeDefinition _selectedPluginType;
        public PluginTypeDefinition SelectedPluginType { get => _selectedPluginType; set => SetProperty(ref _selectedPluginType, value); }

        private DataverseSolution _selectedSolution;
        public DataverseSolution SelectedSolution { get => _selectedSolution; set => SetProperty(ref _selectedSolution, value); }

        public CustomApiEditorViewModel(DataverseClient client, IUserPrompts prompts, bool isEditMode = false)
        {
            _client = client;
            _prompts = prompts;
            IsEditMode = isEditMode;
        }

        public async Task LoadForAddAsync()
        {
            await LoadSolutionsAndAssembliesAsync().ConfigureAwait(true);
            SelectedBindingType = BindingTypes.FirstOrDefault(t => t.Value == 0);
            SelectedProcessingStepType = ProcessingStepTypes.FirstOrDefault(t => t.Value == 2);
        }

        public async Task LoadForEditAsync(CustomApiDefinition existing)
        {
            await LoadSolutionsAndAssembliesAsync().ConfigureAwait(true);

            UniqueName = existing.UniqueName;
            Name = existing.Name;
            DisplayName = existing.DisplayName;
            Description = existing.Description;
            IsFunction = existing.IsFunction;
            IsPrivate = existing.IsPrivate;
            ExecutePrivilegeName = existing.ExecutePrivilegeName;

            _selectedBindingType = BindingTypes.FirstOrDefault(t => t.Value == existing.BindingTypeValue) ?? BindingTypes.FirstOrDefault();
            OnPropertyChanged(nameof(SelectedBindingType));
            OnPropertyChanged(nameof(IsEntityBound));

            SelectedProcessingStepType = ProcessingStepTypes.FirstOrDefault(t => t.Value == existing.AllowedCustomProcessingStepTypeValue) ?? ProcessingStepTypes.FirstOrDefault();

            if (IsEntityBound && !string.IsNullOrEmpty(existing.BoundEntityLogicalName))
            {
                await LoadEntitiesAsync().ConfigureAwait(true);
                SelectedEntity = Entities.FirstOrDefault(e => e.LogicalName == existing.BoundEntityLogicalName) ?? NoEntityOption;
            }

            if (!string.IsNullOrEmpty(existing.PluginTypeId))
            {
                // The type belongs to some assembly we haven't picked yet — search each until found, same
                // cost as PRT's own "which assembly is this type in" lookup since there's no direct query.
                foreach (var assembly in Assemblies.Where(a => a.PluginAssemblyId != null))
                {
                    var types = await _client.GetPluginTypesAsync(assembly.PluginAssemblyId).ConfigureAwait(true);
                    var match = types.FirstOrDefault(t => t.PluginTypeId == existing.PluginTypeId);
                    if (match != null)
                    {
                        _selectedAssembly = assembly;
                        OnPropertyChanged(nameof(SelectedAssembly));
                        PluginTypes.Clear();
                        PluginTypes.Add(NoPluginOption);
                        foreach (var t in types) { PluginTypes.Add(t); }
                        SelectedPluginType = match;
                        break;
                    }
                }
            }
        }

        private async Task LoadSolutionsAndAssembliesAsync()
        {
            var solutions = await _client.GetSolutionsAsync().ConfigureAwait(true);
            Solutions.Clear();
            Solutions.Add(NoSolutionOption);
            foreach (var s in solutions) { Solutions.Add(s); }
            SelectedSolution = NoSolutionOption;

            var assemblies = await _client.GetPluginAssembliesAsync().ConfigureAwait(true);
            Assemblies.Clear();
            Assemblies.Add(NoAssemblyOption);
            foreach (var a in assemblies) { Assemblies.Add(a); }
            _selectedAssembly = NoAssemblyOption;
            OnPropertyChanged(nameof(SelectedAssembly));

            PluginTypes.Clear();
            PluginTypes.Add(NoPluginOption);
            SelectedPluginType = NoPluginOption;
        }

        private async Task LoadPluginTypesAsync()
        {
            PluginTypes.Clear();
            PluginTypes.Add(NoPluginOption);
            SelectedPluginType = NoPluginOption;

            if (SelectedAssembly?.PluginAssemblyId == null) { return; }

            List<PluginTypeDefinition> types;
            try
            {
                types = await _client.GetPluginTypesAsync(SelectedAssembly.PluginAssemblyId).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                _prompts.ShowError($"Failed to load plugin types: {ex.Message}");
                return;
            }

            foreach (var t in types) { PluginTypes.Add(t); }
        }

        private async Task LoadEntitiesAsync()
        {
            List<EntityDefinition> entities;
            try
            {
                entities = await _client.GetEntitiesAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                _prompts.ShowError($"Failed to load entities: {ex.Message}");
                return;
            }

            Entities.Clear();
            Entities.Add(NoEntityOption);
            foreach (var e in entities) { Entities.Add(e); }
        }

        /// <summary>Null if the user should fix something before this can be submitted.</summary>
        public string Validate()
        {
            if (string.IsNullOrWhiteSpace(Name)) { return "Enter a name."; }
            if (!IsEditMode && string.IsNullOrWhiteSpace(UniqueName)) { return "Enter a unique name."; }
            if (SelectedBindingType == null) { return "Choose a binding type."; }
            if (IsEntityBound && (SelectedEntity == null || SelectedEntity.LogicalName == null)) { return "Choose a bound entity."; }
            if (SelectedProcessingStepType == null) { return "Choose an allowed custom processing step type."; }
            return null;
        }

        public CustomApiRegistrationDetails ToRegistrationDetails() => new CustomApiRegistrationDetails
        {
            UniqueName = UniqueName,
            Name = Name,
            DisplayName = DisplayName,
            Description = Description,
            BindingType = SelectedBindingType.Value,
            BoundEntityLogicalName = IsEntityBound ? SelectedEntity?.LogicalName : null,
            AllowedCustomProcessingStepType = SelectedProcessingStepType.Value,
            IsFunction = IsFunction,
            IsPrivate = IsPrivate,
            ExecutePrivilegeName = ExecutePrivilegeName,
            PluginTypeId = SelectedPluginType?.PluginTypeId,
            SolutionUniqueName = SelectedSolution?.SolutionId != null ? SelectedSolution.UniqueName : null,
        };
    }
}
