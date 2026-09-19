using System;
using System.Reflection;
using System.Runtime.Loader;

namespace BackgroundService
{
    /// <summary>
    /// Isolated load context for a single plugin. Assemblies the host already provides (the contract) are
    /// deliberately not loaded here so the plugin binds to the host's copy and type identity is preserved.
    /// </summary>
    public sealed class PluginLoadContext : AssemblyLoadContext
    {
        private static readonly string ContractAssemblyName = typeof(Contracts.IPeriodicMessageSource).Assembly.GetName().Name!;

        private readonly AssemblyDependencyResolver _resolver;

        public PluginLoadContext(string pluginPath)
            : base(name: System.IO.Path.GetFileNameWithoutExtension(pluginPath), isCollectible: false)
        {
            _resolver = new AssemblyDependencyResolver(pluginPath);
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            if (string.Equals(assemblyName.Name, ContractAssemblyName, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            string? path = _resolver.ResolveAssemblyToPath(assemblyName);
            return path is null ? null : LoadFromAssemblyPath(path);
        }

        protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
        {
            string? path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
            return path is null ? IntPtr.Zero : LoadUnmanagedDllFromPath(path);
        }
    }
}
