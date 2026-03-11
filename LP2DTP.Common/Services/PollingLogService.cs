using System;
using System.Collections.ObjectModel;
using LP2DTP.Common.Models;

namespace LP2DTP.Common.Services
{
    /// <summary>
    /// Service for managing polling logs with UI integration
    /// </summary>
    public class PollingLogService
    {
        private static PollingLogService? _instance;
        private static readonly object _lockObject = new object();

        /// <summary>
        /// Collection of log entries (thread-safe, observable)
        /// </summary>
        public ObservableCollection<PollingLogEntry> Logs { get; } = new();

        /// <summary>
        /// Dispatcher queue for UI thread updates (must be set by UI code)
        /// </summary>
        public Action<Action>? DispatchToUI { get; set; }

        /// <summary>
        /// Maximum number of log entries to keep in memory
        /// </summary>
        public int MaxLogEntries { get; set; } = 1000;

        private PollingLogService()
        {
        }

        /// <summary>
        /// Get singleton instance
        /// </summary>
        public static PollingLogService Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lockObject)
                    {
                        _instance ??= new PollingLogService();
                    }
                }
                return _instance;
            }
        }

        /// <summary>
        /// Add a log entry
        /// </summary>
        public void Log(PollingLogEntry entry)
        {
            if (entry == null) return;

            var action = new Action(() =>
            {
                try
                {
                    Logs.Add(entry);

                    // Remove old entries if exceeding max
                    if (Logs.Count > MaxLogEntries)
                    {
                        Logs.RemoveAt(0);
                    }
                }
                catch
                {
                    // Ignore collection update errors
                }
            });

            // Execute on UI thread if dispatcher is set, otherwise execute directly
            if (DispatchToUI != null)
            {
                DispatchToUI(action);
            }
            else
            {
                action();
            }
        }

        /// <summary>
        /// Add a log entry with parameters
        /// </summary>
        public void Log(string level, string component, string? machineName, string message)
        {
            Log(new PollingLogEntry
            {
                Timestamp = DateTime.Now,
                Level = level,
                Component = component,
                MachineName = machineName,
                Message = message
            });
        }

        /// <summary>
        /// Clear all logs
        /// </summary>
        public void Clear()
        {
            var action = new Action(() =>
            {
                try
                {
                    Logs.Clear();
                }
                catch
                {
                    // Ignore clear errors
                }
            });

            // Execute on UI thread if dispatcher is set, otherwise execute directly
            if (DispatchToUI != null)
            {
                DispatchToUI(action);
            }
            else
            {
                action();
            }
        }

        /// <summary>
        /// Export logs as text
        /// </summary>
        public string ExportAsText()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var entry in Logs)
            {
                if (entry != null)
                {
                    sb.AppendLine(entry.FormattedLog);
                }
            }
            return sb.ToString();
        }

        /// <summary>
        /// Save logs to file
        /// </summary>
        public void SaveToFile(string filePath)
        {
            try
            {
                System.IO.File.WriteAllText(filePath, ExportAsText());
            }
            catch
            {
                // Ignore save errors
            }
        }
    }
}
