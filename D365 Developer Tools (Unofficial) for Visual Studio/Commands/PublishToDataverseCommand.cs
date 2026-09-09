using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using D365_Developer_Tools__Unofficial__for_Visual_Studio.Dataverse;
using D365_Developer_Tools__Unofficial__for_Visual_Studio.PluginDebugging;
using D365_Developer_Tools__Unofficial__for_Visual_Studio.PluginPublishing;
using D365_Developer_Tools__Unofficial__for_Visual_Studio.Shared;
using EnvDTE;

namespace D365_Developer_Tools__Unofficial__for_Visual_Studio.Commands
{
    /// <summary>
    /// "Publish to Dataverse" project context menu command. Builds the project (Release), then either
    /// updates the matching PluginAssembly/PluginPackage it finds by name, or — on a first-time publish
    /// for this project — asks which deployment model to use (remembered per-project afterward) and
    /// whether to add the new record to a solution.
    /// </summary>
    internal static class PublishToDataverseCommand
    {
        public static async Task ExecuteAsync(DTE dte, Project project)
        {
            var package = await D365DeveloperToolsPackage.GetReadyInstanceAsync().ConfigureAwait(true);
            var connectionManager = package.ConnectionManager;
            var client = package.DataverseClient;
            var prompts = package.UserPrompts;

            if (!connectionManager.IsConnected)
            {
                prompts.ShowError("D365: Connect to a Dataverse environment before publishing.");
                return;
            }

            string assemblyPath;
            try
            {
                assemblyPath = await prompts.RunWithProgressAsync(
                    $"D365: Building '{project.Name}' (Release)…",
                    () => ProjectBuilder.BuildReleaseAsync(dte, project)).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                prompts.ShowError($"D365: Build failed: {ex.Message}");
                return;
            }

            if (!File.Exists(assemblyPath))
            {
                prompts.ShowError($"D365: Could not find the built assembly at '{assemblyPath}'.");
                return;
            }

            var assemblyName = Path.GetFileNameWithoutExtension(assemblyPath);
            var contentBase64 = Convert.ToBase64String(File.ReadAllBytes(assemblyPath));
            var version = FileVersionInfo.GetVersionInfo(assemblyPath).FileVersion ?? "1.0.0.0";

            try
            {
                var existingAssembly = await client.FindPluginAssemblyByNameAsync(assemblyName).ConfigureAwait(true);
                if (existingAssembly != null)
                {
                    await UpdateAssemblyAsync(client, prompts, existingAssembly, project.FullName, assemblyPath, assemblyName, contentBase64, version).ConfigureAwait(true);
                    return;
                }

                var existingPackage = await client.FindPluginPackageByNameAsync(assemblyName).ConfigureAwait(true);
                if (existingPackage != null)
                {
                    await UpdatePackageAsync(client, prompts, existingPackage, assemblyName, contentBase64, version).ConfigureAwait(true);
                    return;
                }

                await FirstTimePublishAsync(client, prompts, project.FullName, assemblyPath, assemblyName, contentBase64, version).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                prompts.ShowError($"D365: Publish failed: {ex.Message}");
            }
        }

        private static async Task UpdateAssemblyAsync(DataverseClient client, IUserPrompts prompts, PluginRecordRef existing, string projectFilePath, string assemblyPath, string assemblyName, string contentBase64, string version)
        {
            await prompts.RunWithProgressAsync($"D365: Publishing '{assemblyName}'…", async () =>
            {
                await client.UpdatePluginAssemblyContentAsync(existing.Id, contentBase64, version).ConfigureAwait(true);
                await RegisterNewPluginTypesAsync(client, existing.Id, projectFilePath, assemblyPath, solutionUniqueName: null, assemblyName, version).ConfigureAwait(true);
            }).ConfigureAwait(true);

            prompts.ShowInfo($"D365: Published '{assemblyName}' (updated existing plugin assembly).");
        }

        private static async Task UpdatePackageAsync(DataverseClient client, IUserPrompts prompts, PluginRecordRef existing, string assemblyName, string contentBase64, string version)
        {
            // Dataverse derives the package's plugin types from its content server-side, so there's no
            // local type registration step for the package model, unlike the raw-assembly model.
            await prompts.RunWithProgressAsync(
                $"D365: Publishing '{assemblyName}'…",
                () => client.UpdatePluginPackageContentAsync(existing.Id, contentBase64, version)).ConfigureAwait(true);

            prompts.ShowInfo($"D365: Published '{assemblyName}' (updated existing plugin package).");
        }

