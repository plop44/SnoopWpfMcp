#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;

namespace WpfInspector
{
    /// <summary>
    /// Constants for automation pattern actions to ensure consistency between GetAutomationPeerInfo and ExecuteInvokeAutomationPeerCommand
    /// </summary>
    public static class AutomationActions
    {
        // Invoke Pattern
        public const string Invoke_Invoke = "Invoke_Invoke";
        
        // Value Pattern
        public const string Value_Get = "Value_Get";
        public const string Value_Set = "Value_Set";
        
        // SelectionItem Pattern
        public const string SelectionItem_Select = "SelectionItem_Select";
        public const string SelectionItem_AddToSelection = "SelectionItem_AddToSelection";
        public const string SelectionItem_RemoveFromSelection = "SelectionItem_RemoveFromSelection";
        public const string SelectionItem_Status = "SelectionItem_Status";
        
        // Toggle Pattern
        public const string Toggle_Toggle = "Toggle_Toggle";
        public const string Toggle_Status = "Toggle_Status";
        
        // ExpandCollapse Pattern
        public const string ExpandCollapse_Expand = "ExpandCollapse_Expand";
        public const string ExpandCollapse_Collapse = "ExpandCollapse_Collapse";
        public const string ExpandCollapse_Toggle = "ExpandCollapse_Toggle";
        public const string ExpandCollapse_Status = "ExpandCollapse_Status";
        
        // RangeValue Pattern
        public const string RangeValue_Get = "RangeValue_Get";
        public const string RangeValue_Set = "RangeValue_Set";
        
        // Scroll Pattern
        public const string Scroll_Status = "Scroll_Status";
        public const string Scroll_Scroll = "Scroll_Scroll";
        public const string Scroll_SetPosition = "Scroll_SetPosition";
        
    }

    /// <summary>
    /// Handles automation peer operations for WPF UI elements
    /// </summary>
    public static class AutomationPeerHandler
    {
        /// <summary>
        /// Gets automation peer information for a UI element, returning specific action names that can be executed
        /// </summary>
        public static object? GetAutomationPeerInfo(UIElement element)
        {
            try
            {
                var peer = UIElementAutomationPeer.CreatePeerForElement(element);
                if (peer == null)
                    return null;

                var supportedActions = GetSupportedActions(peer);

                // For wrapper peers, try to get the actual source peer
                var sourcePeer = peer.EventsSource;
                if (supportedActions.Count == 0 && sourcePeer != null && peer != sourcePeer)
                {
                    supportedActions = GetSupportedActions(sourcePeer);
                }


                if (supportedActions.Count == 0)
                    // no need to return anything when no actions are supported    
                    return null;

                var peerInfo = new Dictionary<string, object>();

                // Local helper functions
                void AddIfNotEmpty(string key, string? value)
                {
                    if (!string.IsNullOrWhiteSpace(value))
                        peerInfo[key] = value;
                }

                void AddIfTrue(string key, bool value)
                {
                    if (value)
                        peerInfo[key] = true;
                }

                void AddIfFalse(string key, bool value)
                {
                    if (!value)
                        peerInfo[key] = false;
                }
                
                // Add string properties only if they have meaningful values
                AddIfNotEmpty("automationId", peer.GetAutomationId());
                AddIfNotEmpty("name", peer.GetName());
                AddIfNotEmpty("itemType", peer.GetItemType());
                AddIfNotEmpty("itemStatus", peer.GetItemStatus());
                AddIfNotEmpty("helpText", peer.GetHelpText());
                AddIfNotEmpty("acceleratorKey", peer.GetAcceleratorKey());
                AddIfNotEmpty("accessKey", peer.GetAccessKey());

                // Always include control type as it's fundamental
                peerInfo["controlType"] = peer.GetAutomationControlType().ToString();

                // Add boolean properties only if they deviate from expected defaults
                AddIfFalse("isEnabled", peer.IsEnabled());
                AddIfTrue("isKeyboardFocusable", peer.IsKeyboardFocusable());
                AddIfTrue("hasKeyboardFocus", peer.HasKeyboardFocus());
                AddIfTrue("isOffscreen", peer.IsOffscreen());
                AddIfTrue("isRequiredForForm", peer.IsRequiredForForm());
                AddIfTrue("isPassword", peer.IsPassword());

                // Add orientation only if it's not the default "None"
                var orientation = peer.GetOrientation();
                if (orientation != AutomationOrientation.None)
                    peerInfo["orientation"] = orientation.ToString();

                peerInfo["supportedActions"] = supportedActions;

                // Only return the peer info if it contains meaningful data beyond just controlType
                return peerInfo.Count > 1 ? peerInfo : null;
            }
            catch (Exception ex)
            {
                return new
                {
                    error = $"Failed to get automation peer info: {ex.Message}"
                };
            }

