using System;
using System.Threading.Tasks;
using D365_Developer_Tools__Unofficial__for_Visual_Studio.PluginDebugging;

namespace D365_Developer_Tools__Unofficial__for_Visual_Studio.Commands
{
    /// <summary>
    /// Stops per-step profiling — clears Dataverse's own genuine "enablepluginprofiler" flag on the step
    /// (see DataverseClient.SetStepProfilingEnabledAsync) and forgets the local session. Already-captured
    /// plugintracelog rows are completely untouched and remain debuggable afterward; the org's plugin
    /// trace logging setting is left exactly as the user set it. Available both from Plugin Explorer
    /// (toggling the menu label based on ProfilingSessionStore state) and a button in the Plugin
    /// Debugging tool window's Profile Captures tab.
    /// </summary>
    internal static class StopProfilingCommand
    {
        public static async Task ExecuteAsync(string originalStepId)
        {
            var package = await D365DeveloperToolsPackage.GetReadyInstanceAsync().ConfigureAwait(true);
            var client = package.DataverseClient;
            var prompts = package.UserPrompts;

            var entry = ProfilingSessionStore.TryGet(originalStepId);
            if (entry == null)
            {
                prompts.ShowInfo("D365: Not currently profiling this step.");
                return;
            }

            try
            {
                await client.SetStepProfilingEnabledAsync(originalStepId, false).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                prompts.ShowError($"D365: Failed to stop profiling: {ex.Message}");
                return;
            }

            ProfilingSessionStore.Clear(originalStepId);
            package.PluginDebuggingViewModel.OnProfilingStopped();
            prompts.ShowInfo("D365: Profiling stopped for this step. Already-captured executions are still debuggable; the org's plugin trace logging setting is unchanged.");
        }
    }
}
