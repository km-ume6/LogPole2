using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LP2DTP.Common.Models;

namespace LP2DTP.Common.Services
{
    public sealed class ServiceHealthMonitor
    {
        public const string SnapshotFileName = "LP2SVR.health.json";
        public const int DefaultHeartbeatIntervalSeconds = 15;
        public const int MaxSelfRecoveryAttempts = 3;
        public static readonly TimeSpan SelfRecoveryAttemptWindow = TimeSpan.FromMinutes(30);
        public static readonly TimeSpan SelfRecoverySuppressionDuration = TimeSpan.FromHours(2);

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true
        };

        private readonly SemaphoreSlim _saveSemaphore = new(1, 1);
        private readonly object _snapshotLock = new();
        private ServiceHealthSnapshot _snapshot = new();

        public async Task<ServiceHealthSnapshot> LoadAsync(CancellationToken cancellationToken = default)
        {
            var filePath = GetSnapshotFilePath();
            if (!File.Exists(filePath))
            {
                return new ServiceHealthSnapshot();
            }

            try
            {
                await using var stream = File.OpenRead(filePath);
                var snapshot = await JsonSerializer.DeserializeAsync<ServiceHealthSnapshot>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
                return snapshot ?? new ServiceHealthSnapshot();
            }
            catch
            {
                return new ServiceHealthSnapshot();
            }
        }

        public ServiceHealthSnapshot GetCurrentSnapshot()
        {
            lock (_snapshotLock)
            {
                return _snapshot.Clone();
            }
        }

        public async Task MarkStartingAsync(string serviceName, CancellationToken cancellationToken = default)
        {
            var nowUtc = DateTime.UtcNow;
            var existingSnapshot = await LoadAsync(cancellationToken).ConfigureAwait(false);

            lock (_snapshotLock)
            {
                _snapshot = new ServiceHealthSnapshot
                {
                    ServiceName = serviceName,
                    State = "Starting",
                    StartedAtUtc = nowUtc,
                    LastHeartbeatUtc = nowUtc,
                    LastSuccessfulPollUtc = existingSnapshot.LastSuccessfulPollUtc,
                    LastSuccessfulMachineName = existingSnapshot.LastSuccessfulMachineName,
                    LastSuccessfulUnitName = existingSnapshot.LastSuccessfulUnitName,
                    LastSuccessfulIpAddress = existingSnapshot.LastSuccessfulIpAddress,
                    LastErrorUtc = existingSnapshot.LastErrorUtc,
                    LastErrorMessage = existingSnapshot.LastErrorMessage,
                    LastErrorMachineName = existingSnapshot.LastErrorMachineName,
                    LastErrorUnitName = existingSnapshot.LastErrorUnitName,
                    LastErrorIpAddress = existingSnapshot.LastErrorIpAddress,
                    ConsecutiveErrorCount = existingSnapshot.ConsecutiveErrorCount,
                    ConsecutiveSqlWriteErrorCount = 0,
                    PollingIntervalSeconds = existingSnapshot.PollingIntervalSeconds,
                    HealthCheckIntervalSeconds = existingSnapshot.HealthCheckIntervalSeconds,
                    HeartbeatIntervalSeconds = DefaultHeartbeatIntervalSeconds,
                    SelfRecoveryWindowStartedAtUtc = existingSnapshot.SelfRecoveryWindowStartedAtUtc,
                    SelfRecoveryAttemptCount = existingSnapshot.SelfRecoveryAttemptCount,
                    LastSelfRecoveryTriggeredAtUtc = existingSnapshot.LastSelfRecoveryTriggeredAtUtc,
                    SelfRecoverySuppressedUntilUtc = existingSnapshot.SelfRecoverySuppressedUntilUtc,
                    UpdatedAtUtc = nowUtc
                };
            }

            await PersistAsync(cancellationToken).ConfigureAwait(false);
        }

        public async Task ApplyConfigurationAsync(
            string serviceName,
            int pollingIntervalSeconds,
            int healthCheckIntervalSeconds,
            int visaItemCount,
            int modbusItemCount,
            int activeWorkerCount,
            int totalWorkerCount,
            CancellationToken cancellationToken = default)
        {
            lock (_snapshotLock)
            {
                _snapshot.ServiceName = serviceName;
                _snapshot.PollingIntervalSeconds = Math.Clamp(pollingIntervalSeconds, 1, 3600);
                _snapshot.HealthCheckIntervalSeconds = Math.Clamp(healthCheckIntervalSeconds, 1, 3600);
                _snapshot.VisaItemCount = Math.Max(0, visaItemCount);
                _snapshot.ModbusItemCount = Math.Max(0, modbusItemCount);
                _snapshot.ActiveWorkerCount = Math.Max(0, activeWorkerCount);
                _snapshot.TotalWorkerCount = Math.Max(0, totalWorkerCount);
                _snapshot.UpdatedAtUtc = DateTime.UtcNow;
            }

            await PersistAsync(cancellationToken).ConfigureAwait(false);
        }

