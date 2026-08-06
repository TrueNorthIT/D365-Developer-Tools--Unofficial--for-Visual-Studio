using System.Runtime.InteropServices;
using D365_Developer_Tools__Unofficial__for_Visual_Studio.ToolWindows.ViewModels;
using Microsoft.VisualStudio.Shell;

namespace D365_Developer_Tools__Unofficial__for_Visual_Studio.ToolWindows
{
    /// <summary>Hosts EntityExplorerControl — the tool window replacement for entityExplorerWebview.ts.</summary>
    [Guid(PackageGuids.EntityExplorerToolWindowString)]
    public sealed class EntityExplorerToolWindow : ToolWindowPane
    {
        public EntityExplorerToolWindow() : base(null)
        {
            Caption = "D365 Entity Explorer";
        }

        protected override void Initialize()
        {
            base.Initialize();

            var package = (D365DeveloperToolsPackage)Package;
            var viewModel = new EntityExplorerViewModel(package.ConnectionManager, package.DataverseClient, package.UserPrompts);
            Content = new EntityExplorerControl(viewModel);
        }
    }
}
