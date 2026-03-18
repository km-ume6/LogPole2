using System;
using System.Text.Json.Serialization;

namespace LP2DTP.Common.Models
{
    /// <summary>
    /// Application settings
    /// </summary>
    public class AppSettings
    {
        /// <summary>
        /// Polling interval in seconds
        /// </summary>
        public int PollingIntervalSeconds { get; set; } = 1;

        /// <summary>
        /// Health-check interval in seconds
        /// </summary>
        public int HealthCheckIntervalSeconds { get; set; } = 5;

        /// <summary>
        /// Whether Polling Test writes acquired data to the database.
        /// </summary>
        public bool PollingTestSqlLoggingEnabled { get; set; } = true;

        [JsonPropertyName("PollingIntervalMs")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public int LegacyPollingIntervalMs
        {
            get => 0;
            set => PollingIntervalSeconds = NormalizeFromLegacyMilliseconds(value, PollingIntervalSeconds);
        }

        [JsonPropertyName("HealthCheckIntervalMs")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public int LegacyHealthCheckIntervalMs
        {
            get => 0;
            set => HealthCheckIntervalSeconds = NormalizeFromLegacyMilliseconds(value, HealthCheckIntervalSeconds);
        }

        /// <summary>
        /// Default values
        /// </summary>
        public static AppSettings Default => new AppSettings
        {
            PollingIntervalSeconds = 1,
            HealthCheckIntervalSeconds = 5,
            PollingTestSqlLoggingEnabled = true
        };

        public void Normalize()
        {
            PollingIntervalSeconds = Math.Clamp(PollingIntervalSeconds, 1, 3600);
            HealthCheckIntervalSeconds = Math.Clamp(HealthCheckIntervalSeconds, 1, 3600);
        }

        private static int NormalizeFromLegacyMilliseconds(int legacyMilliseconds, int fallback)
        {
            if (legacyMilliseconds <= 0)
            {
                return fallback;
            }

            var seconds = Math.Max(1, (int)Math.Round(legacyMilliseconds / 1000d));
            return Math.Clamp(seconds, 1, 3600);
        }
    }
}
