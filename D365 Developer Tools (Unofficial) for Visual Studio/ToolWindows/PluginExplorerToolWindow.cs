using System.Runtime.InteropServices;
using D365_Developer_Tools__Unofficial__for_Visual_Studio.ToolWindows.ViewModels;
using Microsoft.VisualStudio.Shell;

namespace D365_Developer_Tools__Unofficial__for_Visual_Studio.ToolWindows
{
    /// <summary>Hosts PluginExplorerControl — browses plugin assemblies/types/steps/images.</summary>
    [Guid(PackageGuids.PluginExplorerToolWindowString)]
    public sealed class PluginExplorerToolWindow : ToolWindowPane
    {
        public PluginExplorerToolWindow() : base(null)
        {
            Caption = "D365 Plugin Explorer";
        }

        protected override void Initialize()
        {
            base.Initialize();

            var package = (D365DeveloperToolsPackage)Package;
            var viewModel = new PluginExplorerViewModel(package.ConnectionManager, package.DataverseClient, package.UserPrompts);
            Content = new PluginExplorerControl(viewModel);
        }
    }
}
