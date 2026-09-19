using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace BackgroundService
{
    public sealed class WindowsBackgroundService : Microsoft.Extensions.Hosting.BackgroundService
    {
        private readonly JokeService _jokeService;
        private readonly ILogger<WindowsBackgroundService> _logger;

        public WindowsBackgroundService(
            JokeService jokeService,
            ILogger<WindowsBackgroundService> logger) =>
            (_jokeService, _logger) = (jokeService, logger);

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Warning level is used deliberately so the messages show up in the Windows Application event log,
            // which only receives Warning and above by default.
            _logger.LogWarning("WindowsBackgroundService running as PID {ProcessId} in {Interactivity}interactive environment",
                Environment.ProcessId, Environment.UserInteractive ? string.Empty : "non-");

            using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
            try
            {
                do
                {
                    _logger.LogWarning("{Joke}", _jokeService.GetJoke());
                }
                while (await timer.WaitForNextTickAsync(stoppingToken));
            }
            catch (OperationCanceledException)
            {
                // Expected on service stop; don't let the host log it as an error.
            }
        }
    }
}