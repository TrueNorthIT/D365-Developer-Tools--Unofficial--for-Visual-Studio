using System;

namespace D365_Developer_Tools__Unofficial__for_Visual_Studio
{
    internal static class PackageGuids
    {
        public const string D365DeveloperToolsPackageString = "8f2b6c4e-1a3d-4e6f-9b2a-1c4d5e6f7a8b";
        public const string EntityExplorerToolWindowString = "3d5e6f7a-8b9c-4d1e-af2b-3c4d5e6f7a9c";
        public const string PluginExplorerToolWindowString = "4e6f7a8b-9c1d-4e2f-b3a4-5c6d7e8f9a1d";

        /// <summary>
        /// The Solution Explorer project-node context menu ("Publish to Dataverse") is contributed via
        /// classic VSCT rather than the new VisualStudio.Extensibility model — as of this SDK version,
        /// CommandPlacement.KnownPlacements only covers the Tools/View/Extensions menus, not Solution
        /// Explorer item context menus, so there's no declarative placement to target there yet.
        /// </summary>
        public const string ProjectContextMenuCmdSetString = "5f7a8b9c-1d2e-4f3a-b4c5-d6e7f8a9b0c1";
        public static readonly Guid ProjectContextMenuCmdSet = new Guid(ProjectContextMenuCmdSetString);
    }
}
