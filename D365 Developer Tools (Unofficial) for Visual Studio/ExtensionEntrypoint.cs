using Microsoft.VisualStudio.Extensibility;

namespace D365_Developer_Tools__Unofficial__for_Visual_Studio
{
    /// <summary>
    /// Entry point for the VisualStudio.Extensibility side of this hybrid extension. The classic
    /// AsyncPackage (D365DeveloperToolsPackage) still owns all real state and logic; this just
    /// declares the in-process extension so its commands can be contributed the new way, which is
    /// what actually renders in Visual Studio 2026's ribbon shell.
    /// </summary>
    [VisualStudioContribution]
    internal class ExtensionEntrypoint : Extension
    {
        public override ExtensionConfiguration ExtensionConfiguration => new()
        {
            RequiresInProcessHosting = true,
        };
    }
}
