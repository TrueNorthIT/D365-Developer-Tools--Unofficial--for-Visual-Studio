using System;
using System.Threading.Tasks;
using D365_Developer_Tools__Unofficial__for_Visual_Studio.Dataverse;
using D365_Developer_Tools__Unofficial__for_Visual_Studio.PluginDebugging;

namespace D365_Developer_Tools__Unofficial__for_Visual_Studio.Commands
{
    /// <summary>
    /// Plugin Explorer's "Start Profiling..." context menu command. Arms Dataverse's own genuine,
    /// per-step "enablepluginprofiler" flag on the target step (confirmed live against a real org — see
    /// DataverseClient.SetStepProfilingEnabledAsync) — real, per-step, server-side, and completely
    /// independent of the separate "Plug-in Profiler" managed solution (which isn't installed in this
    /// org and isn't needed at all). ProfilingSessionStore then just mirrors that state locally so the
    /// Start/Stop menu toggle and tree indicator don't need a live server round-trip on every context
    /// menu open, and remembers when the session started (used to scope the Profile Captures tab).
    ///
    /// The org's trace-logging level itself is a separate, manual control (a dropdown in the Plugin
    /// Debugging tool window, see PluginDebuggingViewModel.SelectedTracingLevel) — deliberately not
    /// auto-toggled here. If it's currently Off, nothing will be captured regardless of this step's own
    /// flag; that's flagged as a non-blocking warning below (Exception is a legitimate, deliberate
    /// choice — captures only executions that throw — and never warned about; only Off, where nothing at
    /// all gets captured, is).
    /// </summary>
    internal static class StartProfilingCommand
    {
        public static async Task ExecuteAsync(SdkMessageStepDefinition step, string pluginTypeName, string pluginTypeFriendlyName)
        {
            var package = await D365DeveloperToolsPackage.GetReadyInstanceAsync().ConfigureAwait(true);
            var connectionManager = package.ConnectionManager;
            var client = package.DataverseClient;
            var prompts = package.UserPrompts;

            if (!connectionManager.IsConnected)
            {
                prompts.ShowError("D365: Connect to a Dataverse environment before profiling.");
                return;
            }

            var existing = ProfilingSessionStore.TryGet(step.StepId);
            if (existing != null)
            {
                await OpenProfileCapturesAsync(package, pluginTypeName, pluginTypeFriendlyName, step, existing.StartedAtUtc).ConfigureAwait(true);
                prompts.ShowInfo($"D365: Already profiling this step (since {existing.StartedAtUtc:t} UTC). Trigger the action, or Stop Profiling first.");
                return;
            }

            try
            {
                await client.SetStepProfilingEnabledAsync(step.StepId, true).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                prompts.ShowError($"D365: Failed to start profiling: {ex.Message}");
                return;
            }

            // Best-effort, non-blocking — see the class summary above.
            try
            {
                var settingInfo = await client.GetPluginTraceLogSettingAsync().ConfigureAwait(true);
                if (settingInfo?.Setting == PluginTraceLogSetting.Off)
                {
                    prompts.ShowInfo(
                        "D365: Plugin trace logging is currently Off for this environment — no executions will be captured until you change " +
                        "it (use the dropdown at the top of the Plugin Debugging tool window). Profiling is armed on the step regardless; " +
                        "nothing further is needed here once you change the setting.");
                }
            }
            catch
            {
                // A failed check here shouldn't block arming — worst case, the user just doesn't get the warning.
            }

            var startedAtUtc = DateTime.UtcNow;
            ProfilingSessionStore.Set(step.StepId, new ProfilingSessionEntry { StartedAtUtc = startedAtUtc });

            await OpenProfileCapturesAsync(package, pluginTypeName, pluginTypeFriendlyName, step, startedAtUtc).ConfigureAwait(true);
            prompts.ShowInfo("D365: Trigger the action in Dynamics now, then Refresh to see captures. Use Stop Profiling when you're done.");
        }

        private static async Task OpenProfileCapturesAsync(D365DeveloperToolsPackage package, string pluginTypeName, string pluginTypeFriendlyName, SdkMessageStepDefinition step, DateTime sessionStartedAtUtc)
        {
            await package.ShowPluginDebuggingToolWindowAsync().ConfigureAwait(true);

            var label = $"{pluginTypeFriendlyName} — " +
                (step.PrimaryEntity == null ? $"{step.MessageName}" : $"{step.MessageName}: {step.PrimaryEntity}") +
                $" ({step.Stage})";

            await package.PluginDebuggingViewModel.LoadForProfiledStepAsync(step.StepId, pluginTypeName, sessionStartedAtUtc, label).ConfigureAwait(true);
        }
    }
}