            List<string> GetSupportedActions(AutomationPeer peer)
            {
                // Get available automation actions (only for patterns supported by ExecuteInvokeAutomationPeerCommand)
                var supportedActions = new List<string>();

                // Only check patterns that are supported by ExecuteInvokeAutomationPeerCommand
                var supportedPatternTypes = new[]
                {
                    PatternInterface.Invoke,
                    PatternInterface.Value,
                    PatternInterface.SelectionItem,
                    PatternInterface.Toggle,
                    PatternInterface.ExpandCollapse,
                    PatternInterface.RangeValue,
                    PatternInterface.Scroll
                };

                foreach (var patternType in supportedPatternTypes)
                {
                    try
                    {
                        if (peer.GetPattern(patternType) != null)
                        {
                            // Add specific actions for each supported pattern
                            switch (patternType)
                            {
                                case PatternInterface.Invoke:
                                    supportedActions.Add(AutomationActions.Invoke_Invoke);
                                    break;

                                case PatternInterface.Value:
                                    supportedActions.Add(AutomationActions.Value_Get);
                                    supportedActions.Add(AutomationActions.Value_Set);
                                    break;

                                case PatternInterface.SelectionItem:
                                    supportedActions.Add(AutomationActions.SelectionItem_Select);
                                    supportedActions.Add(AutomationActions.SelectionItem_AddToSelection);
                                    supportedActions.Add(AutomationActions.SelectionItem_RemoveFromSelection);
                                    supportedActions.Add(AutomationActions.SelectionItem_Status);
                                    break;

                                case PatternInterface.Toggle:
                                    supportedActions.Add(AutomationActions.Toggle_Toggle);
                                    supportedActions.Add(AutomationActions.Toggle_Status);
                                    break;

                                case PatternInterface.ExpandCollapse:
                                    supportedActions.Add(AutomationActions.ExpandCollapse_Expand);
                                    supportedActions.Add(AutomationActions.ExpandCollapse_Collapse);
                                    supportedActions.Add(AutomationActions.ExpandCollapse_Toggle);
                                    supportedActions.Add(AutomationActions.ExpandCollapse_Status);
                                    break;

                                case PatternInterface.RangeValue:
                                    supportedActions.Add(AutomationActions.RangeValue_Get);
                                    supportedActions.Add(AutomationActions.RangeValue_Set);
                                    break;


                                case PatternInterface.Scroll:
                                    supportedActions.Add(AutomationActions.Scroll_Status);
                                    supportedActions.Add(AutomationActions.Scroll_Scroll);
                                    supportedActions.Add(AutomationActions.Scroll_SetPosition);
                                    break;
                            }
                        }
                    }
                    catch
                    {
                        // Ignore patterns that can't be retrieved
                    }
                }

                return supportedActions;
            }
        }

        /// <summary>
        /// Executes an automation peer command using the new action format
        /// </summary>
        public static object ExecuteInvokeAutomationPeerCommand(object targetObject, JsonElement commandData)
        {
            try
            {
                if (targetObject is not UIElement element)
                    return new { success = false, error = "Target object is not a UI element" };

                if (!commandData.TryGetProperty("action", out var actionElement))
                {
                    return new { success = false, error = "Missing 'action' parameter for INVOKE_AUTOMATION_PEER command" };
                }

                var actionName = actionElement.GetString();
                if (string.IsNullOrWhiteSpace(actionName))
                {
                    return new { success = false, error = "Action name cannot be empty" };
                }

                var peer = UIElementAutomationPeer.CreatePeerForElement(element);
                if (peer == null)
                    return new { success = false, error = "Cannot create automation peer for element" };

                var isOffscreen = peer.IsOffscreen();
                if(isOffscreen)
                    return new { success = false, error = "Cannot execute automation peer for element that are isOffscreen=true. Please bring element on screen first." };

                // Execute action directly based on action name
                return ExecuteAction(actionName, peer, element, commandData);
            }
            catch (Exception ex)
            {
                return new { success = false, error = $"Error invoking automation peer: {ex.Message}" };
            }
        }

