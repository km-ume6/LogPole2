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
        private static readonly TimeSpan ServiceRestartRetryDelay = TimeSpan.FromSeconds(10);

        private readonly PollingWorkerManager _pollingManager;
        private readonly AppSettingsService _appSettingsService;
        private readonly VisaItemService _visaItemService;
        private readonly ModbusItemService _modbusItemService;
        private readonly ServiceHealthMonitor _serviceHealthMonitor;
        private readonly SqlWriteAlertMailer _sqlWriteAlertMailer;
        private readonly IConfiguration _configuration;
        private readonly ILogger<PollingHostedService> _logger;
        private DateTime _lastSelfRecoveryWarningLoggedAtUtc = DateTime.MinValue;
        private DateTime? _sqlWriteFailureStartedAtUtc;
        private DateTime _lastSqlWriteFailureAlertSentAtUtc = DateTime.MinValue;
        private bool _sqlWriteFailureAlertActive;

        public PollingHostedService(
            PollingWorkerManager pollingManager,
            AppSettingsService appSettingsService,
            VisaItemService visaItemService,
            ModbusItemService modbusItemService,
            ServiceHealthMonitor serviceHealthMonitor,
            SqlWriteAlertMailer sqlWriteAlertMailer,
            IConfiguration configuration,
            ILogger<PollingHostedService> logger)
        {
            _pollingManager = pollingManager;
            _appSettingsService = appSettingsService;
            _visaItemService = visaItemService;
            _modbusItemService = modbusItemService;
            _serviceHealthMonitor = serviceHealthMonitor;
            _sqlWriteAlertMailer = sqlWriteAlertMailer;
            _configuration = configuration;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var serviceName = GetServiceName();
            var restartAttempt = 0;
            _pollingManager.DataReceived += PollingManager_DataReceived;
            _pollingManager.ErrorOccurred += PollingManager_ErrorOccurred;

            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    var shouldMarkStopped = true;
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
                            _pollingManager.PendingSqlWriteCount,
                            _pollingManager.LastSqlWriteErrorUtc,
                            stoppingToken).ConfigureAwait(false);

                        try
                        {
                            await _sqlWriteAlertMailer.SendServiceStartedAsync(serviceName, stoppingToken).ConfigureAwait(false);
                            _logger.LogInformation("Service start mail sent.");
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to send service start mail.");
                        }

                        _logger.LogInformation(
                            "Polling started. Active workers: {ActiveWorkerCount}. Total workers: {TotalWorkerCount}.",
                            _pollingManager.ActiveWorkerCount,
                            _pollingManager.TotalWorkerCount);

                        restartAttempt = 0;
                        using var heartbeatTimer = new PeriodicTimer(TimeSpan.FromSeconds(ServiceHealthMonitor.DefaultHeartbeatIntervalSeconds));
                        while (await heartbeatTimer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
                        {
                            await _serviceHealthMonitor.UpdateHeartbeatAsync(
                                serviceName,
                                _pollingManager.ActiveWorkerCount,
                                _pollingManager.TotalWorkerCount,
                                _pollingManager.InitialCycleCompletedAtUtc,
                                _pollingManager.ConsecutiveSqlWriteErrorCount,
                                _pollingManager.PendingSqlWriteCount,
                                _pollingManager.LastSqlWriteErrorUtc,
                                stoppingToken).ConfigureAwait(false);

                            await EvaluateSqlWriteAlertAsync(serviceName, stoppingToken).ConfigureAwait(false);
                            await EvaluateSelfRecoveryAsync(serviceName, stoppingToken).ConfigureAwait(false);
                        }

                        break;
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        shouldMarkStopped = false;
                        restartAttempt++;
                        await _serviceHealthMonitor.MarkFaultedAsync(serviceName, ex, CancellationToken.None).ConfigureAwait(false);
                        _logger.LogError(
                            ex,
                            "LP2SVR failed while starting or running. Retry attempt={RestartAttempt}. Next retry in {RetryDelaySeconds}s.",
                            restartAttempt,
                            (int)ServiceRestartRetryDelay.TotalSeconds);
                    }
                    finally
                    {
                        try
                        {
                            await _pollingManager.StopAllAsync().ConfigureAwait(false);
                            if (shouldMarkStopped)
                            {
                                await _serviceHealthMonitor.MarkStoppedAsync(serviceName, CancellationToken.None).ConfigureAwait(false);
                            }

                            try
                            {
                                await _sqlWriteAlertMailer.SendServiceStoppedAsync(serviceName, CancellationToken.None).ConfigureAwait(false);
                                _logger.LogInformation("Service stop mail sent.");
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "Failed to send service stop mail.");
                            }

                            _logger.LogInformation("Polling stopped.");
                        }
                        catch (Exception ex)
                        {
                            await _serviceHealthMonitor.MarkFaultedAsync(serviceName, ex, CancellationToken.None).ConfigureAwait(false);
                            _logger.LogError(ex, "Failed to stop polling cleanly.");
                        }
                    }

                    if (stoppingToken.IsCancellationRequested)
                    {
                        break;
                    }

                    try
                    {
                        await Task.Delay(ServiceRestartRetryDelay, stoppingToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            }
            finally
            {
                _pollingManager.DataReceived -= PollingManager_DataReceived;
                _pollingManager.ErrorOccurred -= PollingManager_ErrorOccurred;
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
            var isSqlWriteError = string.Equals(e.Command, "SQL", StringComparison.OrdinalIgnoreCase);
            var isDeviceCommunicationError = IsDeviceCommunicationError(e);

            if (!isSqlWriteError && !isDeviceCommunicationError)
            {
                _serviceHealthMonitor.RecordError(
                    e,
                    _pollingManager.ActiveWorkerCount,
                    _pollingManager.TotalWorkerCount);
            }

            if (e.Exception != null)
            {
                if (isSqlWriteError || isDeviceCommunicationError)
                {
                    _logger.LogWarning(
                        e.Exception,
                        "Deferred non-fatal polling error (no restart). Command={Command} Machine={MachineName} Unit={UnitName} Ip={IpAddress} Message={ErrorMessage}",
                        e.Command,
                        e.MachineName,
                        e.UnitName,
                        e.IpAddress,
                        e.ErrorMessage);
                    return;
                }

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

            if (isSqlWriteError || isDeviceCommunicationError)
            {
                _logger.LogWarning(
                    "Deferred non-fatal polling error (no restart). Command={Command} Machine={MachineName} Unit={UnitName} Ip={IpAddress} Message={ErrorMessage}",
                    e.Command,
                    e.MachineName,
                    e.UnitName,
                    e.IpAddress,
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

        private static bool IsDeviceCommunicationError(PollingErrorEventArgs e)
        {
            return !string.IsNullOrWhiteSpace(e.MachineName)
                || !string.IsNullOrWhiteSpace(e.UnitName)
                || !string.IsNullOrWhiteSpace(e.IpAddress);
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

        private async Task EvaluateSqlWriteAlertAsync(string serviceName, CancellationToken cancellationToken)
        {
            var snapshot = _serviceHealthMonitor.GetCurrentSnapshot();
            var hasSqlWriteFailure = snapshot.ConsecutiveSqlWriteErrorCount > 0 && snapshot.PendingSqlWriteCount > 0;

            if (!hasSqlWriteFailure)
            {
                _sqlWriteFailureStartedAtUtc = null;
                _lastSqlWriteFailureAlertSentAtUtc = DateTime.MinValue;

                if (_sqlWriteFailureAlertActive)
                {
                    _sqlWriteFailureAlertActive = false;
                    try
                    {
                        await _sqlWriteAlertMailer.SendRecoveryAsync(serviceName, cancellationToken).ConfigureAwait(false);
                        _logger.LogInformation("SQL write failure recovery mail sent.");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to send SQL write recovery mail.");
                    }
                }

                return;
            }

            var nowUtc = DateTime.UtcNow;
            _sqlWriteFailureStartedAtUtc ??= nowUtc;

            var thresholdSeconds = Math.Clamp(GetIntConfiguration("AlertMail:SqlWriteFailureThresholdSeconds", 3600), 1, 3600);
            var resendIntervalSeconds = Math.Clamp(GetIntConfiguration("AlertMail:ResendIntervalSeconds", 600), 1, 3600);
            var failureAge = nowUtc - _sqlWriteFailureStartedAtUtc.Value;
            if (failureAge.TotalSeconds < thresholdSeconds)
            {
                return;
            }

            if (_sqlWriteFailureAlertActive && nowUtc - _lastSqlWriteFailureAlertSentAtUtc < TimeSpan.FromSeconds(resendIntervalSeconds))
            {
                return;
            }

            try
            {
                await _sqlWriteAlertMailer.SendFailureAsync(
                    serviceName,
                    snapshot.PendingSqlWriteCount,
                    snapshot.ConsecutiveSqlWriteErrorCount,
                    failureAge,
                    cancellationToken).ConfigureAwait(false);

                _sqlWriteFailureAlertActive = true;
                _lastSqlWriteFailureAlertSentAtUtc = nowUtc;

                _logger.LogWarning(
                    "SQL write failure mail sent. Pending={PendingCount} ConsecutiveSqlErrors={ConsecutiveSqlErrors} FailureAgeSeconds={FailureAgeSeconds}",
                    snapshot.PendingSqlWriteCount,
                    snapshot.ConsecutiveSqlWriteErrorCount,
                    (int)failureAge.TotalSeconds);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send SQL write failure mail.");
            }
        }

        private int GetIntConfiguration(string key, int defaultValue)
        {
            var raw = _configuration[key];
            return int.TryParse(raw, out var parsed) ? parsed : defaultValue;
        }
    }
}
