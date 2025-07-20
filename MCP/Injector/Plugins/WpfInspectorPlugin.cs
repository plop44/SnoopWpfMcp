using System;
using System.ComponentModel;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Injector.Models;
using Injector.Services;
using Microsoft.SemanticKernel;
using Microsoft.Extensions.Logging;

namespace Injector.Plugins
{
    public class WpfInspectorPlugin
    {
        private readonly IWpfProcessService _processService;
        private readonly IInjectionService _injectionService;
        private readonly ILogger<WpfInspectorPlugin> _logger;

        public WpfInspectorPlugin(
            IWpfProcessService processService,
            IInjectionService injectionService,
            ILogger<WpfInspectorPlugin> logger)
        {
            _processService = processService;
            _injectionService = injectionService;
            _logger = logger;
        }

        [KernelFunction("get_wpf_processes")]
        [Description("Gets only interesting WPF processes, excluding system components like explorer, TextInputHost, etc. Returns user applications and development tools that are most likely to be targets for inspection. IMPORTANT: If you need to execute a command on a specific UI element in an application, use run_command_by_process directly with the process name or ID instead of calling this function first.")]
        public async Task<string> GetInterestingWpfProcessesAsync()
        {
            try
            {
                _logger.LogInformation("MCP Function called: get_interesting_wpf_processes");
                
                var allProcesses = await _processService.GetWpfProcessesAsync();
                
                // Filter out common system processes and less interesting applications
                var interestingProcesses = allProcesses.Where(p => IsInterestingProcess(p)).ToList();
                
                var result = new
                {
                    success = true,
                    count = interestingProcesses.Count,
                    totalWpfCount = allProcesses.Count,
                    processes = interestingProcesses.Select(p => new
                    {
                        processId = p.ProcessId,
                        processName = p.ProcessName,
                        mainWindowTitle = p.MainWindowTitle,
                        fileName = p.FileName,
                        workingDirectory = p.WorkingDirectory,
                        isWpfApplication = p.IsWpfApplication,
                        hasMainWindow = p.HasMainWindow,
                        startTime = p.StartTime.ToString("yyyy-MM-dd HH:mm:ss"),
                        category = CategorizeProcess(p)
                    }).ToList()
                };

                var json = JsonSerializer.Serialize(result, new JsonSerializerOptions 
                { 
                    WriteIndented = true 
                });

                _logger.LogInformation($"Found {interestingProcesses.Count} interesting WPF processes out of {allProcesses.Count} total");
                return json;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting interesting WPF processes");
                
                var errorResult = new
                {
                    success = false,
                    error = ex.Message,
                    count = 0,
                    totalWpfCount = 0,
                    processes = new object[0]
                };

                return JsonSerializer.Serialize(errorResult, new JsonSerializerOptions 
                { 
                    WriteIndented = true 
                });
            }
        }

        private bool IsInterestingProcess(WpfProcessInfo process)
        {
            // Exclude common system/shell processes
            var systemProcessNames = new[]
            {
                "explorer",           // Windows Explorer/Shell
                "textinputhost",      // Windows Input Experience  
                "systemsettings",     // Windows Settings
                "applicationframehost", // Settings frame host
                "msedgewebview2",     // Edge WebView components
                "windowsterminal"     // Windows Terminal (unless it's the main terminal)
            };

            var processNameLower = process.ProcessName.ToLowerInvariant();
            
            // Exclude known system processes
            if (systemProcessNames.Contains(processNameLower))
            {
                return false;
            }

            // Exclude processes without meaningful window titles (likely background/system)
            if (string.IsNullOrWhiteSpace(process.MainWindowTitle) || 
                process.MainWindowTitle == process.ProcessName)
            {
                return false;
            }

            // Exclude very short-lived processes or those that just started
            if ((DateTime.Now - process.StartTime).TotalSeconds < 5)
            {
                return false;
            }

            return true;
        }