        private static object ExecuteAction(string actionName, AutomationPeer peer, UIElement element, JsonElement commandData)
        {
            return actionName switch
            {
                // Invoke Pattern
                AutomationActions.Invoke_Invoke => ExecuteInvokeAction(peer, element),
                
                // Value Pattern
                AutomationActions.Value_Get => ExecuteValueGetAction(peer, element),
                AutomationActions.Value_Set => ExecuteValueSetAction(peer, element, commandData),
                
                // SelectionItem Pattern
                AutomationActions.SelectionItem_Select => ExecuteSelectionItemSelectAction(peer, element),
                AutomationActions.SelectionItem_AddToSelection => ExecuteSelectionItemAddToSelectionAction(peer, element),
                AutomationActions.SelectionItem_RemoveFromSelection => ExecuteSelectionItemRemoveFromSelectionAction(peer, element),
                AutomationActions.SelectionItem_Status => ExecuteSelectionItemStatusAction(peer, element),
                
                // Toggle Pattern
                AutomationActions.Toggle_Toggle => ExecuteToggleAction(peer, element),
                AutomationActions.Toggle_Status => ExecuteToggleStatusAction(peer, element),
                
                // ExpandCollapse Pattern
                AutomationActions.ExpandCollapse_Expand => ExecuteExpandCollapseExpandAction(peer, element),
                AutomationActions.ExpandCollapse_Collapse => ExecuteExpandCollapseCollapseAction(peer, element),
                AutomationActions.ExpandCollapse_Toggle => ExecuteExpandCollapseToggleAction(peer, element),
                AutomationActions.ExpandCollapse_Status => ExecuteExpandCollapseStatusAction(peer, element),
                
                // RangeValue Pattern
                AutomationActions.RangeValue_Get => ExecuteRangeValueGetAction(peer, element),
                AutomationActions.RangeValue_Set => ExecuteRangeValueSetAction(peer, element, commandData),
                
                // Scroll Pattern
                AutomationActions.Scroll_Status => ExecuteScrollStatusAction(peer, element),
                AutomationActions.Scroll_Scroll => ExecuteScrollScrollAction(peer, element, commandData),
                AutomationActions.Scroll_SetPosition => ExecuteScrollSetPositionAction(peer, element, commandData),
                
                
                _ => new { success = false, error = $"Unknown action: {actionName}. Use GetAutomationPeerInfo to see supported actions." }
            };
        }

        // Invoke Pattern Actions
        private static object ExecuteInvokeAction(AutomationPeer peer, UIElement element)
        {
            try
            {
                var provider = peer.GetPattern(PatternInterface.Invoke) as IInvokeProvider;
                if (provider == null)
                {
                    return new { success = false, error = $"Invoke pattern not supported by {element.GetType().Name}" };
                }
                
                provider.Invoke();
                return new { success = true, message = $"{element.GetType().Name} invoked successfully via Invoke pattern" };
            }
            catch (Exception ex)
            {
                return new { success = false, error = $"Error invoking: {ex.Message}" };
            }
        }

        // Value Pattern Actions
        private static object ExecuteValueGetAction(AutomationPeer peer, UIElement element)
        {
            try
            {
                var provider = peer.GetPattern(PatternInterface.Value) as IValueProvider;
                if (provider == null)
                    return new { success = false, error = $"Value pattern not supported by {element.GetType().Name}" };
                
                return new { success = true, value = provider.Value, isReadOnly = provider.IsReadOnly };
            }
            catch (Exception ex)
            {
                return new { success = false, error = $"Error with Value pattern: {ex.Message}" };
            }
        }
        
