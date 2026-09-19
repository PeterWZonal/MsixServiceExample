using BackgroundService.Contracts;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace BackgroundService
{
    /// <summary>
    /// Loads every discovered plugin and runs an independent timer loop per <see cref="IPeriodicMessageSource"/>.
    /// </summary>
    public sealed class PluginHostService : Microsoft.Extensions.Hosting.BackgroundService
    {
        private readonly PluginLocator _locator;
        private readonly ILogger<PluginHostService> _logger;

        public PluginHostService(PluginLocator locator, ILogger<PluginHostService> logger) =>
            (_locator, _logger) = (locator, logger);

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var sources = new List<IPeriodicMessageSource>();
            foreach (string pluginPath in _locator.FindPluginAssemblies())
            {
                sources.AddRange(LoadSources(pluginPath));
            }

            if (sources.Count == 0)
            {
                _logger.LogWarning("No plugins loaded");
                return;
            }

            await Task.WhenAll(sources.Select(source => RunSourceAsync(source, stoppingToken)));
        }

        private IEnumerable<IPeriodicMessageSource> LoadSources(string pluginPath)
        {
            var sources = new List<IPeriodicMessageSource>();
            try
            {
                var context = new PluginLoadContext(pluginPath);
                Assembly assembly = context.LoadFromAssemblyPath(pluginPath);

                foreach (Type type in assembly.GetTypes())
                {
                    if (!type.IsPublic || type.IsAbstract || !typeof(IPeriodicMessageSource).IsAssignableFrom(type))
                    {
                        continue;
                    }

                    var source = (IPeriodicMessageSource)Activator.CreateInstance(type)!;
                    _logger.LogWarning("Loaded plugin {Name} (interval {Interval}) from {Path}", source.Name, source.Interval, pluginPath);
                    sources.Add(source);
                }

                if (sources.Count == 0)
                {
                    _logger.LogWarning("Plugin {Path} contains no {Contract} implementations", pluginPath, nameof(IPeriodicMessageSource));
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load plugin {Path}", pluginPath);
            }

            return sources;
        }

        private async Task RunSourceAsync(IPeriodicMessageSource source, CancellationToken stoppingToken)
        {
            using var timer = new PeriodicTimer(source.Interval);
            try
            {
                do
                {
                    try
                    {
                        _logger.LogWarning("[{Name}] {Message}", source.Name, source.GetMessage());
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "[{Name}] GetMessage failed", source.Name);
                    }
                }
                while (await timer.WaitForNextTickAsync(stoppingToken));
            }
            catch (OperationCanceledException)
            {
                // Expected on service stop.
            }
        }
    }
}
