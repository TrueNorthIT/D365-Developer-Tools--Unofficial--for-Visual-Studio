using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Commands;

namespace D365_Developer_Tools__Unofficial__for_Visual_Studio.Commands
{
    /// <summary>
    /// Groups this extension's Tools menu commands into a single "D365 Developer Tools" flyout,
    /// rather than each command placing itself directly on the Tools menu.
    /// </summary>
    internal static class D365ToolsMenu
    {
        [VisualStudioContribution]
        internal static MenuConfiguration Menu => new("D365 Developer Tools")
        {
            Placements = new[] { CommandPlacement.KnownPlacements.ToolsMenu },
            Children = new[]
            {
                MenuChild.Command<ConnectionMenuCommand>(),
                MenuChild.Command<ConfigureMcpCommand>(),
                MenuChild.Separator,
                MenuChild.Command<ShowEntityExplorerCommand>(),
                MenuChild.Command<ShowPluginExplorerCommand>(),
            },
        };
    }
}
