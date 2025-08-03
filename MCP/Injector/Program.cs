using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using SnoopWpfMcpServer.Services;

namespace SnoopWpfMcpServer
{
    public class Program
    {
        public static async Task<int> Main(string[] args)
        {
            try
            {
                Console.WriteLine("WpfInspector MCP HTTP Server");
                Console.WriteLine("Initializing...");

                // Check if stdio mode is requested
                bool useStdio = Environment.GetEnvironmentVariable("MCP_TRANSPORT") == "stdio" ||
                               Array.Exists(args, arg => arg.Equals("--stdio", StringComparison.OrdinalIgnoreCase));

                if (useStdio)
                {
                    Console.WriteLine("Starting in stdio mode...");
                    var stdioHost = CreateStdioHostBuilder(args).Build();
                    await stdioHost.RunAsync();
                }
                else
                {
                    Console.WriteLine("Starting HTTP server...");
                    var httpHost = CreateHttpHostBuilder(args).Build();
                    await httpHost.RunAsync();
                }
                
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Fatal error: {ex.Message}");
                Console.Error.WriteLine($"Stack trace: {ex}");
                return 1;
            }
        }

        private static IHostBuilder CreateHttpHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseStartup<Startup>();
                    webBuilder.UseUrls("http://localhost:8080", "https://localhost:8443");
                })
                .ConfigureLogging(logging =>
                {
                    logging.ClearProviders();
                    logging.AddConsole();
                    logging.SetMinimumLevel(LogLevel.Information);
                });

        private static IHostBuilder CreateStdioHostBuilder(string[] args) =>
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
