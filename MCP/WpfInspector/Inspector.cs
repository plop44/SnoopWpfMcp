#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WpfInspector
{
    public static class Inspector
    {
        private static NamedPipeServerStream? _pipeServer;
        private static CancellationTokenSource? _cancellationTokenSource;
        private static Task? _serverTask;

        private static JsonSerializerOptions GetJsonOptions()
        {
            return new JsonSerializerOptions 
            { 
                WriteIndented = false,
                NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
            };
        }

        /// <summary>
        /// Entry point called by the injector - this method will be called in the target process
        /// </summary>
        /// <param name="settingsFile">Settings file path passed by the injector</param>
        /// <returns>0 for success, non-zero for failure</returns>
        public static int Initialize(string settingsFile)
        {
            try
            {
                LogMessage("WpfInspector.Initialize called");
                
                // Generate a unique pipe name based on the current process ID
                var pipeName = $"WpfInspector_{System.Diagnostics.Process.GetCurrentProcess().Id}";
                LogMessage($"Starting named pipe server: {pipeName}");

                _cancellationTokenSource = new CancellationTokenSource();
                _serverTask = StartPipeServerAsync(pipeName, _cancellationTokenSource.Token);

                LogMessage("WpfInspector successfully initialized");
                return 0; // Success
            }
            catch (Exception ex)
            {
                LogMessage($"Error in WpfInspector.Initialize: {ex}");
                return 1; // Failure
            }
        }

        private static async Task StartPipeServerAsync(string pipeName, CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    _pipeServer = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Message);
                    
                    LogMessage($"Waiting for client connection on pipe: {pipeName}");
                    
                    // Wait for a client to connect
                    await _pipeServer.WaitForConnectionAsync(cancellationToken);
                    
                    LogMessage("Client connected to pipe");
                    
                    // Handle the client connection
                    await HandleClientAsync(_pipeServer, cancellationToken);
                    
                    _pipeServer.Dispose();
                }
            }
            catch (OperationCanceledException)
            {
                LogMessage("Pipe server cancelled");
            }
            catch (Exception ex)
            {
                LogMessage($"Error in pipe server: {ex}");
            }
        }

        private static async Task HandleClientAsync(NamedPipeServerStream pipeServer, CancellationToken cancellationToken)
        {
            try
            {
                var buffer = new byte[1024];
                
                while (pipeServer.IsConnected && !cancellationToken.IsCancellationRequested)
                {
                    var bytesRead = await pipeServer.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
                    
                    if (bytesRead == 0)
                        break;
                    
                    var message = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                    LogMessage($"Received message: {message}");
                    
                    var response = ProcessMessage(message);
                    LogMessage($"Sending response: {response}");
                    
                    var responseBytes = Encoding.UTF8.GetBytes(response);
                    await pipeServer.WriteAsync(responseBytes, 0, responseBytes.Length, cancellationToken);
                    await pipeServer.FlushAsync(cancellationToken);
                }
            }
            catch (Exception ex)
            {
                LogMessage($"Error handling client: {ex}");
            }
            finally
            {
                LogMessage("Client disconnected");
            }
        }

        private static string ProcessMessage(string message)
        {
            try
            {
                var trimmedMessage = message.Trim();
                
                // Check if it's a JSON command
                if (trimmedMessage.StartsWith("{") && trimmedMessage.EndsWith("}"))
                {
                    return ProcessJsonCommand(trimmedMessage);
                }
                
                switch (trimmedMessage.ToUpperInvariant())
                {
                    case "PING":
                        return "PONG";
                    
                    case "STATUS":
                        return $"WpfInspector running in process {System.Diagnostics.Process.GetCurrentProcess().Id}";
                    
                    case "EXIT":
                        _cancellationTokenSource?.Cancel();
                        return "GOODBYE";
                    
                    default:
                        return $"Unknown command: {trimmedMessage}";
                }
            }
            catch (Exception ex)
            {
                return $"Error processing message: {ex.Message}";
            }
        }

        private static string ProcessJsonCommand(string jsonMessage)
        {
            try
            {
                using var doc = JsonDocument.Parse(jsonMessage);
                var root = doc.RootElement;
                
                if (!root.TryGetProperty("commandType", out var commandTypeElement))
                {
                    // Try fallback to 'command' for backward compatibility
                    if (!root.TryGetProperty("command", out commandTypeElement))
                    {
                        return JsonSerializer.Serialize(new { success = false, error = "Missing 'commandType' or 'command' property" }, GetJsonOptions());
                    }
                }
                
                var command = commandTypeElement.GetString();
                
                switch (command?.ToUpperInvariant())
                {
                    case "INVOKE_AUTOMATION_PEER":
                        return ProcessAutomationPeerCommand(root);
                    
                    case "TAKE_SCREENSHOT":
                        return ProcessTakeScreenshotCommand(root);
                    
                    case "GET_VISUAL_TREE":
                        return ProcessGetVisualTreeCommand();
                    
                    case "GET_ELEMENT_BY_HASHCODE":
                        return ProcessGetElementByHashcodeCommand(root);
                    
                    default:
                        return JsonSerializer.Serialize(new { success = false, error = $"Unknown command: {command}" }, GetJsonOptions());
                }
            }
            catch (Exception ex)
            {
                return JsonSerializer.Serialize(new { success = false, error = $"Error processing JSON command: {ex.Message}" }, GetJsonOptions());
            }
        }

        private static string ProcessAutomationPeerCommand(JsonElement commandElement)
        {
            try
            {
                if (!commandElement.TryGetProperty("type", out var typeElement))
                {
                    return JsonSerializer.Serialize(new { success = false, error = "Missing 'type' parameter" }, GetJsonOptions());
                }
                
                if (!commandElement.TryGetProperty("hashcode", out var hashcodeElement))
                {
                    return JsonSerializer.Serialize(new { success = false, error = "Missing 'hashcode' parameter" }, GetJsonOptions());
                }
                
                var typeName = typeElement.GetString();
                var hashcode = hashcodeElement.GetInt32();

                if (string.IsNullOrWhiteSpace(typeName))
                {
                    return JsonSerializer.Serialize(new { success = false, error = "type cannot be empty" }, GetJsonOptions());
                }

                LogMessage($"Attempting to execute automation peer action on {typeName} with hashcode: {hashcode}");

                // Perform the command on the UI thread
                var result = Application.Current?.Dispatcher.Invoke<object>(() =>
                {
                    try
                    {
                        
                        var targetObject = FindObjectByTypeAndHashCode(typeName, hashcode);
                        
                        if (targetObject == null)
                        {
                            LogMessage($"Object not found Type: {typeName}, HashCode: {hashcode}");
                            return new { 
                                success = false, 
                                error = $"{typeName} with hashcode {hashcode} not found in any window" 
                            };
                        }

                        LogMessage($"Found object: {targetObject.GetType().Name} with hashcode {targetObject.GetHashCode()}");

                        // Execute the automation peer command
                        var commandResult = ExecuteAutomationPeerCommandOnObject(targetObject, commandElement);
                        
                        return commandResult;
                    }
                    catch (Exception ex)
                    {
                        LogMessage($"Error executing command: {ex.Message}");
                        return new { success = false, error = $"Error executing command: {ex.Message}" };
                    }
                });

                return JsonSerializer.Serialize(result, GetJsonOptions());
            }
            catch (Exception ex)
            {
                LogMessage($"Error in ProcessRunCommand: {ex.Message}");
                return JsonSerializer.Serialize(new { success = false, error = $"Error processing run command: {ex.Message}" }, GetJsonOptions());
            }
        }

        private static string ProcessTakeScreenshotCommand(JsonElement commandElement)
        {
            try
            {
                LogMessage("Attempting to take screenshot of MainWindow");

                // Perform the screenshot capture on the UI thread
                var result = Application.Current?.Dispatcher.Invoke<object>(() =>
                {
                    try
                    {
                        // Get the main window
                        var mainWindow = Application.Current.MainWindow;
                        if (mainWindow == null)
                        {
                            return new { success = false, error = "No main window found" };
                        }

                        LogMessage($"Taking screenshot of window: {mainWindow.Title}");

                        // Create a render target bitmap
                        var renderTargetBitmap = new RenderTargetBitmap(
                            (int)mainWindow.ActualWidth,
                            (int)mainWindow.ActualHeight,
                            96, 96, // DPI values
                            PixelFormats.Pbgra32);

                        // Render the main window to the bitmap
                        renderTargetBitmap.Render(mainWindow);

                        // Convert to PNG and encode as base64
                        var encoder = new PngBitmapEncoder();
                        encoder.Frames.Add(BitmapFrame.Create(renderTargetBitmap));

                        using var memoryStream = new MemoryStream();
                        encoder.Save(memoryStream);
                        var base64String = Convert.ToBase64String(memoryStream.ToArray());

                        LogMessage($"Successfully captured screenshot - size: {renderTargetBitmap.PixelWidth}x{renderTargetBitmap.PixelHeight}");

                        return new
                        {
                            success = true,
                            message = "Screenshot captured successfully",
                            windowTitle = mainWindow.Title,
                            width = renderTargetBitmap.PixelWidth,
                            height = renderTargetBitmap.PixelHeight,
                            imageData = base64String,
                            format = "PNG"
                        };
                    }
                    catch (Exception ex)
                    {
                        LogMessage($"Error taking screenshot: {ex.Message}");
                        return new { success = false, error = $"Error taking screenshot: {ex.Message}" };
                    }
                });

                return JsonSerializer.Serialize(result, GetJsonOptions());
            }
            catch (Exception ex)
            {
                LogMessage($"Error in ProcessTakeScreenshotCommand: {ex.Message}");
                return JsonSerializer.Serialize(new { success = false, error = $"Error processing take screenshot command: {ex.Message}" }, GetJsonOptions());
            }
        }
        private static object? FindObjectByTypeAndHashCode(string typeName, int hashcode)
        {
            try
            {
                return GetAllWpfControls()
                    .SelectMany(GetChildrenRecursive)
                    .Where(element =>
                    {
                        var type = element.GetType();
                        var elementTypeName = type.FullName ?? type.Name;
                        var elementHashCode = element.GetHashCode();
                        
                        return elementHashCode == hashcode && elementTypeName.Equals(typeName, StringComparison.OrdinalIgnoreCase);
                    })
                    .FirstOrDefault();
            }
            catch (Exception ex)
            {
                LogMessage($"Error in FindObjectByTypeAndHashCode: {ex.Message}");
                return null;
            }
        }


        private static object ExecuteAutomationPeerCommandOnObject(object targetObject, JsonElement commandData)
        {
            try
            {
                return AutomationPeerHandler.ExecuteInvokeAutomationPeerCommand(targetObject, commandData);
            }
            catch (Exception ex)
            {
                LogMessage($"Error executing automation peer command: {ex.Message}");
                return new { success = false, error = $"Error executing automation peer command: {ex.Message}" };
            }
        }



        private static void LogMessage(string message)
        {
            try
            {
                var logMessage = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [WpfInspector] {message}";
                
                // Write to a log file in temp directory
                var logPath = Path.Combine(Path.GetTempPath(), "WpfInspector.log");
                File.AppendAllText(logPath, logMessage + Environment.NewLine);
                
                // Also write to debug output if debugger is attached
                if (System.Diagnostics.Debugger.IsAttached)
                {
                    System.Diagnostics.Debug.WriteLine(logMessage);
                }
            }
            catch
            {
                // Ignore logging errors
            }
        }

        private static IEnumerable<DependencyObject> GetAllWpfControls()
        {
            var wpfControls = new List<DependencyObject>();
            
            try
            {
                // Get all WPF application windows first
                if (Application.Current?.Windows != null)
                {
                    foreach (Window window in Application.Current.Windows)
                    {
                        if (window != null)
                        {
                            wpfControls.Add(window);
                        }
                    }
                }

                // If we found WPF windows, return them and skip WinForms search for performance
                if (wpfControls.Count > 0)
                {
                    LogMessage($"Found {wpfControls.Count} WPF windows, skipping WinForms search");
                    return wpfControls;
                }

                // No WPF windows found, search for WPF controls hosted in WinForms
                LogMessage("No WPF windows found, searching for WPF controls hosted in WinForms");
                var hostedControls = GetAllWpfControlsHostedInWinforms();
                wpfControls.AddRange(hostedControls);
            }
            catch (Exception ex)
            {
                LogMessage($"Error in GetAllWpfControls: {ex.Message}");
            }
            
            return wpfControls;
        }

        /// <summary>
        /// Finds WPF controls hosted inside WinForms applications.
        /// This method covers the most common scenarios but may not catch all edge cases.
        /// It focuses on ElementHost controls and HwndSource-based hosting.
        /// </summary>
        private static IEnumerable<DependencyObject> GetAllWpfControlsHostedInWinforms()
        {
            var wpfControls = new List<DependencyObject>();
            
            try
            {
                var currentProcess = System.Diagnostics.Process.GetCurrentProcess();
                
                // Approach 1: Use HwndSource to find WPF content in any window handle
                // This covers the most common case of WPF content hosted via HwndSource
                var processWindows = new List<IntPtr>();
                EnumWindows((hWnd, lParam) =>
                {
                    uint processId;
                    GetWindowThreadProcessId(hWnd, out processId);
                    if (processId == currentProcess.Id)
                    {
                        processWindows.Add(hWnd);
                    }
                    return true;
                }, IntPtr.Zero);

                // Check each window and its immediate child windows for WPF content
                foreach (var hWnd in processWindows)
                {
                    try
                    {
                        // Check the window itself for WPF content
                        var source = System.Windows.Interop.HwndSource.FromHwnd(hWnd);
                        if (source?.RootVisual is DependencyObject rootVisual)
                        {
                            if (!wpfControls.Any(w => ReferenceEquals(w, rootVisual)))
                            {
                                wpfControls.Add(rootVisual);
                                LogMessage($"Found WPF content in window handle {hWnd}");
                            }
                        }

                        // Check immediate child windows (covers ElementHost scenarios)
                        EnumChildWindows(hWnd, (childHWnd, lParam) =>
                        {
                            try
                            {
                                var childSource = System.Windows.Interop.HwndSource.FromHwnd(childHWnd);
                                if (childSource?.RootVisual is DependencyObject childRootVisual)
                                {
                                    if (!wpfControls.Any(w => ReferenceEquals(w, childRootVisual)))
                                    {
                                        wpfControls.Add(childRootVisual);
                                        LogMessage($"Found WPF content in child window handle {childHWnd}");
                                    }
                                }
                            }
                            catch
                            {
                                // Silently ignore errors when checking child windows
                            }
                            return true;
                        }, IntPtr.Zero);
                    }
                    catch
                    {
                        // Silently ignore errors when checking windows
                    }
                }

                LogMessage($"Found {wpfControls.Count} WPF controls hosted in WinForms");
            }
            catch (Exception ex)
            {
                LogMessage($"Error in GetAllWpfControlsHostedInWinforms: {ex.Message}");
            }
            
            return wpfControls;
        }

        // P/Invoke declarations for window enumeration
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
        
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool EnumChildWindows(IntPtr hWndParent, EnumWindowsProc lpEnumFunc, IntPtr lParam);
        
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
        
        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        private static string ProcessGetVisualTreeCommand()
        {
            try
            {
                LogMessage("Processing GET_VISUAL_TREE command");

                // Execute on the UI thread using Dispatcher
                object? result = null;
                Exception? dispatcherException = null;

                Application.Current?.Dispatcher.Invoke(() =>
                {
                    try
                    {
                        // Get all WPF controls (windows and nested in WinForms)
                        var wpfControls = GetAllWpfControls().ToList();
                        
                        if (wpfControls.Count == 0)
                        {
                            result = new { 
                                success = false, 
                                error = "No WPF controls found" 
                            };
                            return;
                        }

                        // Create context for tracking DataContexts
                        var dataContextTracker = new DataContextTracker();
                        
                        // Create visual tree representations for all WPF controls
                        var visualTrees = wpfControls.Select(control => 
                            CreateVisualTreeNode(control, dataContextTracker, null)).ToList();
                        
                        result = new
                        {
                            success = true,
                            processId = System.Diagnostics.Process.GetCurrentProcess().Id,
                            controlCount = wpfControls.Count,
                            visualTrees = visualTrees,
                            dataContexts = dataContextTracker.GetDataContexts(),
                            timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss UTC")
                        };
                    }
                    catch (Exception ex)
                    {
                        dispatcherException = ex;
                        LogMessage($"Error in Dispatcher.Invoke for visual tree: {ex.Message}");
                    }
                });

                if (dispatcherException != null)
                {
                    return JsonSerializer.Serialize(new { 
                        success = false, 
                        error = $"Error accessing UI thread: {dispatcherException.Message}" 
                    }, GetJsonOptions());
                }

                if (result == null)
                {
                    return JsonSerializer.Serialize(new { 
                        success = false, 
                        error = "Failed to get visual tree - no result returned" 
                    }, GetJsonOptions());
                }

                return JsonSerializer.Serialize(result, GetJsonOptions());
            }
            catch (Exception ex)
            {
                LogMessage($"Error in ProcessGetVisualTreeCommand: {ex.Message}");
                return JsonSerializer.Serialize(new { 
                    success = false, 
                    error = $"Error getting visual tree: {ex.Message}" 
                }, GetJsonOptions());
            }
        }

        private static string ProcessGetElementByHashcodeCommand(JsonElement commandElement)
        {
            try
            {
                LogMessage("Processing GET_ELEMENT_BY_HASHCODE command");

                if (!commandElement.TryGetProperty("type", out var typeElement))
                {
                    return JsonSerializer.Serialize(new { success = false, error = "Missing 'type' parameter" }, GetJsonOptions());
                }
                
                if (!commandElement.TryGetProperty("hashcode", out var hashcodeElement))
                {
                    return JsonSerializer.Serialize(new { success = false, error = "Missing 'hashcode' parameter" }, GetJsonOptions());
                }
                
                var typeName = typeElement.GetString();
                var hashcode = hashcodeElement.GetInt32();

                if (string.IsNullOrWhiteSpace(typeName))
                {
                    return JsonSerializer.Serialize(new { success = false, error = "type cannot be empty" }, GetJsonOptions());
                }

                LogMessage($"Looking for element with type: {typeName}, hashcode: {hashcode}");

                // Execute on the UI thread using Dispatcher
                object? result = null;
                Exception? dispatcherException = null;

                Application.Current?.Dispatcher.Invoke(() =>
                {
                    try
                    {
                        var targetObject = FindObjectByTypeAndHashCode(typeName, hashcode);
                        
                        if (targetObject == null)
                        {
                            LogMessage($"Element not found - Type: {typeName}, HashCode: {hashcode}");
                            result = new { 
                                success = false, 
                                error = $"{typeName} with hashcode {hashcode} not found in any window" 
                            };
                            return;
                        }

                        if (!(targetObject is DependencyObject dependencyObject))
                        {
                            LogMessage($"Found object is not a DependencyObject - Type: {targetObject.GetType().Name}");
                            result = new { 
                                success = false, 
                                error = $"Found object with hashcode {hashcode} is not a DependencyObject" 
                            };
                            return;
                        }

                        LogMessage($"Found element: {dependencyObject.GetType().Name} with hashcode {dependencyObject.GetHashCode()}");

                        // Create context for tracking DataContexts (for consistency with full tree)
                        var dataContextTracker = new DataContextTracker();
                        
                        // Create element representation without children
                        var elementNode = CreateVisualTreeNodeWithoutChildren(dependencyObject, dataContextTracker, null);
                        
                        result = new
                        {
                            success = true,
                            message = "Element retrieved successfully",
                            processId = System.Diagnostics.Process.GetCurrentProcess().Id,
                            type = typeName,
                            hashcode = hashcode,
                            element = elementNode,
                            dataContexts = dataContextTracker.GetDataContexts(),
                            timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss UTC")
                        };
                    }
                    catch (Exception ex)
                    {
                        dispatcherException = ex;
                        LogMessage($"Error in Dispatcher.Invoke for element retrieval: {ex.Message}");
                    }
                });

                if (dispatcherException != null)
                {
                    return JsonSerializer.Serialize(new { 
                        success = false, 
                        error = $"Error accessing UI thread: {dispatcherException.Message}" 
                    }, GetJsonOptions());
                }

                if (result == null)
                {
                    return JsonSerializer.Serialize(new { 
                        success = false, 
                        error = "Failed to get element - no result returned" 
                    }, GetJsonOptions());
                }

                return JsonSerializer.Serialize(result, GetJsonOptions());
            }
            catch (Exception ex)
            {
                LogMessage($"Error in ProcessGetElementByHashcodeCommand: {ex.Message}");
                return JsonSerializer.Serialize(new { 
                    success = false, 
                    error = $"Error getting element by hashcode: {ex.Message}" 
                }, GetJsonOptions());
            }
        }

        private static Dictionary<string, object> CreateVisualTreeNode(DependencyObject element, DataContextTracker dataContextTracker, object? parentDataContext)
        {
            try
            {
                // Get the base element info without children
                var elementInfo = CreateVisualTreeNodeWithoutChildren(element, dataContextTracker, parentDataContext);
                
                // Determine the current DataContext for child processing
                object? currentDataContext = parentDataContext;
                if (element is FrameworkElement fe)
                {
                    var elementDataContext = fe.DataContext;
                    if (elementDataContext != null && !ReferenceEquals(elementDataContext, parentDataContext))
                    {
                        currentDataContext = elementDataContext;
                    }
                }

                // Add children if present
                var children = GetChildren(element)
                    .Select(child => CreateVisualTreeNode(child, dataContextTracker, currentDataContext))
                    .ToList();

                // Only include childCount and children if there are actual children
                if (children.Count > 0)
                {
                    elementInfo["childCount"] = children.Count;
                    elementInfo["children"] = children;
                }

                return elementInfo;
            }
            catch (Exception ex)
            {
                LogMessage($"Error creating visual tree node for {element?.GetType().Name}: {ex.Message}");
                return new Dictionary<string, object>
                {
                    ["type"] = element?.GetType().Name ?? "Unknown",
                    ["error"] = ex.Message,
                    ["children"] = new List<object>(),
                    ["childCount"] = 0
                };
            }
        }

        private static Dictionary<string, object> CreateVisualTreeNodeWithoutChildren(DependencyObject element, DataContextTracker dataContextTracker, object? parentDataContext)
        {
            try
            {
                var elementInfo = new Dictionary<string, object>
                {
                    ["type"] = element.GetType().FullName ?? "Unknown",
                    ["hashCode"] = element.GetHashCode(),
                };

                // Handle DataContext
                if (element is FrameworkElement fe)
                {
                    var elementDataContext = fe.DataContext;
                    if (elementDataContext != null && !ReferenceEquals(elementDataContext, parentDataContext))
                    {
                        // This element has a different DataContext than its parent
                        var dataContextId = dataContextTracker.RegisterDataContext(elementDataContext);
                        if (dataContextId != null)
                        {
                            elementInfo["dataContextId"] = dataContextId;
                        }
                    }
                }

                // Get all dependency properties for this type, ordered from most specific to base classes
                var dependencyProperties = DependencyPropertyCache.GetDependencyProperties(element.GetType());
                foreach (var dp in dependencyProperties)
                {
                    // Skip DataContext property as we handle it separately
                    if (dp.Name == "DataContext")
                        continue;

                    var value = GetValue(element, dp);
                    if (value != null)
                    {
                        elementInfo[dp.Name] = value;
                    }
                }

                // Add AutomationPeer information if element is a UIElement
                if (element is UIElement uiElement)
                {
                    var automationPeerInfo = AutomationPeerHandler.GetAutomationPeerInfo(uiElement);
                    if (automationPeerInfo != null)
                    {
                        elementInfo["automationPeer"] = automationPeerInfo;
                    }
                }

                // No children for single element retrieval
                return elementInfo;
            }
            catch (Exception ex)
            {
                LogMessage($"Error creating visual tree node for {element?.GetType().Name}: {ex.Message}");
                return new Dictionary<string, object>
                {
                    ["type"] = element?.GetType().Name ?? "Unknown",
                    ["error"] = ex.Message
                };
            }
        }

        private static IEnumerable<DependencyObject> GetChildrenRecursive(DependencyObject element)
        {
            var dependencyObjects = new Stack<DependencyObject>();

            dependencyObjects.Push(element);

            while (dependencyObjects.TryPop(out var next))
            {
                yield return next;

                foreach (var dependencyObject in GetChildren(next))
                {
                    dependencyObjects.Push(dependencyObject);
                }
            }
        }

        private static IEnumerable<DependencyObject> GetChildren(DependencyObject element)
        {
            // Get children from LogicalTreeHelper
            foreach (object logicalChild in LogicalTreeHelper.GetChildren(element))
            {
                if (logicalChild is DependencyObject depObj)
                {
                    yield return depObj;
                }
            }

            if (element is ItemsControl itemsControl)
            {
                foreach (var item in itemsControl.Items)
                {
                    if (item is DependencyObject)
                    {
                        continue;
                    }

                    var container = itemsControl.ItemContainerGenerator.ContainerFromItem(item);

                    if (container is not null)
                    {
                        yield return container;
                    }
                }
            }
        }

        // Property filtering configuration
        private static readonly HashSet<string> InteractionPropertyNames = new()
        {
            "Content", "Text", "Header", "Title", "Name", "ToolTip",
            "IsEnabled", "IsSelected", "IsChecked", "IsExpanded", "IsPressed",
            "Visibility", "Command", "CommandParameter", "Value", "EditValue"
        };

        private static readonly HashSet<string> LayoutPropertyNames = new()
        {
            "Width", "Height", "MinWidth", "MinHeight", "MaxWidth", "MaxHeight",
            "Margin", "Padding", "HorizontalAlignment", "VerticalAlignment",
            "Row", "Column", "RowSpan", "ColumnSpan", "Panel.ZIndex"
        };

        private static bool IsInteractionProperty(DependencyProperty dp)
        {
            return InteractionPropertyNames.Contains(dp.Name);
        }

        private static bool IsLayoutProperty(DependencyProperty dp)
        {
            return LayoutPropertyNames.Contains(dp.Name) || 
                   dp.Name.Contains("Grid.") || dp.Name.Contains("Canvas.") || 
                   dp.Name.Contains("DockPanel.");
        }

        private static bool ShouldExcludeProperty(DependencyProperty dp, object? value)
        {
            // Exclude computed/internal properties
            if (dp.Name.StartsWith("Actual")) return true;  // ActualWidth, ActualHeight
            if (dp.Name.Contains("Internal")) return true;
            if (dp.Name.Contains("Cache")) return true;
            
            // Exclude complex framework objects that don't provide user value
            if (value is Style || value is ControlTemplate || value is DataTemplate) return true;
            if (value?.GetType().Name.Contains("Collection") == true && dp.Name.EndsWith("Effects")) return true;
            
            // Exclude properties with default/framework values
            if (dp.Name == "Resources" && value?.ToString() == "System.Windows.ResourceDictionary") return true;
            if (dp.Name.EndsWith("Brush") && value?.GetType().Name == "SolidColorBrush") 
            {
                // Only include if it's not a default system brush
                return IsSystemDefaultBrush(value);
            }
            
            return false;
        }

        private static bool IsSystemDefaultBrush(object brush)
        {
            // Check if it's a system default brush (simplified heuristic)
            return brush.GetHashCode() < 100000;
        }

        private static bool HasMeaningfulValue(DependencyObject element, DependencyProperty dp, object? value)
        {
            if (value == null) return false;
            
            // Always include bindings (they're meaningful for LLM context)
            if (BindingOperations.GetBinding(element, dp) != null) return true;
            if (BindingOperations.GetBindingExpression(element, dp) != null) return true;
            if (BindingOperations.GetMultiBindingExpression(element, dp) != null) return true;
            if (BindingOperations.GetPriorityBindingExpression(element, dp) != null) return true;
            
            // Exclude generic type references that don't provide user context
            var valueString = value.ToString();
            if (valueString?.StartsWith("System.Windows.") == true && 
                !IsInteractionProperty(dp) && !IsLayoutProperty(dp)) return false;
            
            // Include user-set values for interaction properties
            if (IsInteractionProperty(dp)) return true;
            
            // Include layout values that affect positioning (but not default values)
            if (IsLayoutProperty(dp) && !IsDefaultLayoutValue(dp.Name, value)) return true;
            
            return false;
        }

        private static bool IsDefaultLayoutValue(string propertyName, object value)
        {
            var valueString = value.ToString();
            return propertyName switch
            {
                "Width" or "Height" => valueString == "NaN",
                "Margin" or "Padding" => valueString == "0,0,0,0",
                "HorizontalAlignment" => valueString == "Stretch",
                "VerticalAlignment" => valueString == "Stretch",
                _ => false
            };
        }

        private static object? GetValue(DependencyObject element, DependencyProperty dp)
        {
            try
            {
                // First check if value differs from default (existing logic)
                var value = DependencyPropertyCache.GetNonDefaultValue(element, dp);
                
                // Check if the property has a binding (always include bindings)
                var binding = BindingOperations.GetBinding(element, dp);
                if (binding != null)
                {
                    return GetBindingInfo(element, dp, binding);
                }

                // Check for other types of expressions (MultiBinding, PriorityBinding, etc.)
                var bindingExpression = BindingOperations.GetBindingExpression(element, dp);
                if (bindingExpression != null)
                {
                    return GetBindingExpressionInfo(element, dp, bindingExpression);
                }

                var multiBindingExpression = BindingOperations.GetMultiBindingExpression(element, dp);
                if (multiBindingExpression != null)
                {
                    return GetMultiBindingInfo(element, dp, multiBindingExpression);
                }

                var priorityBindingExpression = BindingOperations.GetPriorityBindingExpression(element, dp);
                if (priorityBindingExpression != null)
                {
                    return GetPriorityBindingInfo(element, dp, priorityBindingExpression);
                }

                // Apply programmatic filtering for non-binding properties
                if (value != null && ShouldExcludeProperty(dp, value))
                    return null;
                    
                // Check if the property has meaningful value for LLM context
                if (value != null && !HasMeaningfulValue(element, dp, value))
                    return null;

                // Return simplified value format for LLM consumption
                if (value != null)
                {
                    return value switch
                    {
                        SolidColorBrush brush => brush.Color.ToString(),
                        Thickness thickness => thickness.ToString(),
                        _ => SerializePropertyValue(value)
                    };
                }

                return null;
            }
            catch (Exception ex)
            {
                return new
                {
                    type = "error",
                    error = ex.Message
                };
            }
        }

        private static object GetBindingInfo(DependencyObject element, DependencyProperty dp, Binding binding)
        {
            var bindingExpression = BindingOperations.GetBindingExpression(element, dp);
            
            var info = new Dictionary<string, object>
            {
                ["type"] = "binding",
                ["path"] = binding.Path?.Path ?? "",
                ["mode"] = binding.Mode.ToString()
            };

            // Add source information
            if (binding.Source != null)
            {
                info["source"] = binding.Source.GetType().Name;
            }
            else if (!string.IsNullOrEmpty(binding.ElementName))
            {
                info["elementName"] = binding.ElementName;
            }
            else if (binding.RelativeSource != null)
            {
                info["relativeSource"] = binding.RelativeSource.Mode.ToString();
            }
            else
            {
                info["source"] = "DataContext";
            }

            // Add converter information
            if (binding.Converter != null)
            {
                info["converter"] = binding.Converter.GetType().Name;
                if (binding.ConverterParameter != null)
                {
                    info["converterParameter"] = SerializePropertyValue(binding.ConverterParameter);
                }
            }

            // Add binding validation and error information
            if (bindingExpression != null)
            {
                if (bindingExpression.HasError)
                {
                    info["hasError"] = true;
                    if (bindingExpression.ValidationError != null)
                    {
                        info["error"] = bindingExpression.ValidationError.ErrorContent?.ToString() ?? "Validation error";
                    }
                }

                if (bindingExpression.HasValidationError)
                {
                    info["hasValidationError"] = true;
                }

                // Add resolved value if no errors
                try
                {
                    var resolvedValue = element.GetValue(dp);
                    info["resolvedValue"] = SerializePropertyValue(resolvedValue);
                }
                catch (Exception ex)
                {
                    info["valueError"] = ex.Message;
                }
            }

            return info;
        }

        private static object GetBindingExpressionInfo(DependencyObject element, DependencyProperty dp, BindingExpression bindingExpression)
        {
            var info = new Dictionary<string, object>
            {
                ["type"] = "bindingExpression"
            };

            if (bindingExpression.ParentBinding != null)
            {
                var binding = bindingExpression.ParentBinding;
                info["path"] = binding.Path?.Path ?? "";
                info["mode"] = binding.Mode.ToString();
            }

            if (bindingExpression.HasError)
            {
                info["hasError"] = true;
                if (bindingExpression.ValidationError != null)
                {
                    info["error"] = bindingExpression.ValidationError.ErrorContent?.ToString() ?? "Validation error";
                }
            }

            try
            {
                var resolvedValue = element.GetValue(dp);
                info["resolvedValue"] = SerializePropertyValue(resolvedValue);
            }
            catch (Exception ex)
            {
                info["valueError"] = ex.Message;
            }

            return info;
        }

        private static object GetMultiBindingInfo(DependencyObject element, DependencyProperty dp, MultiBindingExpression multiBindingExpression)
        {
            var info = new Dictionary<string, object>
            {
                ["type"] = "multiBinding"
            };

            if (multiBindingExpression.ParentMultiBinding != null)
            {
                var multiBinding = multiBindingExpression.ParentMultiBinding;
                info["bindingCount"] = multiBinding.Bindings.Count;
                
                if (multiBinding.Converter != null)
                {
                    info["converter"] = multiBinding.Converter.GetType().Name;
                }
            }

            if (multiBindingExpression.HasError)
            {
                info["hasError"] = true;
                if (multiBindingExpression.ValidationError != null)
                {
                    info["error"] = multiBindingExpression.ValidationError.ErrorContent?.ToString() ?? "Validation error";
                }
            }

            try
            {
                var resolvedValue = element.GetValue(dp);
                info["resolvedValue"] = SerializePropertyValue(resolvedValue);
            }
            catch (Exception ex)
            {
                info["valueError"] = ex.Message;
            }

            return info;
        }

        private static object GetPriorityBindingInfo(DependencyObject element, DependencyProperty dp, PriorityBindingExpression priorityBindingExpression)
        {
            var info = new Dictionary<string, object>
            {
                ["type"] = "priorityBinding"
            };

            if (priorityBindingExpression.ParentPriorityBinding != null)
            {
                var priorityBinding = priorityBindingExpression.ParentPriorityBinding;
                info["bindingCount"] = priorityBinding.Bindings.Count;
            }

            if (priorityBindingExpression.HasError)
            {
                info["hasError"] = true;
                if (priorityBindingExpression.ValidationError != null)
                {
                    info["error"] = priorityBindingExpression.ValidationError.ErrorContent?.ToString() ?? "Validation error";
                }
            }

            try
            {
                var resolvedValue = element.GetValue(dp);
                info["resolvedValue"] = SerializePropertyValue(resolvedValue);
            }
            catch (Exception ex)
            {
                info["valueError"] = ex.Message;
            }

            return info;
        }

        private static object? SerializePropertyValue(object value)
        {
            try
            {
                return value switch
                {
                    null => null,
                    string s => s,
                    bool b => b,
                    int i => i,
                    double d => d,
                    float f => f,
                    long l => l,
                    decimal dec => dec,
                    DateTime dt => dt.ToString("yyyy-MM-dd HH:mm:ss"),
                    TimeSpan ts => ts.ToString(),
                    Enum e => e.ToString(),
                    Type t => t.FullName ?? t.Name,
                    DependencyObject depObj => new
                    {
                        Type = depObj.GetType().FullName ?? depObj.GetType().Name,
                        HashCode = depObj.GetHashCode()
                    },
                    _ => value.ToString()
                };
            }
            catch
            {
                return value?.ToString();
            }
        }

    }
}
