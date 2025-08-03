#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace WpfInspector
{
    public class DataContextTracker
    {
        private readonly Dictionary<int, object> _dataContexts = new();
        private readonly Dictionary<object, string> _dataContextIds = new();

        /// <summary>
        /// Registers a DataContext and returns its unique ID, or returns existing ID if already registered.
        /// For simple types (string, primitives), returns null to indicate they should be inlined.
        /// </summary>
        public string? RegisterDataContext(object? dataContext)
        {
            if (dataContext == null)
                return null;

            // Check if we already have this DataContext registered
            if (_dataContextIds.TryGetValue(dataContext, out var existingId))
                return existingId;

            // Create new ID
            var hashCode = dataContext.GetHashCode();
            var id = $"dc_{hashCode}";

            // Handle hash code collisions
            var counter = 1;
            var originalId = id;
            while (_dataContexts.ContainsKey(hashCode))
            {
                hashCode = dataContext.GetHashCode() + counter;
                id = $"{originalId}_{counter}";
                counter++;
            }

            // Register the DataContext
            _dataContexts[hashCode] = CreateDataContextInfo(dataContext);
            _dataContextIds[dataContext] = id;

            return id;
        }

        /// <summary>
        /// Gets all registered DataContexts for the final JSON output
        /// </summary>
        public Dictionary<string, object> GetDataContexts()
        {
            var result = new Dictionary<string, object>();
            
            foreach (var kvp in _dataContextIds)
            {
                var dataContext = kvp.Key;
                var id = kvp.Value;
                result[id] = CreateDataContextInfo(dataContext);
            }

            return result;
        }

        private static object CreateDataContextInfo(object dataContext)
        {
            var info = new Dictionary<string, object>
            {
                ["type"] = dataContext.GetType().FullName ?? dataContext.GetType().Name,
                ["hashCode"] = dataContext.GetHashCode()
            };

            // Get all properties via reflection
            var properties = new Dictionary<string, object>();
            var type = dataContext.GetType();

            try
            {
                var propertyInfos = type.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                
                foreach (var propertyInfo in propertyInfos)
                {
                    try
                    {
                        // Skip indexed properties
                        if (propertyInfo.GetIndexParameters().Length > 0)
                            continue;

                        var propertyData = new Dictionary<string, object>();
                        
                        // Check if property is readonly
                        propertyData["isReadOnly"] = !propertyInfo.CanWrite || propertyInfo.SetMethod == null || !propertyInfo.SetMethod.IsPublic;
                        
                        // Get property value
                        if (propertyInfo.CanRead && propertyInfo.GetMethod != null && propertyInfo.GetMethod.IsPublic)
                        {
                            try
                            {
                                var value = propertyInfo.GetValue(dataContext);
                                propertyData["value"] = SerializeDataContextPropertyValue(value);
                            }
                            catch (Exception ex)
                            {
                                propertyData["error"] = ex.Message;
                            }
                        }
                        else
                        {
                            propertyData["error"] = "Property is not readable";
                        }

                        // Add property type information
                        propertyData["propertyType"] = propertyInfo.PropertyType.Name;
                        if (propertyInfo.PropertyType.IsGenericType)
                        {
                            propertyData["propertyType"] = propertyInfo.PropertyType.GetGenericTypeDefinition().Name.Replace("`1", "") + 
                                                          "<" + string.Join(", ", propertyInfo.PropertyType.GetGenericArguments().Select(t => t.Name)) + ">";
                        }

                        properties[propertyInfo.Name] = propertyData;
                    }
                    catch (Exception ex)
                    {
                        // If we can't process this property, add error info
                        properties[propertyInfo.Name] = new Dictionary<string, object>
                        {
                            ["error"] = ex.Message,
                            ["isReadOnly"] = true
                        };
                    }
                }

                if (properties.Count > 0)
                {
                    info["properties"] = properties;
                }
            }
            catch (Exception ex)
            {
                info["reflectionError"] = ex.Message;
            }

            // Also add ToString() for basic representation
            try
            {
                info["toString"] = dataContext.ToString() ?? "";
            }
            catch
            {
                // Ignore if ToString() fails
            }

            return info;
        }

        private static object? SerializeDataContextPropertyValue(object value)
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
                    // Don't include complex objects in DataContext to avoid circular references
                    _ when value.GetType().IsPrimitive || value.GetType().IsValueType => value,
                    _ => value.GetType().Name
                };
            }
            catch
            {
                return value?.GetType().Name;
            }
        }
    }
}