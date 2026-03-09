namespace LP2DTP.Common.Models
{
    /// <summary>
    /// Application settings
    /// </summary>
    public class AppSettings
    {
        /// <summary>
        /// Polling interval in milliseconds
        /// </summary>
        public int PollingIntervalMs { get; set; } = 1000;

        /// <summary>
        /// Health-check interval in milliseconds
        /// </summary>
        public int HealthCheckIntervalMs { get; set; } = 5000;

        /// <summary>
        /// Default values
        /// </summary>
        public static AppSettings Default => new AppSettings
        {
            PollingIntervalMs = 1000,
            HealthCheckIntervalMs = 5000
        };
    }
}
