using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using D365DeveloperTools.DebugHost.Profile;
using D365DeveloperTools.DebugHost.Replay;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Newtonsoft.Json;

namespace D365DeveloperTools.DebugHost
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            if (args.Length < 1)
            {
                Console.Error.WriteLine("Usage: D365 Developer Tools (Unofficial) for Visual Studio.DebugHost.exe <job.json path>");
                Console.Error.WriteLine("       D365 Developer Tools (Unofficial) for Visual Studio.DebugHost.exe --decode <raw profile.bin path>");
                return 1;
            }

            if (string.Equals(args[0], "--decode", StringComparison.OrdinalIgnoreCase) && args.Length >= 2)
            {
                return RunDecodeDiagnostic(args[1]);
            }

            if (string.Equals(args[0], "--hexdump", StringComparison.OrdinalIgnoreCase) && args.Length >= 2)
            {
                Console.WriteLine(HexDump(File.ReadAllBytes(args[1])));
                return 0;
            }

            if (string.Equals(args[0], "--rawnodes", StringComparison.OrdinalIgnoreCase) && args.Length >= 3)
            {
                return RunRawNodeDump(args[1], int.Parse(args[2]));
            }

            if (string.Equals(args[0], "--dumpentry", StringComparison.OrdinalIgnoreCase) && args.Length >= 3)
            {
                return RunDumpEntry(args[1], int.Parse(args[2]));
            }

            if (string.Equals(args[0], "--replaydiag", StringComparison.OrdinalIgnoreCase) && args.Length >= 2)
            {
                return RunReplayDiagnostic(args[1]);
            }

            var jobPath = args[0];
            var resultPath = jobPath + ".result.json";

            JobRequest job;
            try
            {
                job = JsonConvert.DeserializeObject<JobRequest>(File.ReadAllText(jobPath));
            }
            catch (Exception ex)
            {
                WriteResult(resultPath, ReplayResult.Failed($"Could not read job file: {ex.Message}"));
                return 1;
            }

            // Race-safe attach: spin, don't launch suspended — see the Plugin Debugging Phase 2 plan's
            // auto-attach research (EnvDTE80.Process2.Attach2("Managed"), found via dte.Debugger.LocalProcesses).
            // The extension attaches while this loop runs, so execution never proceeds past this point
            // until a debugger is actually attached.
            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (!Debugger.IsAttached)
            {
                if (DateTime.UtcNow > deadline)
                {
                    WriteResult(resultPath, ReplayResult.Failed("Timed out waiting for the Visual Studio debugger to attach."));
                    return 1;
                }

                Thread.Sleep(50);
            }

            try
            {
                return RunReplay(job, resultPath);
            }
            catch (Exception ex)
            {
                WriteResult(resultPath, ReplayResult.Failed(ex.ToString()));
                return 1;
            }
        }

        /// <summary>
        /// Decode → construct → invoke — the actual replay, once a debugger is confirmed attached.
        /// Failures here are reported through the result file rather than thrown further (the caller's
        /// own catch is a last-resort backstop for anything unexpected, e.g. a bad job file path).
        /// </summary>
        private static int RunReplay(JobRequest job, string resultPath)
        {
            var rawProfileBytes = File.ReadAllBytes(job.ProfileFilePath);
            var contextResult = ProfileContextBuilder.Build(rawProfileBytes, job.MessageName, job.PrimaryEntity);
            if (!contextResult.Succeeded)
            {
                var contextFailure = ReplayResult.Failed(contextResult.FailureReason);
                contextFailure.TraceMessages.AddRange(contextResult.TraceNotes);
                contextFailure.UndecodedProfileEntries.AddRange(contextResult.UndecodedProfileEntries);
                WriteResult(resultPath, contextFailure);
                return 1;
            }

            // Token-provider-function constructor — connects using the already-issued access token in
            // the job file directly, no separate auth of its own (see the Plugin Debugging Phase 2 plan;
            // a fresh process per debug session means no refresh channel is needed for v1).
            using (var serviceClient = new ServiceClient(new Uri(job.EnvironmentUrl), _ => Task.FromResult(job.AccessToken), useUniqueInstance: true))
            {
                if (!serviceClient.IsReady)
                {
                    var connectionFailure = ReplayResult.Failed($"Could not establish the live read connection to '{job.EnvironmentUrl}': {serviceClient.LastError}");
                    connectionFailure.TraceMessages.AddRange(contextResult.TraceNotes);
                    WriteResult(resultPath, connectionFailure);
                    return 1;
                }

                var sandboxedService = new SandboxedOrganizationService(serviceClient);
                var tracingService = new RecordingTracingService();
                var serviceProvider = new DebugHostServiceProvider(contextResult.Context, sandboxedService, tracingService);

                string secureConfig = null;
                if (!string.IsNullOrEmpty(job.SecureConfigFilePath) && File.Exists(job.SecureConfigFilePath))
                {
                    secureConfig = File.ReadAllText(job.SecureConfigFilePath);
                }

                PluginInvoker.Invoke(job.AssemblyPath, job.PluginTypeName, secureConfig, serviceProvider);

                var result = new ReplayResult { Succeeded = true };
                result.TraceMessages.Add($"Debug host attached and replayed session {job.SessionId} against plugin type '{job.PluginTypeName}'.");
                result.TraceMessages.AddRange(contextResult.TraceNotes);
                result.TraceMessages.AddRange(tracingService.Messages);
                result.SandboxedWrites.AddRange(sandboxedService.SandboxedWrites);
                result.UndecodedProfileEntries.AddRange(contextResult.UndecodedProfileEntries);
                WriteResult(resultPath, result);
                return 0;
            }
        }

        /// <summary>
        /// Standalone diagnostic — decodes a raw plugintracelog.profile blob (already Base64-decoded to
        /// bytes on disk) and prints what was found, without needing a full job/replay session. Useful
        /// both for verifying/extending the type-code catalog against real captures, and as an ongoing
        /// troubleshooting tool once this ships.
        /// </summary>
        private static int RunDecodeDiagnostic(string profileBinPath)
        {
            var rawBytes = File.ReadAllBytes(profileBinPath);
            var entries = ProfileEnvelopeReader.SplitEntries(rawBytes, out var resyncNotes);

            Console.WriteLine($"File: {profileBinPath} ({rawBytes.Length} bytes)");
            Console.WriteLine($"Entries found: {entries.Count}");
            foreach (var note in resyncNotes) { Console.WriteLine($"  [envelope] {note}"); }
            Console.WriteLine();

            var decodedCount = 0;
            var strongTypeCount = 0;

            foreach (var entry in entries)
            {
                var decoded = NbfxEntryDecoder.TryDecode(entry);
                var tagDesc = entry.IsBare ? "bare" : $"id={decoded.Id} typeCode={entry.TypeCode}";
                Console.WriteLine($"Entry [{tagDesc}] typeName={decoded.TypeName} payloadBytes={entry.Payload.Length} entryStart={entry.EntryStart}");

                if (!decoded.Succeeded)
                {
                    Console.WriteLine($"  FAILED: {decoded.FailureReason}");
                    Console.WriteLine("  Payload hex dump:");
                    Console.WriteLine(HexDump(entry.Payload));
                    continue;
                }

                decodedCount++;
                if (decoded.Value != null)
                {
                    strongTypeCount++;
                    Console.WriteLine($"  Strongly-typed: {decoded.Value.GetType().Name}");
                }

                var rootLine = decoded.RawXml.ToString().Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)[0];
                Console.WriteLine($"  Raw XML root: {rootLine}");
                if (Environment.GetEnvironmentVariable("DEBUGHOST_DECODE_VERBOSE") == "1")
                {
                    Console.WriteLine(decoded.RawXml.ToString());
                }
            }

            Console.WriteLine();
            Console.WriteLine($"Decoded {decodedCount}/{entries.Count} entries ({strongTypeCount} strongly-typed).");
            return 0;
        }

        /// <summary>
        /// Steps through raw bytes starting at a given offset as a sequence of top-level MC-NBFX nodes
        /// (Read() loop, no single-root requirement) — tests whether content after the last confirmed
        /// envelope entry is actually plain, unwrapped NBFX content (siblings, not further framed
        /// entries) rather than more of the custom envelope.
        /// </summary>
        private static int RunRawNodeDump(string profileBinPath, int startOffset)
        {
            var allBytes = File.ReadAllBytes(profileBinPath);
            var remaining = new byte[allBytes.Length - startOffset];
            Buffer.BlockCopy(allBytes, startOffset, remaining, 0, remaining.Length);

            Console.WriteLine($"Reading as raw NBFX node sequence from offset {startOffset} ({remaining.Length} bytes remaining)");
            using (var stream = new MemoryStream(remaining))
            using (var reader = XmlDictionaryReader.CreateBinaryReader(stream, XmlDictionaryReaderQuotas.Max))
            {
                var nodeCount = 0;
                try
                {
                    while (reader.Read() && nodeCount < 500)
                    {
                        nodeCount++;
                        var pos = startOffset + (int)stream.Position;
                        Console.WriteLine($"[{nodeCount}] pos~{pos} {reader.NodeType} name='{reader.Name}' value='{Truncate(reader.Value, 80)}' depth={reader.Depth}");
                    }
                    Console.WriteLine($"Finished cleanly after {nodeCount} nodes, stream position {stream.Position}/{remaining.Length}.");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"STOPPED after {nodeCount} nodes at stream position {stream.Position}/{remaining.Length}: {ex.Message}");
                }
            }

            return 0;
        }

        /// <summary>
        /// Standalone diagnostic — runs ProfileContextBuilder (step 5/6's RemoteExecutionContext
        /// population) against a real captured profile without needing a live token or a real plugin
        /// assembly, so this half of the replay pipeline can be smoke-tested in isolation before wiring
        /// up PluginCaptureHostLauncher (Plugin Debugging Phase 2 plan, step 6's "tested by hand first").
        /// MessageName/PrimaryEntityName are dummy values here — ProfileContextBuilder only passes them
        /// through onto the context, it doesn't use them to decode.
        /// </summary>
        private static int RunReplayDiagnostic(string profileBinPath)
        {
            var rawBytes = File.ReadAllBytes(profileBinPath);
            var contextResult = ProfileContextBuilder.Build(rawBytes, "Update", "account");

            Console.WriteLine($"File: {profileBinPath} ({rawBytes.Length} bytes)");
            foreach (var note in contextResult.TraceNotes) { Console.WriteLine($"  [note] {note}"); }
            Console.WriteLine($"Undecoded entries: {contextResult.UndecodedProfileEntries.Count}");

            if (!contextResult.Succeeded)
            {
                Console.WriteLine($"FAILED: {contextResult.FailureReason}");
                return 1;
            }

            var context = contextResult.Context;
            Console.WriteLine($"InputParameters: {context.InputParameters.Count} ({string.Join(", ", KeysOf(context.InputParameters))})");
            Console.WriteLine($"SharedVariables: {context.SharedVariables.Count} ({string.Join(", ", KeysOf(context.SharedVariables))})");
            Console.WriteLine($"PreEntityImages: {context.PreEntityImages.Count} ({string.Join(", ", context.PreEntityImages.Keys)})");
            Console.WriteLine($"PostEntityImages: {context.PostEntityImages.Count} ({string.Join(", ", context.PostEntityImages.Keys)})");

            if (context.InputParameters.Contains("Target") && context.InputParameters["Target"] is Entity targetEntity)
            {
                Console.WriteLine($"Target: {targetEntity.LogicalName} ({targetEntity.Id}), {targetEntity.Attributes.Count} attributes: {string.Join(", ", targetEntity.Attributes.Keys)}");
            }
            else
            {
                Console.WriteLine("Target: MISSING or not an Entity — should be unreachable, since contextResult.Succeeded already requires it.");
            }

            return 0;
        }

        private static IEnumerable<string> KeysOf(ParameterCollection parameters)
        {
            foreach (var pair in parameters) { yield return pair.Key; }
        }

        /// <summary>Dumps the full decoded XML for the entry starting at a specific offset — for inspecting one entry's content directly by EntryStart, without needing to scroll through the full --decode output.</summary>
        private static int RunDumpEntry(string profileBinPath, int entryStart)
        {
            var rawBytes = File.ReadAllBytes(profileBinPath);
            var entries = ProfileEnvelopeReader.SplitEntries(rawBytes, out _);
            ProfileEntry entry = null;
            var offsets = new List<int>();
            foreach (var e in entries)
            {
                offsets.Add(e.EntryStart);
                if (e.EntryStart == entryStart) { entry = e; }
            }
            if (entry == null)
            {
                Console.WriteLine($"No entry found starting at offset {entryStart}. Available offsets: {string.Join(", ", offsets)}");
                return 1;
            }

            var decoded = NbfxEntryDecoder.TryDecode(entry);
            if (!decoded.Succeeded)
            {
                Console.WriteLine($"FAILED: {decoded.FailureReason}");
                return 1;
            }

            Console.WriteLine(decoded.RawXml.ToString());
            return 0;
        }

        private static string Truncate(string s, int max) =>
            string.IsNullOrEmpty(s) ? s : (s.Length <= max ? s : s.Substring(0, max) + "...");

        /// <summary>Classic offset/hex/ASCII dump — for eyeballing raw bytes directly (e.g. in a text editor) when the decoder can't make sense of a region.</summary>
        private static string HexDump(byte[] bytes)
        {
            var sb = new StringBuilder();
            for (var offset = 0; offset < bytes.Length; offset += 16)
            {
                var count = Math.Min(16, bytes.Length - offset);
                sb.Append(offset.ToString("X8")).Append("  ");

                for (var i = 0; i < 16; i++)
                {
                    sb.Append(i < count ? bytes[offset + i].ToString("X2") : "  ").Append(' ');
                    if (i == 7) { sb.Append(' '); }
                }

                sb.Append(" ");
                for (var i = 0; i < count; i++)
                {
                    var b = bytes[offset + i];
                    sb.Append(b >= 0x20 && b < 0x7F ? (char)b : '.');
                }

                sb.AppendLine();
            }

            return sb.ToString();
        }

        private static void WriteResult(string resultPath, ReplayResult result)
        {
            try
            {
                File.WriteAllText(resultPath, JsonConvert.SerializeObject(result, Newtonsoft.Json.Formatting.Indented));
            }
            catch
            {
                // Best-effort — if we can't even write the result file, there's nothing further to do;
                // the extension will report "the replay process exited unexpectedly" from the exit code alone.
            }
        }
    }
}
