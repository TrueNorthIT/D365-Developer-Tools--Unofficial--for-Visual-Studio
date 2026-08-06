using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using D365_Developer_Tools__Unofficial__for_Visual_Studio.Dataverse;
using D365_Developer_Tools__Unofficial__for_Visual_Studio.Shared.Mvvm;
using Microsoft.VisualStudio.Shell;

namespace D365_Developer_Tools__Unofficial__for_Visual_Studio.Shared.Dialogs
{
    /// <summary>Backs StepEditorDialog — registering a new SdkMessageProcessingStep for a plugin type.</summary>
    internal sealed class StepEditorViewModel : ObservableObject
    {
        private static readonly SdkMessageFilterOption AllEntitiesOption = new SdkMessageFilterOption { SdkMessageFilterId = null, EntityLogicalName = "(all entities)" };
        private static readonly DataverseSolution NoSolutionOption = new DataverseSolution { SolutionId = null, FriendlyName = "(none)" };
        private static readonly SystemUserOption CallingUserOption = new SystemUserOption { UserId = null, FullName = "(calling user)" };

        private readonly DataverseClient _client;
        private readonly IUserPrompts _prompts;
        private string _filteringAttributes;

        /// <summary>Set by LoadForEditAsync; null on Add (there's nothing to preserve yet) or when the step has no secure config.</summary>
        private string _existingSecureConfigId;

        public string PluginTypeFriendlyName { get; }
        public bool IsEditMode { get; }
        public string DialogTitle => IsEditMode ? "D365: Edit Step" : "D365: Add Step";
        public string OkButtonLabel => IsEditMode ? "Update" : "Register";

        public ObservableCollection<SdkMessageOption> Messages { get; } = new ObservableCollection<SdkMessageOption>();
        public ObservableCollection<SdkMessageFilterOption> Entities { get; } = new ObservableCollection<SdkMessageFilterOption>();
        public ObservableCollection<DataverseSolution> Solutions { get; } = new ObservableCollection<DataverseSolution>();
        public ObservableCollection<SystemUserOption> ImpersonationUsers { get; } = new ObservableCollection<SystemUserOption>();
        public IReadOnlyList<PluginOption> Stages => PluginOptionLabels.RegisterableStages;
        public IReadOnlyList<PluginOption> Modes => PluginOptionLabels.RegisterableModes;

        private string _stepName;
        public string StepName { get => _stepName; set => SetProperty(ref _stepName, value); }

        private SdkMessageOption _selectedMessage;
        public SdkMessageOption SelectedMessage
        {
            get => _selectedMessage;
            set
            {
                if (SetProperty(ref _selectedMessage, value))
                {
                    OnPropertyChanged(nameof(IsUpdateMessage));
                    LoadEntitiesForMessageAsync().FileAndForget("D365DeveloperTools/LoadStepEntities");
                    UpdateDefaultName();
                }
            }
        }

        private SdkMessageFilterOption _selectedEntity;
        public SdkMessageFilterOption SelectedEntity
        {
            get => _selectedEntity;
            set
            {
                if (SetProperty(ref _selectedEntity, value))
                {
                    _filteringAttributes = null;
                    OnPropertyChanged(nameof(FilteringAttributesSummary));
                    UpdateDefaultName();
                }
            }
        }

        private PluginOption _selectedStage;
        public PluginOption SelectedStage { get => _selectedStage; set => SetProperty(ref _selectedStage, value); }

        private PluginOption _selectedMode;
        public PluginOption SelectedMode { get => _selectedMode; set => SetProperty(ref _selectedMode, value); }

        private string _rank = "1";
        public string Rank { get => _rank; set => SetProperty(ref _rank, value); }

        private DataverseSolution _selectedSolution;
        public DataverseSolution SelectedSolution { get => _selectedSolution; set => SetProperty(ref _selectedSolution, value); }

        private SystemUserOption _selectedImpersonationUser;
        public SystemUserOption SelectedImpersonationUser { get => _selectedImpersonationUser; set => SetProperty(ref _selectedImpersonationUser, value); }

        private string _description;
        public string Description { get => _description; set => SetProperty(ref _description, value); }

        private string _unsecureConfiguration;
        public string UnsecureConfiguration { get => _unsecureConfiguration; set => SetProperty(ref _unsecureConfiguration, value); }

        /// <summary>Always starts blank in edit mode — Dataverse never returns the existing secure value. Leaving this blank on update keeps whatever is already stored, it does not clear it.</summary>
        private string _secureConfiguration;
        public string SecureConfiguration { get => _secureConfiguration; set => SetProperty(ref _secureConfiguration, value); }

