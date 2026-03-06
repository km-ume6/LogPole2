using System;
using System.Linq;
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
                // Apply to existing workers
                foreach (var worker in _workers.Values)
                {
                    worker.PollingIntervalMs = value;
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

            if (_workers.ContainsKey(key))
            {
                RemoveVisaItem(item);
            }

            var worker = new PollingWorker(item);
            worker.PollingIntervalMs = _pollingIntervalMs;
            worker.DataReceived += Worker_DataReceived;
            worker.ErrorOccurred += Worker_ErrorOccurred;

            _workers[key] = worker;
        }

        /// <summary>
        /// Add a Modbus item for polling
        /// </summary>
        public void AddModbusItem(ModbusItem item)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));

            var key = GetModbusItemKey(item);

            if (_workers.ContainsKey(key))
            {
                RemoveModbusItem(item);
            }

            var worker = new ModbusPollingWorker(item);
            worker.PollingIntervalMs = _pollingIntervalMs;
            worker.DataReceived += Worker_DataReceived;
            worker.ErrorOccurred += Worker_ErrorOccurred;

            _workers[key] = worker;
        }

        /// <summary>
        /// Remove a VISA item from polling
        /// </summary>
        public void RemoveVisaItem(VisaItem item)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));

            var key = GetVisaItemKey(item);
            
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

        /// <summary>
        /// Remove a Modbus item from polling
        /// </summary>
        public void RemoveModbusItem(ModbusItem item)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));

            var key = GetModbusItemKey(item);
            
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
        /// Start all polling workers
        /// </summary>
        public async Task StartAllAsync()
        {
            var tasks = _workers.Values.Select(w => w.StartAsync());
            await Task.WhenAll(tasks);
        }

        /// <summary>
        /// Stop all polling workers
        /// </summary>
        public async Task StopAllAsync()
        {
            var tasks = _workers.Values.Select(w => w.StopAsync());
            await Task.WhenAll(tasks);
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

            foreach (var worker in _workers.Values)
            {
                worker.DataReceived -= Worker_DataReceived;
                worker.ErrorOccurred -= Worker_ErrorOccurred;

                try
                {
                    worker.StopAsync().ConfigureAwait(false).GetAwaiter().GetResult();
                }
                catch
                {
                    // Ignore stop errors during dispose
                }

                worker.Dispose();
            }

            _workers.Clear();
            _disposed = true;
        }
    }
}
