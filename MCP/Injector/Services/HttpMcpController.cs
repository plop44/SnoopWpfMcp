using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;

namespace SnoopWpfMcpServer.Services
{
    [ApiController]
    [Route("mcp")]
    public class HttpMcpController : ControllerBase
    {
        private readonly ILogger<HttpMcpController> _logger;
        private readonly Kernel _kernel;
        private readonly WpfInspectorPlugin _plugin;

        public HttpMcpController(
            ILogger<HttpMcpController> logger,
            Kernel kernel,
            WpfInspectorPlugin plugin)
        {
            _logger = logger;
            _kernel = kernel;
            _plugin = plugin;

            // Add the plugin to the kernel if not already added
            if (_kernel.Plugins.All(p => p.Name != "WpfInspector"))
            {
                _kernel.Plugins.AddFromObject(_plugin, "WpfInspector");
            }
        }

        [HttpPost("rpc")]
        public async Task<IActionResult> HandleJsonRpcRequest([FromBody] JsonElement request)
        {
            try
            {
                _logger.LogDebug($"Received JSON-RPC request: {request}");

                var requestNode = JsonNode.Parse(request.GetRawText());
                if (requestNode == null)
                    return Ok(CreateErrorResponse(null, -32700, "Parse error"));

                var id = requestNode["id"];
                var method = requestNode["method"]?.ToString();
                var paramsNode = requestNode["params"];

                if (string.IsNullOrEmpty(method))
                    return Ok(CreateErrorResponse(id, -32600, "Invalid Request"));

                _logger.LogInformation($"Processing method: {method}");

                var response = method switch
                {
                    "initialize" => await HandleInitializeAsync(id, paramsNode),
                    "tools/list" => await HandleToolsListAsync(id),
                    "tools/call" => await HandleToolsCallAsync(id, paramsNode),
                    _ => CreateErrorResponse(id, -32601, "Method not found")
                };

                return Ok(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing JSON-RPC request");
                return Ok(CreateErrorResponse(null, -32603, "Internal error", ex.Message));
            }
        }

        [HttpGet("initialize")]
        public IActionResult Initialize()
        {
            _logger.LogInformation("Handling HTTP GET initialize request");
            
            return Ok(new
            {
                protocolVersion = "2024-11-05",
                capabilities = new
                {
                    tools = new { }
                },
                serverInfo = new
                {
                    name = "WpfInspector MCP Server",
                    version = "1.0.0",
                    transport = "http"
                }
            });
        }

        [HttpGet("tools")]
        public IActionResult ListTools()
        {
            _logger.LogInformation("Handling HTTP GET tools request");
            
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

            return Ok(new { tools = tools });
        }

        [HttpPost("tools/{toolName}")]
        public async Task<IActionResult> CallTool(string toolName, [FromBody] JsonElement arguments)
        {
            try
            {
                _logger.LogInformation($"Calling tool: {toolName}");

                var function = _kernel.Plugins.GetFunction("WpfInspector", toolName);
                if (function == null)
                    return NotFound(new { error = $"Tool '{toolName}' not found" });

                var kernelArguments = new KernelArguments();
                
                if (arguments.ValueKind == JsonValueKind.Object)
                {
                    foreach (var property in arguments.EnumerateObject())
                    {
                        kernelArguments[property.Name] = property.Value.ToString();
                    }
                }

                var result = await _kernel.InvokeAsync(function, kernelArguments);
                var resultValue = result.ToString();

                return Ok(new
                {
                    success = true,
                    result = resultValue,
                    toolName = toolName
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error calling tool {toolName}");
                return StatusCode(500, new 
                { 
                    success = false,
                    error = ex.Message,
                    toolName = toolName
                });
            }
        }

        [HttpGet("health")]
        public IActionResult Health()
        {
            return Ok(new 
            { 
                status = "healthy",
                timestamp = DateTime.UtcNow,
                availableTools = _kernel.Plugins.GetFunctionsMetadata().Count()
            });
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
                        version = "1.0.0",
                        transport = "http"
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
                        // Map legacy parameter names for backward compatibility
                        var parameterName = arg.Key;
                        kernelArguments[parameterName] = arg.Value?.ToString();
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