        public bool IsUpdateMessage => string.Equals(SelectedMessage?.Name, "Update", StringComparison.OrdinalIgnoreCase);
        public string FilteringAttributesSummary => string.IsNullOrEmpty(_filteringAttributes) ? "(all attributes)" : _filteringAttributes;
        public string FilteringAttributes => _filteringAttributes;

        private bool _isLoadingEntities;
        public bool IsLoadingEntities { get => _isLoadingEntities; private set => SetProperty(ref _isLoadingEntities, value); }

        public ICommand ChooseFilteringAttributesCommand { get; }

        public StepEditorViewModel(DataverseClient client, IUserPrompts prompts, string pluginTypeFriendlyName, bool isEditMode = false)
        {
            _client = client;
            _prompts = prompts;
            PluginTypeFriendlyName = pluginTypeFriendlyName;
            IsEditMode = isEditMode;
            ChooseFilteringAttributesCommand = new AsyncRelayCommand(ChooseFilteringAttributesAsync);
        }

        public async Task LoadAsync(string pluginAssemblyId)
        {
            var messages = await _client.GetSdkMessagesAsync().ConfigureAwait(true);
            Messages.Clear();
            foreach (var m in messages) { Messages.Add(m); }
            SelectedMessage = Messages.FirstOrDefault(m => m.Name == "Create") ?? Messages.FirstOrDefault();

            var solutions = await _client.GetSolutionsAsync().ConfigureAwait(true);
            Solutions.Clear();
            Solutions.Add(NoSolutionOption);
            foreach (var s in solutions) { Solutions.Add(s); }

            var containingSolutions = await _client.GetSolutionsContainingPluginAssemblyAsync(pluginAssemblyId).ConfigureAwait(true);
            if (containingSolutions.Count == 1)
            {
                SelectedSolution = Solutions.FirstOrDefault(s => s.SolutionId == containingSolutions[0].SolutionId) ?? NoSolutionOption;
            }
            else
            {
                SelectedSolution = NoSolutionOption;
            }

            var users = await _client.GetUsersAsync().ConfigureAwait(true);
            ImpersonationUsers.Clear();
            ImpersonationUsers.Add(CallingUserOption);
            foreach (var u in users) { ImpersonationUsers.Add(u); }
            SelectedImpersonationUser = CallingUserOption;

            SelectedStage = Stages.FirstOrDefault(s => s.Value == 40) ?? Stages.FirstOrDefault();
            SelectedMode = Modes.FirstOrDefault(m => m.Value == 0);
        }

        /// <summary>
        /// Populates the dialog with an existing step's current values for editing. Doesn't go through
        /// the SelectedMessage/SelectedEntity setters — those recompute the default step name and reset
        /// filtering attributes as a side effect of the user picking a *new* message, which would stomp
        /// the existing step's own name/attributes before the user has changed anything.
        /// </summary>
        public async Task LoadForEditAsync(SdkMessageStepDefinition existingStep)
        {
            var messages = await _client.GetSdkMessagesAsync().ConfigureAwait(true);
            Messages.Clear();
            foreach (var m in messages) { Messages.Add(m); }

            var solutions = await _client.GetSolutionsAsync().ConfigureAwait(true);
            Solutions.Clear();
            Solutions.Add(NoSolutionOption);
            foreach (var s in solutions) { Solutions.Add(s); }

            var containingSolutions = await _client.GetSolutionsContainingStepAsync(existingStep.StepId).ConfigureAwait(true);

            var message = Messages.FirstOrDefault(m => m.SdkMessageId == existingStep.SdkMessageId) ?? Messages.FirstOrDefault();
            _selectedMessage = message;
            OnPropertyChanged(nameof(SelectedMessage));
            OnPropertyChanged(nameof(IsUpdateMessage));

            Entities.Clear();
            Entities.Add(AllEntitiesOption);
            if (message != null)
            {
                var filters = await _client.GetSdkMessageFiltersAsync(message.SdkMessageId).ConfigureAwait(true);
                foreach (var f in filters) { Entities.Add(f); }
            }

            _selectedEntity = Entities.FirstOrDefault(e => e != AllEntitiesOption && e.SdkMessageFilterId == existingStep.SdkMessageFilterId) ?? AllEntitiesOption;
            OnPropertyChanged(nameof(SelectedEntity));

            _filteringAttributes = existingStep.FilteringAttributes;
            OnPropertyChanged(nameof(FilteringAttributesSummary));

            StepName = existingStep.Name;
            SelectedStage = Stages.FirstOrDefault(s => s.Value == existingStep.StageValue) ?? Stages.FirstOrDefault();
            SelectedMode = Modes.FirstOrDefault(m => m.Value == existingStep.ModeValue) ?? Modes.FirstOrDefault();
            Rank = existingStep.Rank.ToString();

            SelectedSolution = containingSolutions.Count == 1
                ? Solutions.FirstOrDefault(s => s.SolutionId == containingSolutions[0].SolutionId) ?? NoSolutionOption
                : NoSolutionOption;

            var users = await _client.GetUsersAsync().ConfigureAwait(true);
            ImpersonationUsers.Clear();
            ImpersonationUsers.Add(CallingUserOption);
            foreach (var u in users) { ImpersonationUsers.Add(u); }
            SelectedImpersonationUser = ImpersonationUsers.FirstOrDefault(u => u.UserId == existingStep.ImpersonatingUserId) ?? CallingUserOption;

            Description = existingStep.Description;
            UnsecureConfiguration = existingStep.UnsecureConfiguration;
            SecureConfiguration = string.Empty; // Dataverse never returns the existing value; see the property's doc comment.
            _existingSecureConfigId = existingStep.SecureConfigId;
        }

