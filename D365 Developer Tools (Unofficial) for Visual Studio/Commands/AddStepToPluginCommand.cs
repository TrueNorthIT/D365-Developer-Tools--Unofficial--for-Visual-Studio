using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Interop;
using D365_Developer_Tools__Unofficial__for_Visual_Studio.Dataverse;
using D365_Developer_Tools__Unofficial__for_Visual_Studio.PluginPublishing;
using D365_Developer_Tools__Unofficial__for_Visual_Studio.Shared;
using D365_Developer_Tools__Unofficial__for_Visual_Studio.Shared.Dialogs;

namespace D365_Developer_Tools__Unofficial__for_Visual_Studio.Commands
{
    /// <summary>
    /// "D365: Add Step..." .cs file context menu command. Finds the IPlugin class(es) in the clicked
    /// file, looks up the matching PluginType already published to Dataverse (see
    /// PublishToDataverseCommand — a type has to be published before a step can be added to it), then
    /// walks through StepEditorDialog to register a new SdkMessageProcessingStep.
    /// </summary>
    internal static class AddStepToPluginCommand
    {
        public static async Task ExecuteAsync(string filePath)
        {
            var package = await D365DeveloperToolsPackage.GetReadyInstanceAsync().ConfigureAwait(true);
            var client = package.DataverseClient;
            var prompts = package.UserPrompts;

            if (!package.ConnectionManager.IsConnected)
            {
                prompts.ShowError("D365: Connect to a Dataverse environment before adding a step.");
                return;
            }

            string sourceText;
            try
            {
                sourceText = File.ReadAllText(filePath);
            }
            catch (Exception ex)
            {
                prompts.ShowError($"D365: Could not read '{filePath}': {ex.Message}");
                return;
            }

            var candidateTypeNames = PluginTypeNameExtractor.FindPluginTypeNames(sourceText);
            if (candidateTypeNames.Count == 0)
            {
                prompts.ShowError("D365: Could not find a class implementing IPlugin in this file.");
                return;
            }

            string typeName;
            if (candidateTypeNames.Count == 1)
            {
                typeName = candidateTypeNames[0];
            }
            else
            {
                var items = candidateTypeNames.Select(n => new PickItem<string>(n, null, n)).ToList();
                var pick = await prompts.PickOneAsync("D365: Choose a plugin type", "This file has more than one IPlugin class…", items).ConfigureAwait(true);
                if (pick == null) { return; }
                typeName = pick.Value;
            }

            List<PluginTypeMatch> matches;
            try
            {
                matches = await prompts.RunWithProgressAsync(
                    $"D365: Looking up '{typeName}'…",
                    () => client.FindPluginTypesByTypeNameAsync(typeName)).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                prompts.ShowError($"D365: Failed to look up the plugin type: {ex.Message}");
                return;
            }

            if (matches.Count == 0)
            {
                prompts.ShowError(
                    $"D365: '{typeName}' hasn't been published to Dataverse yet. Publish the project first " +
                    "(right-click it → D365: Publish to Dataverse...).");
                return;
            }

            PluginTypeMatch match;
            if (matches.Count == 1)
            {
                match = matches[0];
            }
            else
            {
                var items = matches.Select(m => new PickItem<PluginTypeMatch>(m.FriendlyName, m.AssemblyName, m)).ToList();
                var pick = await prompts.PickOneAsync(
                    "D365: Choose a plugin assembly",
                    $"'{typeName}' is registered under more than one assembly…",
                    items).ConfigureAwait(true);
                if (pick == null) { return; }
                match = pick.Value;
            }

            await RunAddStepDialogAsync(client, prompts, match.PluginTypeId, match.PluginAssemblyId, match.FriendlyName).ConfigureAwait(true);
        }

        /// <summary>
        /// Shared with the Plugin Explorer's "Add Step..." context menu command, which already knows the
        /// plugin type/assembly directly and doesn't need the file-scanning/type-lookup steps above.
        /// Returns true if a step was actually registered.
        /// </summary>
        public static async Task<bool> RunAddStepDialogAsync(DataverseClient client, IUserPrompts prompts, string pluginTypeId, string pluginAssemblyId, string pluginTypeFriendlyName)
        {
            var viewModel = new StepEditorViewModel(client, prompts, pluginTypeFriendlyName);
            try
            {
                await prompts.RunWithProgressAsync("D365: Loading step options…", () => viewModel.LoadAsync(pluginAssemblyId)).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                prompts.ShowError($"D365: Failed to load step options: {ex.Message}");
                return false;
            }

            var ownerHwnd = await VsShellHelper.GetMainWindowHandleAsync().ConfigureAwait(true);
            var dialog = new StepEditorDialog(viewModel);
            if (ownerHwnd != IntPtr.Zero) { new WindowInteropHelper(dialog).Owner = ownerHwnd; }

            if (dialog.ShowDialog() != true) { return false; }

            try
            {
                await prompts.RunWithProgressAsync("D365: Registering step…", () =>
                    client.CreateSdkMessageStepAsync(pluginTypeId, viewModel.ToStepRegistrationDetails())).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                prompts.ShowError($"D365: Failed to register the step: {ex.Message}");
                return false;
            }

            prompts.ShowInfo($"D365: Registered step '{viewModel.StepName}'.");
            return true;
        }
    }
}
