using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;

namespace D365_Developer_Tools__Unofficial__for_Visual_Studio.PluginPublishing
{
    internal sealed class DiscoveredPluginType
    {
        public string TypeName { get; set; }
        public string FriendlyName { get; set; }
    }

    /// <summary>
    /// Finds IPlugin implementations in a built assembly using MetadataLoadContext rather than
    /// Assembly.LoadFrom: loading the real assembly into this (devenv.exe) process would lock the
    /// file on disk (breaking the next rebuild) and, since .NET Framework can't unload assemblies
    /// from the default AppDomain, leak a little more memory into the VS session on every publish.
    /// </summary>
    internal static class PluginTypeScanner
    {
        private const string PluginInterfaceFullName = "Microsoft.Xrm.Sdk.IPlugin";

        public static List<DiscoveredPluginType> FindPluginTypes(string assemblyPath)
        {
            var assemblyDir = Path.GetDirectoryName(assemblyPath) ?? string.Empty;
            var runtimeDir = RuntimeEnvironment.GetRuntimeDirectory();

            var probePaths = Directory.GetFiles(runtimeDir, "*.dll")
                .Concat(Directory.GetFiles(assemblyDir, "*.dll"))
                .GroupBy(Path.GetFileName)
                .Select(g => g.First())
                .ToList();

            var resolver = new PathAssemblyResolver(probePaths);
            using (var context = new MetadataLoadContext(resolver, coreAssemblyName: "mscorlib"))
            {
                var assembly = context.LoadFromAssemblyPath(assemblyPath);

                Type[] types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException ex)
                {
                    types = ex.Types.Where(t => t != null).ToArray();
                }

                var result = new List<DiscoveredPluginType>();
                foreach (var type in types)
                {
                    if (!type.IsPublic || type.IsAbstract || type.IsInterface) { continue; }

                    bool implementsIPlugin;
                    try
                    {
                        implementsIPlugin = type.GetInterfaces().Any(i => i.FullName == PluginInterfaceFullName);
                    }
                    catch
                    {
                        continue; // type's base chain couldn't be fully resolved; skip rather than fail the whole scan
                    }

                    if (implementsIPlugin)
                    {
                        result.Add(new DiscoveredPluginType { TypeName = type.FullName, FriendlyName = type.Name });
                    }
                }

                return result;
            }
        }
    }
}
