using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.SemanticKernel;
using Microsoft.Extensions.Logging;
using SnoopWpfMcpServer.Models;
using SnoopWpfMcpServer.Services;

namespace SnoopWpfMcpServer
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

        private static JsonSerializerOptions GetJsonOptions()
        {
            return new JsonSerializerOptions 
            { 
                WriteIndented = false,
                NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
            };
        }

        [KernelFunction("get_wpf_processes")]
        [Description("Gets only interesting WPF processes, excluding system components like explorer, TextInputHost, etc. Returns user applications and development tools that are most likely to be targets for inspection. IMPORTANT: If you need to execute an automation action on a specific UI element in an application, use invoke_automation_peer directly with the process name or ID instead of calling this function first.")]
        public async Task<string> GetWpfProcessesAsync()
        {
            var stopwatch = Stopwatch.StartNew();
            try
            {
                _logger.LogInformation("MCP Function called: get_interesting_wpf_processes");
                
                var allProcesses = await _processService.GetWpfProcessesAsync();
                
                // Filter out common system processes and less interesting applications
                var interestingProcesses = allProcesses.Where(p => IsInterestingProcess(p)).ToList();
                
                stopwatch.Stop();
                var result = new
                {
                    tool = "get_wpf_processes",
                    tool_duration = stopwatch.Elapsed.ToString(@"mm\:ss\.fff"),
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

                var json = JsonSerializer.Serialize(result, GetJsonOptions());

                _logger.LogInformation($"Found {interestingProcesses.Count} interesting WPF processes out of {allProcesses.Count} total");
                return json;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _logger.LogError(ex, "Error getting interesting WPF processes");
                
                var errorResult = new
                {
                    tool = "get_wpf_processes",
                    tool_duration = stopwatch.Elapsed.ToString(@"mm\:ss\.fff"),
                    success = false,
                    error = ex.Message,
                    count = 0,
                    totalWpfCount = 0,
                    processes = new object[0]
                };

                return JsonSerializer.Serialize(errorResult, GetJsonOptions());
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
            var stopwatch = Stopwatch.StartNew();
            try
            {
                _logger.LogInformation($"MCP Function called: ping for process {processId}");
                
                var result = await _injectionService.PingAsync(processId);
                
                stopwatch.Stop();
                var response = new
                {
                    tool = "ping",
                    tool_parameter = $"processId={processId}",
                    tool_duration = stopwatch.Elapsed.ToString(@"mm\:ss\.fff"),
                    success = result.Success,
                    processId = result.ProcessId,
                    message = result.Message,
                    response = result.Response,
                    error = result.Error,
                    wasAlreadyInjected = result.WasAlreadyInjected,
                    timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss UTC")
                };

                var json = JsonSerializer.Serialize(response, GetJsonOptions());

                _logger.LogInformation($"Injection and ping result for process {processId}: {(result.Success ? "Success" : "Failed")}");
                return json;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _logger.LogError(ex, $"Error during inject and ping for process {processId}");
                
                var errorResult = new
                {
                    tool = "ping",
                    tool_parameter = $"processId={processId}",
                    tool_duration = stopwatch.Elapsed.ToString(@"mm\:ss\.fff"),
                    success = false,
                    processId,
                    message = "Exception occurred during injection and ping",
                    response = (string?)null,
                    error = ex.Message,
                    wasAlreadyInjected = false,
                    timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss UTC")
                };

                return JsonSerializer.Serialize(errorResult, GetJsonOptions());
            }
        }


        [KernelFunction("invoke_automation_peer")]
        [Description("Invokes automation peer actions on WPF UI elements using the Windows UI Automation framework. This is the primary way to interact with WPF controls programmatically for testing, automation, and accessibility. Use get_visual_tree to discover UI elements first, then use this function to perform actions on them.\n\nSupported Automation Patterns and Actions:\n\n**Invoke Pattern** (Buttons, MenuItems):\n- 'Invoke_Invoke': Clicks/activates the element\n\n**Value Pattern** (TextBoxes, PasswordBoxes):\n- 'Value_Get': Get current text value\n- 'Value_Set': Set text value (requires 'value' parameter)\n\n**SelectionItem Pattern** (ListBoxItems, ComboBoxItems, TabItems):\n- 'SelectionItem_Select': Select this item\n- 'SelectionItem_AddToSelection': Add to multi-selection\n- 'SelectionItem_RemoveFromSelection': Remove from selection\n- 'SelectionItem_Status': Get selection state\n\n**Toggle Pattern** (CheckBoxes, ToggleButtons):\n- 'Toggle_Toggle': Toggle the state\n- 'Toggle_Status': Get current toggle state\n\n**ExpandCollapse Pattern** (TreeViewItems, Expanders):\n- 'ExpandCollapse_Expand': Expand the element\n- 'ExpandCollapse_Collapse': Collapse the element\n- 'ExpandCollapse_Toggle': Toggle expand/collapse\n- 'ExpandCollapse_Status': Get expand/collapse state\n\n**RangeValue Pattern** (Sliders, ProgressBars, NumericUpDown):\n- 'RangeValue_Get': Get value, min, max, step info\n- 'RangeValue_Set': Set numeric value (requires 'value' parameter)\n\n**Selection Pattern** (ListBoxes, ComboBoxes):\n- 'Selection': Get selection info\n**Scroll Pattern** (ScrollViewers, ListBoxes):\n- 'Scroll_Status': Get scroll position and capabilities\n- 'Scroll_Scroll': Scroll by amount (requires 'horizontal'/'vertical' parameters)\n- 'Scroll_SetPosition': Set scroll position (requires 'horizontalPercent'/'verticalPercent')\n\nEach UI element may support multiple patterns. Use get_visual_tree to see which 'supportedActions' are available for each element. Parameters like 'value', 'horizontal', 'vertical', etc. should be passed as a JSON object in the 'parameters' field.\n\nExample: To click a button, use action 'Invoke_Invoke'. To set text in a textbox, use action 'Value_Set' with parameters '{\"value\": \"new text\"}'. To select a list item, use action 'SelectionItem_Select'.")]
        public async Task<string> InvokeAutomationPeerAsync(
            [Description("The Process ID of the target WPF application")]
            int processId,
            [Description("The full type name of the target UI element (e.g., 'System.Windows.Controls.Button', 'System.Windows.Controls.TextBox'). Use the exact 'type' value from the visual tree.")]
            string type,
            [Description("The hashcode of the specific UI element instance. Use the exact 'hashCode' value from the visual tree.")]
            int hashcode,
            [Description("The automation action to execute. Must be one of the supported actions for the element's automation patterns (e.g., 'Invoke_Invoke', 'Value_Set', 'SelectionItem_Select'). Use get_visual_tree to see 'supportedActions' for each element.")]
            string action,
            [Description("Additional parameters for the action as a JSON object string. Required for actions like 'Value_Set' (needs 'value'), 'RangeValue_Set' (needs 'value'), 'Scroll_Scroll' (needs 'horizontal', 'vertical'), etc. Optional for simple actions like 'Invoke_Invoke'.")]
            string? parameters = null)
        {
            var stopwatch = Stopwatch.StartNew();
            try
            {
                _logger.LogInformation($"MCP Function called: invoke_automation_peer for process {processId}, element type: '{type}', hashcode: {hashcode}, action: '{action}'");
                
                if (string.IsNullOrWhiteSpace(type))
                {
                    stopwatch.Stop();
                    var invalidInputResult = new
                    {
                        tool = "invoke_automation_peer",
                        tool_parameter = $"processId={processId}, type={type ?? ""}, hashcode={hashcode}, action={action ?? ""}",
                        tool_duration = stopwatch.Elapsed.ToString(@"mm\:ss\.fff"),
                        success = false,
                        processId,
                        type = type ?? "",
                        hashcode,
                        action = action ?? "",
                        error = "Type cannot be empty",
                        message = "Invalid input: element type is required"
                    };

                    return JsonSerializer.Serialize(invalidInputResult, GetJsonOptions());
                }

                if (string.IsNullOrWhiteSpace(action))
                {
                    stopwatch.Stop();
                    var invalidInputResult = new
                    {
                        tool = "invoke_automation_peer",
                        tool_parameter = $"processId={processId}, type={type}, hashcode={hashcode}, action={action ?? ""}",
                        tool_duration = stopwatch.Elapsed.ToString(@"mm\:ss\.fff"),
                        success = false,
                        processId,
                        type,
                        hashcode,
                        action = action ?? "",
                        error = "Action cannot be empty",
                        message = "Invalid input: action is required"
                    };

                    return JsonSerializer.Serialize(invalidInputResult, GetJsonOptions());
                }

                var result = await _injectionService.InvokeAutomationPeerAsync(processId, type, hashcode, action, parameters);
                
                stopwatch.Stop();
                var response = new
                {
                    tool = "invoke_automation_peer",
                    tool_parameter = $"processId={processId}, type={type}, hashcode={hashcode}, action={action}",
                    tool_duration = stopwatch.Elapsed.ToString(@"mm\:ss\.fff"),
                    success = result.Success,
                    processId = result.ProcessId,
                    type = result.Type,
                    hashcode = result.Hashcode,
                    action = result.Action,
                    message = result.Message,
                    error = result.Error,
                    result = result.Result,
                    timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss UTC")
                };

                var json = JsonSerializer.Serialize(response, GetJsonOptions());

                _logger.LogInformation($"Automation peer action result for process {processId}: {(result.Success ? "Success" : "Failed")} - {result.Message}");
                return json;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _logger.LogError(ex, $"Error during automation peer action for process {processId}, element type: '{type}', action: '{action}'");
                
                var errorResult = new
                {
                    tool = "invoke_automation_peer",
                    tool_parameter = $"processId={processId}, type={type ?? ""}, hashcode={hashcode}, action={action ?? ""}",
                    tool_duration = stopwatch.Elapsed.ToString(@"mm\:ss\.fff"),
                    success = false,
                    processId,
                    type = type ?? "",
                    hashcode,
                    action = action ?? "",
                    message = "Exception occurred during automation peer action",
                    error = ex.Message,
                    result = (object?)null,
                    timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss UTC")
                };

                return JsonSerializer.Serialize(errorResult, GetJsonOptions());
            }
        }

        [KernelFunction("get_visual_tree")]
        [Description("Gets the complete visual tree of a WPF application as JSON. **USAGE GUIDANCE: Call this function ONLY at startup to discover the full UI structure and get element hashcodes. DO NOT call this function repeatedly to check for changes - it's expensive and returns the entire tree. After initial discovery, use get_element_by_hashcode to check specific element updates.** Returns complete UI structure with binding-aware dependency properties and comprehensive DataContext analysis. Properties show binding paths, sources, modes, and errors. DataContext objects include all properties with values, readonly status, and type information - essential for WPF debugging. Structure: { visualTree: {...}, dataContexts: {\"dc_123\": {...}} }. Simple DataContexts (strings, numbers) are inlined. Complex DataContexts are referenced by ID with full property analysis.")]
        public async Task<string> GetVisualTreeAsync(
            [Description("The process ID of the WPF application")] int processId)
        {
            var stopwatch = Stopwatch.StartNew();
            try
            {
                _logger.LogInformation("MCP Function called: get_visual_tree for processId: {ProcessId}", processId);
                
                // Check if process exists and is WPF
                var processes = await _processService.GetWpfProcessesAsync();
                var targetProcess = processes.FirstOrDefault(p => p.ProcessId == processId);
                
                if (targetProcess == null)
                {
                    stopwatch.Stop();
                    var errorResult = new
                    {
                        tool = "get_visual_tree",
                        tool_parameter = $"processId={processId}",
                        tool_duration = stopwatch.Elapsed.ToString(@"mm\:ss\.fff"),
                        success = false,
                        error = $"Process with ID {processId} not found or is not a WPF application",
                        processId,
                        timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss UTC")
                    };
                    
                    return JsonSerializer.Serialize(errorResult, GetJsonOptions());
                }

                // Get visual tree using the injection service
                var result = await _injectionService.GetVisualTreeAsync(processId);
                
                if (!result.Success)
                {
                    stopwatch.Stop();
                    var errorResult = new
                    {
                        tool = "get_visual_tree",
                        tool_parameter = $"processId={processId}",
                        tool_duration = stopwatch.Elapsed.ToString(@"mm\:ss\.fff"),
                        success = false,
                        error = result.Error ?? "Failed to get visual tree",
                        message = result.Message,
                        processId,
                        processName = targetProcess.ProcessName,
                        timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss UTC")
                    };
                    
                    return JsonSerializer.Serialize(errorResult, GetJsonOptions());
                }

                // Return the visual tree JSON (already serialized from WpfInspector)
                // For get_visual_tree, we need to parse and add metadata to the existing JSON
                stopwatch.Stop();
                
                if (string.IsNullOrWhiteSpace(result.VisualTreeJson) || result.VisualTreeJson == "{}")
                {
                    var emptyResult = new
                    {
                        tool = "get_visual_tree",
                        tool_parameter = $"processId={processId}",
                        tool_duration = stopwatch.Elapsed.ToString(@"mm\:ss\.fff"),
                        success = true,
                        processId,
                        message = "Visual tree is empty",
                        timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss UTC")
                    };
                    
                    return JsonSerializer.Serialize(emptyResult, GetJsonOptions());
                }
                
                // Parse the existing JSON and add metadata
                try
                {
                    var visualTreeDoc = JsonDocument.Parse(result.VisualTreeJson);
                    var visualTreeElement = visualTreeDoc.RootElement;
                    
                    var enhancedResult = new
                    {
                        tool = "get_visual_tree",
                        tool_parameter = $"processId={processId}",
                        tool_duration = stopwatch.Elapsed.ToString(@"mm\:ss\.fff"),
                        success = true,
                        processId,
                        timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss UTC"),
                        visualTree = visualTreeElement
                    };
                    
                    _logger.LogInformation("Successfully retrieved visual tree for process {ProcessId}", processId);
                    return JsonSerializer.Serialize(enhancedResult, GetJsonOptions());
                }
                catch (JsonException)
                {
                    // If parsing fails, return the raw JSON with basic metadata
                    _logger.LogInformation("Successfully retrieved visual tree for process {ProcessId} (raw format)", processId);
                    return result.VisualTreeJson;
                }
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _logger.LogError(ex, "Error getting visual tree for process {ProcessId}", processId);
                
                var errorResult = new
                {
                    tool = "get_visual_tree",
                    tool_parameter = $"processId={processId}",
                    tool_duration = stopwatch.Elapsed.ToString(@"mm\:ss\.fff"),
                    success = false,
                    error = $"Error getting visual tree: {ex.Message}",
                    processId,
                    timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss UTC")
                };

                return JsonSerializer.Serialize(errorResult, GetJsonOptions());
            }
        }

        [KernelFunction("get_element_by_hashcode")]
        [Description("Gets a single UI element by its type and hashcode. **PREFERRED FOR UPDATES: Use this function to check element state changes after performing automation actions. Much faster than get_visual_tree for targeted element inspection. Only call get_visual_tree if you need to discover new elements or their hashcodes are missing.** This function returns the current state of a specific element with all its properties and DataContext, perfect for verifying that UI changes took effect.")]
        public async Task<string> GetElementByHashcodeAsync(
            [Description("The process ID of the WPF application")] 
            int processId,
            [Description("The full type name of the target UI element (e.g., 'System.Windows.Controls.Button'). Use the exact 'type' value from a previous visual tree.")]
            string type,
            [Description("The hashcode of the specific UI element instance. Use the exact 'hashCode' value from a previous visual tree.")]
            int hashcode)
        {
            var stopwatch = Stopwatch.StartNew();
            try
            {
                _logger.LogInformation("MCP Function called: get_element_by_hashcode for processId: {ProcessId}, type: {Type}, hashcode: {Hashcode}", processId, type, hashcode);
                
                if (string.IsNullOrWhiteSpace(type))
                {
                    stopwatch.Stop();
                    var invalidInputResult = new
                    {
                        tool = "get_element_by_hashcode",
                        tool_parameter = $"processId={processId}, type={type ?? ""}, hashcode={hashcode}",
                        tool_duration = stopwatch.Elapsed.ToString(@"mm\:ss\.fff"),
                        success = false,
                        error = "Type cannot be empty",
                        processId,
                        type = type ?? "",
                        hashcode,
                        timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss UTC")
                    };

                    return JsonSerializer.Serialize(invalidInputResult, GetJsonOptions());
                }

                // Check if process exists and is WPF
                var processes = await _processService.GetWpfProcessesAsync();
                var targetProcess = processes.FirstOrDefault(p => p.ProcessId == processId);
                
                if (targetProcess == null)
                {
                    stopwatch.Stop();
                    var errorResult = new
                    {
                        tool = "get_element_by_hashcode",
                        tool_parameter = $"processId={processId}, type={type}, hashcode={hashcode}",
                        tool_duration = stopwatch.Elapsed.ToString(@"mm\:ss\.fff"),
                        success = false,
                        error = $"Process with ID {processId} not found or is not a WPF application",
                        processId,
                        type,
                        hashcode,
                        timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss UTC")
                    };
                    
                    return JsonSerializer.Serialize(errorResult, GetJsonOptions());
                }

                // Get element using the injection service
                var result = await _injectionService.GetElementByHashcodeAsync(processId, type, hashcode);
                
                stopwatch.Stop();
                
                // Return the element result with metadata
                if (result.Success)
                {
                    var enhancedResult = new
                    {
                        tool = "get_element_by_hashcode",
                        tool_parameter = $"processId={processId}, type={type}, hashcode={hashcode}",
                        tool_duration = stopwatch.Elapsed.ToString(@"mm\:ss\.fff"),
                        success = true,
                        processId,
                        type,
                        hashcode,
                        message = result.Message,
                        timestamp = result.Timestamp ?? DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss UTC"),
                        element = result.Element,
                        dataContexts = result.DataContexts
                    };
                    
                    _logger.LogInformation($"Get element by hashcode result for process {processId}: Success - {result.Message}");
                    return JsonSerializer.Serialize(enhancedResult, GetJsonOptions());
                }
                
                // Error response or parsing failed
                var response = new
                {
                    tool = "get_element_by_hashcode",
                    tool_parameter = $"processId={processId}, type={type}, hashcode={hashcode}",
                    tool_duration = stopwatch.Elapsed.ToString(@"mm\:ss\.fff"),
                    success = result.Success,
                    processId,
                    type,
                    hashcode,
                    message = result.Message,
                    error = result.Error,
                    element = (object?)null,
                    dataContexts = (object?)null,
                    timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss UTC")
                };

                var json = JsonSerializer.Serialize(response, GetJsonOptions());

                _logger.LogInformation($"Get element by hashcode result for process {processId}: {(result.Success ? "Success" : "Failed")} - {result.Message}");
                return json;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _logger.LogError(ex, "Error getting element by hashcode for process {ProcessId}, type: {Type}, hashcode: {Hashcode}", processId, type, hashcode);
                
                var errorResult = new
                {
                    tool = "get_element_by_hashcode",
                    tool_parameter = $"processId={processId}, type={type ?? ""}, hashcode={hashcode}",
                    tool_duration = stopwatch.Elapsed.ToString(@"mm\:ss\.fff"),
                    success = false,
                    processId,
                    type = type ?? "",
                    hashcode,
                    message = "Exception occurred during element retrieval",
                    error = ex.Message,
                    element = (object?)null,
                    timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss UTC")
                };

                return JsonSerializer.Serialize(errorResult, GetJsonOptions());
            }
        }

        [KernelFunction("take_wpf_screenshot")]
        [Description("Takes a screenshot of the MainWindow of a WPF application. Captures a screenshot of the specified WPF process's main window. Returns the screenshot as base64-encoded PNG data. WARNING: This function is SLOW compared to get_visual_tree. Use get_visual_tree instead for fast inspection of UI state - only use screenshots when you specifically need the visual appearance.")]
        public async Task<string> TakeWpfScreenshotAsync(
            [Description("The Process ID of the target WPF application")]
            int processId)
        {
            var stopwatch = Stopwatch.StartNew();
            try
            {
                _logger.LogInformation($"MCP Function called: take_wpf_screenshot for process {processId}");
                
                var result = await _injectionService.TakeScreenshotAsync(processId);
                
                stopwatch.Stop();
                var response = new
                {
                    tool = "take_wpf_screenshot",
                    tool_parameter = $"processId={processId}",
                    tool_duration = stopwatch.Elapsed.ToString(@"mm\:ss\.fff"),
                    success = result.Success,
                    processId = result.ProcessId,
                    processName = result.ProcessName,
                    message = result.Message,
                    error = result.Error,
                    windowTitle = result.WindowTitle,
                    width = result.Width,
                    height = result.Height,
                    imageData = result.ImageData,
                    format = result.Format,
                    timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss UTC")
                };

                var json = JsonSerializer.Serialize(response, GetJsonOptions());

                _logger.LogInformation($"Screenshot capture result for process {processId}: {(result.Success ? "Success" : "Failed")} - {result.Message}");
                return json;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _logger.LogError(ex, $"Error during screenshot capture for process {processId}");
                
                var errorResult = new
                {
                    tool = "take_wpf_screenshot",
                    tool_parameter = $"processId={processId}",
                    tool_duration = stopwatch.Elapsed.ToString(@"mm\:ss\.fff"),
                    success = false,
                    processId,
                    message = "Exception occurred during screenshot capture",
                    error = ex.Message,
                    processName = (string?)null,
                    windowTitle = (string?)null,
                    width = 0,
                    height = 0,
                    imageData = (string?)null,
                    format = "PNG",
                    timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss UTC")
                };

                return JsonSerializer.Serialize(errorResult, GetJsonOptions());
            }
        }
    }
}
