using System;
using System.Threading;
using System.Threading.Tasks;
using LP2DTP.Common.Models;

namespace LP2DTP.Common.Services
{
    /// <summary>
    /// VISA polling worker implementation
    /// </summary>
    public class VisaPollingWorker : IPollingWorker
    {
        private readonly VisaItem _visaItem;
        private readonly IVisaCommunication _visaCommunication;
        private bool _isRunning;
        private bool _isConnected;
        private DateTime _nextPollingAtUtc = DateTime.MinValue;
        private DateTime _nextHealthCheckAtUtc = DateTime.MinValue;
        private bool _isEndpointAlive;

        public bool IsRunning => _isRunning;
        public int PollingIntervalMs { get; set; } = 1000;
        public int HealthCheckIntervalMs { get; set; } = 5000;

        public event EventHandler<PollingDataReceivedEventArgs>? DataReceived;
        public event EventHandler<PollingErrorEventArgs>? ErrorOccurred;

        public VisaPollingWorker(VisaItem visaItem) : this(visaItem, new VisaTcpCommunication())
        {
        }

        public VisaPollingWorker(VisaItem visaItem, IVisaCommunication visaCommunication)
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
            _isEndpointAlive = true;
            _nextHealthCheckAtUtc = DateTime.UtcNow;
            _nextPollingAtUtc = DateTime.UtcNow.AddMilliseconds(CalculateInitialDelay());
            return Task.CompletedTask;
        }

        public async Task StopAsync()
        {
            if (!_isRunning)
            {
                return;
            }

            _isRunning = false;

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

        public async Task ExecuteCycleAsync(CancellationToken cancellationToken)
        {
            if (!_isRunning)
            {
                return;
            }

            var now = DateTime.UtcNow;

            if (now >= _nextHealthCheckAtUtc)
            {
                _isEndpointAlive = await CheckEndpointAliveAsync(cancellationToken).ConfigureAwait(false);
                _nextHealthCheckAtUtc = DateTime.UtcNow.AddMilliseconds(Math.Max(1, HealthCheckIntervalMs));
            }

            if (!_isEndpointAlive)
            {
                if (_isConnected)
                {
                    await _visaCommunication.CloseAsync().ConfigureAwait(false);
                    _isConnected = false;
                }

                return;
            }

            if (now < _nextPollingAtUtc)
            {
                return;
            }

            await ExecuteSingleCycleAsync(cancellationToken).ConfigureAwait(false);
            _nextPollingAtUtc = DateTime.UtcNow.AddMilliseconds(Math.Max(1, PollingIntervalMs));
        }

        private async Task<bool> CheckEndpointAliveAsync(CancellationToken cancellationToken)
        {
            if (_isConnected && _visaCommunication.IsConnected)
            {
                return true;
            }

            try
            {
                var resourceName = $"TCPIP::{_visaItem.Device.IpAddress}::5025::SOCKET";
                var connected = await _visaCommunication.OpenAsync(resourceName).ConfigureAwait(false);
                if (!connected)
                {
                    return false;
                }

                await _visaCommunication.CloseAsync().ConfigureAwait(false);
                _isConnected = false;
                return true;
            }
            catch
            {
                _isConnected = false;
                return false;
            }
        }

        private async Task ExecuteSingleCycleAsync(CancellationToken cancellationToken)
        {
            try
            {
                if (_visaItem.IsEnabled)
                {
                    await ExecutePollingAsync(cancellationToken).ConfigureAwait(false);
                }
                else if (_isConnected)
                {
                    // Ensure no VISA connection is kept while item is disabled
                    await _visaCommunication.CloseAsync().ConfigureAwait(false);
                    _isConnected = false;
                }
            }
            catch (Exception ex)
            {
                _isEndpointAlive = false;
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
            }
        }

        private int CalculateInitialDelay()
        {
            var now = DateTime.Now;
            var intervalMs = Math.Max(1, PollingIntervalMs);

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

            (_visaCommunication as IDisposable)?.Dispose();
        }
    }
}
