using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using D365_Developer_Tools__Unofficial__for_Visual_Studio.Dataverse;
using D365_Developer_Tools__Unofficial__for_Visual_Studio.PluginDebugging;
using D365_Developer_Tools__Unofficial__for_Visual_Studio.PluginPublishing;
using D365_Developer_Tools__Unofficial__for_Visual_Studio.Shared;
using Microsoft.VisualStudio.Shell;

namespace D365_Developer_Tools__Unofficial__for_Visual_Studio.Commands
{
    /// <summary>
    /// "Debug This" on a Plugin Debugging "Profile Captures" row (a plugintracelog row with a captured
    /// profile). Resolves the local project (auto-remembered by PublishToDataverseCommand's own type
    /// discovery, auto-matched by an already-open project's AssemblyName, or a one-time picker
    /// otherwise), builds it in Debug configuration (real breakpoint/symbol fidelity — Release strips
    /// that), fetches the full capture, and hands off to PluginCaptureHostLauncher to launch the isolated
    /// host process and attach Visual Studio's debugger to it.
    /// </summary>
    internal static class DebugPluginCaptureCommand
    {
        public static async Task ExecuteAsync(PluginTraceLogEntry entry)
        {
            if (entry == null) { return; }

            var package = await D365DeveloperToolsPackage.GetReadyInstanceAsync().ConfigureAwait(true);
            var connectionManager = package.ConnectionManager;
            var client = package.DataverseClient;
            var prompts = package.UserPrompts;

            if (!connectionManager.IsConnected)
            {
                prompts.ShowError("D365: Connect to a Dataverse environment before debugging.");
                return;
            }

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var dte = package.GetDte();
            if (dte == null)
            {
                prompts.ShowError("D365: Could not access Visual Studio's automation (DTE).");
                return;
            }

            // entry.TypeName (from plugintracelog.typename) is assembly-qualified ("Namespace.Class,
            // AssemblyName, Version=..., Culture=..., PublicKeyToken=..." — see PluginTraceLogModels.cs),
            // but DebugTargetStore is auto-populated by PublishToDataverseCommand using PluginTypeScanner's
            // bare Type.FullName. Using entry.TypeName as-is here would never hit an auto-remembered
            // entry — normalize to the bare name first so both sides of DebugTargetStore actually share
            // one key space.
            var bareTypeName = entry.TypeName.Split(',')[0].Trim();
            var openProjects = VsShellHelper.GetAllProjects(dte);

            var projectFilePath = DebugTargetStore.TryGetProjectPath(bareTypeName);
            if (projectFilePath == null || !File.Exists(projectFilePath))
            {
                // Falls back to auto-matching by output assembly name before ever asking the user to pick
                // — the plugin's own project is very often already open in this same solution (that's
                // usually how it got published in the first place), so it shouldn't need a manual picker
                // just because it's never been debugged through this extension before.
                projectFilePath = TryFindOpenProjectByAssemblyName(openProjects, ExtractAssemblyName(entry.TypeName));
            }

            if (projectFilePath == null || !File.Exists(projectFilePath))
            {
                projectFilePath = PromptForProjectFile();
                if (projectFilePath == null) { return; } // user cancelled
            }

            var project = openProjects
                .FirstOrDefault(p => string.Equals(p.FullName, projectFilePath, StringComparison.OrdinalIgnoreCase));
            if (project == null)
            {
                prompts.ShowError($"D365: Could not find project '{Path.GetFileName(projectFilePath)}' in the currently open solution. Open its solution first, then try again.");
                return;
            }

            // Only remembered once the project is confirmed actually open/loadable — an unresolved pick
            // shouldn't get baked in as "the" answer for this plugin type.
            DebugTargetStore.SetProjectPath(bareTypeName, projectFilePath);

            string assemblyPath;
            try
            {
                assemblyPath = await prompts.RunWithProgressAsync(
                    $"D365: Building '{project.Name}' (Debug)…",
                    () => ProjectBuilder.BuildDebugAsync(dte, project)).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                prompts.ShowError($"D365: Build failed: {ex.Message}");
                return;
            }

            if (!File.Exists(assemblyPath))
            {
                prompts.ShowError($"D365: Could not find the built assembly at '{assemblyPath}'.");
                return;
            }

            PluginTraceLogCapture capture;
            try
            {
                capture = await prompts.RunWithProgressAsync(
                    "D365: Fetching captured execution…",
                    () => client.GetPluginTraceLogCaptureAsync(entry.TraceLogId)).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                prompts.ShowError($"D365: Failed to fetch the captured execution: {ex.Message}");
                return;
            }

            if (capture == null || string.IsNullOrEmpty(capture.ProfileBase64))
            {
                prompts.ShowError("D365: This trace log row has no captured execution to replay.");
                return;
            }

            string accessToken;
            try
            {
                accessToken = await connectionManager.GetAccessTokenAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                prompts.ShowError($"D365: Failed to acquire an access token: {ex.Message}");
                return;
            }

            var resolvedCapture = new ResolvedPluginCapture
            {
                PluginTypeName = entry.TypeName,
                MessageName = entry.MessageName,
                PrimaryEntity = entry.PrimaryEntity,
                AssemblyPath = assemblyPath,
                ProfileBase64 = capture.ProfileBase64,
                SecureConfiguration = capture.SecureConfiguration,
                EnvironmentUrl = connectionManager.Connection.EnvironmentUrl,
                AccessToken = accessToken,
                // A fresh process per debug session needs no refresh channel — an hour is comfortably
                // longer than any realistic build+attach+step-through window.
                AccessTokenExpiresOnUtc = DateTime.UtcNow.AddHours(1),
            };

            package.PluginDebuggingViewModel.BeginDebugSession($"{entry.TypeName} — {entry.MessageName}: {entry.PrimaryEntity}");

            ReplaySessionResult result;
            try
            {
                result = await PluginCaptureHostLauncher.LaunchAndAttachAsync(
                    dte, resolvedCapture, process => package.PluginDebuggingViewModel.OnDebugProcessAttached(process)).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                result = ReplaySessionResult.Failed(ex.ToString());
            }
            finally
            {
                package.PluginDebuggingViewModel.EndDebugSession();
            }

            ReportResult(prompts, result);
        }

