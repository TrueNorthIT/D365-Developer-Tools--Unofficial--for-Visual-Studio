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
        public static Task<string> BuildReleaseAsync(DTE dte, Project project) => BuildAsync(dte, project, "Release");

        /// <summary>
        /// Builds the given project in the Debug configuration and returns the path to its built output
        /// assembly. Used by Plugin Debugging's "Debug This" — Release strips the PDB fidelity real
        /// breakpoints need, unlike the Release build "Publish to Dataverse" itself uses.
        /// </summary>
        public static Task<string> BuildDebugAsync(DTE dte, Project project) => BuildAsync(dte, project, "Debug");

        private static async Task<string> BuildAsync(DTE dte, Project project, string configurationName)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            var solutionBuild = dte.Solution.SolutionBuild;
            solutionBuild.BuildProject(configurationName, project.UniqueName, true);

            if (solutionBuild.LastBuildInfo != 0)
            {
                throw new InvalidOperationException($"Build failed for '{project.Name}' ({configurationName} configuration). Check the Output window for details.");
            }

            return GetOutputAssemblyPath(project, configurationName);
        }

        private static string GetOutputAssemblyPath(Project project, string configurationName)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var projectDir = Path.GetDirectoryName(project.FullName);
            var platformName = project.ConfigurationManager.ActiveConfiguration.PlatformName;
            var config = project.ConfigurationManager.Item(configurationName, platformName);
            var outputPath = (string)config.Properties.Item("OutputPath").Value;
            var outputFileName = (string)project.Properties.Item("OutputFileName").Value;

            return Path.GetFullPath(Path.Combine(projectDir ?? string.Empty, outputPath, outputFileName));
        }
    }
}
