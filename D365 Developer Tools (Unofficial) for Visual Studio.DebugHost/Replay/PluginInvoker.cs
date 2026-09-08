using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.Xrm.Sdk;

namespace D365DeveloperTools.DebugHost.Replay
{
    /// <summary>Loads the developer's local Debug-configuration plugin assembly and invokes the target type's Execute(IServiceProvider) — a normal execution-capable load (Assembly.LoadFrom), deliberately not the MetadataLoadContext PluginTypeScanner.cs uses elsewhere in this extension, which is reflection-only and cannot invoke code.</summary>
    internal static class PluginInvoker
    {
        public static void Invoke(string assemblyPath, string pluginTypeName, string secureConfig, IServiceProvider serviceProvider)
        {
            // pluginTypeName comes from plugintracelog.typename, which holds the *assembly-qualified*
            // type name ("Namespace.Class, AssemblyName, Version=..., Culture=..., PublicKeyToken=...")
            // — see PluginTraceLogModels.cs's own doc comment on the main extension side. Assembly.GetType
            // treats its argument as a literal type name to search *within that one assembly*, so passing
            // the assembly-qualified string through unchanged never matches anything (Type.FullName for a
            // normal type never includes the assembly-qualification suffix either) — confirmed via a real
            // "Type '...' was not found" failure during live end-to-end testing. Only the first
            // comma-separated segment (the actual namespace-qualified type name) is meaningful for a
            // by-name lookup within the one already-loaded assembly.
            var bareTypeName = pluginTypeName.Split(',')[0].Trim();

            var assembly = Assembly.LoadFrom(assemblyPath);
            var pluginType = assembly.GetType(bareTypeName, throwOnError: false)
                ?? assembly.GetTypes().FirstOrDefault(t => string.Equals(t.FullName, bareTypeName, StringComparison.Ordinal));

            if (pluginType == null)
            {
                throw new InvalidOperationException($"Type '{bareTypeName}' was not found in '{Path.GetFileName(assemblyPath)}'.");
            }

            if (!typeof(IPlugin).IsAssignableFrom(pluginType))
            {
                throw new InvalidOperationException($"Type '{bareTypeName}' does not implement Microsoft.Xrm.Sdk.IPlugin.");
            }

            var instance = CreateInstance(pluginType, secureConfig);
            ((IPlugin)instance).Execute(serviceProvider);
        }

        /// <summary>
        /// Most plugin registration tools generate a (string unsecureConfiguration, string secureConfiguration)
        /// constructor even when both are unused — prefer it when present, passing only the recorded secure
        /// config (unsecure configuration isn't part of the capture/job handoff contract yet — see the
        /// Plugin Debugging Phase 2 plan's full file list). Falls back to a parameterless constructor.
        /// </summary>
        private static object CreateInstance(Type pluginType, string secureConfig)
        {
            var twoStringCtor = pluginType.GetConstructor(new[] { typeof(string), typeof(string) });
            if (twoStringCtor != null)
            {
                return twoStringCtor.Invoke(new object[] { null, secureConfig });
            }

            var defaultCtor = pluginType.GetConstructor(Type.EmptyTypes);
            if (defaultCtor != null)
            {
                return defaultCtor.Invoke(null);
            }

            throw new InvalidOperationException($"Type '{pluginType.FullName}' has neither a parameterless constructor nor a (string, string) configuration constructor.");
        }
    }
}
