using System.Text.Json.Serialization;

namespace Injector.Models
{
    public class ButtonClickResult
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }

        [JsonPropertyName("processId")]
        public int ProcessId { get; set; }

        [JsonPropertyName("processName")]
        public string? ProcessName { get; set; }

        [JsonPropertyName("message")]
        public string Message { get; set; } = string.Empty;

        [JsonPropertyName("buttonText")]
        public string ButtonText { get; set; } = string.Empty;

        [JsonPropertyName("buttonName")]
        public string? ButtonName { get; set; }

        [JsonPropertyName("buttonContent")]
        public string? ButtonContent { get; set; }

        [JsonPropertyName("error")]
        public string? Error { get; set; }

        [JsonPropertyName("wasInjected")]
        public bool WasInjected { get; set; }
    }
}