        public async Task MarkRunningAsync(
            string serviceName,
            int activeWorkerCount,
            int totalWorkerCount,
            DateTime? initialCycleCompletedAtUtc,
            int consecutiveSqlWriteErrorCount,
            DateTime? lastSqlWriteErrorUtc,
            CancellationToken cancellationToken = default)
        {
            var nowUtc = DateTime.UtcNow;
            lock (_snapshotLock)
            {
                _snapshot.ServiceName = serviceName;
                _snapshot.State = "Running";
                _snapshot.LastHeartbeatUtc = nowUtc;
                _snapshot.InitialCycleCompletedAtUtc = initialCycleCompletedAtUtc;
                _snapshot.ConsecutiveSqlWriteErrorCount = Math.Max(0, consecutiveSqlWriteErrorCount);
                _snapshot.LastSqlWriteErrorUtc = lastSqlWriteErrorUtc;
                _snapshot.ActiveWorkerCount = Math.Max(0, activeWorkerCount);
                _snapshot.TotalWorkerCount = Math.Max(0, totalWorkerCount);
                _snapshot.UpdatedAtUtc = nowUtc;
            }

            await PersistAsync(cancellationToken).ConfigureAwait(false);
        }

        public async Task UpdateHeartbeatAsync(
            string serviceName,
            int activeWorkerCount,
            int totalWorkerCount,
            DateTime? initialCycleCompletedAtUtc,
            int consecutiveSqlWriteErrorCount,
            DateTime? lastSqlWriteErrorUtc,
            CancellationToken cancellationToken = default)
        {
            var nowUtc = DateTime.UtcNow;
            lock (_snapshotLock)
            {
                _snapshot.ServiceName = serviceName;
                _snapshot.State = "Running";
                _snapshot.LastHeartbeatUtc = nowUtc;
                _snapshot.InitialCycleCompletedAtUtc = initialCycleCompletedAtUtc;
                _snapshot.ConsecutiveSqlWriteErrorCount = Math.Max(0, consecutiveSqlWriteErrorCount);
                _snapshot.LastSqlWriteErrorUtc = lastSqlWriteErrorUtc;
                _snapshot.ActiveWorkerCount = Math.Max(0, activeWorkerCount);
                _snapshot.TotalWorkerCount = Math.Max(0, totalWorkerCount);
                _snapshot.UpdatedAtUtc = nowUtc;
            }

            await PersistAsync(cancellationToken).ConfigureAwait(false);
        }

        public void RecordPollSuccess(PollingDataReceivedEventArgs args, int activeWorkerCount, int totalWorkerCount)
        {
            if (args == null)
            {
                return;
            }

            lock (_snapshotLock)
            {
                _snapshot.State = "Running";
                _snapshot.LastSuccessfulPollUtc = args.Timestamp.Kind == DateTimeKind.Utc
                    ? args.Timestamp
                    : args.Timestamp.ToUniversalTime();
                _snapshot.LastSuccessfulMachineName = NullIfWhiteSpace(args.MachineName);
                _snapshot.LastSuccessfulUnitName = NullIfWhiteSpace(args.UnitName);
                _snapshot.LastSuccessfulIpAddress = NullIfWhiteSpace(args.IpAddress);
                _snapshot.ActiveWorkerCount = Math.Max(0, activeWorkerCount);
                _snapshot.TotalWorkerCount = Math.Max(0, totalWorkerCount);
                _snapshot.ConsecutiveErrorCount = 0;
                _snapshot.SelfRecoveryWindowStartedAtUtc = null;
                _snapshot.SelfRecoveryAttemptCount = 0;
                _snapshot.LastSelfRecoveryTriggeredAtUtc = null;
                _snapshot.SelfRecoverySuppressedUntilUtc = null;
                _snapshot.UpdatedAtUtc = DateTime.UtcNow;
            }
        }

        public void RecordError(PollingErrorEventArgs args, int activeWorkerCount, int totalWorkerCount)
        {
            if (args == null)
            {
                return;
            }

            lock (_snapshotLock)
            {
                _snapshot.LastErrorUtc = args.Timestamp.Kind == DateTimeKind.Utc
                    ? args.Timestamp
                    : args.Timestamp.ToUniversalTime();
                _snapshot.LastErrorMessage = NullIfWhiteSpace(args.ErrorMessage) ?? args.Exception?.Message;
                _snapshot.LastErrorMachineName = NullIfWhiteSpace(args.MachineName);
                _snapshot.LastErrorUnitName = NullIfWhiteSpace(args.UnitName);
                _snapshot.LastErrorIpAddress = NullIfWhiteSpace(args.IpAddress);
                _snapshot.ActiveWorkerCount = Math.Max(0, activeWorkerCount);
                _snapshot.TotalWorkerCount = Math.Max(0, totalWorkerCount);
                _snapshot.ConsecutiveErrorCount++;
                _snapshot.UpdatedAtUtc = DateTime.UtcNow;
            }
        }

