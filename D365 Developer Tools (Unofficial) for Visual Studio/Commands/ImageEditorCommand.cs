using System;
using System.Threading.Tasks;
using System.Windows.Interop;
using D365_Developer_Tools__Unofficial__for_Visual_Studio.Dataverse;
using D365_Developer_Tools__Unofficial__for_Visual_Studio.Shared;
using D365_Developer_Tools__Unofficial__for_Visual_Studio.Shared.Dialogs;

namespace D365_Developer_Tools__Unofficial__for_Visual_Studio.Commands
{
    /// <summary>Plugin Explorer's "Add Image..."/"Edit Image..." context menu commands.</summary>
    internal static class ImageEditorCommand
    {
        /// <summary>Returns true if an image was actually registered.</summary>
        public static async Task<bool> AddAsync(DataverseClient client, IUserPrompts prompts, string stepId, string primaryEntity)
        {
            var viewModel = new ImageEditorViewModel(client, prompts, primaryEntity);
            viewModel.LoadForAdd();

            if (!await ShowDialogAsync(viewModel).ConfigureAwait(true)) { return false; }

            try
            {
                await prompts.RunWithProgressAsync("D365: Registering image…", () =>
                    client.CreateSdkMessageStepImageAsync(stepId, viewModel.ToImageRegistrationDetails())).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                prompts.ShowError($"D365: Failed to register the image: {ex.Message}");
                return false;
            }

            prompts.ShowInfo($"D365: Registered image '{viewModel.Name}'.");
            return true;
        }

        /// <summary>Returns true if the image was actually updated.</summary>
        public static async Task<bool> EditAsync(DataverseClient client, IUserPrompts prompts, SdkMessageStepImageDefinition existingImage, string primaryEntity)
        {
            var viewModel = new ImageEditorViewModel(client, prompts, primaryEntity, isEditMode: true);
            viewModel.LoadForEdit(existingImage);

            if (!await ShowDialogAsync(viewModel).ConfigureAwait(true)) { return false; }

            try
            {
                await prompts.RunWithProgressAsync("D365: Updating image…", () =>
                    client.UpdateSdkMessageStepImageAsync(existingImage.ImageId, viewModel.ToImageRegistrationDetails())).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                prompts.ShowError($"D365: Failed to update the image: {ex.Message}");
                return false;
            }

            prompts.ShowInfo($"D365: Updated image '{viewModel.Name}'.");
            return true;
        }

        private static async Task<bool> ShowDialogAsync(ImageEditorViewModel viewModel)
        {
            var ownerHwnd = await VsShellHelper.GetMainWindowHandleAsync().ConfigureAwait(true);
            var dialog = new ImageEditorDialog(viewModel);
            if (ownerHwnd != IntPtr.Zero) { new WindowInteropHelper(dialog).Owner = ownerHwnd; }

            return dialog.ShowDialog() == true;
        }
    }
}
