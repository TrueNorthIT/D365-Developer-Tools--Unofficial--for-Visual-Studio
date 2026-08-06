using System;
using System.Threading.Tasks;
using System.Windows.Interop;
using D365_Developer_Tools__Unofficial__for_Visual_Studio.Dataverse;
using D365_Developer_Tools__Unofficial__for_Visual_Studio.Shared;
using D365_Developer_Tools__Unofficial__for_Visual_Studio.Shared.Dialogs;

namespace D365_Developer_Tools__Unofficial__for_Visual_Studio.Commands
{
    /// <summary>Plugin Explorer's "Add Custom API..."/"Edit Custom API..." commands.</summary>
    internal static class CustomApiEditorCommand
    {
        /// <summary>Returns true if a Custom API was actually registered.</summary>
        public static async Task<bool> AddAsync(DataverseClient client, IUserPrompts prompts)
        {
            var viewModel = new CustomApiEditorViewModel(client, prompts);
            try
            {
                await prompts.RunWithProgressAsync("D365: Loading options…", viewModel.LoadForAddAsync).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                prompts.ShowError($"D365: Failed to load options: {ex.Message}");
                return false;
            }

            if (!await ShowDialogAsync(viewModel).ConfigureAwait(true)) { return false; }

            try
            {
                await prompts.RunWithProgressAsync("D365: Registering Custom API…", () =>
                    client.CreateCustomApiAsync(viewModel.ToRegistrationDetails())).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                prompts.ShowError($"D365: Failed to register the Custom API: {ex.Message}");
                return false;
            }

            prompts.ShowInfo($"D365: Registered Custom API '{viewModel.Name}'.");
            return true;
        }

        /// <summary>Returns true if the Custom API was actually updated.</summary>
        public static async Task<bool> EditAsync(DataverseClient client, IUserPrompts prompts, CustomApiDefinition existing)
        {
            var viewModel = new CustomApiEditorViewModel(client, prompts, isEditMode: true);
            try
            {
                await prompts.RunWithProgressAsync("D365: Loading Custom API…", () => viewModel.LoadForEditAsync(existing)).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                prompts.ShowError($"D365: Failed to load the Custom API: {ex.Message}");
                return false;
            }

            if (!await ShowDialogAsync(viewModel).ConfigureAwait(true)) { return false; }

            try
            {
                await prompts.RunWithProgressAsync("D365: Updating Custom API…", () =>
                    client.UpdateCustomApiAsync(existing.CustomApiId, viewModel.ToRegistrationDetails())).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                prompts.ShowError($"D365: Failed to update the Custom API: {ex.Message}");
                return false;
            }

            prompts.ShowInfo($"D365: Updated Custom API '{viewModel.Name}'.");
            return true;
        }

        private static async Task<bool> ShowDialogAsync(CustomApiEditorViewModel viewModel)
        {
            var ownerHwnd = await VsShellHelper.GetMainWindowHandleAsync().ConfigureAwait(true);
            var dialog = new CustomApiEditorDialog(viewModel);
            if (ownerHwnd != IntPtr.Zero) { new WindowInteropHelper(dialog).Owner = ownerHwnd; }

            return dialog.ShowDialog() == true;
        }
    }
}