        public async Task MarkStoppedAsync(string serviceName, CancellationToken cancellationToken = default)
        {
            var nowUtc = DateTime.UtcNow;
            lock (_snapshotLock)
            {
                _snapshot.ServiceName = serviceName;
                _snapshot.State = "Stopped";
                _snapshot.LastHeartbeatUtc = nowUtc;
                _snapshot.ActiveWorkerCount = 0;
                _snapshot.UpdatedAtUtc = nowUtc;
            }

            await PersistAsync(cancellationToken).ConfigureAwait(false);
        }

        public async Task MarkFaultedAsync(string serviceName, Exception exception, CancellationToken cancellationToken = default)
        {
            var nowUtc = DateTime.UtcNow;
            lock (_snapshotLock)
            {
                _snapshot.ServiceName = serviceName;
                _snapshot.State = "Faulted";
                _snapshot.LastHeartbeatUtc = nowUtc;
                _snapshot.LastErrorUtc = nowUtc;
                _snapshot.LastErrorMessage = exception.Message;
                _snapshot.ConsecutiveErrorCount++;
                _snapshot.UpdatedAtUtc = nowUtc;
            }

            await PersistAsync(cancellationToken).ConfigureAwait(false);
        }

        public async Task<(bool ShouldRestart, int AttemptCount, DateTime? SuppressedUntilUtc)> TryBeginSelfRecoveryAsync(
            string serviceName,
            string reason,
            CancellationToken cancellationToken = default)
        {
            var nowUtc = DateTime.UtcNow;

            lock (_snapshotLock)
            {
                _snapshot.ServiceName = serviceName;

                if (_snapshot.SelfRecoverySuppressedUntilUtc.HasValue
                    && _snapshot.SelfRecoverySuppressedUntilUtc.Value > nowUtc)
                {
                    _snapshot.LastErrorUtc = nowUtc;
                    _snapshot.LastErrorMessage = reason;
                    _snapshot.UpdatedAtUtc = nowUtc;
                }
                else
                {
                    if (!_snapshot.SelfRecoveryWindowStartedAtUtc.HasValue
                        || nowUtc - _snapshot.SelfRecoveryWindowStartedAtUtc.Value > SelfRecoveryAttemptWindow)
                    {
                        _snapshot.SelfRecoveryWindowStartedAtUtc = nowUtc;
                        _snapshot.SelfRecoveryAttemptCount = 0;
                    }

                    _snapshot.SelfRecoveryAttemptCount++;
                    _snapshot.LastSelfRecoveryTriggeredAtUtc = nowUtc;
                    _snapshot.LastErrorUtc = nowUtc;
                    _snapshot.LastErrorMessage = reason;
                    _snapshot.UpdatedAtUtc = nowUtc;

                    if (_snapshot.SelfRecoveryAttemptCount > MaxSelfRecoveryAttempts)
                    {
                        _snapshot.SelfRecoverySuppressedUntilUtc = nowUtc.Add(SelfRecoverySuppressionDuration);
                    }
                }
            }

            await PersistAsync(cancellationToken).ConfigureAwait(false);

            lock (_snapshotLock)
            {
                var shouldRestart = !_snapshot.SelfRecoverySuppressedUntilUtc.HasValue
                    || _snapshot.SelfRecoverySuppressedUntilUtc.Value <= nowUtc;
                return (shouldRestart, _snapshot.SelfRecoveryAttemptCount, _snapshot.SelfRecoverySuppressedUntilUtc);
            }
        }

        private async Task PersistAsync(CancellationToken cancellationToken)
        {
            ServiceHealthSnapshot snapshot;
            lock (_snapshotLock)
            {
                snapshot = _snapshot.Clone();
            }

            ApplicationDataPath.EnsureDirectoryExists();
            var filePath = GetSnapshotFilePath();
            var tempFilePath = filePath + ".tmp";
            var json = JsonSerializer.Serialize(snapshot, JsonOptions);

            await _saveSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await File.WriteAllTextAsync(tempFilePath, json, cancellationToken).ConfigureAwait(false);
                File.Move(tempFilePath, filePath, true);
            }
            finally
            {
                try
                {
                    if (File.Exists(tempFilePath))
                    {
                        File.Delete(tempFilePath);
                    }
                }
                catch
                {
                }

                _saveSemaphore.Release();
            }
        }

        private static string GetSnapshotFilePath()
        {
            ApplicationDataPath.MigrateLegacyFileIfNeeded(SnapshotFileName);
            return ApplicationDataPath.GetFilePath(SnapshotFileName);
        }

        private static string? NullIfWhiteSpace(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
    }
}
