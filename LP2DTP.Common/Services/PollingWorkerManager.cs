using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LP2DTP.Common.Models;
using System.Collections.Concurrent;

namespace LP2DTP.Common.Services
{
    /// <summary>
    /// Manager for multiple polling workers
    /// </summary>
    public class PollingWorkerManager : IDisposable
    {
        private readonly ConcurrentDictionary<string, IPollingWorker> _workers = new();
        private bool _disposed;
        private int _pollingIntervalMs = 1000;
        private int _healthCheckIntervalMs = 5000;
        private CancellationTokenSource? _loopCancellationTokenSource;
        private Task? _loopTask;
        private bool _isLoopRunning;

        public event EventHandler<PollingDataReceivedEventArgs>? DataReceived;
        public event EventHandler<PollingErrorEventArgs>? ErrorOccurred;

        /// <summary>
        /// Polling interval in milliseconds (applies to new workers)
        /// </summary>
        public int PollingIntervalMs
        {
            get => _pollingIntervalMs;
            set
            {
                _pollingIntervalMs = value;
                foreach (var worker in _workers.Values)
                {
                    worker.PollingIntervalMs = value;
                }
            }
        }

        /// <summary>
        /// Health-check interval in milliseconds (applies to new and existing workers)
        /// </summary>
        public int HealthCheckIntervalMs
        {
            get => _healthCheckIntervalMs;
            set
            {
                _healthCheckIntervalMs = value;
                foreach (var worker in _workers.Values)
                {
                    worker.HealthCheckIntervalMs = value;
                }
            }
        }

        /// <summary>
        /// Add a VISA item for polling
        /// </summary>
        public void AddVisaItem(VisaItem item)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));

            var key = GetVisaItemKey(item);
            AddWorker(key, new VisaPollingWorker(item));
        }

        /// <summary>
        /// Add a Modbus item for polling
        /// </summary>
        public void AddModbusItem(ModbusItem item)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));

            var key = GetModbusItemKey(item);
            AddWorker(key, new ModbusPollingWorker(item));
        }

        /// <summary>
        /// Remove a VISA item from polling
        /// </summary>
        public void RemoveVisaItem(VisaItem item)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));
            RemoveWorker(GetVisaItemKey(item));
        }

        /// <summary>
        /// Remove a Modbus item from polling
        /// </summary>
        public void RemoveModbusItem(ModbusItem item)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));
            RemoveWorker(GetModbusItemKey(item));
        }

        /// <summary>
        /// Update a VISA item
        /// </summary>
        public void UpdateVisaItem(VisaItem item)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));

            RemoveVisaItem(item);
            AddVisaItem(item);
        }

        /// <summary>
        /// Update a Modbus item
        /// </summary>
        public void UpdateModbusItem(ModbusItem item)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));

            RemoveModbusItem(item);
            AddModbusItem(item);
        }

        /// <summary>
        /// Start unified polling loop for all workers
        /// </summary>
        public async Task StartAllAsync()
        {
            if (_isLoopRunning)
            {
                return;
            }

            _isLoopRunning = true;
            await Task.WhenAll(_workers.Values.Select(w => w.StartAsync())).ConfigureAwait(false);

            _loopCancellationTokenSource = new CancellationTokenSource();
            _loopTask = Task.Run(() => UnifiedPollingLoopAsync(_loopCancellationTokenSource.Token), _loopCancellationTokenSource.Token);
        }

        /// <summary>
        /// Stop unified polling loop for all workers
        /// </summary>
        public async Task StopAllAsync()
        {
            _isLoopRunning = false;

            if (_loopCancellationTokenSource != null)
            {
                _loopCancellationTokenSource.Cancel();

                if (_loopTask != null)
                {
                    try
                    {
                        await _loopTask.ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        // Expected when cancelling
                    }
                }

                _loopCancellationTokenSource.Dispose();
                _loopCancellationTokenSource = null;
                _loopTask = null;
            }

            await Task.WhenAll(_workers.Values.Select(w => w.StopAsync())).ConfigureAwait(false);
        }

        private void AddWorker(string key, IPollingWorker worker)
        {
            RemoveWorker(key);

            worker.PollingIntervalMs = _pollingIntervalMs;
            worker.HealthCheckIntervalMs = _healthCheckIntervalMs;
            worker.DataReceived += Worker_DataReceived;
            worker.ErrorOccurred += Worker_ErrorOccurred;

            _workers[key] = worker;

            if (_isLoopRunning)
            {
                worker.StartAsync().ConfigureAwait(false).GetAwaiter().GetResult();
            }
        }

        private void RemoveWorker(string key)
        {
            if (_workers.TryRemove(key, out var worker))
            {
                worker.DataReceived -= Worker_DataReceived;
                worker.ErrorOccurred -= Worker_ErrorOccurred;

                try
                {
                    worker.StopAsync().ConfigureAwait(false).GetAwaiter().GetResult();
                }
                catch
                {
                    // Ignore stop errors during removal
                }

                worker.Dispose();
            }
        }

        private async Task UnifiedPollingLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var workers = _workers.Values.Where(w => w.IsRunning).ToArray();
                await Task.WhenAll(workers.Select(w => ExecuteWorkerCycleSafelyAsync(w, cancellationToken))).ConfigureAwait(false);
                await Task.Delay(50, cancellationToken).ConfigureAwait(false);
            }
        }

        private async Task ExecuteWorkerCycleSafelyAsync(IPollingWorker worker, CancellationToken cancellationToken)
        {
            try
            {
                await worker.ExecuteCycleAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected on stop
            }
            catch (Exception ex)
            {
                OnManagerErrorOccurred(ex);
            }
        }

        private void OnManagerErrorOccurred(Exception ex)
        {
            ErrorOccurred?.Invoke(this, new PollingErrorEventArgs
            {
                ErrorMessage = ex.Message,
                Exception = ex,
                Timestamp = DateTime.Now
            });
        }

        /// <summary>
        /// Get the count of active workers
        /// </summary>
        public int ActiveWorkerCount => _workers.Count(w => w.Value.IsRunning);

        /// <summary>
        /// Get the count of total workers
        /// </summary>
        public int TotalWorkerCount => _workers.Count;

        private string GetVisaItemKey(VisaItem item)
        {
            return $"VISA_{item.Device.MachineName}_{item.Device.UnitName}_{item.Device.IpAddress}";
        }

        private string GetModbusItemKey(ModbusItem item)
        {
            return $"MODBUS_{item.Device.MachineName}_{item.Device.UnitName}_{item.Device.IpAddress}_{item.UnitId}";
        }

        private void Worker_DataReceived(object? sender, PollingDataReceivedEventArgs e)
        {
            DataReceived?.Invoke(this, e);
        }

        private void Worker_ErrorOccurred(object? sender, PollingErrorEventArgs e)
        {
            ErrorOccurred?.Invoke(this, e);
        }

        public void Dispose()
        {
            if (_disposed) return;

            try
            {
                StopAllAsync().ConfigureAwait(false).GetAwaiter().GetResult();
            }
            catch
            {
                // Ignore stop errors during dispose
            }

            foreach (var worker in _workers.Values)
            {
                worker.DataReceived -= Worker_DataReceived;
                worker.ErrorOccurred -= Worker_ErrorOccurred;
                worker.Dispose();
            }

            _workers.Clear();
            _disposed = true;
        }
    }
}
