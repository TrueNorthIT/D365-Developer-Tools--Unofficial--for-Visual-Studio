using System;
using System.Threading.Tasks;
using D365_Developer_Tools__Unofficial__for_Visual_Studio.Dataverse;
using D365_Developer_Tools__Unofficial__for_Visual_Studio.PluginDebugging;

namespace D365_Developer_Tools__Unofficial__for_Visual_Studio.Commands
{
    /// <summary>
    /// Plugin Explorer's "Debug This Step..." context menu command. "Arming" a capture is just ensuring
    /// the org's plugintracelogsetting is "All" (a step-registration change is neither required nor
    /// possible — see the Plugin Debugging Phase 2 plan's research findings): prompts to enable it if
    /// it's currently Off, remembering the prior value so DebugPluginCaptureCommand can offer to restore
    /// it later, then jumps to the Plugin Debugging tool window pre-filtered to this step with "has
    /// capture" checked, guiding the user to trigger the action if nothing's there yet.
    /// </summary>
    internal static class DebugStepCommand
    {
        public static async Task ExecuteAsync(SdkMessageStepDefinition step, string pluginTypeName, string pluginTypeFriendlyName)
        {
            var package = await D365DeveloperToolsPackage.GetReadyInstanceAsync().ConfigureAwait(true);
            var connectionManager = package.ConnectionManager;
            var client = package.DataverseClient;
            var prompts = package.UserPrompts;

            if (!connectionManager.IsConnected)
            {
                prompts.ShowError("D365: Connect to a Dataverse environment before debugging.");
                return;
            }

            PluginTraceLogSettingsInfo settingInfo;
            try
            {
                settingInfo = await client.GetPluginTraceLogSettingAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                prompts.ShowError($"D365: Could not read the org's plugin trace logging setting: {ex.Message}");
                return;
            }

            if (settingInfo.Setting == PluginTraceLogSetting.Off)
            {
                var confirmed = await prompts.ConfirmAsync(
                    "D365: Enable plugin trace logging?",
                    "Plugin trace logging is currently Off for this environment. Debugging a real execution needs a captured trace — enable it now? (The previous setting will be offered back once you've debugged a capture.)").ConfigureAwait(true);

                if (!confirmed) { return; }

                var environmentUrl = connectionManager.Connection.EnvironmentUrl;
                TracingRestoreStore.SetPriorSetting(environmentUrl, settingInfo.Setting);

                try
                {
                    await client.SetPluginTraceLogSettingAsync(settingInfo.OrganizationId, PluginTraceLogSetting.All).ConfigureAwait(true);
                }
                catch (Exception ex)
                {
                    prompts.ShowError($"D365: Failed to enable plugin trace logging: {ex.Message}");
                    return;
                }
            }

            await package.ShowPluginDebuggingToolWindowAsync().ConfigureAwait(true);

            var label = $"{pluginTypeFriendlyName} — " +
                (step.PrimaryEntity == null ? $"{step.MessageName}" : $"{step.MessageName}: {step.PrimaryEntity}") +
                $" ({step.Stage})";

            await package.PluginDebuggingViewModel.LoadForStepAsync(pluginTypeName, label, requireCapturedProfile: true).ConfigureAwait(true);

            prompts.ShowInfo("D365: Trigger the action in Dynamics now, then Refresh the Plugin Debugging grid to find your capture.");
        }
    }
}
