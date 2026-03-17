using System;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;
using LP2DTP.Common.Models;

namespace LP2DTP.Common.Services
{
    /// <summary>
    /// VISA polling worker implementation
    /// </summary>
    public class VisaPollingWorker : PollingWorkerBase
    {
        private readonly VisaItem _visaItem;
        private readonly IVisaCommunication _visaCommunication;
        private bool _isConnected;
        private readonly PollingLogService _logService = PollingLogService.Instance;

        public override event EventHandler<PollingDataReceivedEventArgs>? DataReceived;
        public override event EventHandler<PollingErrorEventArgs>? ErrorOccurred;

        private void LogMessage(string message, string level = "INFO")
        {
            _logService.Log(level, "VISA", _visaItem.Device.MachineName, message);
        }

        protected override async Task<bool> CheckEndpointAliveAsync(CancellationToken cancellationToken)
        {
            try
            {
                LogMessage($"Pinging {_visaItem.Device.IpAddress}");
                
                using (var ping = new Ping())
                {
                    var reply = await ping.SendPingAsync(_visaItem.Device.IpAddress, 2000);
                    
                    if (reply.Status == IPStatus.Success)
                    {
                        LogMessage($"Ping OK (RTT={reply.RoundtripTime}ms)");
                        return true;
                    }
                    else
                    {
                        LogMessage($"Ping failed: {reply.Status}", "WARNING");
                        _isConnected = false;
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                LogMessage($"Health check error: {ex.Message}", "ERROR");
                _isConnected = false;
                return false;
            }
        }

        protected override async Task ExecutePollingAsync(CancellationToken cancellationToken)
        {
            if (!_isConnected || !_visaCommunication.IsConnected)
            {
                var resourceName = $"TCPIP::{_visaItem.Device.IpAddress}::5025::SOCKET";
                LogMessage($"Connecting to {resourceName}");
                
                try
                {
                    _isConnected = await _visaCommunication.OpenAsync(resourceName).ConfigureAwait(false);

                    if (!_isConnected)
                    {
                        LogMessage($"Connection failed: OpenAsync returned false", "ERROR");
                        throw new InvalidOperationException("Not connected to device");
                    }
                    LogMessage($"Connected successfully");
                }
                catch (Exception ex)
                {
                    LogMessage($"Connection error: {ex.GetType().Name} - {ex.Message}", "ERROR");
                    _isConnected = false;
                    throw;
                }
            }

            try
            {
                if (!string.IsNullOrEmpty(_visaItem.CommandCurr))
                {
                    LogMessage($"Querying current: {_visaItem.CommandCurr}");
                    var currentResponse = await _visaCommunication.QueryAsync(_visaItem.CommandCurr).ConfigureAwait(false);
                    LogMessage($"Current response: {currentResponse}");

                    if (TryParseResponse(currentResponse, out double currentValue))
                    {
                        _visaItem.CurrentValue = currentValue;
                        LogMessage($"Current: {currentValue}A");
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

                if (!string.IsNullOrEmpty(_visaItem.CommandVolt))
                {
                    LogMessage($"Querying voltage: {_visaItem.CommandVolt}");
                    var voltageResponse = await _visaCommunication.QueryAsync(_visaItem.CommandVolt).ConfigureAwait(false);
                    LogMessage($"Voltage response: {voltageResponse}");

                    if (TryParseResponse(voltageResponse, out double voltageValue))
                    {
                        _visaItem.VoltageValue = voltageValue;
                        LogMessage($"Voltage: {voltageValue}V");
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
                LogMessage($"Polling error: {ex.GetType().Name} - {ex.Message}", "ERROR");
                _isConnected = false;
                throw new InvalidOperationException($"Communication error: {ex.Message}", ex);
            }
        }

        protected override async Task DisconnectAsync()
        {
            LogMessage("Disconnecting");
            await _visaCommunication.CloseAsync().ConfigureAwait(false);
            _isConnected = false;
        }

        protected override void OnPollingError(Exception ex)
        {
            LogMessage($"Error: {ex.Message}", "ERROR");
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

            if (!string.IsNullOrEmpty(_visaItem.CommandCurr))
            {
                OnDataReceived(new PollingDataReceivedEventArgs
                {
                    MachineName = _visaItem.Device.MachineName,
                    UnitName = _visaItem.Device.UnitName,
                    IpAddress = _visaItem.Device.IpAddress,
                    Command = _visaItem.CommandCurr,
                    Response = _visaItem.DefaultCurrentValue.ToString("F4"),
                    Timestamp = DateTime.Now
                });
            }

            if (!string.IsNullOrEmpty(_visaItem.CommandVolt))
            {
                OnDataReceived(new PollingDataReceivedEventArgs
                {
                    MachineName = _visaItem.Device.MachineName,
                    UnitName = _visaItem.Device.UnitName,
                    IpAddress = _visaItem.Device.IpAddress,
                    Command = _visaItem.CommandVolt,
                    Response = _visaItem.DefaultVoltageValue.ToString("F4"),
                    Timestamp = DateTime.Now
                });
            }
        }

        private bool TryParseResponse(string response, out double value)
        {
            value = 0.0;

            if (string.IsNullOrWhiteSpace(response))
            {
                return false;
            }

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

        public VisaPollingWorker(VisaItem visaItem) : this(visaItem, new VisaTcpCommunication())
        {
        }

        public VisaPollingWorker(VisaItem visaItem, IVisaCommunication visaCommunication)
        {
            _visaItem = visaItem ?? throw new ArgumentNullException(nameof(visaItem));
            _visaCommunication = visaCommunication ?? throw new ArgumentNullException(nameof(visaCommunication));
        }

        protected override bool IsItemEnabled => _visaItem.IsEnabled;

        protected override bool IsConnectionOpen => _isConnected;

        protected override void DisposeCore()
        {
            (_visaCommunication as IDisposable)?.Dispose();
        }
    }
}