        private static object ExecuteValueSetAction(AutomationPeer peer, UIElement element, JsonElement commandData)
        {
            try
            {
                var provider = peer.GetPattern(PatternInterface.Value) as IValueProvider;
                if (provider == null)
                    return new { success = false, error = $"Value pattern not supported by {element.GetType().Name}" };
                
                if (!commandData.TryGetProperty("value", out var valueElement))
                    return new { success = false, error = "Missing 'value' parameter for set action" };
                
                var value = valueElement.GetString() ?? "";
                provider.SetValue(value);
                return new { success = true, message = $"Value set to '{value}' on {element.GetType().Name}" };
            }
            catch (Exception ex)
            {
                return new { success = false, error = $"Error with Value pattern: {ex.Message}" };
            }
        }

        // SelectionItem Pattern Actions
        private static object ExecuteSelectionItemSelectAction(AutomationPeer peer, UIElement element)
        {
            try
            {
                var provider = peer.GetPattern(PatternInterface.SelectionItem) as ISelectionItemProvider ?? peer.EventsSource?.GetPattern(PatternInterface.SelectionItem) as ISelectionItemProvider;

                if (provider == null)
                    return new { success = false, error = $"SelectionItem pattern not supported by {element.GetType().Name}" };
                
                provider.Select();
                return new { success = true, message = $"{element.GetType().Name} selected via SelectionItem pattern" };
            }
            catch (Exception ex)
            {
                return new { success = false, error = $"Error with SelectionItem pattern: {ex.Message}" };
            }
        }
        
        private static object ExecuteSelectionItemAddToSelectionAction(AutomationPeer peer, UIElement element)
        {
            try
            {
                var provider = peer.GetPattern(PatternInterface.SelectionItem) as ISelectionItemProvider ?? peer.EventsSource?.GetPattern(PatternInterface.SelectionItem) as ISelectionItemProvider;
                if (provider == null)
                    return new { success = false, error = $"SelectionItem pattern not supported by {element.GetType().Name}" };
                
                provider.AddToSelection();
                return new { success = true, message = $"{element.GetType().Name} added to selection via SelectionItem pattern" };
            }
            catch (Exception ex)
            {
                return new { success = false, error = $"Error with SelectionItem pattern: {ex.Message}" };
            }
        }
        
        private static object ExecuteSelectionItemRemoveFromSelectionAction(AutomationPeer peer, UIElement element)
        {
            try
            {
                var provider = peer.GetPattern(PatternInterface.SelectionItem) as ISelectionItemProvider ?? peer.EventsSource?.GetPattern(PatternInterface.SelectionItem) as ISelectionItemProvider;
                if (provider == null)
                    return new { success = false, error = $"SelectionItem pattern not supported by {element.GetType().Name}" };
                
                provider.RemoveFromSelection();
                return new { success = true, message = $"{element.GetType().Name} removed from selection via SelectionItem pattern" };
            }
            catch (Exception ex)
            {
                return new { success = false, error = $"Error with SelectionItem pattern: {ex.Message}" };
            }
        }
        
        private static object ExecuteSelectionItemStatusAction(AutomationPeer peer, UIElement element)
        {
            try
            {
                var provider = peer.GetPattern(PatternInterface.SelectionItem) as ISelectionItemProvider ?? peer.EventsSource?.GetPattern(PatternInterface.SelectionItem) as ISelectionItemProvider;
                if (provider == null)
                    return new { success = false, error = $"SelectionItem pattern not supported by {element.GetType().Name}" };
                
                return new { success = true, isSelected = provider.IsSelected };
            }
            catch (Exception ex)
            {
                return new { success = false, error = $"Error with SelectionItem pattern: {ex.Message}" };
            }
        }

        // Toggle Pattern Actions
        private static object ExecuteToggleAction(AutomationPeer peer, UIElement element)
        {
            try
            {
                var provider = peer.GetPattern(PatternInterface.Toggle) as IToggleProvider;
                if (provider == null)
                    return new { success = false, error = $"Toggle pattern not supported by {element.GetType().Name}" };
                
                provider.Toggle();
                return new { success = true, message = $"{element.GetType().Name} toggled via Toggle pattern", newState = provider.ToggleState.ToString() };
            }
            catch (Exception ex)
            {
                return new { success = false, error = $"Error with Toggle pattern: {ex.Message}" };
            }
        }
        
