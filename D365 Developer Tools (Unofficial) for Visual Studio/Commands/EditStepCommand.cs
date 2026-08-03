using System;
using System.Threading.Tasks;
using System.Windows.Interop;
using D365_Developer_Tools__Unofficial__for_Visual_Studio.Dataverse;
using D365_Developer_Tools__Unofficial__for_Visual_Studio.Shared;
using D365_Developer_Tools__Unofficial__for_Visual_Studio.Shared.Dialogs;

namespace D365_Developer_Tools__Unofficial__for_Visual_Studio.Commands
{
    /// <summary>
    /// Plugin Explorer's "Edit Step..." context menu command — loads an already-registered
    /// SdkMessageProcessingStep's current values into StepEditorDialog and updates it on save.
    /// </summary>
    internal static class EditStepCommand
    {
        /// <summary>Returns true if the step was actually updated.</summary>
        public static async Task<bool> ExecuteAsync(DataverseClient client, IUserPrompts prompts, SdkMessageStepDefinition step, string pluginTypeFriendlyName)
        {
            var viewModel = new StepEditorViewModel(client, prompts, pluginTypeFriendlyName, isEditMode: true);
            try
            {
                await prompts.RunWithProgressAsync("D365: Loading step…", () => viewModel.LoadForEditAsync(step)).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                prompts.ShowError($"D365: Failed to load the step: {ex.Message}");
                return false;
            }

            var ownerHwnd = await VsShellHelper.GetMainWindowHandleAsync().ConfigureAwait(true);
            var dialog = new StepEditorDialog(viewModel);
            if (ownerHwnd != IntPtr.Zero) { new WindowInteropHelper(dialog).Owner = ownerHwnd; }

            if (dialog.ShowDialog() != true) { return false; }

            try
            {
                await prompts.RunWithProgressAsync("D365: Updating step…", () =>
                    client.UpdateSdkMessageStepAsync(step.StepId, viewModel.ToStepRegistrationDetails())).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                prompts.ShowError($"D365: Failed to update the step: {ex.Message}");
                return false;
            }

            prompts.ShowInfo($"D365: Updated step '{viewModel.StepName}'.");
            return true;
        }
    }
}
