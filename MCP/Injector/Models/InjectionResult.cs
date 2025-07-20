using System.Text.Json.Serialization;

namespace Injector.Models
{
    public class InjectionResult
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }

        [JsonPropertyName("processId")]
        public int ProcessId { get; set; }

        [JsonPropertyName("message")]
        public string Message { get; set; } = string.Empty;

        [JsonPropertyName("response")]
        public string? Response { get; set; }

        [JsonPropertyName("error")]
        public string? Error { get; set; }

        [JsonPropertyName("wasAlreadyInjected")]
        public bool WasAlreadyInjected { get; set; }
    }
}
