using System;
using System.Text.Json.Serialization;

namespace Injector.Models
{
    public class WpfProcessInfo
    {
        [JsonPropertyName("processId")]
        public int ProcessId { get; set; }

        [JsonPropertyName("processName")]
        public string ProcessName { get; set; } = string.Empty;

        [JsonPropertyName("mainWindowTitle")]
        public string MainWindowTitle { get; set; } = string.Empty;

        [JsonPropertyName("fileName")]
        public string FileName { get; set; } = string.Empty;

        [JsonPropertyName("workingDirectory")]
        public string WorkingDirectory { get; set; } = string.Empty;

        [JsonPropertyName("isWpfApplication")]
        public bool IsWpfApplication { get; set; }

        [JsonPropertyName("hasMainWindow")]
        public bool HasMainWindow { get; set; }

        [JsonPropertyName("startTime")]
        public DateTime StartTime { get; set; }
    }
}