        private static async Task FirstTimePublishAsync(DataverseClient client, IUserPrompts prompts, string projectFilePath, string assemblyPath, string assemblyName, string contentBase64, string version)
        {
            var model = PublishSettingsStore.TryGetDeploymentModel(projectFilePath);
            if (model == null)
            {
                model = await PromptForDeploymentModelAsync(prompts).ConfigureAwait(true);
                if (model == null) { return; } // user cancelled

                PublishSettingsStore.SetDeploymentModel(projectFilePath, model.Value);
            }

            var solutionUniqueName = await PromptForSolutionAsync(prompts, client).ConfigureAwait(true);

            if (model == PluginDeploymentModel.Assembly)
            {
                string newAssemblyId = null;
                await prompts.RunWithProgressAsync($"D365: Publishing '{assemblyName}'…", async () =>
                {
                    newAssemblyId = await client.CreatePluginAssemblyAsync(assemblyName, contentBase64, version, solutionUniqueName).ConfigureAwait(true);
                    await RegisterNewPluginTypesAsync(client, newAssemblyId, projectFilePath, assemblyPath, solutionUniqueName, assemblyName, version).ConfigureAwait(true);
                }).ConfigureAwait(true);

                prompts.ShowInfo($"D365: Published '{assemblyName}' as a new plugin assembly.");
            }
            else
            {
                await prompts.RunWithProgressAsync(
                    $"D365: Publishing '{assemblyName}'…",
                    () => client.CreatePluginPackageAsync(assemblyName, contentBase64, version, solutionUniqueName)).ConfigureAwait(true);

                prompts.ShowInfo($"D365: Published '{assemblyName}' as a new plugin package.");
            }
        }

        /// <summary>Scans the built assembly for IPlugin and custom-workflow-activity (CodeActivity) types and registers any not already present in Dataverse. Never removes existing plugin types.</summary>
        private static async Task RegisterNewPluginTypesAsync(DataverseClient client, string pluginAssemblyId, string projectFilePath, string assemblyPath, string solutionUniqueName, string assemblyName, string version)
        {
            var discovered = PluginTypeScanner.FindPluginTypes(assemblyPath);
            if (discovered.Count == 0) { return; }

            var existingTypeNames = await client.GetExistingPluginTypeNamesAsync(pluginAssemblyId).ConfigureAwait(true);

            // Matches the Plugin Registration Tool's own default WorkflowActivityGroupName so newly
            // registered activities show up grouped sensibly in the classic process designer.
            var workflowActivityGroupName = $"{assemblyName} ({version})";

            foreach (var type in discovered)
            {
                // Every discovered type gets mapped for Plugin Debugging's "Debug This" — regardless of
                // whether it's newly registered here or already existed — so any project published
                // through this extension needs no extra prompting to find its own source later.
                DebugTargetStore.SetProjectPath(type.TypeName, projectFilePath);

                if (existingTypeNames.Contains(type.TypeName)) { continue; }
                await client.CreatePluginTypeAsync(
                    pluginAssemblyId, type.TypeName, type.FriendlyName, solutionUniqueName,
                    type.IsWorkflowActivity ? workflowActivityGroupName : null).ConfigureAwait(true);
            }
        }

        /// <summary>Shared with <see cref="ChangeDeploymentModelCommand"/>, which lets the user revisit this choice before a project's first publish.</summary>
        internal static async Task<PluginDeploymentModel?> PromptForDeploymentModelAsync(IUserPrompts prompts, PluginDeploymentModel? current = null)
        {
            var items = new List<PickItem<PluginDeploymentModel>>
            {
                new PickItem<PluginDeploymentModel>(
                    "Traditional Plugin Assembly", "Uploads the built .dll to a Plugin Assembly record", PluginDeploymentModel.Assembly,
                    detail: current == PluginDeploymentModel.Assembly ? "Current choice" : null),
                new PickItem<PluginDeploymentModel>(
                    "NuGet-style Plugin Package", "Uploads to a Plugin Package record", PluginDeploymentModel.Package,
                    detail: current == PluginDeploymentModel.Package ? "Current choice" : null),
            };

            var pick = await prompts.PickOneAsync(
                "D365: Deployment model",
                "How should this project be published to Dataverse? (Remembered for next time.)",
                items).ConfigureAwait(true);

            return pick?.Value;
        }

        /// <summary>Returns the chosen solution's unique name, or null if the user skipped adding it to a solution.</summary>
        private static async Task<string> PromptForSolutionAsync(IUserPrompts prompts, DataverseClient client)
        {
            List<DataverseSolution> solutions;
            try
            {
                solutions = await prompts.RunWithProgressAsync("D365: Loading solutions…", () => client.GetSolutionsAsync()).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                prompts.ShowError($"D365: Failed to load solutions, publishing without adding to one: {ex.Message}");
                return null;
            }

            var items = new List<PickItem<string>> { new PickItem<string>("Don't add to a solution", null, null) };
            items.AddRange(solutions.Select(s => new PickItem<string>(s.FriendlyName, s.UniqueName, s.UniqueName)));

            var pick = await prompts.PickOneAsync("D365: Add to a solution?", "Choose a solution, or skip.", items).ConfigureAwait(true);
            return pick?.Value;
        }
    }
}