        private string CategorizeProcess(WpfProcessInfo process)
        {
            var processNameLower = process.ProcessName.ToLowerInvariant();
            var windowTitleLower = process.MainWindowTitle.ToLowerInvariant();

            // Development tools
            if (processNameLower.Contains("devenv") || windowTitleLower.Contains("visual studio"))
                return "Development Tool";
            
            if (processNameLower.Contains("code") || windowTitleLower.Contains("visual studio code"))
                return "Development Tool";

            // Browsers
            if (processNameLower.Contains("chrome") || processNameLower.Contains("firefox") || 
                processNameLower.Contains("edge") || processNameLower.Contains("browser"))
                return "Browser";

            // Communication/Productivity
            if (processNameLower.Contains("teams") || processNameLower.Contains("slack") || 
                processNameLower.Contains("discord") || processNameLower.Contains("zoom"))
                return "Communication";

            // Check if it's likely a custom WPF application (has meaningful window title)
            if (!string.IsNullOrWhiteSpace(process.MainWindowTitle) && 
                process.MainWindowTitle != process.ProcessName)
                return "User Application";

            return "Other";
        }

        [KernelFunction("ping")]
        [Description("Sends a ping command to a specified WPF process (by Process ID) and returns the response. This establishes communication with the target WPF application.")]
        public async Task<string> PingAsync(
            [Description("The Process ID of the target WPF application to inject into")]
            int processId)
        {
            try
            {
                _logger.LogInformation($"MCP Function called: ping for process {processId}");
                
                var result = await _injectionService.InjectAndPingAsync(processId);
                
                var response = new
                {
                    success = result.Success,
                    processId = result.ProcessId,
                    message = result.Message,
                    response = result.Response,
                    error = result.Error,
                    wasAlreadyInjected = result.WasAlreadyInjected,
                    timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss UTC")
                };

                var json = JsonSerializer.Serialize(response, new JsonSerializerOptions 
                { 
                    WriteIndented = true 
                });

                _logger.LogInformation($"Injection and ping result for process {processId}: {(result.Success ? "Success" : "Failed")}");
                return json;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error during inject and ping for process {processId}");
                
                var errorResult = new
                {
                    success = false,
                    processId = processId,
                    message = "Exception occurred during injection and ping",
                    response = (string?)null,
                    error = ex.Message,
                    wasAlreadyInjected = false,
                    timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss UTC")
                };

                return JsonSerializer.Serialize(errorResult, new JsonSerializerOptions 
                { 
                    WriteIndented = true 
                });
            }
        }

