using System;
using System.Threading;
using System.Threading.Tasks;

namespace LP2DTP.Common.Services
{
    /// <summary>
    /// Polling worker interface
    /// </summary>
    public interface IPollingWorker : IDisposable
    {
        /// <summary>
        /// Start polling
        /// </summary>
        Task StartAsync();

        /// <summary>
        /// Stop polling
        /// </summary>
        Task StopAsync();

        /// <summary>
        /// Check if polling is running
        /// </summary>
        bool IsRunning { get; }

        /// <summary>
        /// Polling interval in milliseconds
        /// </summary>
        int PollingIntervalMs { get; set; }

        /// <summary>
        /// Health-check interval in milliseconds.
        /// </summary>
        int HealthCheckIntervalMs { get; set; }

        /// <summary>
        /// Event raised when polling data is received
        /// </summary>
        event EventHandler<PollingDataReceivedEventArgs>? DataReceived;

        /// <summary>
        /// Event raised when polling error occurs
        /// </summary>
        event EventHandler<PollingErrorEventArgs>? ErrorOccurred;

        /// <summary>
        /// Returns whether health-check should run at the specified UTC time.
        /// </summary>
        bool IsHealthCheckDue(DateTime utcNow);

        /// <summary>
        /// Returns whether polling should run at the specified UTC time.
        /// </summary>
        bool IsPollingDue(DateTime utcNow);

        /// <summary>
        /// Execute only health-check phase and update internal timing/state.
        /// </summary>
        Task<bool> ExecuteHealthCheckPhaseAsync(CancellationToken cancellationToken);

        /// <summary>
        /// Execute only polling phase and update internal timing/state.
        /// </summary>
        Task ExecutePollingPhaseAsync(CancellationToken cancellationToken);

        /// <summary>
        /// Execute one polling cycle when scheduled.
        /// </summary>
        Task ExecuteCycleAsync(CancellationToken cancellationToken);
    }

    /// <summary>
    /// Event args for polling data received
    /// </summary>
    public class PollingDataReceivedEventArgs : EventArgs
    {
        public string MachineName { get; set; } = string.Empty;
        public string UnitName { get; set; } = string.Empty;
        public string IpAddress { get; set; } = string.Empty;
        public string Command { get; set; } = string.Empty;
        public string Response { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }

    /// <summary>
    /// Event args for polling error
    /// </summary>
    public class PollingErrorEventArgs : EventArgs
    {
        public string MachineName { get; set; } = string.Empty;
        public string UnitName { get; set; } = string.Empty;
        public string IpAddress { get; set; } = string.Empty;
        public string Command { get; set; } = string.Empty;
        public Exception? Exception { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }
}
