using System;
using System.IO;
using System.Threading.Tasks;
using D365_Developer_Tools__Unofficial__for_Visual_Studio.Persistence;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using Newtonsoft.Json;

namespace D365_Developer_Tools__Unofficial__for_Visual_Studio.PluginDebugging
{
    /// <summary>
    /// Writes the per-session job folder, launches the isolated .DebugHost process, and attaches Visual
    /// Studio's own debugger to it via EnvDTE80.Process2.Attach2 — race-safe because the host spins on
    /// Debugger.IsAttached before doing anything else, so there's no suspended-launch/resume dance needed
    /// here, only a retry loop waiting for the new process to show up in dte.Debugger.LocalProcesses.
    /// See the Plugin Debugging Phase 2 plan's "Auto-attaching the debugger" research.
    /// </summary>
    internal static class PluginCaptureHostLauncher
    {
        private const string ManagedEngineName = "Managed";
        private const int AttachRetryCount = 100;
        private const int AttachRetryDelayMs = 100;

        /// <summary>onAttached, when given, is invoked (on the main thread) right after the debugger successfully attaches — lets the caller track the running process (e.g. for a "Stop" button) without this method needing to expose it any other way.</summary>
        public static async Task<ReplaySessionResult> LaunchAndAttachAsync(DTE dte, ResolvedPluginCapture capture, Action<System.Diagnostics.Process> onAttached = null)
        {
            var sessionId = Guid.NewGuid().ToString("N");
            var sessionDir = Path.Combine(JsonFileStore.RootDirectory, "DebugSessions", sessionId);
            Directory.CreateDirectory(sessionDir);

            try
            {
                var jobPath = WriteJobFolder(sessionDir, sessionId, capture);

                var hostExePath = GetHostExePath();
                if (!File.Exists(hostExePath))
                {
                    return ReplaySessionResult.Failed($"Debug host executable not found at '{hostExePath}'. Try rebuilding/reinstalling the extension.");
                }

                System.Diagnostics.Process process;
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                using (new ComMessageFilter())
                {
                    process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(hostExePath, $"\"{jobPath}\"")
                    {
                        UseShellExecute = false,
                    });

                    if (process == null)
                    {
                        return ReplaySessionResult.Failed("Failed to start the debug host process.");
                    }

                    var attached = await TryAttachDebuggerAsync(dte, process).ConfigureAwait(true);
                    if (!attached)
                    {
                        if (!process.HasExited) { process.Kill(); }
                        return ReplaySessionResult.Failed("Could not attach the Visual Studio debugger to the debug host process before it exited or the attach attempt was exhausted.");
                    }

                    onAttached?.Invoke(process);
                }

                // Off the main thread for the (potentially long — the developer is stepping through code)
                // wait, so this doesn't freeze the IDE for the duration of the debug session.
                await Task.Run(() => process.WaitForExit()).ConfigureAwait(true);

                return ReadResult(jobPath, sessionDir, process.ExitCode);
            }
            finally
            {
                TryDeleteDirectory(sessionDir);
            }
        }

        private static string WriteJobFolder(string sessionDir, string sessionId, ResolvedPluginCapture capture)
        {
            var profilePath = Path.Combine(sessionDir, "profile.bin");
            File.WriteAllBytes(profilePath, Convert.FromBase64String(capture.ProfileBase64));

            string secureConfigPath = null;
            if (!string.IsNullOrEmpty(capture.SecureConfiguration))
            {
                secureConfigPath = Path.Combine(sessionDir, "secureconfig.txt");
                File.WriteAllText(secureConfigPath, capture.SecureConfiguration);
            }

            // Deliberately an anonymous object, not DebugHost's own JobRequest type — the two projects
            // don't share a reference, so this is the wire shape, hand-kept in sync with JobRequest.cs's
            // property names. .DebugHost proxies live Retrieve/RetrieveMultiple/read-shaped Execute calls
            // against the real org during replay (writes are sandboxed/recorded, never sent) — see
            // SandboxedOrganizationService — so EnvironmentUrl/AccessToken do need to travel with the job.
            var job = new
            {
                SchemaVersion = 1,
                SessionId = sessionId,
                capture.PluginTypeName,
                capture.MessageName,
                capture.PrimaryEntity,
                capture.AssemblyPath,
                ProfileFilePath = profilePath,
                SecureConfigFilePath = secureConfigPath,
                capture.EnvironmentUrl,
                capture.AccessToken,
                capture.AccessTokenExpiresOnUtc,
            };

            var jobPath = Path.Combine(sessionDir, "job.json");
            File.WriteAllText(jobPath, JsonConvert.SerializeObject(job, Formatting.Indented));
            return jobPath;
        }

        private static async Task<bool> TryAttachDebuggerAsync(DTE dte, System.Diagnostics.Process process)
        {
            for (var attempt = 0; attempt < AttachRetryCount; attempt++)
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                if (process.HasExited) { return false; }

                foreach (EnvDTE.Process candidate in dte.Debugger.LocalProcesses)
                {
                    if (candidate.ProcessID == process.Id)
                    {
                        ((Process2)candidate).Attach2(ManagedEngineName);
                        return true;
                    }
                }

                await Task.Delay(AttachRetryDelayMs).ConfigureAwait(true);
            }

            return false;
        }

        private static ReplaySessionResult ReadResult(string jobPath, string sessionDir, int exitCode)
        {
            var resultPath = jobPath + ".result.json";
            if (!File.Exists(resultPath))
            {
                return ReplaySessionResult.Failed($"The debug host process exited (code {exitCode}) without writing a result file — check the session folder at '{sessionDir}'.");
            }

            var result = JsonConvert.DeserializeObject<ReplaySessionResult>(File.ReadAllText(resultPath));
            return result ?? ReplaySessionResult.Failed("The debug host process wrote an unreadable result file.");
        }

        private static string GetHostExePath()
        {
            var extensionDir = Path.GetDirectoryName(typeof(PluginCaptureHostLauncher).Assembly.Location);
            return Path.Combine(extensionDir ?? string.Empty, "DebugHost", "D365 Developer Tools (Unofficial) for Visual Studio.DebugHost.exe");
        }

        private static void TryDeleteDirectory(string path)
        {
            try { Directory.Delete(path, recursive: true); }
            catch { /* best-effort cleanup only — a leftover session folder is a minor annoyance, not a failure */ }
        }
    }
}
