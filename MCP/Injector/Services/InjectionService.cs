using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SnoopWpfMcpServer.Models;

namespace SnoopWpfMcpServer.Services
{
    public interface IInjectionService
    {
        Task<InjectionResult> PingAsync(int processId);
        Task<AutomationPeerResult> InvokeAutomationPeerAsync(int processId, string type, int hashcode, string action, string? parameters = null);
        Task<ScreenshotResult> TakeScreenshotAsync(int processId);
        Task<VisualTreeResult> GetVisualTreeAsync(int processId);
        Task<ElementResult> GetElementByHashcodeAsync(int processId, string type, int hashcode);
    }

    public class InjectionService : IInjectionService
    {
        private readonly ILogger<InjectionService> _logger;
        private readonly IWpfProcessService _processService;
        private readonly Dictionary<int, DateTime> _injectedProcesses = new();

        public InjectionService(ILogger<InjectionService> logger, IWpfProcessService processService)
        {
            _logger = logger;
            _processService = processService;
        }

        private static JsonSerializerOptions GetJsonOptions()
        {
            return new JsonSerializerOptions
            {
                WriteIndented = false,
                NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
            };
        }

        public async Task<InjectionResult> PingAsync(int processId)
        {
            _logger.LogInformation($"Starting injection and ping for process {processId}");

            try
            {
                // Check if process exists
                var process = GetProcess(processId);
                if (process == null)
                {
                    return new InjectionResult
                    {
                        Success = false,
                        ProcessId = processId,
                        Message = $"Process with ID {processId} not found",
                        Error = "Process not found"
                    };
                }

                _logger.LogInformation($"Target process: {process.ProcessName} (PID: {process.Id})");

                // Check if already injected
                var alreadyInjected = await IsAlreadyInjectedAsync(processId);
                if (alreadyInjected)
                {
                    _logger.LogInformation($"Process {processId} already has WpfInspector injected, attempting direct ping");
                    
                    var directPingResponse = await SendPingAsync(processId);
                    if (directPingResponse != null)
                    {
                        return new InjectionResult
                        {
                            Success = true,
                            ProcessId = processId,
                            Message = "WpfInspector was already injected",
                            Response = directPingResponse,
                            WasAlreadyInjected = true
                        };
                    }
                    else
                    {
                        _logger.LogWarning($"Process {processId} was marked as injected but ping failed, re-injecting");
                        _injectedProcesses.Remove(processId);
                    }
                }

                // Perform injection
                _logger.LogInformation($"Injecting WpfInspector into process {processId}");
                var injectionSuccess = await InjectWpfInspectorAsync(processId);
                
                if (!injectionSuccess)
                {
                    return new InjectionResult
                    {
                        Success = false,
                        ProcessId = processId,
                        Message = "Failed to inject WpfInspector",
                        Error = "Injection failed"
                    };
                }

                _logger.LogInformation($"WpfInspector injected successfully into process {processId}");
                _injectedProcesses[processId] = DateTime.UtcNow;

                // Wait for pipe server to start
                _logger.LogInformation("Waiting for pipe server to start...");
                await Task.Delay(2000);

                // Send ping
                _logger.LogInformation($"Sending ping to process {processId}");
                var response = await SendPingAsync(processId);

                if (response != null)
                {
                    return new InjectionResult
                    {
                        Success = true,
                        ProcessId = processId,
                        Message = "WpfInspector injected and ping successful",
                        Response = response,
                        WasAlreadyInjected = false
                    };
                }
                else
                {
                    return new InjectionResult
                    {
                        Success = false,
                        ProcessId = processId,
                        Message = "WpfInspector injected but ping failed",
                        Error = "Ping timeout or failed"
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error during injection and ping for process {processId}");
                return new InjectionResult
                {
                    Success = false,
                    ProcessId = processId,
                    Message = "Exception occurred during injection",
                    Error = ex.Message
                };
            }
        }

        private async Task<bool> IsAlreadyInjectedAsync(int processId)
        {
            // Check our internal tracking
            if (_injectedProcesses.ContainsKey(processId))
            {
                // Try a quick ping to verify the injection is still active
                var response = await SendPingAsync(processId, TimeSpan.FromSeconds(2));
                if (response != null)
                {
                    return true;
                }
                else
                {
                    // Remove from tracking if ping failed
                    _injectedProcesses.Remove(processId);
                }
            }

            return false;
        }

        public async Task<AutomationPeerResult> InvokeAutomationPeerAsync(int processId, string type, int hashcode, string action, string? parameters = null)
        {
            _logger.LogInformation($"Starting automation peer action for process {processId}, element type: '{type}', hashcode: {hashcode}, action: '{action}'");

            try
            {
                // Check if process exists
                var process = GetProcess(processId);
                if (process == null)
                {
                    return new AutomationPeerResult
                    {
                        Success = false,
                        ProcessId = processId,
                        Type = type,
                        Hashcode = hashcode,
                        Action = action,
                        Message = $"Process with ID {processId} not found",
                        Error = "Process not found"
                    };
                }

                _logger.LogInformation($"Target process: {process.ProcessName} (PID: {process.Id})");

                // Check if already injected
                var alreadyInjected = await IsAlreadyInjectedAsync(processId);

                if (!alreadyInjected)
                {
                    // Perform injection
                    _logger.LogInformation($"Injecting WpfInspector into process {processId}");
                    var injectionSuccess = await InjectWpfInspectorAsync(processId);
                    
                    if (!injectionSuccess)
                    {
                        return new AutomationPeerResult
                        {
                            Success = false,
                            ProcessId = processId,
                            Type = type,
                            Hashcode = hashcode,
                            Action = action,
                            Message = "Failed to inject WpfInspector",
                            Error = "Injection failed"
                        };
                    }

                    _logger.LogInformation($"Successfully injected WpfInspector into process {processId}");
                }

                // Create automation peer command data
                var commandData = new
                {
                    commandType = "INVOKE_AUTOMATION_PEER",
                    type = type,
                    hashcode = hashcode,
                    action = action
                };

                // Add parameters if provided
                object finalCommandData;
                if (!string.IsNullOrWhiteSpace(parameters))
                {
                    try
                    {
                        using var paramsDoc = JsonDocument.Parse(parameters);
                        var paramsDict = new Dictionary<string, object>();
                        
                        // Copy basic command properties
                        paramsDict["commandType"] = "INVOKE_AUTOMATION_PEER";
                        paramsDict["type"] = type;
                        paramsDict["hashcode"] = hashcode;
                        paramsDict["action"] = action;
                        
                        // Add additional parameters
                        foreach (var prop in paramsDoc.RootElement.EnumerateObject())
                        {
                            paramsDict[prop.Name] = prop.Value.GetRawText().Trim('"');
                        }
                        
                        finalCommandData = paramsDict;
                    }
                    catch (JsonException)
                    {
                        // If parameters are not valid JSON, just use the basic command
                        finalCommandData = commandData;
                    }
                }
                else
                {
                    finalCommandData = commandData;
                }

                _logger.LogInformation($"Executing automation peer action on process {processId}");
                var response = await SendRunCommandAsync(processId, finalCommandData);

                if (string.IsNullOrWhiteSpace(response))
                {
                    return new AutomationPeerResult
                    {
                        Success = false,
                        ProcessId = processId,
                        Type = type,
                        Hashcode = hashcode,
                        Action = action,
                        Message = "No response received from WpfInspector",
                        Error = "Communication timeout or failure"
                    };
                }

                // Parse the JSON response
                using var doc = JsonDocument.Parse(response);
                var root = doc.RootElement;
                
                var success = root.TryGetProperty("success", out var successElement) ? successElement.GetBoolean() : false;
                var message = root.TryGetProperty("message", out var messageElement) ? messageElement.GetString() ?? "" : "";
                var error = root.TryGetProperty("error", out var errorElement) ? errorElement.GetString() : null;
                var result = root.TryGetProperty("result", out var resultElement) ? resultElement.GetRawText() : null;

                return new AutomationPeerResult
                {
                    Success = success,
                    ProcessId = processId,
                    Type = type,
                    Hashcode = hashcode,
                    Action = action,
                    Message = success ? message : (error ?? "Unknown error"),
                    Error = success ? null : (error ?? "Unknown error"),
                    Result = result
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error during automation peer action for process {processId}");
                
                return new AutomationPeerResult
                {
                    Success = false,
                    ProcessId = processId,
                    Type = type,
                    Hashcode = hashcode,
                    Action = action,
                    Message = "Exception occurred during automation peer action",
                    Error = ex.Message
                };
            }
        }

        public async Task<ScreenshotResult> TakeScreenshotAsync(int processId)
        {
            _logger.LogInformation($"Starting screenshot capture for process {processId}");

            try
            {
                // Check if process exists
                var process = GetProcess(processId);
                if (process == null)
                {
                    return new ScreenshotResult
                    {
                        Success = false,
                        ProcessId = processId,
                        Message = $"Process with ID {processId} not found",
                        Error = "Process not found"
                    };
                }

                _logger.LogInformation($"Target process: {process.ProcessName} (PID: {process.Id})");

                // Check if already injected
                var alreadyInjected = await IsAlreadyInjectedAsync(processId);

                if (!alreadyInjected)
                {
                    // Perform injection
                    _logger.LogInformation($"Injecting WpfInspector into process {processId}");
                    var injectionSuccess = await InjectWpfInspectorAsync(processId);
                    
                    if (!injectionSuccess)
                    {
                        return new ScreenshotResult
                        {
                            Success = false,
                            ProcessId = processId,
                            Message = "Failed to inject WpfInspector",
                            Error = "Injection failed"
                        };
                    }

                    _logger.LogInformation($"WpfInspector injected successfully into process {processId}");
                    _injectedProcesses[processId] = DateTime.UtcNow;

                    // Wait for pipe server to start
                    _logger.LogInformation("Waiting for pipe server to start...");
                    await Task.Delay(2000);
                }

                // Send screenshot command
                _logger.LogInformation($"Sending screenshot command to process {processId}");
                var commandResult = await SendScreenshotCommandAsync(processId);

                bool success = false;
                string? message = null;
                string? error = null;
                string? windowTitle = null;
                int width = 0;
                int height = 0;
                string? imageData = null;
                string format = "PNG";

                if (commandResult != null)
                {
                    var doc = JsonDocument.Parse(commandResult);
                    var root = doc.RootElement;
                    
                    success = root.TryGetProperty("success", out var successElement) && successElement.GetBoolean();
                    message = root.TryGetProperty("message", out var messageElement) ? messageElement.GetString() : null;
                    error = root.TryGetProperty("error", out var errorElement) ? errorElement.GetString() : null;
                    windowTitle = root.TryGetProperty("windowTitle", out var titleElement) ? titleElement.GetString() : null;
                    
                    if (root.TryGetProperty("width", out var widthElement))
                        width = widthElement.GetInt32();
                    if (root.TryGetProperty("height", out var heightElement))
                        height = heightElement.GetInt32();
                    
                    imageData = root.TryGetProperty("imageData", out var imageElement) ? imageElement.GetString() : null;
                    format = root.TryGetProperty("format", out var formatElement) ? formatElement.GetString() ?? "PNG" : "PNG";
                }

                return new ScreenshotResult
                {
                    Success = success,
                    ProcessId = processId,
                    ProcessName = process.ProcessName,
                    Message = success ? message : (error ?? "Unknown error"),
                    Error = success ? null : (error ?? "Unknown error"),
                    WindowTitle = windowTitle,
                    Width = width,
                    Height = height,
                    ImageData = imageData,
                    Format = format
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error during screenshot capture for process {processId}");
                
                return new ScreenshotResult
                {
                    Success = false,
                    ProcessId = processId,
                    Message = "Exception occurred during screenshot capture",
                    Error = ex.Message
                };
            }
        }

        public async Task<VisualTreeResult> GetVisualTreeAsync(int processId)
        {
            _logger.LogInformation($"Starting visual tree retrieval for process {processId}");

            try
            {
                // Check if process exists
                var process = GetProcess(processId);
                if (process == null)
                {
                    return new VisualTreeResult
                    {
                        Success = false,
                        ProcessId = processId,
                        Message = $"Process with ID {processId} not found",
                        Error = "Process not found"
                    };
                }

                _logger.LogInformation($"Target process: {process.ProcessName} (PID: {process.Id})");

                // Check if already injected
                var alreadyInjected = await IsAlreadyInjectedAsync(processId);
                
                if (!alreadyInjected)
                {
                    _logger.LogInformation($"WpfInspector not yet injected into process {processId}, injecting now...");
                    
                    var injectionResult = await PingAsync(processId);
                    if (!injectionResult.Success)
                    {
                        return new VisualTreeResult
                        {
                            Success = false,
                            ProcessId = processId,
                            Message = $"Failed to inject WpfInspector: {injectionResult.Error}",
                            Error = injectionResult.Error ?? "Injection failed"
                        };
                    }
                    _logger.LogInformation($"Successfully injected WpfInspector into process {processId}");
                }
                else
                {
                    _logger.LogInformation($"WpfInspector already injected into process {processId}");
                }

                // Send visual tree command
                var response = await SendVisualTreeCommandAsync(processId);
                
                if (string.IsNullOrEmpty(response))
                {
                    return new VisualTreeResult
                    {
                        Success = false,
                        ProcessId = processId,
                        Message = "No response received from WpfInspector",
                        Error = "No response"
                    };
                }

                _logger.LogInformation($"Successfully retrieved visual tree for process {processId}");
                
                return new VisualTreeResult
                {
                    Success = true,
                    ProcessId = processId,
                    Message = "Visual tree retrieved successfully",
                    VisualTreeJson = response
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error during visual tree retrieval for process {processId}");
                
                return new VisualTreeResult
                {
                    Success = false,
                    ProcessId = processId,
                    Message = "Exception occurred during visual tree retrieval",
                    Error = ex.Message
                };
            }
        }

        public async Task<ElementResult> GetElementByHashcodeAsync(int processId, string type, int hashcode)
        {
            _logger.LogInformation($"Starting element retrieval for process {processId}, type: '{type}', hashcode: {hashcode}");

            try
            {
                // Check if process exists
                var process = GetProcess(processId);
                if (process == null)
                {
                    return new ElementResult
                    {
                        Success = false,
                        ProcessId = processId,
                        Type = type,
                        Hashcode = hashcode,
                        Message = $"Process with ID {processId} not found",
                        Error = "Process not found"
                    };
                }

                _logger.LogInformation($"Target process: {process.ProcessName} (PID: {process.Id})");

                // Check if already injected
                var alreadyInjected = await IsAlreadyInjectedAsync(processId);

                if (!alreadyInjected)
                {
                    // Perform injection
                    _logger.LogInformation($"Injecting WpfInspector into process {processId}");
                    var injectionSuccess = await InjectWpfInspectorAsync(processId);
                    
                    if (!injectionSuccess)
                    {
                        return new ElementResult
                        {
                            Success = false,
                            ProcessId = processId,
                            Type = type,
                            Hashcode = hashcode,
                            Message = "Failed to inject WpfInspector",
                            Error = "Injection failed"
                        };
                    }

                    _logger.LogInformation($"Successfully injected WpfInspector into process {processId}");
                    _injectedProcesses[processId] = DateTime.UtcNow;

                    // Wait for pipe server to start
                    _logger.LogInformation("Waiting for pipe server to start...");
                    await Task.Delay(2000);
                }

                // Create get element command data
                var commandData = new
                {
                    commandType = "GET_ELEMENT_BY_HASHCODE",
                    type = type,
                    hashcode = hashcode
                };

                _logger.LogInformation($"Executing get element by hashcode on process {processId}");
                var response = await SendRunCommandAsync(processId, commandData);

                if (string.IsNullOrWhiteSpace(response))
                {
                    return new ElementResult
                    {
                        Success = false,
                        ProcessId = processId,
                        Type = type,
                        Hashcode = hashcode,
                        Message = "No response received from WpfInspector",
                        Error = "Communication timeout or failure"
                    };
                }

                // Parse the JSON response
                using var doc = JsonDocument.Parse(response);
                var root = doc.RootElement;
                
                var success = root.TryGetProperty("success", out var successElement) ? successElement.GetBoolean() : false;
                var message = root.TryGetProperty("message", out var messageElement) ? messageElement.GetString() ?? "" : "";
                var error = root.TryGetProperty("error", out var errorElement) ? errorElement.GetString() : null;
                var timestamp = root.TryGetProperty("timestamp", out var timestampElement) ? timestampElement.GetString() : null;
                
                // Get the element and dataContexts objects
                object? element = null;
                if (root.TryGetProperty("element", out var elementElement) && elementElement.ValueKind != JsonValueKind.Null)
                {
                    element = JsonSerializer.Deserialize<object>(elementElement.GetRawText());
                }

                object? dataContexts = null;
                if (root.TryGetProperty("dataContexts", out var dataContextsElement) && dataContextsElement.ValueKind != JsonValueKind.Null)
                {
                    dataContexts = JsonSerializer.Deserialize<object>(dataContextsElement.GetRawText());
                }

                return new ElementResult
                {
                    Success = success,
                    ProcessId = processId,
                    Type = type,
                    Hashcode = hashcode,
                    Message = success ? message : (error ?? "Unknown error"),
                    Error = success ? null : (error ?? "Unknown error"),
                    Element = element,
                    DataContexts = dataContexts,
                    Timestamp = timestamp
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error during element retrieval for process {processId}");
                
                return new ElementResult
                {
                    Success = false,
                    ProcessId = processId,
                    Type = type,
                    Hashcode = hashcode,
                    Message = "Exception occurred during element retrieval",
                    Error = ex.Message
                };
            }
        }


        private Process? GetProcess(int processId)
        {
            try
            {
                return Process.GetProcessById(processId);
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        private async Task<bool> InjectWpfInspectorAsync(int processId)
        {
            try
            {
                // Get the path to WpfInspector.dll
                var currentDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                var wpfInspectorPath = Path.Combine(currentDirectory!, "WpfInspector.dll");
                
                if (!File.Exists(wpfInspectorPath))
                {
                    _logger.LogError($"Could not find WpfInspector.dll at {wpfInspectorPath}");
                    return false;
                }

                // Get the path to Snoop.InjectorLauncher
                var injectorLauncherPath = Path.Combine(currentDirectory!, "Snoop.InjectorLauncher.x64.exe");
                
                if (!File.Exists(injectorLauncherPath))
                {
                    _logger.LogError($"Could not find Snoop.InjectorLauncher at {injectorLauncherPath}");
                    return false;
                }

                // Build the command line arguments for the injector launcher
                var arguments = $"--targetPID {processId} " +
                              $"--assembly \"{wpfInspectorPath}\" " +
                              $"--className \"WpfInspector.Inspector\" " +
                              $"--methodName \"Initialize\"";

                _logger.LogDebug($"Executing: {injectorLauncherPath} {arguments}");

                // Start the injector launcher process
                var processStartInfo = new ProcessStartInfo
                {
                    FileName = injectorLauncherPath,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var injectorProcess = Process.Start(processStartInfo);
                if (injectorProcess == null)
                {
                    _logger.LogError("Failed to start injector launcher process");
                    return false;
                }

                // Read output from the injector
                var output = await injectorProcess.StandardOutput.ReadToEndAsync();
                var error = await injectorProcess.StandardError.ReadToEndAsync();

                await injectorProcess.WaitForExitAsync();

                if (!string.IsNullOrEmpty(output))
                {
                    _logger.LogDebug($"Injector output: {output}");
                }

                if (!string.IsNullOrEmpty(error))
                {
                    _logger.LogWarning($"Injector errors: {error}");
                }

                var success = injectorProcess.ExitCode == 0;
                _logger.LogInformation($"Injection process completed with exit code: {injectorProcess.ExitCode}");
                
                return success;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception during injection");
                return false;
            }
        }

        private async Task<string?> SendPingAsync(int processId, TimeSpan? timeout = null)
        {
            var actualTimeout = timeout ?? TimeSpan.FromSeconds(10);
            var pipeName = $"WpfInspector_{processId}";

            try
            {
                using var cts = new CancellationTokenSource(actualTimeout);
                
                _logger.LogDebug($"Connecting to named pipe: {pipeName}");
                
                using var pipeClient = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut);
                
                // Try to connect with timeout
                await pipeClient.ConnectAsync(cts.Token);
                _logger.LogDebug("Connected to pipe successfully");

                // Send ping message
                var message = "PING";
                var messageBytes = Encoding.UTF8.GetBytes(message);
                await pipeClient.WriteAsync(messageBytes, 0, messageBytes.Length, cts.Token);
                await pipeClient.FlushAsync(cts.Token);
                
                _logger.LogDebug($"Sent message: {message}");

                // Read response
                var buffer = new byte[1024];
                var bytesRead = await pipeClient.ReadAsync(buffer, 0, buffer.Length, cts.Token);
                
                var response = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                _logger.LogDebug($"Received response: {response}");
                
                return response;
            }
            catch (TimeoutException)
            {
                _logger.LogDebug($"Ping timeout after {actualTimeout.TotalSeconds} seconds");
                return null;
            }
            catch (OperationCanceledException)
            {
                _logger.LogDebug($"Ping operation cancelled after {actualTimeout.TotalSeconds} seconds");
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogDebug($"Error during ping: {ex.Message}");
                return null;
            }
        }

        private async Task<string?> SendScreenshotCommandAsync(int processId, TimeSpan? timeout = null)
        {
            var actualTimeout = timeout ?? TimeSpan.FromSeconds(15);
            var pipeName = $"WpfInspector_{processId}";

            try
            {
                using var cts = new CancellationTokenSource(actualTimeout);
                
                _logger.LogDebug($"Connecting to named pipe: {pipeName}");
                
                using var pipeClient = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut);
                
                // Try to connect with timeout
                await pipeClient.ConnectAsync(cts.Token);
                _logger.LogDebug("Connected to pipe successfully");

                // Create screenshot command
                var command = new
                {
                    command = "TAKE_SCREENSHOT"
                };

                var commandJson = JsonSerializer.Serialize(command, GetJsonOptions());
                var messageBytes = Encoding.UTF8.GetBytes(commandJson);
                await pipeClient.WriteAsync(messageBytes, 0, messageBytes.Length, cts.Token);
                await pipeClient.FlushAsync(cts.Token);
                
                _logger.LogDebug($"Sent command: {commandJson}");

                // Read response - use larger buffer for base64 image data
                var bufferSize = 1024 * 1024; // 1MB buffer for large screenshots
                var buffer = new byte[bufferSize];
                var totalBytesRead = 0;
                var allData = new List<byte>();

                // Read all available data
                while (pipeClient.IsConnected && !cts.Token.IsCancellationRequested)
                {
                    var bytesRead = await pipeClient.ReadAsync(buffer, 0, buffer.Length, cts.Token);
                    if (bytesRead == 0) break;
                    
                    allData.AddRange(buffer.Take(bytesRead));
                    totalBytesRead += bytesRead;
                    
                    // If we read less than the buffer size, we've likely read all data
                    if (bytesRead < buffer.Length) break;
                }
                
                var response = Encoding.UTF8.GetString(allData.ToArray());
                _logger.LogDebug($"Received response ({totalBytesRead} bytes): {response.Substring(0, Math.Min(200, response.Length))}...");
                
                return response;
            }
            catch (TimeoutException)
            {
                _logger.LogDebug($"Screenshot command timeout after {actualTimeout.TotalSeconds} seconds");
                return null;
            }
            catch (OperationCanceledException)
            {
                _logger.LogDebug($"Screenshot command cancelled after {actualTimeout.TotalSeconds} seconds");
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogDebug($"Error during screenshot command: {ex.Message}");
                return null;
            }
        }

        private async Task<string?> SendVisualTreeCommandAsync(int processId, TimeSpan? timeout = null)
        {
            var actualTimeout = timeout ?? TimeSpan.FromSeconds(15);
            var pipeName = $"WpfInspector_{processId}";

            try
            {
                using var cts = new CancellationTokenSource(actualTimeout);
                
                _logger.LogDebug($"Connecting to named pipe: {pipeName}");
                
                using var pipeClient = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut);
                
                // Try to connect with timeout
                await pipeClient.ConnectAsync(cts.Token);
                _logger.LogDebug("Connected to pipe successfully");

                // Create visual tree command
                var command = new
                {
                    command = "GET_VISUAL_TREE"
                };

                var commandJson = JsonSerializer.Serialize(command, GetJsonOptions());
                var messageBytes = Encoding.UTF8.GetBytes(commandJson);
                await pipeClient.WriteAsync(messageBytes, 0, messageBytes.Length, cts.Token);
                await pipeClient.FlushAsync(cts.Token);
                
                _logger.LogDebug($"Sent command: {commandJson}");

                // Read response - using larger buffer and chunked reading for potentially large visual tree
                var allData = new List<byte>();
                var buffer = new byte[8192];
                int totalBytesRead = 0;
                
                while (true)
                {
                    var bytesRead = await pipeClient.ReadAsync(buffer, 0, buffer.Length, cts.Token);
                    if (bytesRead == 0) break;
                    
                    totalBytesRead += bytesRead;
                    for (int i = 0; i < bytesRead; i++)
                    {
                        allData.Add(buffer[i]);
                    }
                    
                    // Check if we have a complete JSON response
                    var currentData = Encoding.UTF8.GetString(allData.ToArray());
                    if (IsCompleteJson(currentData))
                    {
                        break;
                    }
                    
                    // Safety check to prevent infinite reading
                    if (totalBytesRead > 10 * 1024 * 1024) // 10MB limit
                    {
                        _logger.LogWarning("Visual tree response exceeded 10MB limit, stopping read");
                        break;
                    }
                }
                
                var response = Encoding.UTF8.GetString(allData.ToArray());
                _logger.LogDebug($"Received visual tree response ({totalBytesRead} bytes): {response.Substring(0, Math.Min(200, response.Length))}...");
                
                return response;
            }
            catch (TimeoutException)
            {
                _logger.LogDebug($"Visual tree command timeout after {actualTimeout.TotalSeconds} seconds");
                return null;
            }
            catch (OperationCanceledException)
            {
                _logger.LogDebug($"Visual tree command cancelled after {actualTimeout.TotalSeconds} seconds");
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogDebug($"Error during visual tree command: {ex.Message}");
                return null;
            }
        }

        private async Task<string?> SendRunCommandAsync(int processId, object commandData, TimeSpan? timeout = null)
        {
            var actualTimeout = timeout ?? TimeSpan.FromSeconds(15);
            var pipeName = $"WpfInspector_{processId}";

            try
            {
                using var cts = new CancellationTokenSource(actualTimeout);
                
                _logger.LogDebug($"Connecting to named pipe: {pipeName}");
                
                using var pipeClient = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut);
                
                // Try to connect with timeout
                await pipeClient.ConnectAsync(cts.Token);
                _logger.LogDebug("Connected to pipe successfully");

                var commandJson = JsonSerializer.Serialize(commandData, GetJsonOptions());
                var messageBytes = Encoding.UTF8.GetBytes(commandJson);
                await pipeClient.WriteAsync(messageBytes, 0, messageBytes.Length, cts.Token);
                await pipeClient.FlushAsync(cts.Token);
                
                _logger.LogDebug($"Sent command: {commandJson}");

                // Read response
                var buffer = new byte[4096]; // Buffer for JSON response
                var bytesRead = await pipeClient.ReadAsync(buffer, 0, buffer.Length, cts.Token);
                
                var response = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                _logger.LogDebug($"Received response: {response}");
                
                return response;
            }
            catch (TimeoutException)
            {
                _logger.LogDebug($"Run command timeout after {actualTimeout.TotalSeconds} seconds");
                return null;
            }
            catch (OperationCanceledException)
            {
                _logger.LogDebug($"Run command cancelled after {actualTimeout.TotalSeconds} seconds");
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogDebug($"Error during run command: {ex.Message}");
                return null;
            }
        }

        private bool IsCompleteJson(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
