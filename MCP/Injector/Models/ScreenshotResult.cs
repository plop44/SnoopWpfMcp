using System.Text.Json.Serialization;

namespace Injector.Models
{
    public class ScreenshotResult
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }

        [JsonPropertyName("processId")]
        public int ProcessId { get; set; }

        [JsonPropertyName("processName")]
        public string? ProcessName { get; set; }

        [JsonPropertyName("message")]
        public string Message { get; set; } = string.Empty;

        [JsonPropertyName("windowTitle")]
        public string? WindowTitle { get; set; }

        [JsonPropertyName("width")]
        public int Width { get; set; }

        [JsonPropertyName("height")]
        public int Height { get; set; }

        [JsonPropertyName("imageData")]
        public string? ImageData { get; set; }

        [JsonPropertyName("format")]
        public string Format { get; set; } = "PNG";

        [JsonPropertyName("error")]
        public string? Error { get; set; }

        [JsonPropertyName("wasInjected")]
        public bool WasInjected { get; set; }
    }
}
