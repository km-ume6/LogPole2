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
        private readonly PollingLogService _logService = PollingLogService.Instance;

        public event EventHandler<PollingDataReceivedEventArgs>? DataReceived;
        public event EventHandler<PollingErrorEventArgs>? ErrorOccurred;

        private void LogMessage(string message, string level = "INFO")
        {
            _logService.Log(level, "MANAGER", null, message);
        }

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
                LogMessage("[Manager] StartAllAsync: Loop already running");
                return;
            }

            LogMessage($"[Manager] StartAllAsync: Starting {_workers.Count} workers");
            _isLoopRunning = true;
            await Task.WhenAll(_workers.Values.Select(w => w.StartAsync())).ConfigureAwait(false);

            _loopCancellationTokenSource = new CancellationTokenSource();
            _loopTask = Task.Run(() => UnifiedPollingLoopAsync(_loopCancellationTokenSource.Token), _loopCancellationTokenSource.Token);
            LogMessage("[Manager] StartAllAsync: Polling loop started");
        }

        /// <summary>
        /// Stop unified polling loop for all workers
        /// </summary>
        public async Task StopAllAsync()
        {
            _isLoopRunning = false;

            var cancellationTokenSource = Interlocked.Exchange(ref _loopCancellationTokenSource, null);
            var loopTask = Interlocked.Exchange(ref _loopTask, null);

            if (cancellationTokenSource != null)
            {
                cancellationTokenSource.Cancel();

                if (loopTask != null)
                {
                    try
                    {
                        await loopTask.ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        // Expected when cancelling
                    }
                }

                cancellationTokenSource.Dispose();
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

        /// <summary>
        /// 実行中のすべてのワーカーに対してポーリングサイクルを繰り返し実行する統合ループです。
        /// </summary>
        /// <param name="cancellationToken">
        /// ループ停止を通知するキャンセルトークンです。
        /// </param>
        /// <remarks>
        /// 各反復では、実行中のワーカーだけを対象に
        /// <see cref="ExecuteWorkerCycleSafelyAsync(IPollingWorker, CancellationToken)"/> を並列実行します。
        /// 全ワーカーのサイクル完了後、次の反復まで 50 ミリ秒待機します。
        /// </remarks>
        private async Task UnifiedPollingLoopAsync(CancellationToken cancellationToken)
        {
            LogMessage("[Manager] UnifiedPollingLoopAsync: Started");
            int cycleCount = 0;
            while (!cancellationToken.IsCancellationRequested)
            {
                var nowUtc = DateTime.UtcNow;
                var workers = _workers.Values.Where(w => w.IsRunning).ToArray();

                if (cycleCount++ % 20 == 0)
                {
                    LogMessage($"[Manager] UnifiedPollingLoopAsync: Cycle #{cycleCount}, Active workers: {workers.Length}/{_workers.Count}");
                }

                await Task.WhenAll(workers.Select(w => ExecuteWorkerCycleSafelyAsync(w, nowUtc, cancellationToken))).ConfigureAwait(false);
                await Task.Delay(50, cancellationToken).ConfigureAwait(false);
            }
            LogMessage("[Manager] UnifiedPollingLoopAsync: Stopped");
        }

        /// <summary>
        /// ワーカーの単一ポーリングサイクルを安全な例外処理で実行します。
        /// </summary>
        /// <param name="worker">ポーリングサイクルを実行するポーリングワーカーインスタンス。</param>
        /// <param name="nowUtc"></param>
        /// <param name="cancellationToken">ポーリング操作のキャンセルを通知するトークン。</param>
        /// <remarks>
        /// このメソッドはワーカーのポーリングサイクルを安全に実行し、例外を適切に処理します：
        /// <list type="bullet">
        /// <item><description><see cref="OperationCanceledException"/> はキャッチされて暗黙的に無視されます。これはシャットダウン時に予期される例外です。</description></item>
        /// <item><description>その他の例外は <see cref="OnManagerErrorOccurred(Exception)"/> を介して報告され、エラーイベントがサブスクライバーに伝播されることを許可します。</description></item>
        /// </list>
        /// <para>
        /// このアプローチにより、あるワーカーの例外が統合ポーリングループ内の他のワーカーの実行を妨げないようにします。
        /// </para>
        /// </remarks>
        private async Task ExecuteWorkerCycleSafelyAsync(IPollingWorker worker, DateTime nowUtc, CancellationToken cancellationToken)
        {
            try
            {
                bool endpointAlive = true;

                if (worker.IsHealthCheckDue(nowUtc))
                {
                    endpointAlive = await worker.ExecuteHealthCheckPhaseAsync(cancellationToken).ConfigureAwait(false);
                }

                if (!endpointAlive)
                {
                    return;
                }

                if (worker.IsPollingDue(nowUtc))
                {
                    await worker.ExecutePollingPhaseAsync(cancellationToken).ConfigureAwait(false);
                }
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
