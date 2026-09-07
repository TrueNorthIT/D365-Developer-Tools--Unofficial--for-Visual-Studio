using System.Threading.Tasks;
using D365_Developer_Tools__Unofficial__for_Visual_Studio.Dataverse;

namespace D365_Developer_Tools__Unofficial__for_Visual_Studio.Commands
{
    /// <summary>
    /// Plugin Explorer's "View Trace Logs..." context menu command — opens (or focuses) the Plugin
    /// Debugging tool window pre-filtered to the selected step. Goes straight from
    /// PluginExplorerControl's code-behind to here rather than through PluginExplorerViewModel, so
    /// Plugin Explorer doesn't need a dependency on the unrelated Plugin Debugging feature.
    /// </summary>
    internal static class ViewStepTraceLogsCommand
    {
        /// <summary>
        /// pluginTypeName is the plugin type's fully-qualified type name (matches plugintracelog.typename)
        /// — plugintracelog has no lookup back to the step itself, and PluginDebuggingViewModel scopes
        /// by TypeName alone (see its LoadForStepAsync doc comment for why message/entity aren't AND'd
        /// in too). step's message/entity/stage only appear in the label here, for context.
        /// </summary>
        public static async Task ExecuteAsync(SdkMessageStepDefinition step, string pluginTypeName, string pluginTypeFriendlyName)
        {
            var package = await D365DeveloperToolsPackage.GetReadyInstanceAsync().ConfigureAwait(true);
            await package.ShowPluginDebuggingToolWindowAsync().ConfigureAwait(true);

            var label = $"{pluginTypeFriendlyName} — " +
                (step.PrimaryEntity == null ? $"{step.MessageName}" : $"{step.MessageName}: {step.PrimaryEntity}") +
                $" ({step.Stage})";

            await package.PluginDebuggingViewModel.LoadForStepAsync(pluginTypeName, label).ConfigureAwait(true);
        }
    }
}
