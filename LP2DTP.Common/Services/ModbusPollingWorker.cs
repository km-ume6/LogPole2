using System;
using System.Collections.Concurrent;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;
using LP2DTP.Common.Models;

namespace LP2DTP.Common.Services
{
    /// <summary>
    /// Modbus TCP polling worker implementation
    /// </summary>
    public class ModbusPollingWorker : PollingWorkerBase
    {
        private readonly ModbusItem _modbusItem;
        private readonly IModbusCommunication _modbusCommunication;
        private bool _isConnected;
        private readonly PollingLogService _logService = PollingLogService.Instance;
        private static readonly ConcurrentDictionary<string, SemaphoreSlim> _healthCheckLocks = new(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<string, CachedHealthCheckResult> _healthCheckCache = new(StringComparer.OrdinalIgnoreCase);

        public override event EventHandler<PollingDataReceivedEventArgs>? DataReceived;
        public override event EventHandler<PollingErrorEventArgs>? ErrorOccurred;

        private void LogMessage(string message, string level = "INFO")
        {
            _logService.Log(level, "MODBUS", _modbusItem.Device.MachineName, message);
        }

        public ModbusPollingWorker(ModbusItem modbusItem) : this(modbusItem, new ModbusTcpCommunication())
        {
        }

        public ModbusPollingWorker(ModbusItem modbusItem, IModbusCommunication modbusCommunication)
        {
            _modbusItem = modbusItem ?? throw new ArgumentNullException(nameof(modbusItem));
            _modbusCommunication = modbusCommunication ?? throw new ArgumentNullException(nameof(modbusCommunication));
        }

        protected override bool IsItemEnabled => _modbusItem.IsEnabled;

        protected override bool IsConnectionOpen => _isConnected;

        protected override async Task<bool> CheckEndpointAliveAsync(CancellationToken cancellationToken)
        {
            var endpoint = _modbusItem.Device.IpAddress;
            var cacheDuration = TimeSpan.FromSeconds(Math.Max(1, HealthCheckIntervalSeconds));
            var endpointLock = _healthCheckLocks.GetOrAdd(endpoint, static _ => new SemaphoreSlim(1, 1));

            await endpointLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var nowUtc = DateTime.UtcNow;
                if (_healthCheckCache.TryGetValue(endpoint, out var cached) && cached.ExpiresAtUtc > nowUtc)
                {
                    if (!cached.IsAlive)
                    {
                        _isConnected = false;
                    }

                    return cached.IsAlive;
                }

                try
                {
                    LogMessage($"Pinging {endpoint}");

                    using var ping = new Ping();
                    var reply = await ping.SendPingAsync(endpoint, 2000).ConfigureAwait(false);
                    var isAlive = reply.Status == IPStatus.Success;

                    if (isAlive)
                    {
                        LogMessage($"Ping OK (RTT={reply.RoundtripTime}ms)");
                    }
                    else
                    {
                        LogMessage($"Ping failed: {reply.Status}", "WARNING");
                        _isConnected = false;
                    }

                    _healthCheckCache[endpoint] = new CachedHealthCheckResult(isAlive, nowUtc.Add(cacheDuration));
                    return isAlive;
                }
                catch (Exception ex)
                {
                    LogMessage($"Health check error: {ex.Message}", "ERROR");
                    _isConnected = false;
                    _healthCheckCache[endpoint] = new CachedHealthCheckResult(false, nowUtc.Add(cacheDuration));
                    return false;
                }
            }
            finally
            {
                endpointLock.Release();
            }
        }

