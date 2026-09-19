using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Windows.ApplicationModel;
using Windows.Management.Deployment;

namespace BackgroundService
{
    /// <summary>
    /// Finds plugin entry assemblies shipped by modification packages. A modification package's files are never
    /// merged into the main package's folder (verified empirically for a packaged service), so each targeting
    /// optional package's own <c>BackgroundService\</c> folder - the same relative path the host exe lives at - is
    /// scanned instead. A DLL is treated as a plugin entry point when a sibling <c>&lt;name&gt;.deps.json</c> exists
    /// (emitted by <c>EnableDynamicLoading</c>); plain dependency DLLs have none.
    /// </summary>
    public sealed class PluginLocator
    {
        private const string ServiceFolderName = "BackgroundService";

        private readonly ILogger<PluginLocator> _logger;

        public PluginLocator(ILogger<PluginLocator> logger) => _logger = logger;

        public IReadOnlyList<string> FindPluginAssemblies()
        {
            var results = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (string dependencyRoot in GetOptionalPackageRoots())
            {
                AddFrom(Path.Combine(dependencyRoot, ServiceFolderName), results);
            }

            return results.Values.ToList();
        }

        private void AddFrom(string directory, Dictionary<string, string> results)
        {
            if (!Directory.Exists(directory))
            {
                _logger.LogWarning("Plugin discovery: directory does not exist: {Directory}", directory);
                return;
            }

            string[] files = Directory.GetFiles(directory, "*.dll", SearchOption.TopDirectoryOnly)
                .Where(f => File.Exists(Path.ChangeExtension(f, ".deps.json")))
                .ToArray();
            _logger.LogWarning("Plugin discovery: {Count} plugin entry dll(s) in {Directory}: {Files}",
                files.Length, directory, string.Join(", ", files.Select(Path.GetFileName)));

            foreach (string file in files)
            {
                string name = Path.GetFileName(file);
                if (results.TryAdd(name, file))
                {
                    continue;
                }

                _logger.LogWarning("Plugin discovery: {File} ignored, already found at {Existing}", file, results[name]);
            }
        }

        private IEnumerable<string> GetOptionalPackageRoots()
        {
            var roots = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string? familyName = null;

            try
            {
                Package current = Package.Current;
                familyName = current.Id.FamilyName;
                var dependencies = current.Dependencies.ToList();
                _logger.LogWarning("Running as package {PackageFullName} with {Count} dependency package(s) in the process package graph: {Names}",
                    current.Id.FullName, dependencies.Count, string.Join(", ", dependencies.Select(d => d.Id.FullName)));

                foreach (Package dependency in dependencies.Where(d => d.IsOptional))
                {
                    roots.TryAdd(dependency.Id.FullName, dependency.InstalledLocation.Path);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Package.Current is unavailable (no package identity?)");
            }

            // The process package graph of a LocalSystem service does not include per-user optional package
            // registrations (and Package.Dependencies is empty for packages returned by the all-users query), so
            // ask the deployment API which users have our main package and check each of their optional
            // packages' manifests for a MainPackageDependency on us. Requires admin, which LocalSystem is.
            try
            {
                familyName ??= GetMainFamilyNameFromBaseDirectory();
                string identityName = familyName.Split('_')[0];
                var manager = new PackageManager();

                var userSids = manager.FindPackagesWithPackageTypes(PackageTypes.Main)
                    .Where(p => string.Equals(p.Id.FamilyName, familyName, StringComparison.OrdinalIgnoreCase))
                    .SelectMany(p => manager.FindUsers(p.Id.FullName))
                    .Select(u => u.UserSecurityId)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                _logger.LogWarning("PackageManager: family {FamilyName} is registered for {Count} user(s): {Sids}", familyName, userSids.Count, string.Join(", ", userSids));

                foreach (string sid in userSids)
                {
                    foreach (Package optional in manager.FindPackagesForUserWithPackageTypes(sid, PackageTypes.Optional))
                    {
                        string path = optional.InstalledLocation.Path;
                        if (!TargetsMainPackage(path, identityName))
                        {
                            continue;
                        }

                        if (roots.TryAdd(optional.Id.FullName, path))
                        {
                            _logger.LogWarning("PackageManager: optional package {PackageFullName} (user {Sid}) targets us, at {Path}", optional.Id.FullName, sid, path);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "PackageManager query failed; only the process package graph and local plugins will be used");
            }

            return roots.Values;
        }

        private static bool TargetsMainPackage(string packageRoot, string identityName)
        {
            string manifestPath = Path.Combine(packageRoot, "AppxManifest.xml");
            if (!File.Exists(manifestPath))
            {
                return false;
            }

            XDocument manifest = XDocument.Load(manifestPath);
            return manifest.Descendants()
                .Where(e => e.Name.LocalName == "MainPackageDependency")
                .Any(e => string.Equals((string?)e.Attribute("Name"), identityName, StringComparison.OrdinalIgnoreCase));
        }

        // Installed packages live in ...\WindowsApps\<Name>_<Version>_<Arch>__<PublisherId>\; the family name is <Name>_<PublisherId>.
        private static string GetMainFamilyNameFromBaseDirectory()
        {
            string packageDir = new DirectoryInfo(AppContext.BaseDirectory).Parent?.Name ?? string.Empty;
            string[] parts = packageDir.Split("__", 2);
            return parts.Length == 2 ? $"{parts[0].Split('_')[0]}_{parts[1]}" : packageDir;
        }
    }
}
