using System;
using System.Threading;
using System.Threading.Tasks;
using LP2DTP.Common.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LP2SVR
{
    public sealed class PollingHostedService : BackgroundService
    {
        private static readonly TimeSpan MinimumSelfRecoveryGracePeriod = TimeSpan.FromMinutes(3);

        private readonly PollingWorkerManager _pollingManager;
        private readonly AppSettingsService _appSettingsService;
        private readonly VisaItemService _visaItemService;
        private readonly ModbusItemService _modbusItemService;
        private readonly ServiceHealthMonitor _serviceHealthMonitor;
        private readonly IConfiguration _configuration;
        private readonly ILogger<PollingHostedService> _logger;
        private DateTime _lastSelfRecoveryWarningLoggedAtUtc = DateTime.MinValue;

        public PollingHostedService(
            PollingWorkerManager pollingManager,
            AppSettingsService appSettingsService,
            VisaItemService visaItemService,
            ModbusItemService modbusItemService,
            ServiceHealthMonitor serviceHealthMonitor,
            IConfiguration configuration,
            ILogger<PollingHostedService> logger)
        {
            _pollingManager = pollingManager;
            _appSettingsService = appSettingsService;
            _visaItemService = visaItemService;
            _modbusItemService = modbusItemService;
            _serviceHealthMonitor = serviceHealthMonitor;
            _configuration = configuration;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var serviceName = GetServiceName();
            var shouldMarkStopped = true;
            _pollingManager.DataReceived += PollingManager_DataReceived;
            _pollingManager.ErrorOccurred += PollingManager_ErrorOccurred;

            try
            {
                await _serviceHealthMonitor.MarkStartingAsync(serviceName, stoppingToken).ConfigureAwait(false);

                var settings = await _appSettingsService.LoadSettingsAsync().ConfigureAwait(false);
                _pollingManager.PollingIntervalSeconds = settings.PollingIntervalSeconds;
                _pollingManager.HealthCheckIntervalSeconds = settings.HealthCheckIntervalSeconds;

                var visaItems = await _visaItemService.LoadAsync().ConfigureAwait(false);
                foreach (var item in visaItems)
                {
                    _pollingManager.AddVisaItem(item);
                }

                var modbusItems = await _modbusItemService.LoadAsync().ConfigureAwait(false);
                foreach (var item in modbusItems)
                {
                    _pollingManager.AddModbusItem(item);
                }

                await _serviceHealthMonitor.ApplyConfigurationAsync(
                    serviceName,
                    settings.PollingIntervalSeconds,
                    settings.HealthCheckIntervalSeconds,
                    visaItems.Count,
                    modbusItems.Count,
                    _pollingManager.ActiveWorkerCount,
                    _pollingManager.TotalWorkerCount,
                    stoppingToken).ConfigureAwait(false);

                _logger.LogInformation(
                    "Loaded {VisaCount} VISA items and {ModbusCount} Modbus items. Polling={PollingIntervalSeconds}s HealthCheck={HealthCheckIntervalSeconds}s.",
                    visaItems.Count,
                    modbusItems.Count,
                    settings.PollingIntervalSeconds,
                    settings.HealthCheckIntervalSeconds);

                await _pollingManager.StartAllAsync().ConfigureAwait(false);
                await _serviceHealthMonitor.MarkRunningAsync(
                    serviceName,
                    _pollingManager.ActiveWorkerCount,
                    _pollingManager.TotalWorkerCount,
                    _pollingManager.InitialCycleCompletedAtUtc,
                    _pollingManager.ConsecutiveSqlWriteErrorCount,
                    _pollingManager.LastSqlWriteErrorUtc,
                    stoppingToken).ConfigureAwait(false);

                _logger.LogInformation(
                    "Polling started. Active workers: {ActiveWorkerCount}. Total workers: {TotalWorkerCount}.",
                    _pollingManager.ActiveWorkerCount,
                    _pollingManager.TotalWorkerCount);

                using var heartbeatTimer = new PeriodicTimer(TimeSpan.FromSeconds(ServiceHealthMonitor.DefaultHeartbeatIntervalSeconds));
                while (await heartbeatTimer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
                {
                    await _serviceHealthMonitor.UpdateHeartbeatAsync(
                        serviceName,
                        _pollingManager.ActiveWorkerCount,
                        _pollingManager.TotalWorkerCount,
                        _pollingManager.InitialCycleCompletedAtUtc,
                        _pollingManager.ConsecutiveSqlWriteErrorCount,
                        _pollingManager.LastSqlWriteErrorUtc,
                        stoppingToken).ConfigureAwait(false);

                    await EvaluateSelfRecoveryAsync(serviceName, stoppingToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                shouldMarkStopped = false;
                await _serviceHealthMonitor.MarkFaultedAsync(serviceName, ex, CancellationToken.None).ConfigureAwait(false);
                _logger.LogError(ex, "LP2SVR failed while starting or running.");
                throw;
            }
            finally
            {
                _pollingManager.DataReceived -= PollingManager_DataReceived;
                _pollingManager.ErrorOccurred -= PollingManager_ErrorOccurred;

                try
                {
                    await _pollingManager.StopAllAsync().ConfigureAwait(false);
                    if (shouldMarkStopped)
                    {
                        await _serviceHealthMonitor.MarkStoppedAsync(serviceName, CancellationToken.None).ConfigureAwait(false);
                    }
                    _logger.LogInformation("Polling stopped.");
                }
                catch (Exception ex)
                {
                    await _serviceHealthMonitor.MarkFaultedAsync(serviceName, ex, CancellationToken.None).ConfigureAwait(false);
                    _logger.LogError(ex, "Failed to stop polling cleanly.");
                }
            }
        }

        private void PollingManager_DataReceived(object? sender, PollingDataReceivedEventArgs e)
        {
            _serviceHealthMonitor.RecordPollSuccess(
                e,
                _pollingManager.ActiveWorkerCount,
                _pollingManager.TotalWorkerCount);
        }

        private void PollingManager_ErrorOccurred(object? sender, PollingErrorEventArgs e)
        {
            _serviceHealthMonitor.RecordError(
                e,
                _pollingManager.ActiveWorkerCount,
                _pollingManager.TotalWorkerCount);

            if (e.Exception != null)
            {
                _logger.LogError(
                    e.Exception,
                    "Polling error. Machine={MachineName} Unit={UnitName} Ip={IpAddress} Command={Command} Message={ErrorMessage}",
                    e.MachineName,
                    e.UnitName,
                    e.IpAddress,
                    e.Command,
                    e.ErrorMessage);
                return;
            }

            _logger.LogError(
                "Polling error. Machine={MachineName} Unit={UnitName} Ip={IpAddress} Command={Command} Message={ErrorMessage}",
                e.MachineName,
                e.UnitName,
                e.IpAddress,
                e.Command,
                e.ErrorMessage);
        }

        private async Task EvaluateSelfRecoveryAsync(string serviceName, CancellationToken cancellationToken)
        {
            var snapshot = _serviceHealthMonitor.GetCurrentSnapshot();
            if (snapshot.TotalWorkerCount <= 0 || snapshot.ActiveWorkerCount <= 0)
            {
                return;
            }

            var initialCycleCompletedAtUtc = _pollingManager.InitialCycleCompletedAtUtc;
            if (!initialCycleCompletedAtUtc.HasValue)
            {
                return;
            }

            var nowUtc = DateTime.UtcNow;
            var startedAtUtc = snapshot.StartedAtUtc ?? nowUtc;
            var startupGracePeriod = GetStartupGracePeriod(snapshot);
            var earliestEvaluationAtUtc = startedAtUtc.Add(startupGracePeriod);
            if (initialCycleCompletedAtUtc.Value > earliestEvaluationAtUtc)
            {
                earliestEvaluationAtUtc = initialCycleCompletedAtUtc.Value;
            }

            if (nowUtc < earliestEvaluationAtUtc)
            {
                return;
            }

            var requiredErrorCount = Math.Max(3, Math.Min(snapshot.TotalWorkerCount, 10));
            var successTimeout = GetSuccessfulPollingTimeout(snapshot);
            string? reason = null;

            var consecutiveSqlWriteErrorCount = _pollingManager.ConsecutiveSqlWriteErrorCount;
            if (consecutiveSqlWriteErrorCount >= requiredErrorCount)
            {
                var lastSqlWriteErrorUtc = _pollingManager.LastSqlWriteErrorUtc;
                var sqlErrorAge = lastSqlWriteErrorUtc.HasValue
                    ? nowUtc - lastSqlWriteErrorUtc.Value
                    : TimeSpan.Zero;
                var sqlErrorRecencyThreshold = TimeSpan.FromSeconds(Math.Max(ServiceHealthMonitor.DefaultHeartbeatIntervalSeconds * 2, 30));
                if (!lastSqlWriteErrorUtc.HasValue || sqlErrorAge <= sqlErrorRecencyThreshold)
                {
                    reason = $"SQL write failure persisted. ConsecutiveSqlErrors={consecutiveSqlWriteErrorCount}. LastSqlErrorAgeSeconds={Math.Max(0, Math.Floor(sqlErrorAge.TotalSeconds))}.";
                }
            }

            if (reason == null)
            {
                if (snapshot.ConsecutiveErrorCount < requiredErrorCount)
                {
                    return;
                }

                if (!snapshot.LastSuccessfulPollUtc.HasValue)
                {
                    reason = $"No successful polling completed within {startupGracePeriod.TotalMinutes:0.#} minutes after startup. ConsecutiveErrors={snapshot.ConsecutiveErrorCount}.";
                }
                else
                {
                    var lastSuccessAge = nowUtc - snapshot.LastSuccessfulPollUtc.Value;
                    if (lastSuccessAge >= successTimeout)
                    {
                        reason = $"No successful polling for {lastSuccessAge.TotalMinutes:0.#} minutes. Timeout={successTimeout.TotalMinutes:0.#} minutes. ConsecutiveErrors={snapshot.ConsecutiveErrorCount}.";
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(reason))
            {
                return;
            }

            var (shouldRestart, attemptCount, suppressedUntilUtc) = await _serviceHealthMonitor
                .TryBeginSelfRecoveryAsync(serviceName, reason, cancellationToken)
                .ConfigureAwait(false);

            if (!shouldRestart)
            {
                LogSelfRecoverySuppressed(reason, attemptCount, suppressedUntilUtc);
                return;
            }

            _logger.LogCritical(
                "Self-recovery is restarting LP2SVR. Attempt={Attempt}/{MaxAttempts}. Reason={Reason}",
                attemptCount,
                ServiceHealthMonitor.MaxSelfRecoveryAttempts,
                reason);

            throw new InvalidOperationException($"Self-recovery requested restart: {reason}");
        }

        private void LogSelfRecoverySuppressed(string reason, int attemptCount, DateTime? suppressedUntilUtc)
        {
            var nowUtc = DateTime.UtcNow;
            if (nowUtc - _lastSelfRecoveryWarningLoggedAtUtc < TimeSpan.FromMinutes(5))
            {
                return;
            }

            _lastSelfRecoveryWarningLoggedAtUtc = nowUtc;
            _logger.LogError(
                "Self-recovery restart suppressed to avoid a restart loop. Attempts={AttemptCount} SuppressedUntil={SuppressedUntil} Reason={Reason}",
                attemptCount,
                suppressedUntilUtc?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "-",
                reason);
        }

        private static TimeSpan GetStartupGracePeriod(LP2DTP.Common.Models.ServiceHealthSnapshot snapshot)
        {
            var pollingSeconds = Math.Max(snapshot.PollingIntervalSeconds, 1);
            var healthCheckSeconds = Math.Max(snapshot.HealthCheckIntervalSeconds, 1);
            var derivedSeconds = Math.Max(pollingSeconds * 10, healthCheckSeconds * 6);
            return TimeSpan.FromSeconds(Math.Max((int)MinimumSelfRecoveryGracePeriod.TotalSeconds, derivedSeconds));
        }

        private static TimeSpan GetSuccessfulPollingTimeout(LP2DTP.Common.Models.ServiceHealthSnapshot snapshot)
        {
            var pollingSeconds = Math.Max(snapshot.PollingIntervalSeconds, 1);
            var healthCheckSeconds = Math.Max(snapshot.HealthCheckIntervalSeconds, 1);
            var derivedSeconds = Math.Max(pollingSeconds * 20, healthCheckSeconds * 10);
            return TimeSpan.FromSeconds(Math.Max(300, derivedSeconds));
        }

        private string GetServiceName()
        {
            var serviceName = _configuration["ServiceName"];
            return string.IsNullOrWhiteSpace(serviceName)
                ? "LP2SVR"
                : serviceName.Trim();
        }
    }
}
