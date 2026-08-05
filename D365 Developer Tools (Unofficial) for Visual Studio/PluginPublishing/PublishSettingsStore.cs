using System.IO;
using D365_Developer_Tools__Unofficial__for_Visual_Studio.Persistence;

namespace D365_Developer_Tools__Unofficial__for_Visual_Studio.PluginPublishing
{
    internal enum PluginDeploymentModel
    {
        Assembly,
        Package,
    }

    internal sealed class PublishSettings
    {
        public PluginDeploymentModel DeploymentModel { get; set; }
    }

    /// <summary>
    /// Remembers, per project file, which deployment model "Publish to Dataverse" should use for a
    /// project's first-ever publish — once a matching PluginAssembly or PluginPackage already exists
    /// remotely, the model is detected from that instead and this stored preference is ignored.
    /// </summary>
    internal static class PublishSettingsStore
    {
        private static string PathFor(string projectFilePath) =>
            Path.Combine(JsonFileStore.RootDirectory, "publish-settings", JsonFileStore.HashKey(projectFilePath) + ".json");

        public static PluginDeploymentModel? TryGetDeploymentModel(string projectFilePath) =>
            JsonFileStore.Load<PublishSettings>(PathFor(projectFilePath))?.DeploymentModel;

        public static void SetDeploymentModel(string projectFilePath, PluginDeploymentModel model) =>
            JsonFileStore.Save(PathFor(projectFilePath), new PublishSettings { DeploymentModel = model });
    }
}
