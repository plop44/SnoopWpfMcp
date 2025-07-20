using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Injector.Models;
using Microsoft.Extensions.Logging;

namespace Injector.Services
{
    public interface IInjectionService
    {
        Task<InjectionResult> InjectAndPingAsync(int processId);
        Task<bool> IsAlreadyInjectedAsync(int processId);
        Task<ButtonClickResult> ClickButtonAsync(int processId, string buttonText);
        Task<ButtonClickResult> FindInjectAndClickButtonAsync(string processIdentifier, string buttonText, bool isProcessId = false);
        Task<CommandResult> RunCommandAsync(int processId, string elementType, int hashcode, string command, string? parameters = null);
        Task<CommandResult> FindInjectAndRunCommandAsync(string processIdentifier, string elementType, int hashcode, string command, string? parameters = null, bool isProcessId = false);
        Task<ScreenshotResult> TakeScreenshotAsync(int processId);
        Task<ScreenshotResult> FindInjectAndTakeScreenshotAsync(string processIdentifier, bool isProcessId = false);
        Task<VisualTreeResult> GetVisualTreeAsync(int processId);
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

        public async Task<InjectionResult> InjectAndPingAsync(int processId)
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

        public async Task<bool> IsAlreadyInjectedAsync(int processId)
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

        public async Task<ButtonClickResult> ClickButtonAsync(int processId, string buttonText)
        {
            _logger.LogInformation($"Starting button click for process {processId}, button text: '{buttonText}'");

            try
            {
                // Check if process exists
                var process = GetProcess(processId);
                if (process == null)
                {
                    return new ButtonClickResult
                    {
                        Success = false,
                        ProcessId = processId,
                        ButtonText = buttonText,
                        Message = $"Process with ID {processId} not found",
                        Error = "Process not found"
                    };
                }

                _logger.LogInformation($"Target process: {process.ProcessName} (PID: {process.Id})");

                // Check if already injected
                var alreadyInjected = await IsAlreadyInjectedAsync(processId);
                var wasInjected = false;

                if (!alreadyInjected)
                {
                    // Perform injection
                    _logger.LogInformation($"Injecting WpfInspector into process {processId}");
                    var injectionSuccess = await InjectWpfInspectorAsync(processId);
                    
                    if (!injectionSuccess)
                    {
                        return new ButtonClickResult
                        {
                            Success = false,
                            ProcessId = processId,
                            ButtonText = buttonText,
                            Message = "Failed to inject WpfInspector",
                            Error = "Injection failed"
                        };
                    }

                    _logger.LogInformation($"WpfInspector injected successfully into process {processId}");
                    _injectedProcesses[processId] = DateTime.UtcNow;
                    wasInjected = true;

                    // Wait for pipe server to start
                    _logger.LogInformation("Waiting for pipe server to start...");
                    await Task.Delay(2000);
                }

                // Send button click command
                _logger.LogInformation($"Sending button click command to process {processId}");
                var response = await SendButtonClickCommandAsync(processId, buttonText);
                
                if (response == null)
                {
                    return new ButtonClickResult
                    {
                        Success = false,
                        ProcessId = processId,
                        ButtonText = buttonText,
                        Message = "No response received from WpfInspector",
                        Error = "Communication timeout",
                        WasInjected = wasInjected
                    };
                }

                // Parse the JSON response
                using var doc = JsonDocument.Parse(response);
                var root = doc.RootElement;
                
                var success = root.TryGetProperty("success", out var successElement) ? successElement.GetBoolean() : false;
                var message = root.TryGetProperty("message", out var messageElement) ? messageElement.GetString() ?? "" : "";
                var error = root.TryGetProperty("error", out var errorElement) ? errorElement.GetString() : null;
                var buttonName = root.TryGetProperty("buttonName", out var buttonNameElement) ? buttonNameElement.GetString() : null;
                var buttonContent = root.TryGetProperty("buttonContent", out var buttonContentElement) ? buttonContentElement.GetString() : null;

                return new ButtonClickResult
                {
                    Success = success,
                    ProcessId = processId,
                    ButtonText = buttonText,
                    Message = success ? message : (error ?? "Unknown error"),
                    Error = success ? null : (error ?? "Unknown error"),
                    ButtonName = buttonName,
                    ButtonContent = buttonContent,
                    WasInjected = wasInjected
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error during button click for process {processId}");
                
                return new ButtonClickResult
                {
                    Success = false,
                    ProcessId = processId,
                    ButtonText = buttonText,
                    Message = "Exception occurred during button click",
                    Error = ex.Message
                };
            }
        }

        public async Task<CommandResult> RunCommandAsync(int processId, string elementType, int hashcode, string command, string? parameters = null)
        {
            _logger.LogInformation($"Starting command execution for process {processId}, element type: '{elementType}', hashcode: {hashcode}, command: '{command}'");

            try
            {
                // Check if process exists
                var process = GetProcess(processId);
                if (process == null)
                {
                    return new CommandResult
                    {
                        Success = false,
                        ProcessId = processId,
                        ElementType = elementType,
                        Hashcode = hashcode,
                        Command = command,
                        Message = $"Process with ID {processId} not found",
                        Error = "Process not found"
                    };
                }

                _logger.LogInformation($"Target process: {process.ProcessName} (PID: {process.Id})");

                // Check if already injected
                var alreadyInjected = await IsAlreadyInjectedAsync(processId);
                var wasInjected = false;

                if (!alreadyInjected)
                {
                    // Perform injection
                    _logger.LogInformation($"Injecting WpfInspector into process {processId}");
                    var injectionSuccess = await InjectWpfInspectorAsync(processId);
                    
                    if (!injectionSuccess)
                    {
                        return new CommandResult
                        {
                            Success = false,
                            ProcessId = processId,
                            ElementType = elementType,
                            Hashcode = hashcode,
                            Command = command,
                            Message = "Failed to inject WpfInspector",
                            Error = "Injection failed"
                        };
                    }

                    wasInjected = true;
                    _logger.LogInformation($"Successfully injected WpfInspector into process {processId}");
                }

                // Create command data
                var commandData = new
                {
                    commandType = "RUN_COMMAND",
                    type = elementType,
                    hashcode = hashcode,
                    command = command
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
                        paramsDict["commandType"] = "RUN_COMMAND";
                        paramsDict["type"] = elementType;
                        paramsDict["hashcode"] = hashcode;
                        paramsDict["command"] = command;
                        
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

                _logger.LogInformation($"Executing command on process {processId}");
                var response = await SendRunCommandAsync(processId, finalCommandData);

                if (string.IsNullOrWhiteSpace(response))
                {
                    return new CommandResult
                    {
                        Success = false,
                        ProcessId = processId,
                        ElementType = elementType,
                        Hashcode = hashcode,
                        Command = command,
                        Message = "No response received from WpfInspector",
                        Error = "Communication timeout or failure",
                        WasInjected = wasInjected
                    };
                }

                // Parse the JSON response
                using var doc = JsonDocument.Parse(response);
                var root = doc.RootElement;
                
                var success = root.TryGetProperty("success", out var successElement) ? successElement.GetBoolean() : false;
                var message = root.TryGetProperty("message", out var messageElement) ? messageElement.GetString() ?? "" : "";
                var error = root.TryGetProperty("error", out var errorElement) ? errorElement.GetString() : null;
                var result = root.TryGetProperty("result", out var resultElement) ? resultElement.GetRawText() : null;

                return new CommandResult
                {
                    Success = success,
                    ProcessId = processId,
                    ElementType = elementType,
                    Hashcode = hashcode,
                    Command = command,
                    Message = success ? message : (error ?? "Unknown error"),
                    Error = success ? null : (error ?? "Unknown error"),
                    Result = result,
                    WasInjected = wasInjected
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error during command execution for process {processId}");
                
                return new CommandResult
                {
                    Success = false,
                    ProcessId = processId,
                    ElementType = elementType,
                    Hashcode = hashcode,
                    Command = command,
                    Message = "Exception occurred during command execution",
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
                var wasInjected = false;

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
                    wasInjected = true;

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
                    Format = format,
                    WasInjected = wasInjected
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
                    
                    var injectionResult = await InjectAndPingAsync(processId);
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

        public async Task<ScreenshotResult> FindInjectAndTakeScreenshotAsync(string processIdentifier, bool isProcessId = false)
        {
            _logger.LogInformation($"Starting find, inject and screenshot for process identifier: '{processIdentifier}' (isProcessId: {isProcessId})");

            try
            {
                WpfProcessInfo? targetProcess = null;

                if (isProcessId)
                {
                    // Try to parse as process ID
                    if (int.TryParse(processIdentifier, out var pid))
                    {
                        var allProcesses = await _processService.GetWpfProcessesAsync();
                        targetProcess = allProcesses.FirstOrDefault(p => p.ProcessId == pid);
                        
                        if (targetProcess == null)
                        {
                            return new ScreenshotResult
                            {
                                Success = false,
                                ProcessId = pid,
                                Message = $"No WPF process found with ID {pid}",
                                Error = "Process not found"
                            };
                        }
                    }
                    else
                    {
                        return new ScreenshotResult
                        {
                            Success = false,
                            ProcessId = 0,
                            Message = $"Invalid process ID format: '{processIdentifier}'",
                            Error = "Invalid process ID"
                        };
                    }
                }
                else
                {
                    // Find process by name or window title
                    var allProcesses = await _processService.GetWpfProcessesAsync();
                    
                    // First try exact match on process name
                    targetProcess = allProcesses.FirstOrDefault(p => 
                        string.Equals(p.ProcessName, processIdentifier, StringComparison.OrdinalIgnoreCase));

                    // If not found, try partial match on process name
                    if (targetProcess == null)
                    {
                        targetProcess = allProcesses.FirstOrDefault(p => 
                            p.ProcessName.Contains(processIdentifier, StringComparison.OrdinalIgnoreCase));
                    }

                    // If still not found, try window title
                    if (targetProcess == null)
                    {
                        targetProcess = allProcesses.FirstOrDefault(p => 
                            !string.IsNullOrEmpty(p.MainWindowTitle) && 
                            p.MainWindowTitle.Contains(processIdentifier, StringComparison.OrdinalIgnoreCase));
                    }

                    if (targetProcess == null)
                    {
                        var availableProcesses = allProcesses.Take(10).Select(p => $"{p.ProcessName} ({p.ProcessId})").ToList();
                        var processList = availableProcesses.Any() 
                            ? $"Available processes (first 10): {string.Join(", ", availableProcesses)}"
                            : "No WPF processes found";

                        return new ScreenshotResult
                        {
                            Success = false,
                            ProcessId = 0,
                            Message = $"No WPF process found matching '{processIdentifier}'. {processList}",
                            Error = "Process not found"
                        };
                    }
                }

                _logger.LogInformation($"Found target process: {targetProcess.ProcessName} (PID: {targetProcess.ProcessId})");

                // Now take the screenshot
                return await TakeScreenshotAsync(targetProcess.ProcessId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error during find, inject and screenshot for '{processIdentifier}'");
                
                return new ScreenshotResult
                {
                    Success = false,
                    ProcessId = 0,
                    Message = "Exception occurred during find, inject and screenshot",
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

        private async Task<string?> SendButtonClickCommandAsync(int processId, string buttonText, TimeSpan? timeout = null)
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

                // Create button click command
                var command = new
                {
                    command = "CLICK_BUTTON",
                    buttonText = buttonText
                };

                var commandJson = JsonSerializer.Serialize(command);
                var messageBytes = Encoding.UTF8.GetBytes(commandJson);
                await pipeClient.WriteAsync(messageBytes, 0, messageBytes.Length, cts.Token);
                await pipeClient.FlushAsync(cts.Token);
                
                _logger.LogDebug($"Sent command: {commandJson}");

                // Read response
                var buffer = new byte[4096]; // Larger buffer for JSON response
                var bytesRead = await pipeClient.ReadAsync(buffer, 0, buffer.Length, cts.Token);
                
                var response = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                _logger.LogDebug($"Received response: {response}");
                
                return response;
            }
            catch (TimeoutException)
            {
                _logger.LogDebug($"Button click command timeout after {actualTimeout.TotalSeconds} seconds");
                return null;
            }
            catch (OperationCanceledException)
            {
                _logger.LogDebug($"Button click command cancelled after {actualTimeout.TotalSeconds} seconds");
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogDebug($"Error during button click command: {ex.Message}");
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

                var commandJson = JsonSerializer.Serialize(command);
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

                var commandJson = JsonSerializer.Serialize(command);
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

                var commandJson = JsonSerializer.Serialize(commandData);
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

        public async Task<ButtonClickResult> FindInjectAndClickButtonAsync(string processIdentifier, string buttonText, bool isProcessId = false)
        {
            _logger.LogInformation($"Starting consolidated find-inject-click for '{processIdentifier}', button text: '{buttonText}', isProcessId: {isProcessId}");

            try
            {
                if (string.IsNullOrWhiteSpace(processIdentifier))
                {
                    return new ButtonClickResult
                    {
                        Success = false,
                        ProcessId = 0,
                        ButtonText = buttonText,
                        Error = "Process identifier cannot be empty",
                        Message = "Invalid input: process identifier is required"
                    };
                }

                if (string.IsNullOrWhiteSpace(buttonText))
                {
                    return new ButtonClickResult
                    {
                        Success = false,
                        ProcessId = 0,
                        ButtonText = buttonText ?? "",
                        Error = "Button text cannot be empty",
                        Message = "Invalid input: button text is required"
                    };
                }

                WpfProcessInfo? processInfo = null;
                int targetProcessId = 0;

                // Step 1: Find the process
                if (isProcessId)
                {
                    // Process identifier is a PID
                    if (int.TryParse(processIdentifier, out targetProcessId))
                    {
                        _logger.LogInformation($"Looking up process by PID: {targetProcessId}");
                        processInfo = await _processService.GetProcessInfoAsync(targetProcessId);
                        if (processInfo == null)
                        {
                            return new ButtonClickResult
                            {
                                Success = false,
                                ProcessId = targetProcessId,
                                ButtonText = buttonText,
                                Error = $"Process with ID {targetProcessId} not found or not accessible",
                                Message = "Process not found"
                            };
                        }
                    }
                    else
                    {
                        return new ButtonClickResult
                        {
                            Success = false,
                            ProcessId = 0,
                            ButtonText = buttonText,
                            Error = $"Invalid process ID format: '{processIdentifier}'",
                            Message = "Process ID must be a valid integer"
                        };
                    }
                }
                else
                {
                    // Process identifier is a name/title
                    _logger.LogInformation($"Looking up process by name: '{processIdentifier}'");
                    processInfo = await _processService.FindProcessByNameAsync(processIdentifier);
                    if (processInfo == null)
                    {
                        return new ButtonClickResult
                        {
                            Success = false,
                            ProcessId = 0,
                            ButtonText = buttonText,
                            Error = $"No WPF process found matching '{processIdentifier}'",
                            Message = "Process not found by name"
                        };
                    }
                    targetProcessId = processInfo.ProcessId;
                }

                _logger.LogInformation($"Found target process: {processInfo.ProcessName} (PID: {targetProcessId})");

                if (!processInfo.IsWpfApplication)
                {
                    return new ButtonClickResult
                    {
                        Success = false,
                        ProcessId = targetProcessId,
                        ButtonText = buttonText,
                        Error = $"Process {targetProcessId} ({processInfo.ProcessName}) is not a WPF application",
                        Message = "Target process is not WPF"
                    };
                }

                // Step 2: Inject WpfInspector if not already done
                var wasAlreadyInjected = await IsAlreadyInjectedAsync(targetProcessId);
                if (!wasAlreadyInjected)
                {
                    _logger.LogInformation($"WpfInspector not yet injected into process {targetProcessId}, injecting now...");
                    var injectionResult = await InjectAndPingAsync(targetProcessId);
                    if (!injectionResult.Success)
                    {
                        return new ButtonClickResult
                        {
                            Success = false,
                            ProcessId = targetProcessId,
                            ButtonText = buttonText,
                            Error = injectionResult.Error,
                            Message = "Failed to inject WpfInspector",
                            WasInjected = false
                        };
                    }
                    _logger.LogInformation($"Successfully injected WpfInspector into process {targetProcessId}");
                }
                else
                {
                    _logger.LogInformation($"WpfInspector already injected into process {targetProcessId}");
                }

                // Step 3: Click the button
                _logger.LogInformation($"Attempting to click button '{buttonText}' in process {targetProcessId}");
                var clickResult = await ClickButtonAsync(targetProcessId, buttonText);
                
                // Add process name to the result for better context
                clickResult.ProcessName = processInfo.ProcessName;
                clickResult.WasInjected = !wasAlreadyInjected;

                if (clickResult.Success)
                {
                    _logger.LogInformation($"Successfully completed find-inject-click for '{processIdentifier}' -> PID {targetProcessId}");
                }
                else
                {
                    _logger.LogWarning($"Failed to click button in process {targetProcessId}: {clickResult.Error}");
                }

                return clickResult;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error during find-inject-click for '{processIdentifier}', button text: '{buttonText}'");
                
                return new ButtonClickResult
                {
                    Success = false,
                    ProcessId = 0,
                    ButtonText = buttonText ?? "",
                    Error = ex.Message,
                    Message = "Exception occurred during find-inject-click operation",
                    WasInjected = false
                };
            }
        }

        public async Task<CommandResult> FindInjectAndRunCommandAsync(string processIdentifier, string elementType, int hashcode, string command, string? parameters = null, bool isProcessId = false)
        {
            _logger.LogInformation($"Starting consolidated find-inject-command for '{processIdentifier}', element type: '{elementType}', hashcode: {hashcode}, command: '{command}', isProcessId: {isProcessId}");

            try
            {
                if (string.IsNullOrWhiteSpace(processIdentifier))
                {
                    return new CommandResult
                    {
                        Success = false,
                        ProcessId = 0,
                        ElementType = elementType,
                        Hashcode = hashcode,
                        Command = command,
                        Error = "Process identifier cannot be empty",
                        Message = "Invalid input: process identifier is required"
                    };
                }

                if (string.IsNullOrWhiteSpace(elementType))
                {
                    return new CommandResult
                    {
                        Success = false,
                        ProcessId = 0,
                        ElementType = elementType ?? "",
                        Hashcode = hashcode,
                        Command = command,
                        Error = "Element type cannot be empty",
                        Message = "Invalid input: element type is required"
                    };
                }

                if (string.IsNullOrWhiteSpace(command))
                {
                    return new CommandResult
                    {
                        Success = false,
                        ProcessId = 0,
                        ElementType = elementType,
                        Hashcode = hashcode,
                        Command = command ?? "",
                        Error = "Command cannot be empty",
                        Message = "Invalid input: command is required"
                    };
                }

                WpfProcessInfo? processInfo = null;
                int targetProcessId = 0;

                // Step 1: Find the process
                if (isProcessId)
                {
                    // Process identifier is a PID
                    if (int.TryParse(processIdentifier, out targetProcessId))
                    {
                        _logger.LogInformation($"Looking up process by PID: {targetProcessId}");
                        processInfo = await _processService.GetProcessInfoAsync(targetProcessId);
                        if (processInfo == null)
                        {
                            return new CommandResult
                            {
                                Success = false,
                                ProcessId = targetProcessId,
                                ElementType = elementType,
                                Hashcode = hashcode,
                                Command = command,
                                Error = $"Process with ID {targetProcessId} not found or not accessible",
                                Message = "Process not found"
                            };
                        }
                    }
                    else
                    {
                        return new CommandResult
                        {
                            Success = false,
                            ProcessId = 0,
                            ElementType = elementType,
                            Hashcode = hashcode,
                            Command = command,
                            Error = $"Invalid process ID format: '{processIdentifier}'",
                            Message = "Process ID must be a valid integer"
                        };
                    }
                }
                else
                {
                    // Process identifier is a name/title
                    _logger.LogInformation($"Looking up process by name: '{processIdentifier}'");
                    processInfo = await _processService.FindProcessByNameAsync(processIdentifier);
                    if (processInfo == null)
                    {
                        return new CommandResult
                        {
                            Success = false,
                            ProcessId = 0,
                            ElementType = elementType,
                            Hashcode = hashcode,
                            Command = command,
                            Error = $"No WPF process found matching '{processIdentifier}'",
                            Message = "Process not found by name"
                        };
                    }
                    targetProcessId = processInfo.ProcessId;
                }

                _logger.LogInformation($"Found target process: {processInfo.ProcessName} (PID: {targetProcessId})");

                if (!processInfo.IsWpfApplication)
                {
                    return new CommandResult
                    {
                        Success = false,
                        ProcessId = targetProcessId,
                        ElementType = elementType,
                        Hashcode = hashcode,
                        Command = command,
                        Error = $"Process {targetProcessId} ({processInfo.ProcessName}) is not a WPF application",
                        Message = "Target process is not WPF"
                    };
                }

                // Step 2: Inject WpfInspector if not already done
                var wasAlreadyInjected = await IsAlreadyInjectedAsync(targetProcessId);
                if (!wasAlreadyInjected)
                {
                    _logger.LogInformation($"WpfInspector not yet injected into process {targetProcessId}, injecting now...");
                    var injectionResult = await InjectAndPingAsync(targetProcessId);
                    if (!injectionResult.Success)
                    {
                        return new CommandResult
                        {
                            Success = false,
                            ProcessId = targetProcessId,
                            ElementType = elementType,
                            Hashcode = hashcode,
                            Command = command,
                            Error = injectionResult.Error,
                            Message = "Failed to inject WpfInspector",
                            WasInjected = false
                        };
                    }
                    _logger.LogInformation($"Successfully injected WpfInspector into process {targetProcessId}");
                }
                else
                {
                    _logger.LogInformation($"WpfInspector already injected into process {targetProcessId}");
                }

                // Step 3: Execute the command
                _logger.LogInformation($"Attempting to execute command '{command}' on {elementType} with hashcode {hashcode} in process {targetProcessId}");
                var commandResult = await RunCommandAsync(targetProcessId, elementType, hashcode, command, parameters);
                
                // Add process name to the result for better context
                commandResult.ProcessName = processInfo.ProcessName;
                commandResult.WasInjected = !wasAlreadyInjected;

                if (commandResult.Success)
                {
                    _logger.LogInformation($"Successfully completed find-inject-command for '{processIdentifier}' -> PID {targetProcessId}");
                }
                else
                {
                    _logger.LogWarning($"Failed to execute command in process {targetProcessId}: {commandResult.Error}");
                }

                return commandResult;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error during find-inject-command for '{processIdentifier}', element type: '{elementType}', command: '{command}'");
                
                return new CommandResult
                {
                    Success = false,
                    ProcessId = 0,
                    ElementType = elementType ?? "",
                    Hashcode = hashcode,
                    Command = command ?? "",
                    Error = ex.Message,
                    Message = "Exception occurred during find-inject-command operation",
                    WasInjected = false
                };
            }
        }
    }
}