        /// <summary>Builds the request body for either CreateSdkMessageStepAsync or UpdateSdkMessageStepAsync.</summary>
        public StepRegistrationDetails ToStepRegistrationDetails() => new StepRegistrationDetails
        {
            SdkMessageId = SelectedMessage.SdkMessageId,
            SdkMessageFilterId = SelectedEntity?.SdkMessageFilterId,
            Name = StepName,
            Stage = SelectedStage.Value,
            Mode = SelectedMode.Value,
            Rank = int.Parse(Rank),
            FilteringAttributes = FilteringAttributes,
            SolutionUniqueName = SelectedSolution?.SolutionId != null ? SelectedSolution.UniqueName : null,
            Description = Description,
            UnsecureConfiguration = UnsecureConfiguration,
            ImpersonatingUserId = SelectedImpersonationUser?.UserId,
            SecureConfiguration = SecureConfiguration,
            ExistingSecureConfigId = _existingSecureConfigId,
        };

        private async Task LoadEntitiesForMessageAsync()
        {
            if (SelectedMessage == null) { Entities.Clear(); return; }

            IsLoadingEntities = true;
            try
            {
                var filters = await _client.GetSdkMessageFiltersAsync(SelectedMessage.SdkMessageId).ConfigureAwait(true);
                Entities.Clear();
                Entities.Add(AllEntitiesOption);
                foreach (var f in filters) { Entities.Add(f); }

                SelectedEntity = Entities.FirstOrDefault(e => e != AllEntitiesOption) ?? AllEntitiesOption;
            }
            finally
            {
                IsLoadingEntities = false;
            }
        }

        private void UpdateDefaultName()
        {
            if (SelectedMessage == null) { return; }
            var entityPart = SelectedEntity != null && SelectedEntity != AllEntitiesOption ? $" of {SelectedEntity.EntityLogicalName}" : string.Empty;
            StepName = $"{PluginTypeFriendlyName}: {SelectedMessage.Name}{entityPart}";
        }

        private async Task ChooseFilteringAttributesAsync()
        {
            if (SelectedEntity == null || SelectedEntity == AllEntitiesOption) { return; }

            List<AttributeDefinition> attributes;
            try
            {
                attributes = await _prompts.RunWithProgressAsync(
                    $"D365: Loading fields for '{SelectedEntity.EntityLogicalName}'…",
                    () => _client.GetAttributesAsync(SelectedEntity.EntityLogicalName)).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                _prompts.ShowError($"Failed to load fields: {ex.Message}");
                return;
            }

            var currentlyChecked = new HashSet<string>((_filteringAttributes ?? string.Empty).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
            var items = attributes
                .Select(a => new PickItem<AttributeDefinition>(a.DisplayName, a.LogicalName, a, @checked: currentlyChecked.Contains(a.LogicalName)))
                .ToList();

            var picked = await _prompts.PickManyAsync(
                "D365: Filtering attributes",
                "Choose the attributes that should trigger this step…",
                items).ConfigureAwait(true);
            if (picked == null) { return; }

            _filteringAttributes = string.Join(",", picked.Select(p => p.Value.LogicalName));
            OnPropertyChanged(nameof(FilteringAttributesSummary));
        }

        /// <summary>Null if the user should fix something before this can be submitted.</summary>
        public string Validate()
        {
            if (string.IsNullOrWhiteSpace(StepName)) { return "Enter a name for the step."; }
            if (SelectedMessage == null) { return "Choose a message."; }
            if (SelectedStage == null) { return "Choose a stage."; }
            if (SelectedMode == null) { return "Choose an execution mode."; }
            if (!int.TryParse(Rank, out _)) { return "Execution order must be a whole number."; }
            return null;
        }
    }
}
