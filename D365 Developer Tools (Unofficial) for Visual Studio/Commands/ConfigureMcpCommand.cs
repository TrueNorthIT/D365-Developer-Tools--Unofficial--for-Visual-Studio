using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using D365_Developer_Tools__Unofficial__for_Visual_Studio.Shared;
using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Commands;
using Microsoft.VisualStudio.Shell;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace D365_Developer_Tools__Unofficial__for_Visual_Studio.Commands
{
    /// <summary>
    /// Tools menu entry that wires the standalone D365 MCP server into Claude Code for the currently
    /// open solution. Ports extension.ts's "D365: Configure MCP Server for this Workspace" — writes
    /// (or merges into) .mcp.json next to the solution file, pointing at the MCP server bundled
    /// alongside this extension's own install.
    /// </summary>
    [VisualStudioContribution]
    internal class ConfigureMcpCommand : Command
    {
        private const string ServerKey = "d365";

        public ConfigureMcpCommand(VisualStudioExtensibility extensibility)
            : base(extensibility)
        {
        }

        public override CommandConfiguration CommandConfiguration => new("Configure MCP Server for this Solution");

        public override async Task ExecuteCommandAsync(IClientContext context, CancellationToken cancellationToken)
        {
            try
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

                var dte = await AsyncServiceProvider.GlobalProvider.GetServiceAsync(typeof(EnvDTE.DTE)) as EnvDTE.DTE;
                var solutionFullName = dte?.Solution?.FullName;
                if (string.IsNullOrEmpty(solutionFullName))
                {
                    ShowMessage("Open a solution before configuring the MCP server.", MessageBoxImage.Warning);
                    return;
                }

                var extensionDir = Path.GetDirectoryName(typeof(D365DeveloperToolsPackage).Assembly.Location);
                var serverExePath = Path.Combine(
                    extensionDir ?? string.Empty,
                    "McpServer",
                    "D365 Developer Tools (Unofficial) for Visual Studio.McpServer.exe");

                if (!File.Exists(serverExePath))
                {
                    ShowMessage("MCP server bundle not found in this extension install.", MessageBoxImage.Error);
                    return;
                }

                var solutionDir = Path.GetDirectoryName(solutionFullName);
                var mcpJsonPath = Path.Combine(solutionDir ?? string.Empty, ".mcp.json");

                var config = new JObject();
                if (File.Exists(mcpJsonPath))
                {
                    try
                    {
                        config = JObject.Parse(File.ReadAllText(mcpJsonPath));
                    }
                    catch (JsonException)
                    {
                        ShowMessage(".mcp.json exists but is not valid JSON. Fix or remove it, then retry.", MessageBoxImage.Error);
                        return;
                    }

                    if (config["mcpServers"]?[ServerKey] != null)
                    {
                        EnsureGitIgnored(solutionDir);
                        ShowMessage("MCP server is already configured for this solution.", MessageBoxImage.Information);
                        return;
                    }
                }

                if (!(config["mcpServers"] is JObject servers))
                {
                    servers = new JObject();
                    config["mcpServers"] = servers;
                }

                servers[ServerKey] = new JObject { ["command"] = serverExePath };

                File.WriteAllText(mcpJsonPath, config.ToString(Formatting.Indented) + Environment.NewLine);
                EnsureGitIgnored(solutionDir);

                ShowMessage(
                    "Configured .mcp.json for this solution — restart Claude Code to enable Dataverse schema queries.",
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                ActivityLog.LogError("D365DeveloperTools", "ConfigureMcpCommand FAILED: " + ex);
                ShowMessage($"Failed to configure the MCP server: {ex.Message}", MessageBoxImage.Error);
            }
        }

        private static void ShowMessage(string message, MessageBoxImage icon) =>
            DialogForegroundHelper.ShowMessage(message, "D365 Developer Tools", MessageBoxButton.OK, icon);

        /// <summary>
        /// .mcp.json can embed a machine-specific path to the bundled MCP server, so if the solution
        /// lives in a git repo that already has a .gitignore, keep it out of source control. Does
        /// nothing if no .gitignore exists anywhere between the solution and the repo root — this
        /// only extends an existing ignore file, it never creates one.
        /// </summary>
        private static void EnsureGitIgnored(string solutionDir)
        {
            var gitignorePath = FindNearestGitignoreInRepo(solutionDir);
            if (gitignorePath == null) { return; }

            var content = File.ReadAllText(gitignorePath);
            var alreadyIgnored = content
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Any(line => line.Trim() == ".mcp.json");
            if (alreadyIgnored) { return; }

            var separator = content.Length > 0 && !content.EndsWith("\n") ? Environment.NewLine : string.Empty;
            File.AppendAllText(gitignorePath, separator + ".mcp.json" + Environment.NewLine);
        }

        private static string FindNearestGitignoreInRepo(string startDir)
        {
            var dir = startDir;
            while (!string.IsNullOrEmpty(dir))
            {
                var candidate = Path.Combine(dir, ".gitignore");
                if (File.Exists(candidate)) { return candidate; }

                if (Directory.Exists(Path.Combine(dir, ".git"))) { return null; } // reached the repo root without finding one

                var parent = Directory.GetParent(dir);
                if (parent == null) { return null; }
                dir = parent.FullName;
            }

            return null;
        }
    }
}
