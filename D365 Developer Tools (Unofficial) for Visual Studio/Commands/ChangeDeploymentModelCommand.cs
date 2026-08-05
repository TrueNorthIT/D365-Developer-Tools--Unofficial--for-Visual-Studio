using System;
using System.IO;
using System.Threading.Tasks;
using D365_Developer_Tools__Unofficial__for_Visual_Studio.PluginPublishing;
using EnvDTE;

namespace D365_Developer_Tools__Unofficial__for_Visual_Studio.Commands
{
    /// <summary>
    /// "Change Deployment Model..." project context menu command. Lets the user revisit the
    /// Assembly-vs-Package choice that <see cref="PublishToDataverseCommand"/> remembers per project —
    /// useful when that choice was made by mistake, or before a project's first-ever publish.
    /// Has no effect once a matching PluginAssembly or PluginPackage already exists remotely, since
    /// PublishToDataverseCommand detects the model from that record and ignores the stored preference.
    /// </summary>
    internal static class ChangeDeploymentModelCommand
    {
        public static async Task ExecuteAsync(DTE dte, Project project)
        {
            var package = await D365DeveloperToolsPackage.GetReadyInstanceAsync().ConfigureAwait(true);
            var connectionManager = package.ConnectionManager;
            var client = package.DataverseClient;
            var prompts = package.UserPrompts;

            if (!connectionManager.IsConnected)
            {
                prompts.ShowError("D365: Connect to a Dataverse environment before changing the deployment model.");
                return;
            }

            var assemblyName = Path.GetFileNameWithoutExtension((string)project.Properties.Item("OutputFileName").Value);

            try
            {
                var existingAssembly = await client.FindPluginAssemblyByNameAsync(assemblyName).ConfigureAwait(true);
                var existingPackage = existingAssembly == null
                    ? await client.FindPluginPackageByNameAsync(assemblyName).ConfigureAwait(true)
                    : null;

                if (existingAssembly != null || existingPackage != null)
                {
                    var kind = existingAssembly != null ? "Plugin Assembly" : "Plugin Package";
                    prompts.ShowError(
                        $"D365: '{assemblyName}' is already published as a {kind} record. The deployment model is " +
                        "determined by that existing record and can't be changed here — remove or rename it in " +
                        "Dataverse first if you want to switch models.");
                    return;
                }
            }
            catch (Exception ex)
            {
                prompts.ShowError($"D365: Could not check existing Dataverse records: {ex.Message}");
                return;
            }

            var current = PublishSettingsStore.TryGetDeploymentModel(project.FullName);
            var model = await PublishToDataverseCommand.PromptForDeploymentModelAsync(prompts, current).ConfigureAwait(true);
            if (model == null) { return; } // user cancelled

            PublishSettingsStore.SetDeploymentModel(project.FullName, model.Value);

            var label = model == PluginDeploymentModel.Assembly ? "Traditional Plugin Assembly" : "NuGet-style Plugin Package";
            prompts.ShowInfo($"D365: '{project.Name}' will publish as a {label} next time.");
        }
    }
}
