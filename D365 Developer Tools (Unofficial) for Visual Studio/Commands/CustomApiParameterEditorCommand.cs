using System;
using System.Threading.Tasks;
using System.Windows.Interop;
using D365_Developer_Tools__Unofficial__for_Visual_Studio.Dataverse;
using D365_Developer_Tools__Unofficial__for_Visual_Studio.Shared;
using D365_Developer_Tools__Unofficial__for_Visual_Studio.Shared.Dialogs;

namespace D365_Developer_Tools__Unofficial__for_Visual_Studio.Commands
{
    /// <summary>Plugin Explorer's "Add Request Parameter.../Add Response Property..." and "Edit..." commands — shared since the two entities are structurally identical apart from IsOptional.</summary>
    internal static class CustomApiParameterEditorCommand
    {
        /// <summary>Returns true if a parameter/property was actually registered.</summary>
        public static async Task<bool> AddAsync(DataverseClient client, IUserPrompts prompts, string customApiId, bool isRequestParameter)
        {
            var viewModel = new CustomApiParameterEditorViewModel(isRequestParameter);
            viewModel.LoadForAdd();

            if (!await ShowDialogAsync(viewModel).ConfigureAwait(true)) { return false; }

            try
            {
                await prompts.RunWithProgressAsync("D365: Registering…", () => isRequestParameter
                    ? client.CreateCustomApiRequestParameterAsync(customApiId, viewModel.ToRegistrationDetails())
                    : client.CreateCustomApiResponsePropertyAsync(customApiId, viewModel.ToRegistrationDetails())).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                prompts.ShowError($"D365: Failed to register '{viewModel.Name}': {ex.Message}");
                return false;
            }

            prompts.ShowInfo($"D365: Registered '{viewModel.Name}'.");
            return true;
        }

        /// <summary>Returns true if the parameter/property was actually updated.</summary>
        public static async Task<bool> EditAsync(DataverseClient client, IUserPrompts prompts, CustomApiParameterDefinition existing)
        {
            var viewModel = new CustomApiParameterEditorViewModel(existing.IsRequestParameter, isEditMode: true);
            viewModel.LoadForEdit(existing);

            if (!await ShowDialogAsync(viewModel).ConfigureAwait(true)) { return false; }

            try
            {
                await prompts.RunWithProgressAsync("D365: Updating…", () => existing.IsRequestParameter
                    ? client.UpdateCustomApiRequestParameterAsync(existing.Id, viewModel.ToRegistrationDetails())
                    : client.UpdateCustomApiResponsePropertyAsync(existing.Id, viewModel.ToRegistrationDetails())).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                prompts.ShowError($"D365: Failed to update '{viewModel.Name}': {ex.Message}");
                return false;
            }

            prompts.ShowInfo($"D365: Updated '{viewModel.Name}'.");
            return true;
        }

        private static async Task<bool> ShowDialogAsync(CustomApiParameterEditorViewModel viewModel)
        {
            var ownerHwnd = await VsShellHelper.GetMainWindowHandleAsync().ConfigureAwait(true);
            var dialog = new CustomApiParameterEditorDialog(viewModel);
            if (ownerHwnd != IntPtr.Zero) { new WindowInteropHelper(dialog).Owner = ownerHwnd; }

            return dialog.ShowDialog() == true;
        }
    }
}
