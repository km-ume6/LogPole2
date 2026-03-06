using System;
using System.Threading;
using System.Threading.Tasks;
using LP2DTP.Common.Models;

namespace LP2DTP.Common.Services
{
    /// <summary>
    /// Polling worker implementation
    /// </summary>
    public class PollingWorker : IPollingWorker
    {
        private readonly VisaItem _visaItem;
        private readonly IVisaCommunication _visaCommunication;
        private CancellationTokenSource? _cancellationTokenSource;
        private Task? _pollingTask;
        private bool _isRunning;
        private bool _isConnected;

        public bool IsRunning => _isRunning;
        public int PollingIntervalMs { get; set; } = 1000;

        public event EventHandler<PollingDataReceivedEventArgs>? DataReceived;
        public event EventHandler<PollingErrorEventArgs>? ErrorOccurred;

        public PollingWorker(VisaItem visaItem) : this(visaItem, new VisaTcpCommunication())
        {
        }

        public PollingWorker(VisaItem visaItem, IVisaCommunication visaCommunication)
        {
            _visaItem = visaItem ?? throw new ArgumentNullException(nameof(visaItem));
            _visaCommunication = visaCommunication ?? throw new ArgumentNullException(nameof(visaCommunication));
        }

        public Task StartAsync()
        {
            if (_isRunning)
            {
                return Task.CompletedTask;
            }

            _isRunning = true;
            _cancellationTokenSource = new CancellationTokenSource();
            _pollingTask = Task.Run(() => PollingLoopAsync(_cancellationTokenSource.Token), _cancellationTokenSource.Token);
            return Task.CompletedTask;
        }

        public async Task StopAsync()
        {
            if (!_isRunning)
            {
                return;
            }

            _isRunning = false;

            if (_cancellationTokenSource != null)
            {
                _cancellationTokenSource.Cancel();

                if (_pollingTask != null)
                {
                    try
                    {
                        await _pollingTask.ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        // Expected when cancelling
                    }
                }

                _cancellationTokenSource.Dispose();
                _cancellationTokenSource = null;
            }

            _pollingTask = null;

            // Close VISA connection
            try
            {
                await _visaCommunication.CloseAsync().ConfigureAwait(false);
                _isConnected = false;
            }
            catch
            {
                // Ignore errors during cleanup
            }
        }

        private async Task PollingLoopAsync(CancellationToken cancellationToken)
        {
            // Wait until next aligned time before starting first poll
            var initialDelay = CalculateInitialDelay();
            if (initialDelay > 0)
            {
                try
                {
                    await Task.Delay(initialDelay, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    if (_visaItem.IsEnabled)
                    {
                        await ExecutePollingAsync(cancellationToken);
                    }
                    else if (_isConnected)
                    {
                        // Ensure no VISA connection is kept while item is disabled
                        await _visaCommunication.CloseAsync().ConfigureAwait(false);
                        _isConnected = false;
                    }

                    await Task.Delay(PollingIntervalMs, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    // Expected when stopping
                    break;
                }
                catch (Exception ex)
                {
                    OnErrorOccurred(new PollingErrorEventArgs
                    {
                        MachineName = _visaItem.Device.MachineName,
                        UnitName = _visaItem.Device.UnitName,
                        IpAddress = _visaItem.Device.IpAddress,
                        Command = _visaItem.CommandCurr,
                        Exception = ex,
                        ErrorMessage = ex.Message,
                        Timestamp = DateTime.Now
                    });

                    // Continue polling even after error
                    await Task.Delay(PollingIntervalMs, cancellationToken);
                }
            }
        }

        private int CalculateInitialDelay()
        {
            var now = DateTime.Now;
            var intervalMs = PollingIntervalMs;

            // Calculate milliseconds since midnight
            var nowMs = (long)now.TimeOfDay.TotalMilliseconds;

            // Calculate next aligned time (round up to next interval)
            var nextMs = (long)(Math.Ceiling((double)nowMs / intervalMs) * intervalMs);

            // Handle day overflow (if next time is past midnight)
            if (nextMs >= 86400000) // 24 hours in milliseconds
            {
                nextMs = 0; // Start at midnight
            }

            // Calculate delay
            var delayMs = nextMs - nowMs;
            if (delayMs < 0)
            {
                delayMs += 86400000; // Add 24 hours
            }

            return (int)delayMs;
        }

        private async Task ExecutePollingAsync(CancellationToken cancellationToken)
        {
            if (!_isConnected || !_visaCommunication.IsConnected)
            {
                // Try to reconnect
                var resourceName = $"TCPIP::{_visaItem.Device.IpAddress}::5025::SOCKET";
                _isConnected = await _visaCommunication.OpenAsync(resourceName);

                if (!_isConnected)
                {
                    throw new InvalidOperationException("Not connected to device");
                }
            }

            try
            {
                // Measure current
                if (!string.IsNullOrEmpty(_visaItem.CommandCurr))
                {
                    var currentResponse = await _visaCommunication.QueryAsync(_visaItem.CommandCurr);

                    // Try to parse numeric value
                    if (TryParseResponse(currentResponse, out double currentValue))
                    {
                        _visaItem.CurrentValue = currentValue;
                    }

                    OnDataReceived(new PollingDataReceivedEventArgs
                    {
                        MachineName = _visaItem.Device.MachineName,
                        UnitName = _visaItem.Device.UnitName,
                        IpAddress = _visaItem.Device.IpAddress,
                        Command = _visaItem.CommandCurr,
                        Response = currentResponse,
                        Timestamp = DateTime.Now
                    });
                }

                // Measure voltage
                if (!string.IsNullOrEmpty(_visaItem.CommandVolt))
                {
                    var voltageResponse = await _visaCommunication.QueryAsync(_visaItem.CommandVolt);

                    // Try to parse numeric value
                    if (TryParseResponse(voltageResponse, out double voltageValue))
                    {
                        _visaItem.VoltageValue = voltageValue;
                    }

                    OnDataReceived(new PollingDataReceivedEventArgs
                    {
                        MachineName = _visaItem.Device.MachineName,
                        UnitName = _visaItem.Device.UnitName,
                        IpAddress = _visaItem.Device.IpAddress,
                        Command = _visaItem.CommandVolt,
                        Response = voltageResponse,
                        Timestamp = DateTime.Now
                    });
                }
			}
			catch (Exception ex)
            {
                // Connection lost, mark as disconnected
                _isConnected = false;
                throw new InvalidOperationException($"Communication error: {ex.Message}", ex);
            }
        }

        private bool TryParseResponse(string response, out double value)
        {
            value = 0.0;

            if (string.IsNullOrWhiteSpace(response))
            {
                return false;
            }

            // Remove common units and extra characters
            var cleaned = response.Trim()
                .Replace("A", "")
                .Replace("V", "")
                .Replace("W", "")
                .Replace("Hz", "")
                .Trim();

            return double.TryParse(cleaned, out value);
        }

        protected virtual void OnDataReceived(PollingDataReceivedEventArgs e)
        {
            DataReceived?.Invoke(this, e);
        }

        protected virtual void OnErrorOccurred(PollingErrorEventArgs e)
        {
            ErrorOccurred?.Invoke(this, e);
        }

        public void Dispose()
        {
            try
            {
                StopAsync().ConfigureAwait(false).GetAwaiter().GetResult();
            }
            catch
            {
                // Ignore stop errors during dispose
            }

            _cancellationTokenSource?.Dispose();
            (_visaCommunication as IDisposable)?.Dispose();
        }
    }
}