        protected override async Task ExecutePollingAsync(CancellationToken cancellationToken)
        {
            if (!_isConnected || !_modbusCommunication.IsConnected)
            {
                LogMessage($"Connecting to {_modbusItem.Device.IpAddress}:502");
                
                try
                {
                    _isConnected = await _modbusCommunication.ConnectAsync(_modbusItem.Device.IpAddress).ConfigureAwait(false);

                    if (!_isConnected)
                    {
                        LogMessage("Connection failed: ConnectAsync returned false", "ERROR");
                        throw new InvalidOperationException("Not connected to Modbus device");
                    }
                    LogMessage("Connected successfully");
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
                var (pduAddress, functionCode) = ResolveAddressAndFunction();

                ushort[] registers;

                LogMessage($"Reading FC{functionCode} Unit{_modbusItem.UnitId} Reg{pduAddress} Count{_modbusItem.RegisterCount}");

                if (functionCode == 3)
                {
                    registers = await _modbusCommunication.ReadHoldingRegistersAsync(
                        _modbusItem.UnitId,
                        pduAddress,
                        _modbusItem.RegisterCount).ConfigureAwait(false);
                }
                else if (functionCode == 4)
                {
                    registers = await _modbusCommunication.ReadInputRegistersAsync(
                        _modbusItem.UnitId,
                        pduAddress,
                        _modbusItem.RegisterCount).ConfigureAwait(false);
                }
                else
                {
                    throw new NotSupportedException($"Function code {functionCode} is not supported");
                }

                var temperature = ConvertRegistersToTemperature(registers);

                _modbusItem.TemperatureValue = temperature;
                LogMessage($"Temperature: {temperature:F2}°C");

                OnDataReceived(new PollingDataReceivedEventArgs
                {
                    MachineName = _modbusItem.Device.MachineName,
                    UnitName = _modbusItem.Device.UnitName,
                    IpAddress = _modbusItem.Device.IpAddress,
                    Command = $"FC{functionCode} Unit{_modbusItem.UnitId} Reg{_modbusItem.TemperatureRegisterAddress}",
                    Response = $"{temperature:F2}°C",
                    Timestamp = DateTime.Now
                });
            }
            catch (Exception ex)
            {
                LogMessage($"Polling error: {ex.GetType().Name} - {ex.Message}", "ERROR");
                _isConnected = false;
                throw new InvalidOperationException($"Modbus communication error: {ex.Message}", ex);
            }
        }

        protected override async Task DisconnectAsync()
        {
            LogMessage("Disconnecting");
            await _modbusCommunication.DisconnectAsync().ConfigureAwait(false);
            _isConnected = false;
        }

        protected override void OnPollingError(Exception ex)
        {
            LogMessage($"Error: {ex.Message}", "ERROR");
            OnErrorOccurred(new PollingErrorEventArgs
            {
                MachineName = _modbusItem.Device.MachineName,
                UnitName = _modbusItem.Device.UnitName,
                IpAddress = _modbusItem.Device.IpAddress,
                Command = $"Unit {_modbusItem.UnitId}, Reg {_modbusItem.TemperatureRegisterAddress}",
                Exception = ex,
                ErrorMessage = ex.Message,
                Timestamp = DateTime.Now
            });
        }

        private double ConvertRegistersToTemperature(ushort[] registers)
        {
            return _modbusItem.ItemType switch
            {
                ModbusItemType.Ohkura => ConvertRegistersToTemperatureOhkura(registers),
                _ => ConvertRegistersToTemperatureKeyence(registers)
            };
        }

        private double ConvertRegistersToTemperatureKeyence(ushort[] registers)
        {
            return ConvertRegistersToTemperatureCommon(registers);
        }

        private double ConvertRegistersToTemperatureOhkura(ushort[] registers)
        {
            return ConvertRegistersToTemperatureCommon(registers);
        }

        private double ConvertRegistersToTemperatureCommon(ushort[] registers)
        {
            if (_modbusItem.RegisterCount == 2)
            {
                return ConvertRegistersToFloat(registers, _modbusItem.ByteOrder);
            }

            if (_modbusItem.RegisterCount == 1)
            {
                return registers[0] / 10.0;
            }

            throw new NotSupportedException($"RegisterCount {_modbusItem.RegisterCount} is not supported");
        }

        private (ushort PduAddress, byte FunctionCode) ResolveAddressAndFunction()
        {
            return _modbusItem.ItemType switch
            {
                ModbusItemType.Ohkura => ResolveAddressAndFunctionOhkura(),
                _ => ResolveAddressAndFunctionKeyence()
            };
        }

        private (ushort PduAddress, byte FunctionCode) ResolveAddressAndFunctionKeyence()
        {
            var pduAddress = ConvertModiconToPduAddress(_modbusItem.TemperatureRegisterAddress, out byte functionCode);

            if (_modbusItem.TemperatureRegisterAddress < 100000)
            {
                functionCode = _modbusItem.FunctionCode;
            }

            return (pduAddress, functionCode);
        }

        private (ushort PduAddress, byte FunctionCode) ResolveAddressAndFunctionOhkura()
        {
            if (_modbusItem.TemperatureRegisterAddress > ushort.MaxValue)
            {
                throw new InvalidOperationException(
                    $"Ohkura mode requires raw PDU address (0-{ushort.MaxValue}). Current={_modbusItem.TemperatureRegisterAddress}");
            }

            return ((ushort)_modbusItem.TemperatureRegisterAddress, _modbusItem.FunctionCode);
        }

        /// <summary>
        /// Convert 2 Modbus registers to float (32-bit)
        /// </summary>
        /// <param name="registers">Array of 2 registers</param>
        /// <param name="byteOrder">Byte order (0=ABCD, 1=DCBA, 2=BADC, 3=CDAB)</param>
        /// <returns>Float value</returns>
        private float ConvertRegistersToFloat(ushort[] registers, byte byteOrder)
        {
            if (registers.Length < 2)
            {
                throw new ArgumentException("At least 2 registers are required for float conversion");
            }

            byte[] bytes = new byte[4];

            switch (byteOrder)
            {
                case 0:
                    bytes[0] = (byte)(registers[0] >> 8);
                    bytes[1] = (byte)(registers[0] & 0xFF);
                    bytes[2] = (byte)(registers[1] >> 8);
                    bytes[3] = (byte)(registers[1] & 0xFF);
                    break;

                case 1:
                    bytes[0] = (byte)(registers[1] & 0xFF);
                    bytes[1] = (byte)(registers[1] >> 8);
                    bytes[2] = (byte)(registers[0] & 0xFF);
                    bytes[3] = (byte)(registers[0] >> 8);
                    break;

                case 2:
                    bytes[0] = (byte)(registers[0] & 0xFF);
                    bytes[1] = (byte)(registers[0] >> 8);
                    bytes[2] = (byte)(registers[1] & 0xFF);
                    bytes[3] = (byte)(registers[1] >> 8);
                    break;

                case 3:
                    bytes[0] = (byte)(registers[1] >> 8);
                    bytes[1] = (byte)(registers[1] & 0xFF);
                    bytes[2] = (byte)(registers[0] >> 8);
                    bytes[3] = (byte)(registers[0] & 0xFF);
                    break;

                default:
                    throw new ArgumentException($"Invalid byte order: {byteOrder}");
            }

            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(bytes);
            }

            return BitConverter.ToSingle(bytes, 0);
        }

