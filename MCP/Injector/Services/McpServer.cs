using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Injector.Plugins;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;

namespace Injector.Services
{
    public class McpServer : BackgroundService
    {
        private readonly ILogger<McpServer> _logger;
        private readonly Kernel _kernel;
        private readonly WpfInspectorPlugin _plugin;

        public McpServer(
            ILogger<McpServer> logger,
            Kernel kernel,
            WpfInspectorPlugin plugin)
        {
            _logger = logger;
            _kernel = kernel;
            _plugin = plugin;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("WPF Inspector MCP Server starting...");
            
            // Add the plugin to the kernel
            _kernel.Plugins.AddFromObject(_plugin, "WpfInspector");
            
            _logger.LogInformation("Available functions:");
            foreach (var function in _kernel.Plugins.GetFunctionsMetadata())
            {
                _logger.LogInformation($"  - {function.Name}: {function.Description}");
            }

            _logger.LogInformation("MCP Server ready. Listening for JSON-RPC requests on stdin/stdout...");

            try
            {
                await ProcessMcpRequestsAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("MCP Server stopping...");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in MCP Server");
            }
        }

        private async Task ProcessMcpRequestsAsync(CancellationToken cancellationToken)
        {
            using var stdin = Console.OpenStandardInput();
            using var stdout = Console.OpenStandardOutput();
            using var reader = new StreamReader(stdin);
            using var writer = new StreamWriter(stdout) { AutoFlush = true };

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var line = await reader.ReadLineAsync();
                    if (line == null)
                    {
                        // EOF reached
                        break;
                    }

                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    _logger.LogDebug($"Received request: {line}");

                    var response = await ProcessJsonRpcRequestAsync(line);
                    if (response != null)
                    {
                        var responseJson = JsonSerializer.Serialize(response);
                        _logger.LogDebug($"Sending response: {responseJson}");
                        await writer.WriteLineAsync(responseJson);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing MCP request");
                    
                    // Send error response
                    var errorResponse = new
                    {
                        jsonrpc = "2.0",
                        error = new
                        {
                            code = -32603,
                            message = "Internal error",
                            data = ex.Message
                        },
                        id = (object?)null
                    };
                    
                    var errorJson = JsonSerializer.Serialize(errorResponse);
                    await writer.WriteLineAsync(errorJson);
                }
            }
        }

        private async Task<object?> ProcessJsonRpcRequestAsync(string requestJson)
        {
            try
            {
                var request = JsonSerializer.Deserialize<JsonNode>(requestJson);
                if (request == null)
                    return CreateErrorResponse(null, -32700, "Parse error");

                var id = request["id"];
                var method = request["method"]?.ToString();
                var paramsNode = request["params"];

                if (string.IsNullOrEmpty(method))
                    return CreateErrorResponse(id, -32600, "Invalid Request");

                _logger.LogInformation($"Processing method: {method}");

                return method switch
                {
                    "initialize" => await HandleInitializeAsync(id, paramsNode),
                    "tools/list" => await HandleToolsListAsync(id),
                    "tools/call" => await HandleToolsCallAsync(id, paramsNode),
                    _ => CreateErrorResponse(id, -32601, "Method not found")
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing JSON-RPC request");
                return CreateErrorResponse(null, -32603, "Internal error", ex.Message);
            }
        }

        private async Task<object> HandleInitializeAsync(JsonNode? id, JsonNode? paramsNode)
        {
            _logger.LogInformation("Handling initialize request");
            
            return new
            {
                jsonrpc = "2.0",
                result = new
                {
                    protocolVersion = "2024-11-05",
                    capabilities = new
                    {
                        tools = new { }
                    },
                    serverInfo = new
                    {
                        name = "WpfInspector MCP Server",
                        version = "1.0.0"
                    }
                },
                id = id
            };
        }

        private async Task<object> HandleToolsListAsync(JsonNode? id)
        {
            _logger.LogInformation("Handling tools/list request");
            
            var functions = _kernel.Plugins.GetFunctionsMetadata();
            var tools = functions.Select(f => new
            {
                name = f.Name,
                description = f.Description,
                inputSchema = new
                {
                    type = "object",
                    properties = f.Parameters.ToDictionary(
                        p => p.Name,
                        p => new
                        {
                            type = GetJsonSchemaType(p.ParameterType ?? typeof(string)),
                            description = p.Description
                        }),
                    required = f.Parameters
                        .Where(p => p.IsRequired)
                        .Select(p => p.Name)
                        .ToArray()
                }
            }).ToArray();

            return new
            {
                jsonrpc = "2.0",
                result = new
                {
                    tools = tools
                },
                id = id
            };
        }

        private async Task<object> HandleToolsCallAsync(JsonNode? id, JsonNode? paramsNode)
        {
            try
            {
                if (paramsNode == null)
                    return CreateErrorResponse(id, -32602, "Invalid params");

                var name = paramsNode["name"]?.ToString();
                var arguments = paramsNode["arguments"] as JsonObject;

                if (string.IsNullOrEmpty(name))
                    return CreateErrorResponse(id, -32602, "Missing tool name");

                _logger.LogInformation($"Calling tool: {name}");

                var function = _kernel.Plugins.GetFunction("WpfInspector", name);
                if (function == null)
                    return CreateErrorResponse(id, -32601, $"Tool '{name}' not found");

                var kernelArguments = new KernelArguments();
                
                if (arguments != null)
                {
                    foreach (var arg in arguments)
                    {
                        kernelArguments[arg.Key] = arg.Value?.ToString();
                    }
                }

                var result = await _kernel.InvokeAsync(function, kernelArguments);
                var resultValue = result.ToString();

                return new
                {
                    jsonrpc = "2.0",
                    result = new
                    {
                        content = new[]
                        {
                            new
                            {
                                type = "text",
                                text = resultValue
                            }
                        }
                    },
                    id = id
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calling tool");
                return CreateErrorResponse(id, -32603, "Internal error", ex.Message);
            }
        }

        private object CreateErrorResponse(JsonNode? id, int code, string message, string? data = null)
        {
            var error = new
            {
                code = code,
                message = message,
                data = data
            };

            return new
            {
                jsonrpc = "2.0",
                error = error,
                id = id
            };
        }

        private string GetJsonSchemaType(Type type)
        {
            if (type == typeof(int) || type == typeof(long) || type == typeof(short))
                return "integer";
            if (type == typeof(double) || type == typeof(float) || type == typeof(decimal))
                return "number";
            if (type == typeof(bool))
                return "boolean";
            if (type == typeof(string))
                return "string";
            
            return "string"; // Default fallback
        }
    }
}
