#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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
                        return JsonSerializer.Serialize(new { success = false, error = "Missing 'commandType' or 'command' property" });
                    }
                }
                
                var command = commandTypeElement.GetString();
                
                switch (command?.ToUpperInvariant())
                {
                    case "RUN_COMMAND":
                        return ProcessRunCommand(root);
                    
                    case "TAKE_SCREENSHOT":
                        return ProcessTakeScreenshotCommand(root);
                    
                    case "GET_VISUAL_TREE":
                        return ProcessGetVisualTreeCommand(root);
                    
                    default:
                        return JsonSerializer.Serialize(new { success = false, error = $"Unknown command: {command}" });
                }
            }
            catch (Exception ex)
            {
                return JsonSerializer.Serialize(new { success = false, error = $"Error processing JSON command: {ex.Message}" });
            }
        }

        private static string ProcessRunCommand(JsonElement commandElement)
        {
            try
            {
                if (!commandElement.TryGetProperty("type", out var typeElement))
                {
                    return JsonSerializer.Serialize(new { success = false, error = "Missing 'type' parameter" });
                }
                
                if (!commandElement.TryGetProperty("hashcode", out var hashcodeElement))
                {
                    return JsonSerializer.Serialize(new { success = false, error = "Missing 'hashcode' parameter" });
                }
                
                if (!commandElement.TryGetProperty("command", out var commandTypeElement))
                {
                    return JsonSerializer.Serialize(new { success = false, error = "Missing 'command' parameter" });
                }

                var typeName = typeElement.GetString();
                var hashcode = hashcodeElement.GetInt32();
                var commandType = commandTypeElement.GetString();

                if (string.IsNullOrWhiteSpace(typeName))
                {
                    return JsonSerializer.Serialize(new { success = false, error = "type cannot be empty" });
                }
                
                if (string.IsNullOrWhiteSpace(commandType))
                {
                    return JsonSerializer.Serialize(new { success = false, error = "command cannot be empty" });
                }

                LogMessage($"Attempting to run command '{commandType}' on {typeName} with hashcode: {hashcode}");

                // Perform the command on the UI thread
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

                        LogMessage($"Searching for {typeName} with hashcode {hashcode} in window: {mainWindow.Title}");

                        // Find object by type and hashcode - search main window first
                        var targetObject = FindObjectByTypeAndHashCode(mainWindow, typeName, hashcode);
                        
                        // If not found in main window, search all other application windows
                        if (targetObject == null)
                        {
                            LogMessage($"Not found in main window, searching {Application.Current.Windows.Count - 1} other windows...");
                            foreach (Window window in Application.Current.Windows)
                            {
                                if (window != mainWindow)
                                {
                                    LogMessage($"Searching in window: {window.Title}");
                                    targetObject = FindObjectByTypeAndHashCode(window, typeName, hashcode);
                                    if (targetObject != null)
                                    {
                                        LogMessage($"Found {typeName} with hashcode {hashcode} in window: {window.Title}");
                                        break;
                                    }
                                }
                            }
                        }
                        else
                        {
                            LogMessage($"Found {typeName} with hashcode {hashcode} in main window");
                        }
                        
                        if (targetObject == null)
                        {
                            LogMessage($"Object not found after searching all windows. Type: {typeName}, HashCode: {hashcode}");
                            return new { 
                                success = false, 
                                error = $"{typeName} with hashcode {hashcode} not found in any window" 
                            };
                        }

                        LogMessage($"Found object: {targetObject.GetType().Name} with hashcode {targetObject.GetHashCode()}");

                        // Execute the command based on type
                        var commandResult = ExecuteCommandOnObject(targetObject, commandType, commandElement);
                        
                        return commandResult;
                    }
                    catch (Exception ex)
                    {
                        LogMessage($"Error executing command: {ex.Message}");
                        return new { success = false, error = $"Error executing command: {ex.Message}" };
                    }
                });

                return JsonSerializer.Serialize(result);
            }
            catch (Exception ex)
            {
                LogMessage($"Error in ProcessRunCommand: {ex.Message}");
                return JsonSerializer.Serialize(new { success = false, error = $"Error processing run command: {ex.Message}" });
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

                return JsonSerializer.Serialize(result);
            }
            catch (Exception ex)
            {
                LogMessage($"Error in ProcessTakeScreenshotCommand: {ex.Message}");
                return JsonSerializer.Serialize(new { success = false, error = $"Error processing take screenshot command: {ex.Message}" });
            }
        }

        private static Button? FindButtonByText(DependencyObject parent, string buttonText)
        {
            try
            {
                if (parent == null) return null;

                var allButtons = new List<Button>();
                CollectAllButtons(parent, allButtons);

                if (!allButtons.Any()) return null;

                // First pass: Try exact match (case-insensitive)
                foreach (var button in allButtons)
                {
                    var content = button.Content?.ToString();
                    if (string.Equals(content, buttonText, StringComparison.OrdinalIgnoreCase))
                    {
                        LogMessage($"Found exact match for '{buttonText}': '{content}'");
                        return button;
                    }
                }

                // Second pass: Try fuzzy matching if no exact match found
                LogMessage($"No exact match found for '{buttonText}', trying fuzzy matching...");
                
                var bestMatch = FindBestFuzzyMatch(allButtons, buttonText);
                if (bestMatch != null)
                {
                    var content = bestMatch.Content?.ToString();
                    LogMessage($"Found fuzzy match for '{buttonText}': '{content}'");
                    return bestMatch;
                }

                return null;
            }
            catch (Exception ex)
            {
                LogMessage($"Error in FindButtonByText: {ex.Message}");
                return null;
            }
        }

        private static void CollectAllButtons(DependencyObject parent, List<Button> buttons)
        {
            if (parent == null) return;

            try
            {
                // Check if this element is a button
                if (parent is Button button)
                {
                    buttons.Add(button);
                }

                // Search children recursively
                int childCount = VisualTreeHelper.GetChildrenCount(parent);
                for (int i = 0; i < childCount; i++)
                {
                    var child = VisualTreeHelper.GetChild(parent, i);
                    CollectAllButtons(child, buttons);
                }
            }
            catch (Exception ex)
            {
                LogMessage($"Error in CollectAllButtons: {ex.Message}");
            }
        }

        private static Button? FindBestFuzzyMatch(List<Button> buttons, string targetText)
        {
            if (!buttons.Any() || string.IsNullOrWhiteSpace(targetText))
                return null;

            Button? bestMatch = null;
            double bestScore = 0.0;
            const double minimumScore = 0.6; // Require at least 60% similarity

            foreach (var button in buttons)
            {
                var content = button.Content?.ToString();
                if (string.IsNullOrWhiteSpace(content))
                    continue;

                var score = CalculateSimilarity(targetText, content);
                
                LogMessage($"Comparing '{targetText}' with '{content}': similarity = {score:F2}");
                
                if (score > bestScore && score >= minimumScore)
                {
                    bestScore = score;
                    bestMatch = button;
                }
            }

            if (bestMatch != null)
            {
                LogMessage($"Best fuzzy match: '{bestMatch.Content}' with score {bestScore:F2}");
            }
            else
            {
                LogMessage($"No fuzzy match found above threshold {minimumScore}");
            }

            return bestMatch;
        }

        private static double CalculateSimilarity(string text1, string text2)
        {
            if (string.IsNullOrWhiteSpace(text1) || string.IsNullOrWhiteSpace(text2))
                return 0.0;

            // Normalize texts for comparison
            var normalizedText1 = NormalizeText(text1);
            var normalizedText2 = NormalizeText(text2);

            // Check if one contains the other (high score for partial matches)
            if (normalizedText1.Contains(normalizedText2) || normalizedText2.Contains(normalizedText1))
            {
                var shorterLength = Math.Min(normalizedText1.Length, normalizedText2.Length);
                var longerLength = Math.Max(normalizedText1.Length, normalizedText2.Length);
                return (double)shorterLength / longerLength * 0.9; // Slightly penalize partial matches
            }

            // Use Levenshtein distance for similarity calculation
            var distance = CalculateLevenshteinDistance(normalizedText1, normalizedText2);
            var maxLength = Math.Max(normalizedText1.Length, normalizedText2.Length);
            
            if (maxLength == 0) return 1.0;
            
            return 1.0 - (double)distance / maxLength;
        }

        private static string NormalizeText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            // Remove common prefixes/suffixes and normalize
            var normalized = text.Trim()
                                .Replace("+", "")
                                .Replace("&", "")
                                .Replace("_", " ")
                                .Replace("-", " ")
                                .ToLowerInvariant();

            // Remove extra whitespace
            while (normalized.Contains("  "))
            {
                normalized = normalized.Replace("  ", " ");
            }

            return normalized.Trim();
        }

        private static int CalculateLevenshteinDistance(string s1, string s2)
        {
            if (string.IsNullOrEmpty(s1)) return s2?.Length ?? 0;
            if (string.IsNullOrEmpty(s2)) return s1.Length;

            var matrix = new int[s1.Length + 1, s2.Length + 1];

            for (int i = 0; i <= s1.Length; i++)
                matrix[i, 0] = i;

            for (int j = 0; j <= s2.Length; j++)
                matrix[0, j] = j;

            for (int i = 1; i <= s1.Length; i++)
            {
                for (int j = 1; j <= s2.Length; j++)
                {
                    int cost = s1[i - 1] == s2[j - 1] ? 0 : 1;
                    matrix[i, j] = Math.Min(
                        Math.Min(matrix[i - 1, j] + 1, matrix[i, j - 1] + 1),
                        matrix[i - 1, j - 1] + cost);
                }
            }

            return matrix[s1.Length, s2.Length];
        }

        private static object? FindObjectByTypeAndHashCode(DependencyObject parent, string typeName, int hashcode)
        {
            if (parent == null) return null;

            try
            {
                // Check if this element matches the type and hashcode
                var elementType = parent.GetType();
                var elementTypeName = elementType.FullName ?? elementType.Name;
                var elementHashCode = parent.GetHashCode();
                
                // Log every DataGridRow we encounter for debugging
                if (elementTypeName.Contains("DataGridRow"))
                {
                    LogMessage($"Found DataGridRow: Type={elementTypeName}, HashCode={elementHashCode}, Target={typeName}:{hashcode}");
                }
                
                if (elementHashCode == hashcode && elementTypeName.Equals(typeName, StringComparison.OrdinalIgnoreCase))
                {
                    LogMessage($"MATCH FOUND: {typeName} with hashcode {hashcode}");
                    return parent;
                }

                // Search visual tree children recursively
                int childCount = VisualTreeHelper.GetChildrenCount(parent);
                for (int i = 0; i < childCount; i++)
                {
                    var child = VisualTreeHelper.GetChild(parent, i);
                    var found = FindObjectByTypeAndHashCode(child, typeName, hashcode);
                    if (found != null)
                    {
                        return found;
                    }
                }

                // Additional search for elements not in visual tree
                var logicalFound = FindInLogicalElements(parent, typeName, hashcode);
                if (logicalFound != null)
                {
                    return logicalFound;
                }
            }
            catch (Exception ex)
            {
                LogMessage($"Error in FindObjectByTypeAndHashCode: {ex.Message}");
            }

            return null;
        }

        private static object? FindInLogicalElements(DependencyObject element, string typeName, int hashcode)
        {
            try
            {
                // Search ContextMenu items for FrameworkElement types
                if (element is FrameworkElement frameworkElement && frameworkElement.ContextMenu != null)
                {
                    var contextMenu = frameworkElement.ContextMenu;
                    
                    // Check the ContextMenu itself
                    if (contextMenu.GetHashCode() == hashcode && 
                        contextMenu.GetType().FullName?.Equals(typeName, StringComparison.OrdinalIgnoreCase) == true)
                    {
                        return contextMenu;
                    }
                    
                    // Search ContextMenu items
                    var found = FindInMenuItems(contextMenu.Items, typeName, hashcode);
                    if (found != null)
                    {
                        return found;
                    }
                }

                // Search MenuItem subitems
                if (element is MenuItem menuItem && menuItem.Items.Count > 0)
                {
                    var found = FindInMenuItems(menuItem.Items, typeName, hashcode);
                    if (found != null)
                    {
                        return found;
                    }
                }

                // Search logical children that might not be in visual tree
                foreach (object logicalChild in LogicalTreeHelper.GetChildren(element))
                {
                    if (logicalChild is DependencyObject depObj)
                    {
                        // Check if this logical child matches
                        if (depObj.GetHashCode() == hashcode && 
                            depObj.GetType().FullName?.Equals(typeName, StringComparison.OrdinalIgnoreCase) == true)
                        {
                            return depObj;
                        }
                        
                        // Recursively search logical children
                        var found = FindInLogicalElements(depObj, typeName, hashcode);
                        if (found != null)
                        {
                            return found;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogMessage($"Error in FindInLogicalElements: {ex.Message}");
            }

            return null;
        }

        private static object? FindInMenuItems(ItemCollection items, string typeName, int hashcode)
        {
            try
            {
                foreach (var item in items)
                {
                    if (item is DependencyObject depObj)
                    {
                        // Check if this item matches
                        if (depObj.GetHashCode() == hashcode && 
                            depObj.GetType().FullName?.Equals(typeName, StringComparison.OrdinalIgnoreCase) == true)
                        {
                            return depObj;
                        }
                        
                        // If it's a MenuItem, search its subitems recursively
                        if (item is MenuItem menuItem && menuItem.Items.Count > 0)
                        {
                            var found = FindInMenuItems(menuItem.Items, typeName, hashcode);
                            if (found != null)
                            {
                                return found;
                            }
                        }
                        
                        // Also search logical children of this menu item
                        var logicalFound = FindInLogicalElements(depObj, typeName, hashcode);
                        if (logicalFound != null)
                        {
                            return logicalFound;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogMessage($"Error in FindInMenuItems: {ex.Message}");
            }

            return null;
        }

        private static object ExecuteCommandOnObject(object targetObject, string commandType, JsonElement commandData)
        {
            try
            {
                switch (commandType.ToUpperInvariant())
                {
                    case "CLICK":
                        return ExecuteClickCommand(targetObject);
                    
                    case "SET_TEXT":
                        return ExecuteSetTextCommand(targetObject, commandData);
                    
                    case "GET_PROPERTY":
                        return ExecuteGetPropertyCommand(targetObject, commandData);
                    
                    case "SET_PROPERTY":
                        return ExecuteSetPropertyCommand(targetObject, commandData);
                    
                    case "INVOKE_METHOD":
                        return ExecuteInvokeMethodCommand(targetObject, commandData);
                    
                    default:
                        return new { 
                            success = false, 
                            error = $"Unknown command type: {commandType}. Supported: CLICK, SET_TEXT, GET_PROPERTY, SET_PROPERTY, INVOKE_METHOD" 
                        };
                }
            }
            catch (Exception ex)
            {
                LogMessage($"Error executing command {commandType}: {ex.Message}");
                return new { success = false, error = $"Error executing command: {ex.Message}" };
            }
        }

        private static object ExecuteClickCommand(object targetObject)
        {
            try
            {
                switch (targetObject)
                {
                    case Button button:
                        if (!button.IsEnabled)
                            return new { success = false, error = "Button is disabled" };
                        if (!button.IsVisible)
                            return new { success = false, error = "Button is not visible" };

                        if (button.Command != null && button.Command.CanExecute(button.CommandParameter))
                        {
                            button.Command.Execute(button.CommandParameter);
                        }
                        else
                        {
                            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        }
                        return new { success = true, message = "Button clicked successfully" };

                    case System.Windows.Controls.Primitives.ButtonBase buttonBase:
                        if (!buttonBase.IsEnabled)
                            return new { success = false, error = "Button is disabled" };
                        if (!buttonBase.IsVisible)
                            return new { success = false, error = "Button is not visible" };

                        if (buttonBase.Command != null && buttonBase.Command.CanExecute(buttonBase.CommandParameter))
                        {
                            buttonBase.Command.Execute(buttonBase.CommandParameter);
                        }
                        else
                        {
                            buttonBase.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                        }
                        return new { success = true, message = "Button clicked successfully" };

                    default:
                        return new { success = false, error = $"CLICK command not supported for type {targetObject.GetType().Name}" };
                }
            }
            catch (Exception ex)
            {
                return new { success = false, error = $"Error clicking object: {ex.Message}" };
            }
        }

        private static object ExecuteSetTextCommand(object targetObject, JsonElement commandData)
        {
            try
            {
                if (!commandData.TryGetProperty("text", out var textElement))
                {
                    return new { success = false, error = "Missing 'text' parameter for SET_TEXT command" };
                }

                var text = textElement.GetString() ?? "";

                switch (targetObject)
                {
                    case TextBox textBox:
                        textBox.Text = text;
                        return new { success = true, message = "Text set successfully" };

                    case System.Windows.Controls.Primitives.TextBoxBase textBoxBase:
                        // Use reflection to set text for other TextBox-derived types
                        var textProperty = textBoxBase.GetType().GetProperty("Text");
                        if (textProperty != null && textProperty.CanWrite)
                        {
                            textProperty.SetValue(textBoxBase, text);
                            return new { success = true, message = "Text set successfully" };
                        }
                        return new { success = false, error = "Cannot set text on this text control" };

                    default:
                        return new { success = false, error = $"SET_TEXT command not supported for type {targetObject.GetType().Name}" };
                }
            }
            catch (Exception ex)
            {
                return new { success = false, error = $"Error setting text: {ex.Message}" };
            }
        }

        private static object ExecuteGetPropertyCommand(object targetObject, JsonElement commandData)
        {
            try
            {
                if (!commandData.TryGetProperty("property", out var propertyElement))
                {
                    return new { success = false, error = "Missing 'property' parameter for GET_PROPERTY command" };
                }

                var propertyName = propertyElement.GetString();
                if (string.IsNullOrWhiteSpace(propertyName))
                {
                    return new { success = false, error = "Property name cannot be empty" };
                }

                var property = targetObject.GetType().GetProperty(propertyName);
                if (property == null)
                {
                    return new { success = false, error = $"Property '{propertyName}' not found on type {targetObject.GetType().Name}" };
                }

                var value = property.GetValue(targetObject);
                return new { success = true, property = propertyName, value = value?.ToString() ?? "null" };
            }
            catch (Exception ex)
            {
                return new { success = false, error = $"Error getting property: {ex.Message}" };
            }
        }

        private static object ExecuteSetPropertyCommand(object targetObject, JsonElement commandData)
        {
            try
            {
                if (!commandData.TryGetProperty("property", out var propertyElement))
                {
                    return new { success = false, error = "Missing 'property' parameter for SET_PROPERTY command" };
                }

                if (!commandData.TryGetProperty("value", out var valueElement))
                {
                    return new { success = false, error = "Missing 'value' parameter for SET_PROPERTY command" };
                }

                var propertyName = propertyElement.GetString();
                if (string.IsNullOrWhiteSpace(propertyName))
                {
                    return new { success = false, error = "Property name cannot be empty" };
                }

                var property = targetObject.GetType().GetProperty(propertyName);
                if (property == null)
                {
                    return new { success = false, error = $"Property '{propertyName}' not found on type {targetObject.GetType().Name}" };
                }

                if (!property.CanWrite)
                {
                    return new { success = false, error = $"Property '{propertyName}' is read-only" };
                }

                // Convert value to appropriate type
                object? convertedValue = null;
                if (valueElement.ValueKind != JsonValueKind.Null)
                {
                    var propertyType = property.PropertyType;
                    if (propertyType == typeof(string))
                        convertedValue = valueElement.GetString();
                    else if (propertyType == typeof(bool) || propertyType == typeof(bool?))
                        convertedValue = valueElement.GetBoolean();
                    else if (propertyType == typeof(int) || propertyType == typeof(int?))
                        convertedValue = valueElement.GetInt32();
                    else if (propertyType == typeof(double) || propertyType == typeof(double?))
                        convertedValue = valueElement.GetDouble();
                    else
                        convertedValue = Convert.ChangeType(valueElement.GetString(), propertyType);
                }

                property.SetValue(targetObject, convertedValue);
                return new { success = true, message = $"Property '{propertyName}' set successfully" };
            }
            catch (Exception ex)
            {
                return new { success = false, error = $"Error setting property: {ex.Message}" };
            }
        }

        private static object ExecuteInvokeMethodCommand(object targetObject, JsonElement commandData)
        {
            try
            {
                if (!commandData.TryGetProperty("method", out var methodElement))
                {
                    return new { success = false, error = "Missing 'method' parameter for INVOKE_METHOD command" };
                }

                var methodName = methodElement.GetString();
                if (string.IsNullOrWhiteSpace(methodName))
                {
                    return new { success = false, error = "Method name cannot be empty" };
                }

                // Get parameters if provided
                var parameters = new object[0];
                if (commandData.TryGetProperty("parameters", out var parametersElement) && 
                    parametersElement.ValueKind == JsonValueKind.Array)
                {
                    var paramList = new List<object>();
                    foreach (var param in parametersElement.EnumerateArray())
                    {
                        if (param.ValueKind == JsonValueKind.String)
                            paramList.Add(param.GetString() ?? "");
                        else if (param.ValueKind == JsonValueKind.Number)
                            paramList.Add(param.GetDouble());
                        else if (param.ValueKind == JsonValueKind.True || param.ValueKind == JsonValueKind.False)
                            paramList.Add(param.GetBoolean());
                        else
                            paramList.Add(param.GetRawText());
                    }
                    parameters = paramList.ToArray();
                }

                var method = targetObject.GetType().GetMethod(methodName, parameters.Select(p => p.GetType()).ToArray());
                if (method == null)
                {
                    return new { success = false, error = $"Method '{methodName}' not found on type {targetObject.GetType().Name}" };
                }

                var result = method.Invoke(targetObject, parameters);
                return new { success = true, method = methodName, result = result?.ToString() ?? "null" };
            }
            catch (Exception ex)
            {
                return new { success = false, error = $"Error invoking method: {ex.Message}" };
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

        private static string ProcessGetVisualTreeCommand(JsonElement commandElement)
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
                        // Get the main window
                        var mainWindow = Application.Current?.MainWindow;
                        if (mainWindow == null)
                        {
                            result = new { 
                                success = false, 
                                error = "Main window not found" 
                            };
                            return;
                        }

                        // Create visual tree representation
                        var visualTree = CreateVisualTreeNode(mainWindow);
                        
                        result = new
                        {
                            success = true,
                            processId = System.Diagnostics.Process.GetCurrentProcess().Id,
                            mainWindowTitle = mainWindow.Title,
                            visualTree = visualTree,
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
                    });
                }

                if (result == null)
                {
                    return JsonSerializer.Serialize(new { 
                        success = false, 
                        error = "Failed to get visual tree - no result returned" 
                    });
                }

                return JsonSerializer.Serialize(result, new JsonSerializerOptions 
                { 
                    WriteIndented = true 
                });
            }
            catch (Exception ex)
            {
                LogMessage($"Error in ProcessGetVisualTreeCommand: {ex.Message}");
                return JsonSerializer.Serialize(new { 
                    success = false, 
                    error = $"Error getting visual tree: {ex.Message}" 
                });
            }
        }

        private static object CreateVisualTreeNode(DependencyObject element)
        {
            try
            {
                var elementInfo = new Dictionary<string, object>
                {
                    ["type"] = element.GetType().FullName ?? "Unknown",
                    ["hashCode"] = element.GetHashCode(),
                };

                // Add common properties based on element type
                if (element is FrameworkElement fe)
                {
                    if(!string.IsNullOrEmpty(fe.Name))
                        elementInfo["name"] = fe.Name ?? "";

                    var dataContext = fe.DataContext;
                    if (dataContext != null)
                    {
                        elementInfo["dataContext"] = dataContext.GetType().FullName ?? "Unknown";
                        elementInfo["dataContextHashCode"] = dataContext.GetHashCode();
                    }
                }

                if (element is Window window)
                {
                    elementInfo["title"] = window.Title;
                }

                if (element is ContentControl contentControl)
                {
                    if (contentControl.Content != null)
                        elementInfo["content"] = contentControl.Content.ToString();
                }

                if (element is TextBlock textBlock)
                {
                    elementInfo["text"] = textBlock.Text ?? "";
                }

                if (element is Button button)
                {
                    elementInfo["content"] = button.Content?.ToString() ?? "";
                    var binding = BindingOperations.GetBinding(button, ButtonBase.CommandProperty);
                    var bindingPath = binding?.Path?.Path;
                    if (!string.IsNullOrEmpty(bindingPath))
                    {
                        elementInfo["commandBindingPath"] = bindingPath;
                    }

                }

                if (element is TextBox textBox)
                {
                    elementInfo["text"] = textBox.Text ?? "";
                    elementInfo["isReadOnly"] = textBox.IsReadOnly;
                }

                if (element is DataGrid dataGrid)
                {
                    elementInfo["itemsCount"] = dataGrid.Items.Count;

                    var contextMenu = dataGrid.ContextMenu;
                    if (contextMenu != null)
                    {
                        elementInfo["contextMenuCount"] = contextMenu.Items.Count;
                        elementInfo["contextMenuItems"] = contextMenu.Items.OfType<DependencyObject>().Select(CreateVisualTreeNode).ToList();
                    }
                }

                if (element is MenuItem menuItem)
                {
                    elementInfo["header"] = menuItem.Header;
                    var binding = BindingOperations.GetBinding(menuItem, ButtonBase.CommandProperty);
                    var bindingPath = binding?.Path?.Path;
                    if (!string.IsNullOrEmpty(bindingPath))
                    {
                        elementInfo["commandBindingPath"] = bindingPath;
                    }

                    if (menuItem.Items.Count > 0)
                    {
                        elementInfo["contextMenuCount"] = menuItem.Items.Count;
                        elementInfo["contextMenuItems"] = menuItem.Items.OfType<DependencyObject>().Select(CreateVisualTreeNode).ToList();
                    }
                }

                // Special handling for DataGridCell - extract actual content directly
                if (element is DataGridCell dataGridCell)
                {
                    var leafNodes = FindLeafNodesInCell(dataGridCell);
                    
                    elementInfo["childCount"] = leafNodes.Count;
                    elementInfo["cells"] = leafNodes.Select(CreateVisualTreeNode).ToList();
                    
                    return elementInfo;
                }

                // Get children normally for all other elements, including DataGridRow
                var children = new List<object>();
                int childCount = VisualTreeHelper.GetChildrenCount(element);
                for (int i = 0; i < childCount; i++)
                {
                    var child = VisualTreeHelper.GetChild(element, i);
                    children.Add(CreateVisualTreeNode(child));
                }

                elementInfo["childCount"] = childCount;
                elementInfo["children"] = children;

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

        private static List<DependencyObject> FindLeafNodesInCell(DependencyObject parent)
        {
            var leafNodes = new List<DependencyObject>();
            
            int childCount = VisualTreeHelper.GetChildrenCount(parent);
            
            // If this node has no children, it's a leaf node
            if (childCount == 0)
            {
                // Skip certain container types that aren't meaningful content
                if (!(parent is Border || parent is ContentPresenter || parent is Panel))
                {
                    leafNodes.Add(parent);
                }
                return leafNodes;
            }
            
            // If it has children, recursively search
            for (int i = 0; i < childCount; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                leafNodes.AddRange(FindLeafNodesInCell(child));
            }
            
            return leafNodes;
        }

        /// <summary>
        /// Cleanup method - should be called when the inspector is being unloaded
        /// </summary>
        public static void Cleanup()
        {
            try
            {
                LogMessage("WpfInspector.Cleanup called");
                
                _cancellationTokenSource?.Cancel();
                _pipeServer?.Dispose();
                _serverTask?.Wait(5000); // Wait up to 5 seconds for cleanup
                
                LogMessage("WpfInspector cleanup completed");
            }
            catch (Exception ex)
            {
                LogMessage($"Error in WpfInspector.Cleanup: {ex}");
            }
        }
    }
}
