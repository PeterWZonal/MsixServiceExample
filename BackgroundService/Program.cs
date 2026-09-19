using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Threading.Tasks;

namespace BackgroundService
{
    public static class Program
    {
        public static async Task Main(string[] args)
        {
            using (IHost host = Host.CreateDefaultBuilder(args)
                .UseWindowsService(options =>
                {
                    // the name set in code must match the desktop6:Service Name in Package.appxmanifest, this one is used for start, stop operation logging
                    options.ServiceName = "BackgroundService";
                })
                .ConfigureServices(services =>
                {
                    services.AddSingleton<JokeService>();
                    services.AddHostedService<WindowsBackgroundService>();
                })
                .Build())
            {
                await host.RunAsync();
            }
        }
    }
}