        /// <summary>The assembly name segment of an assembly-qualified type name ("Namespace.Class, AssemblyName, Version=...") — null if entry.TypeName didn't have one (shouldn't happen for a real plugintracelog row, but this is best-effort, not load-bearing).</summary>
        private static string ExtractAssemblyName(string assemblyQualifiedTypeName)
        {
            var segments = assemblyQualifiedTypeName.Split(',');
            return segments.Length >= 2 ? segments[1].Trim() : null;
        }

        /// <summary>Matches by output assembly name, not project name — the two very often differ, and assembly name is what plugintracelog.typename actually records. Best-effort: a project kind that doesn't expose an AssemblyName property (or is otherwise unusual) is simply skipped, not treated as an error.</summary>
        private static string TryFindOpenProjectByAssemblyName(System.Collections.Generic.List<EnvDTE.Project> openProjects, string assemblyName)
        {
            if (string.IsNullOrEmpty(assemblyName)) { return null; }

            foreach (var project in openProjects)
            {
                try
                {
                    if (project.Properties?.Item("AssemblyName")?.Value is string candidateAssemblyName &&
                        string.Equals(candidateAssemblyName, assemblyName, StringComparison.OrdinalIgnoreCase))
                    {
                        return project.FullName;
                    }
                }
                catch
                {
                    // Some project kinds don't expose an AssemblyName property at all — skip, don't fail.
                }
            }

            return null;
        }

        private static string PromptForProjectFile()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "D365: Select the plugin project to debug",
                Filter = "C# Project (*.csproj)|*.csproj",
            };

            return dialog.ShowDialog() == true ? dialog.FileName : null;
        }

        private static void ReportResult(IUserPrompts prompts, ReplaySessionResult result)
        {
            if (result == null)
            {
                prompts.ShowError("D365: The debug session produced no result.");
                return;
            }

            if (!result.Succeeded)
            {
                prompts.ShowError($"D365: Debug session failed: {result.FailureMessage}");
                return;
            }

            var message = $"D365: Debug session finished. {result.SandboxedWrites.Count} write(s) were sandboxed (not sent to Dataverse).";
            if (result.UndecodedProfileEntries.Count > 0)
            {
                message += $" {result.UndecodedProfileEntries.Count} profile entr{(result.UndecodedProfileEntries.Count == 1 ? "y" : "ies")} could not be decoded.";
            }

            prompts.ShowInfo(message);
        }
    }
}
