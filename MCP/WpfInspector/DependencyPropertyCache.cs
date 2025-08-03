#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Windows;

namespace WpfInspector
{
    public static class DependencyPropertyCache
    {
        private static readonly ConcurrentDictionary<Type, List<DependencyProperty>> _cache = new();

        /// <summary>
        /// Gets all dependency properties for a type, ordered from most specific type to base classes
        /// </summary>
        public static List<DependencyProperty> GetDependencyProperties(Type type)
        {
            return _cache.GetOrAdd(type, BuildDependencyPropertyList);
        }

        private static List<DependencyProperty> BuildDependencyPropertyList(Type type)
        {
            var properties = new List<DependencyProperty>();
            var processedTypes = new HashSet<Type>();

            // Build inheritance hierarchy from most specific to most general
            var typeHierarchy = new List<Type>();
            var currentType = type;
            while (currentType != null && typeof(DependencyObject).IsAssignableFrom(currentType))
            {
                typeHierarchy.Add(currentType);
                currentType = currentType.BaseType;
            }

            // Process types from most specific to most general
            foreach (var hierarchyType in typeHierarchy)
            {
                if (processedTypes.Add(hierarchyType))
                {
                    var typeProperties = GetDependencyPropertiesForType(hierarchyType);
                    properties.AddRange(typeProperties);
                }
            }

            return properties;
        }

        private static List<DependencyProperty> GetDependencyPropertiesForType(Type type)
        {
            var properties = new List<DependencyProperty>();

            // Get all public static fields that are DependencyProperty
            var fields = type.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
            
            foreach (var field in fields)
            {
                if (field.FieldType == typeof(DependencyProperty) && field.Name.EndsWith("Property"))
                {
                    if (field.GetValue(null) is DependencyProperty dp)
                    {
                        properties.Add(dp);
                    }
                }
            }

            return properties;
        }

        /// <summary>
        /// Gets the value of a dependency property for an object, returning null if it equals the default value
        /// </summary>
        public static object? GetNonDefaultValue(DependencyObject obj, DependencyProperty property)
        {
            try
            {
                var currentValue = obj.GetValue(property);
                var defaultValue = property.DefaultMetadata?.DefaultValue;

                // Compare with default value
                if (Equals(currentValue, defaultValue))
                {
                    return null; // Value is default, so we discard it
                }

                return currentValue;
            }
            catch
            {
                // If we can't get the value for any reason, return null
                return null;
            }
        }
    }
}