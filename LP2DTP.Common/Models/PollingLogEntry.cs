using System;

namespace LP2DTP.Common.Models
{
    /// <summary>
    /// Polling log entry for displaying in UI
    /// </summary>
    public class PollingLogEntry
    {
        /// <summary>
        /// Timestamp of the log entry
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.Now;

        /// <summary>
        /// Log level (INFO, WARNING, ERROR)
        /// </summary>
        public string Level { get; set; } = "INFO";

        /// <summary>
        /// Component that generated the log (VISA, Modbus, Manager, Base)
        /// </summary>
        public string Component { get; set; } = string.Empty;

        /// <summary>
        /// Machine/Device name
        /// </summary>
        public string? MachineName { get; set; }

        /// <summary>
        /// Log message
        /// </summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// Formatted timestamp for display
        /// </summary>
        public string FormattedTimestamp => Timestamp.ToString("HH:mm:ss.fff");

        /// <summary>
        /// Formatted log line for display
        /// </summary>
        public string FormattedLog => $"[{FormattedTimestamp}] [{Level:5}] [{Component:6}] {MachineName ?? "-":20} | {Message}";

        /// <summary>
        /// String representation for ListView display
        /// </summary>
        public override string ToString()
        {
            return FormattedLog;
        }
    }
}