        private static object ExecuteToggleStatusAction(AutomationPeer peer, UIElement element)
        {
            try
            {
                var provider = peer.GetPattern(PatternInterface.Toggle) as IToggleProvider;
                if (provider == null)
                    return new { success = false, error = $"Toggle pattern not supported by {element.GetType().Name}" };
                
                return new { success = true, toggleState = provider.ToggleState.ToString() };
            }
            catch (Exception ex)
            {
                return new { success = false, error = $"Error with Toggle pattern: {ex.Message}" };
            }
        }

        // ExpandCollapse Pattern Actions
        private static object ExecuteExpandCollapseExpandAction(AutomationPeer peer, UIElement element)
        {
            try
            {
                var provider = peer.GetPattern(PatternInterface.ExpandCollapse) as IExpandCollapseProvider;
                if (provider == null)
                    return new { success = false, error = $"ExpandCollapse pattern not supported by {element.GetType().Name}" };
                
                provider.Expand();
                return new { success = true, message = $"{element.GetType().Name} expanded via ExpandCollapse pattern" };
            }
            catch (Exception ex)
            {
                return new { success = false, error = $"Error with ExpandCollapse pattern: {ex.Message}" };
            }
        }
        
        private static object ExecuteExpandCollapseCollapseAction(AutomationPeer peer, UIElement element)
        {
            try
            {
                var provider = peer.GetPattern(PatternInterface.ExpandCollapse) as IExpandCollapseProvider;
                if (provider == null)
                    return new { success = false, error = $"ExpandCollapse pattern not supported by {element.GetType().Name}" };
                
                provider.Collapse();
                return new { success = true, message = $"{element.GetType().Name} collapsed via ExpandCollapse pattern" };
            }
            catch (Exception ex)
            {
                return new { success = false, error = $"Error with ExpandCollapse pattern: {ex.Message}" };
            }
        }
        
        private static object ExecuteExpandCollapseToggleAction(AutomationPeer peer, UIElement element)
        {
            try
            {
                var provider = peer.GetPattern(PatternInterface.ExpandCollapse) as IExpandCollapseProvider;
                if (provider == null)
                    return new { success = false, error = $"ExpandCollapse pattern not supported by {element.GetType().Name}" };
                
                if (provider.ExpandCollapseState == ExpandCollapseState.Collapsed)
                    provider.Expand();
                else if (provider.ExpandCollapseState == ExpandCollapseState.Expanded)
                    provider.Collapse();
                    
                return new { success = true, message = $"{element.GetType().Name} toggled via ExpandCollapse pattern", newState = provider.ExpandCollapseState.ToString() };
            }
            catch (Exception ex)
            {
                return new { success = false, error = $"Error with ExpandCollapse pattern: {ex.Message}" };
            }
        }
        
        private static object ExecuteExpandCollapseStatusAction(AutomationPeer peer, UIElement element)
        {
            try
            {
                var provider = peer.GetPattern(PatternInterface.ExpandCollapse) as IExpandCollapseProvider;
                if (provider == null)
                    return new { success = false, error = $"ExpandCollapse pattern not supported by {element.GetType().Name}" };
                
                return new { success = true, expandCollapseState = provider.ExpandCollapseState.ToString() };
            }
            catch (Exception ex)
            {
                return new { success = false, error = $"Error with ExpandCollapse pattern: {ex.Message}" };
            }
        }

        // RangeValue Pattern Actions
        private static object ExecuteRangeValueGetAction(AutomationPeer peer, UIElement element)
        {
            try
            {
                var provider = peer.GetPattern(PatternInterface.RangeValue) as IRangeValueProvider;
                if (provider == null)
                    return new { success = false, error = $"RangeValue pattern not supported by {element.GetType().Name}" };
                
                return new { 
                    success = true, 
                    value = provider.Value, 
                    minimum = provider.Minimum, 
                    maximum = provider.Maximum, 
                    smallChange = provider.SmallChange, 
                    largeChange = provider.LargeChange,
                    isReadOnly = provider.IsReadOnly
                };
            }
            catch (Exception ex)
            {
                return new { success = false, error = $"Error with RangeValue pattern: {ex.Message}" };
            }
        }
        
