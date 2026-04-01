using System;

namespace LP2DTP.Common.Models
{
    public sealed class ServiceHealthSnapshot
    {
        public string ServiceName { get; set; } = string.Empty;
        public string State { get; set; } = "Unknown";
        public DateTime? StartedAtUtc { get; set; }
        public DateTime? LastHeartbeatUtc { get; set; }
        public DateTime? InitialCycleCompletedAtUtc { get; set; }
        public DateTime? LastSuccessfulPollUtc { get; set; }
        public string? LastSuccessfulMachineName { get; set; }
        public string? LastSuccessfulUnitName { get; set; }
        public string? LastSuccessfulIpAddress { get; set; }
        public DateTime? LastErrorUtc { get; set; }
        public string? LastErrorMessage { get; set; }
        public string? LastErrorMachineName { get; set; }
        public string? LastErrorUnitName { get; set; }
        public string? LastErrorIpAddress { get; set; }
        public int ConsecutiveErrorCount { get; set; }
        public int ConsecutiveSqlWriteErrorCount { get; set; }
        public int PendingSqlWriteCount { get; set; }
        public DateTime? LastSqlWriteErrorUtc { get; set; }
        public int ActiveWorkerCount { get; set; }
        public int TotalWorkerCount { get; set; }
        public int VisaItemCount { get; set; }
        public int ModbusItemCount { get; set; }
        public int PollingIntervalSeconds { get; set; }
        public int HealthCheckIntervalSeconds { get; set; }
        public int HeartbeatIntervalSeconds { get; set; }
        public DateTime? SelfRecoveryWindowStartedAtUtc { get; set; }
        public int SelfRecoveryAttemptCount { get; set; }
        public DateTime? LastSelfRecoveryTriggeredAtUtc { get; set; }
        public DateTime? SelfRecoverySuppressedUntilUtc { get; set; }
        public DateTime UpdatedAtUtc { get; set; }

        public ServiceHealthSnapshot Clone()
        {
            return new ServiceHealthSnapshot
            {
                ServiceName = ServiceName,
                State = State,
                StartedAtUtc = StartedAtUtc,
                LastHeartbeatUtc = LastHeartbeatUtc,
                InitialCycleCompletedAtUtc = InitialCycleCompletedAtUtc,
                LastSuccessfulPollUtc = LastSuccessfulPollUtc,
                LastSuccessfulMachineName = LastSuccessfulMachineName,
                LastSuccessfulUnitName = LastSuccessfulUnitName,
                LastSuccessfulIpAddress = LastSuccessfulIpAddress,
                LastErrorUtc = LastErrorUtc,
                LastErrorMessage = LastErrorMessage,
                LastErrorMachineName = LastErrorMachineName,
                LastErrorUnitName = LastErrorUnitName,
                LastErrorIpAddress = LastErrorIpAddress,
                ConsecutiveErrorCount = ConsecutiveErrorCount,
                ConsecutiveSqlWriteErrorCount = ConsecutiveSqlWriteErrorCount,
                PendingSqlWriteCount = PendingSqlWriteCount,
                LastSqlWriteErrorUtc = LastSqlWriteErrorUtc,
                ActiveWorkerCount = ActiveWorkerCount,
                TotalWorkerCount = TotalWorkerCount,
                VisaItemCount = VisaItemCount,
                ModbusItemCount = ModbusItemCount,
                PollingIntervalSeconds = PollingIntervalSeconds,
                HealthCheckIntervalSeconds = HealthCheckIntervalSeconds,
                HeartbeatIntervalSeconds = HeartbeatIntervalSeconds,
                SelfRecoveryWindowStartedAtUtc = SelfRecoveryWindowStartedAtUtc,
                SelfRecoveryAttemptCount = SelfRecoveryAttemptCount,
                LastSelfRecoveryTriggeredAtUtc = LastSelfRecoveryTriggeredAtUtc,
                SelfRecoverySuppressedUntilUtc = SelfRecoverySuppressedUntilUtc,
                UpdatedAtUtc = UpdatedAtUtc
            };
        }
    }
}
