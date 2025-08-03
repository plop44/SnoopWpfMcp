using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using SnoopWpfMcpServer.Services;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SnoopWpfMcpServer
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        public void ConfigureServices(IServiceCollection services)
        {
            // Add controllers
            services.AddControllers()
                .AddJsonOptions(options =>
                {
                    options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
                    options.JsonSerializerOptions.WriteIndented = true;
                    options.JsonSerializerOptions.NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals;
                });

            // Add CORS
            services.AddCors(options =>
            {
                options.AddDefaultPolicy(builder =>
                {
                    builder.AllowAnyOrigin()
                           .AllowAnyMethod()
                           .AllowAnyHeader();
                });
            });

            // Add Swagger/OpenAPI
            services.AddEndpointsApiExplorer();
            services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", new() { 
                    Title = "WpfInspector MCP HTTP API", 
                    Version = "v1",
                    Description = "HTTP-based Model Context Protocol server for WPF application inspection"
                });
            });

            // Register our services
            services.AddSingleton<IWpfProcessService, WpfProcessService>();
            services.AddSingleton<IInjectionService, InjectionService>();
            services.AddSingleton<WpfInspectorPlugin>();

            // Create kernel with plugins using modern builder pattern
            services.AddSingleton<Kernel>(serviceProvider =>
            {
                var kernelBuilder = Kernel.CreateBuilder();
                var plugin = serviceProvider.GetRequiredService<WpfInspectorPlugin>();
                kernelBuilder.Plugins.AddFromObject(plugin, "WpfInspector");
                return kernelBuilder.Build();
            });
        }

        public void Configure(IApplicationBuilder app, IWebHostEnvironment env, ILogger<Startup> logger)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
                app.UseSwagger();
                app.UseSwaggerUI(c =>
                {
                    c.SwaggerEndpoint("/swagger/v1/swagger.json", "WpfInspector MCP HTTP API v1");
                    c.RoutePrefix = string.Empty; // Set Swagger UI at the app's root
                });
            }

            app.UseRouting();
            app.UseCors();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
                
                // Add a root endpoint that provides information about the MCP server
                endpoints.MapGet("/", async context =>
                {
                    var response = new
                    {
                        service = "WpfInspector MCP HTTP Server",
                        version = "1.0.0",
                        transport = "http",
                        endpoints = new
                        {
                            initialize = "GET /mcp/initialize",
                            tools = "GET /mcp/tools",
                            callTool = "POST /mcp/tools/{toolName}",
                            jsonRpc = "POST /mcp/rpc",
                            health = "GET /mcp/health",
                            swagger = "/swagger"
                        },
                        documentation = "Visit /swagger for API documentation"
                    };
                    
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsync(JsonSerializer.Serialize(response, new JsonSerializerOptions 
                    { 
                        WriteIndented = true,
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
                    }));
                });
            });

            logger.LogInformation("WpfInspector MCP HTTP Server configured and ready");
            logger.LogInformation("Available endpoints:");
            logger.LogInformation("  - GET  /mcp/initialize - Initialize MCP session");
            logger.LogInformation("  - GET  /mcp/tools - List available tools");
            logger.LogInformation("  - POST /mcp/tools/{{toolName}} - Call a specific tool");
            logger.LogInformation("  - POST /mcp/rpc - JSON-RPC endpoint");
            logger.LogInformation("  - GET  /mcp/health - Health check");
            logger.LogInformation("  - GET  /swagger - API documentation");
        }
    }
}