        private static object ExecuteRangeValueSetAction(AutomationPeer peer, UIElement element, JsonElement commandData)
        {
            try
            {
                var provider = peer.GetPattern(PatternInterface.RangeValue) as IRangeValueProvider;
                if (provider == null)
                    return new { success = false, error = $"RangeValue pattern not supported by {element.GetType().Name}" };
                
                if (!commandData.TryGetProperty("value", out var valueElement))
                    return new { success = false, error = "Missing 'value' parameter for set action" };
                
                var value = valueElement.GetDoubleOrStringAsDouble();
                provider.SetValue(value);
                return new { success = true, message = $"Range value set to {value} on {element.GetType().Name}" };
            }
            catch (Exception ex)
            {
                return new { success = false, error = $"Error with RangeValue pattern: {ex.Message}" };
            }
        }


        // Scroll Pattern Actions
        private static object ExecuteScrollStatusAction(AutomationPeer peer, UIElement element)
        {
            try
            {
                var provider = peer.GetPattern(PatternInterface.Scroll) as IScrollProvider;
                if (provider == null)
                    return new { success = false, error = $"Scroll pattern not supported by {element.GetType().Name}" };
                
                return new { 
                    success = true,
                    horizontalScrollPercent = provider.HorizontalScrollPercent,
                    verticalScrollPercent = provider.VerticalScrollPercent,
                    horizontalViewSize = provider.HorizontalViewSize,
                    verticalViewSize = provider.VerticalViewSize,
                    horizontallyScrollable = provider.HorizontallyScrollable,
                    verticallyScrollable = provider.VerticallyScrollable
                };
            }
            catch (Exception ex)
            {
                return new { success = false, error = $"Error with Scroll pattern: {ex.Message}" };
            }
        }
        
        private static object ExecuteScrollScrollAction(AutomationPeer peer, UIElement element, JsonElement commandData)
        {
            try
            {
                var provider = peer.GetPattern(PatternInterface.Scroll) as IScrollProvider;
                if (provider == null)
                    return new { success = false, error = $"Scroll pattern not supported by {element.GetType().Name}" };
                
                if (!commandData.TryGetProperty("horizontal", out var horizontalElement) ||
                    !commandData.TryGetProperty("vertical", out var verticalElement))
                    return new { success = false, error = "Missing 'horizontal' and 'vertical' parameters for scroll action" };
                
                if (!Enum.TryParse<ScrollAmount>(horizontalElement.GetString(), true, out var horizontalAmount) ||
                    !Enum.TryParse<ScrollAmount>(verticalElement.GetString(), true, out var verticalAmount))
                    return new { success = false, error = "Invalid 'horizontal' and 'vertical' parameters for scroll action" };
                
                provider.Scroll(horizontalAmount, verticalAmount);
                return new { success = true, message = $"Scrolled {horizontalAmount} horizontally and {verticalAmount} vertically" };
            }
            catch (Exception ex)
            {
                return new { success = false, error = $"Error with Scroll pattern: {ex.Message}" };
            }
        }
        
        private static object ExecuteScrollSetPositionAction(AutomationPeer peer, UIElement element, JsonElement commandData)
        {
            try
            {
                var provider = peer.GetPattern(PatternInterface.Scroll) as IScrollProvider;
                if (provider == null)
                    return new { success = false, error = $"Scroll pattern not supported by {element.GetType().Name}" };
                
                if (!commandData.TryGetProperty("horizontalPercent", out var hPercentElement) ||
                    !commandData.TryGetProperty("verticalPercent", out var vPercentElement))
                    return new { success = false, error = "Missing 'horizontalPercent' and 'verticalPercent' parameters for setposition action" };
                
                var hPercent = hPercentElement.GetDouble();
                var vPercent = vPercentElement.GetDouble();
                provider.SetScrollPercent(hPercent, vPercent);
                return new { success = true, message = $"Scroll position set to {hPercent}% horizontal, {vPercent}% vertical" };
            }
            catch (Exception ex)
            {
                return new { success = false, error = $"Error with Scroll pattern: {ex.Message}" };
            }
        }
    }
}