using System.Text.Json.Serialization;

namespace SnoopWpfMcpServer.Models
{
    public class ElementResult
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }

        [JsonPropertyName("processId")]
        public int ProcessId { get; set; }

        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;

        [JsonPropertyName("hashcode")]
        public int Hashcode { get; set; }

        [JsonPropertyName("message")]
        public string Message { get; set; } = string.Empty;

        [JsonPropertyName("element")]
        public object? Element { get; set; }

        [JsonPropertyName("dataContexts")]
        public object? DataContexts { get; set; }

        [JsonPropertyName("error")]
        public string? Error { get; set; }

        [JsonPropertyName("timestamp")]
        public string? Timestamp { get; set; }
    }
}