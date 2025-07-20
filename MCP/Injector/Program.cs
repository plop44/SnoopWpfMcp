using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Injector.Services;
using Injector.Plugins;

namespace Injector
{
    public class Program
    {
        public static async Task<int> Main(string[] args)
        {
            try
            {
                Console.WriteLine("WpfInspector MCP Server");
                Console.WriteLine("Initializing...");

                // Wait for debugger if requested
                if (Environment.GetEnvironmentVariable("WAIT_FOR_DEBUGGER") == "true")
                {
                    Console.WriteLine("Waiting for debugger to attach...");
                    Console.WriteLine($"Process ID: {Environment.ProcessId}");
                    while (!System.Diagnostics.Debugger.IsAttached)
                    {
                        await Task.Delay(100);
                    }
                    Console.WriteLine("Debugger attached!");
                }

                // Build the host
                var host = CreateHostBuilder(args).Build();
                
                // Run the host
                await host.RunAsync();
                
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Fatal error: {ex.Message}");
                Console.Error.WriteLine($"Stack trace: {ex}");
                return 1;
            }
        }

        private static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureLogging(logging =>
                {
                    logging.ClearProviders();
                    logging.AddConsole();
                    logging.SetMinimumLevel(LogLevel.Information);
                })
                .ConfigureServices((context, services) =>
                {
                    // Register Semantic Kernel
                    services.AddKernel();
                    
                    // Register our services
                    services.AddSingleton<IWpfProcessService, WpfProcessService>();
                    services.AddSingleton<IInjectionService, InjectionService>();
                    services.AddSingleton<WpfInspectorPlugin>();
                    
                    // Register the MCP server as a hosted service
                    services.AddHostedService<McpServer>();
                });
    }
}
