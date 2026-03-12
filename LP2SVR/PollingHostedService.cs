using System;
using System.Threading;
using System.Threading.Tasks;
using LP2DTP.Common.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LP2SVR
{
    public sealed class PollingHostedService : BackgroundService
    {
        private readonly PollingWorkerManager _pollingManager;
        private readonly AppSettingsService _appSettingsService;
        private readonly VisaItemService _visaItemService;
        private readonly ModbusItemService _modbusItemService;
        private readonly ILogger<PollingHostedService> _logger;

        public PollingHostedService(
            PollingWorkerManager pollingManager,
            AppSettingsService appSettingsService,
            VisaItemService visaItemService,
            ModbusItemService modbusItemService,
            ILogger<PollingHostedService> logger)
        {
            _pollingManager = pollingManager;
            _appSettingsService = appSettingsService;
            _visaItemService = visaItemService;
            _modbusItemService = modbusItemService;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _pollingManager.ErrorOccurred += PollingManager_ErrorOccurred;

            try
            {
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

                _logger.LogInformation(
                    "Loaded {VisaCount} VISA items and {ModbusCount} Modbus items. Polling={PollingIntervalSeconds}s HealthCheck={HealthCheckIntervalSeconds}s.",
                    visaItems.Count,
                    modbusItems.Count,
                    settings.PollingIntervalSeconds,
                    settings.HealthCheckIntervalSeconds);

                await _pollingManager.StartAllAsync().ConfigureAwait(false);

                _logger.LogInformation(
                    "Polling started. Active workers: {ActiveWorkerCount}. Total workers: {TotalWorkerCount}.",
                    _pollingManager.ActiveWorkerCount,
                    _pollingManager.TotalWorkerCount);

                await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                _pollingManager.ErrorOccurred -= PollingManager_ErrorOccurred;

                try
                {
                    await _pollingManager.StopAllAsync().ConfigureAwait(false);
                    _logger.LogInformation("Polling stopped.");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to stop polling cleanly.");
                }
            }
        }

        private void PollingManager_ErrorOccurred(object? sender, PollingErrorEventArgs e)
        {
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
    }
}
