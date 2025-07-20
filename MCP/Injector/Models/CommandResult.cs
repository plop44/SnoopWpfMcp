using System.Text.Json.Serialization;

namespace Injector.Models
{
    public class CommandResult
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }

        [JsonPropertyName("processId")]
        public int ProcessId { get; set; }

        [JsonPropertyName("processName")]
        public string? ProcessName { get; set; }

        [JsonPropertyName("message")]
        public string Message { get; set; } = string.Empty;

        [JsonPropertyName("elementType")]
        public string ElementType { get; set; } = string.Empty;

        [JsonPropertyName("hashcode")]
        public int Hashcode { get; set; }

        [JsonPropertyName("command")]
        public string Command { get; set; } = string.Empty;

        [JsonPropertyName("result")]
        public object? Result { get; set; }

        [JsonPropertyName("error")]
        public string? Error { get; set; }

        [JsonPropertyName("wasInjected")]
        public bool WasInjected { get; set; }
    }
}