        /// <summary>
        /// Convert Modicon address format to PDU address
        /// </summary>
        /// <param name="modiconAddress">Modicon address (e.g., 300001, 400001)</param>
        /// <param name="functionCode">Derived function code</param>
        /// <returns>PDU address (0-65535)</returns>
        private ushort ConvertModiconToPduAddress(uint modiconAddress, out byte functionCode)
        {
            if (modiconAddress >= 400001 && modiconAddress <= 499999)
            {
                functionCode = 3;
                return (ushort)(modiconAddress - 400001);
            }
            else if (modiconAddress >= 300001 && modiconAddress <= 399999)
            {
                functionCode = 4;
                return (ushort)(modiconAddress - 300001);
            }
            else if (modiconAddress >= 100001 && modiconAddress <= 199999)
            {
                functionCode = 2;
                return (ushort)(modiconAddress - 100001);
            }
            else if (modiconAddress >= 1 && modiconAddress <= 99999)
            {
                functionCode = 1;
                return (ushort)(modiconAddress - 1);
            }
            else
            {
                functionCode = 3;
                return (ushort)(modiconAddress & 0xFFFF);
            }
        }

        protected virtual void OnDataReceived(PollingDataReceivedEventArgs e)
        {
            DataReceived?.Invoke(this, e);
        }

        protected virtual void OnErrorOccurred(PollingErrorEventArgs e)
        {
            ErrorOccurred?.Invoke(this, e);
        }

        protected override void DisposeCore()
        {
            (_modbusCommunication as IDisposable)?.Dispose();
        }

        private readonly record struct CachedHealthCheckResult(bool IsAlive, DateTime ExpiresAtUtc);
    }
}
