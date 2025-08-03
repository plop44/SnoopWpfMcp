#nullable enable
using System;
using System.Text.Json;

namespace WpfInspector;

public static class VariousExtensions
{
    public static double GetDoubleOrStringAsDouble(this JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Number => element.GetDouble(),
            JsonValueKind.String when double.TryParse(element.GetString(), out var result) => result,
            _ => throw new InvalidOperationException($"Cannot convert {element.ValueKind} to double")
        };
    }
}