        [KernelFunction("get_process_info")]
        [Description("Gets detailed information about a specific process by Process ID, including whether it's a WPF application.")]
        public async Task<string> GetProcessInfoAsync(
            [Description("The Process ID of the process to get information about")]
            int processId)
        {
            try
            {
                _logger.LogInformation($"MCP Function called: get_process_info for process {processId}");
                
                var processInfo = await _processService.GetProcessInfoAsync(processId);
                
                if (processInfo != null)
                {
                    var result = new
                    {
                        success = true,
                        processId = processInfo.ProcessId,
                        processName = processInfo.ProcessName,
                        mainWindowTitle = processInfo.MainWindowTitle,
                        fileName = processInfo.FileName,
                        workingDirectory = processInfo.WorkingDirectory,
                        isWpfApplication = processInfo.IsWpfApplication,
                        hasMainWindow = processInfo.HasMainWindow,
                        startTime = processInfo.StartTime.ToString("yyyy-MM-dd HH:mm:ss"),
                        isAlreadyInjected = await _injectionService.IsAlreadyInjectedAsync(processId)
                    };

                    return JsonSerializer.Serialize(result, new JsonSerializerOptions 
                    { 
                        WriteIndented = true 
                    });
                }
                else
                {
                    var notFoundResult = new
                    {
                        success = false,
                        error = $"Process with ID {processId} not found or not accessible",
                        processId = processId
                    };

                    return JsonSerializer.Serialize(notFoundResult, new JsonSerializerOptions 
                    { 
                        WriteIndented = true 
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting process info for {processId}");
                
                var errorResult = new
                {
                    success = false,
                    error = ex.Message,
                    processId = processId
                };

                return JsonSerializer.Serialize(errorResult, new JsonSerializerOptions 
                { 
                    WriteIndented = true 
                });
            }
        }

        [KernelFunction("run_command")]
        [Description("Executes a command on a specific UI element in a WPF application. Finds the element by type and hashcode, then executes the specified command. Supports various commands like CLICK, SET_TEXT, GET_PROPERTY, SET_PROPERTY, INVOKE_METHOD.")]
        public async Task<string> RunCommandAsync(
            [Description("The Process ID of the target WPF application")]
            int processId,
            [Description("The type name of the target UI element (e.g., 'Button', 'TextBox')")]
            string elementType,
            [Description("The hashcode of the specific UI element instance")]
            int hashcode,
            [Description("The command to execute (CLICK, SET_TEXT, GET_PROPERTY, SET_PROPERTY, INVOKE_METHOD)")]
            string command,
            [Description("Additional parameters for the command (optional, JSON object as string)")]
            string? parameters = null)
        {
            try
            {
                _logger.LogInformation($"MCP Function called: run_command for process {processId}, element type: '{elementType}', hashcode: {hashcode}, command: '{command}'");
                
                if (string.IsNullOrWhiteSpace(elementType))
                {
                    var invalidInputResult = new
                    {
                        success = false,
                        processId = processId,
                        elementType = elementType ?? "",
                        hashcode = hashcode,
                        command = command ?? "",
                        error = "Element type cannot be empty",
                        message = "Invalid input: element type is required"
                    };

                    return JsonSerializer.Serialize(invalidInputResult, new JsonSerializerOptions 
                    { 
                        WriteIndented = true 
                    });
                }

                if (string.IsNullOrWhiteSpace(command))
                {
                    var invalidInputResult = new
                    {
                        success = false,
                        processId = processId,
                        elementType = elementType,
                        hashcode = hashcode,
                        command = command ?? "",
                        error = "Command cannot be empty",
                        message = "Invalid input: command is required"
                    };

                    return JsonSerializer.Serialize(invalidInputResult, new JsonSerializerOptions 
                    { 
                        WriteIndented = true 
                    });
                }

                var result = await _injectionService.RunCommandAsync(processId, elementType, hashcode, command, parameters);
                
                var response = new
                {
                    success = result.Success,
                    processId = result.ProcessId,
                    elementType = result.ElementType,
                    hashcode = result.Hashcode,
                    command = result.Command,
                    message = result.Message,
                    error = result.Error,
                    result = result.Result,
                    wasInjected = result.WasInjected,
                    timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss UTC")
                };

                var json = JsonSerializer.Serialize(response, new JsonSerializerOptions 
                { 
                    WriteIndented = true 
                });

                _logger.LogInformation($"Command execution result for process {processId}: {(result.Success ? "Success" : "Failed")} - {result.Message}");
                return json;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error during command execution for process {processId}, element type: '{elementType}', command: '{command}'");
                
                var errorResult = new
                {
                    success = false,
                    processId = processId,
                    elementType = elementType ?? "",
                    hashcode = hashcode,
                    command = command ?? "",
                    message = "Exception occurred during command execution",
                    error = ex.Message,
                    result = (object?)null,
                    wasInjected = false,
                    timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss UTC")
                };

                return JsonSerializer.Serialize(errorResult, new JsonSerializerOptions 
                { 
                    WriteIndented = true 
                });
            }
        }

        [KernelFunction("get_visual_tree")]
        [Description("Gets the visual tree of a WPF application as JSON. Takes a process ID as input and returns the complete UI structure including all controls, their properties, and hierarchy.")]
        public async Task<string> GetVisualTreeAsync(
            [Description("The process ID of the WPF application")] int processId)
        {
            try
            {
                _logger.LogInformation("MCP Function called: get_visual_tree for processId: {ProcessId}", processId);
                
                // Check if process exists and is WPF
                var processes = await _processService.GetWpfProcessesAsync();
                var targetProcess = processes.FirstOrDefault(p => p.ProcessId == processId);
                
                if (targetProcess == null)
                {
                    var errorResult = new
                    {
                        success = false,
                        error = $"Process with ID {processId} not found or is not a WPF application",
                        processId = processId,
                        timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss UTC")
                    };
                    
                    return JsonSerializer.Serialize(errorResult, new JsonSerializerOptions 
                    { 
                        WriteIndented = true 
                    });
                }

                // Get visual tree using the injection service
                var result = await _injectionService.GetVisualTreeAsync(processId);
                
                if (!result.Success)
                {
                    var errorResult = new
                    {
                        success = false,
                        error = result.Error ?? "Failed to get visual tree",
                        message = result.Message,
                        processId = processId,
                        processName = targetProcess.ProcessName,
                        timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss UTC")
                    };
                    
                    return JsonSerializer.Serialize(errorResult, new JsonSerializerOptions 
                    { 
                        WriteIndented = true 
                    });
                }

                // Return the visual tree JSON (already serialized from WpfInspector)
                _logger.LogInformation("Successfully retrieved visual tree for process {ProcessId}", processId);
                return result.VisualTreeJson ?? "{}";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting visual tree for process {ProcessId}", processId);
                
                var errorResult = new
                {
                    success = false,
                    error = $"Error getting visual tree: {ex.Message}",
                    processId = processId,
                    timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss UTC")
                };

                return JsonSerializer.Serialize(errorResult, new JsonSerializerOptions 
                { 
                    WriteIndented = true 
                });
            }
        }

        [KernelFunction("take_wpf_screenshot")]
        [Description("Takes a screenshot of the MainWindow of a WPF application. Finds a WPF process (by PID or name) and captures a screenshot of its main window. Returns the screenshot as base64-encoded PNG data. WARNING: This function is SLOW compared to get_visual_tree. Use get_visual_tree instead for fast inspection of UI state - only use screenshots when you specifically need the visual appearance.")]
        public async Task<string> TakeWpfScreenshotAsync(
            [Description("The process identifier - either a Process ID (number) or process/window name (string)")]
            string processIdentifier)
        {
            try
            {
                _logger.LogInformation($"MCP Function called: take_wpf_screenshot for process '{processIdentifier}'");
                
                if (string.IsNullOrWhiteSpace(processIdentifier))
                {
                    var invalidInputResult = new
                    {
                        success = false,
                        processIdentifier = processIdentifier ?? "",
                        error = "Process identifier cannot be empty",
                        message = "Invalid input: process identifier is required"
                    };

                    return JsonSerializer.Serialize(invalidInputResult, new JsonSerializerOptions 
                    { 
                        WriteIndented = true 
                    });
                }

                // Determine if the identifier is a PID or a name
                bool isProcessId = int.TryParse(processIdentifier, out _);
                
                var result = await _injectionService.FindInjectAndTakeScreenshotAsync(processIdentifier, isProcessId);
                
                var response = new
                {
                    success = result.Success,
                    processId = result.ProcessId,
                    processName = result.ProcessName,
                    processIdentifier = processIdentifier,
                    message = result.Message,
                    error = result.Error,
                    windowTitle = result.WindowTitle,
                    width = result.Width,
                    height = result.Height,
                    imageData = result.ImageData,
                    format = result.Format,
                    wasInjected = result.WasInjected,
                    identifierType = isProcessId ? "PID" : "Name",
                    timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss UTC")
                };

                var json = JsonSerializer.Serialize(response, new JsonSerializerOptions 
                { 
                    WriteIndented = true 
                });

                _logger.LogInformation($"Screenshot capture result for '{processIdentifier}': {(result.Success ? "Success" : "Failed")} - {result.Message}");
                return json;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error during screenshot capture for '{processIdentifier}'");
                
                var errorResult = new
                {
                    success = false,
                    processIdentifier = processIdentifier ?? "",
                    message = "Exception occurred during screenshot capture",
                    error = ex.Message,
                    processId = 0,
                    processName = (string?)null,
                    windowTitle = (string?)null,
                    width = 0,
                    height = 0,
                    imageData = (string?)null,
                    format = "PNG",
                    wasInjected = false,
                    identifierType = "Unknown",
                    timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss UTC")
                };

                return JsonSerializer.Serialize(errorResult, new JsonSerializerOptions 
                { 
                    WriteIndented = true 
                });
            }
        }
    }
}
