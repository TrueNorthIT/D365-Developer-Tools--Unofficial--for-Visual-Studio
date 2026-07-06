using System;
using System.IO;
using System.Threading.Tasks;
using EnvDTE;
using Microsoft.VisualStudio.Shell;

namespace D365_Developer_Tools__Unofficial__for_Visual_Studio.PluginPublishing
{
    internal static class ProjectBuilder
    {
        /// <summary>Builds the given project in the Release configuration and returns the path to its built output assembly.</summary>
        public static async Task<string> BuildReleaseAsync(DTE dte, Project project)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            var solutionBuild = dte.Solution.SolutionBuild;
            solutionBuild.BuildProject("Release", project.UniqueName, true);

            if (solutionBuild.LastBuildInfo != 0)
            {
                throw new InvalidOperationException($"Build failed for '{project.Name}' (Release configuration). Check the Output window for details.");
            }

            return GetOutputAssemblyPath(project);
        }

        private static string GetOutputAssemblyPath(Project project)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var projectDir = Path.GetDirectoryName(project.FullName);
            var platformName = project.ConfigurationManager.ActiveConfiguration.PlatformName;
            var releaseConfig = project.ConfigurationManager.Item("Release", platformName);
            var outputPath = (string)releaseConfig.Properties.Item("OutputPath").Value;
            var outputFileName = (string)project.Properties.Item("OutputFileName").Value;

            return Path.GetFullPath(Path.Combine(projectDir ?? string.Empty, outputPath, outputFileName));
        }
    }
}
