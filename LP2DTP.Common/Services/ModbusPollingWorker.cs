using System;
using System.Threading;
using System.Threading.Tasks;
using LP2DTP.Common.Models;

namespace LP2DTP.Common.Services
{
    /// <summary>
    /// Modbus TCP polling worker implementation
    /// </summary>
    public class ModbusPollingWorker : IPollingWorker
    {
        private readonly ModbusItem _modbusItem;
        private readonly IModbusCommunication _modbusCommunication;
        private CancellationTokenSource? _cancellationTokenSource;
        private Task? _pollingTask;
        private bool _isRunning;
        private bool _isConnected;

        public bool IsRunning => _isRunning;
        public int PollingIntervalMs { get; set; } = 1000;

        public event EventHandler<PollingDataReceivedEventArgs>? DataReceived;
        public event EventHandler<PollingErrorEventArgs>? ErrorOccurred;

        public ModbusPollingWorker(ModbusItem modbusItem) : this(modbusItem, new ModbusTcpCommunication())
        {
        }

        public ModbusPollingWorker(ModbusItem modbusItem, IModbusCommunication modbusCommunication)
        {
            _modbusItem = modbusItem ?? throw new ArgumentNullException(nameof(modbusItem));
            _modbusCommunication = modbusCommunication ?? throw new ArgumentNullException(nameof(modbusCommunication));
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

            // Close Modbus connection
            try
            {
                await _modbusCommunication.DisconnectAsync().ConfigureAwait(false);
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
                    if (_modbusItem.IsEnabled)
                    {
                        await ExecutePollingAsync(cancellationToken);
                    }
                    else if (_isConnected)
                    {
                        // Ensure no Modbus connection is kept while item is disabled
                        await _modbusCommunication.DisconnectAsync().ConfigureAwait(false);
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
                        MachineName = _modbusItem.Device.MachineName,
                        UnitName = _modbusItem.Device.UnitName,
                        IpAddress = _modbusItem.Device.IpAddress,
                        Command = $"Unit {_modbusItem.UnitId}, Reg {_modbusItem.TemperatureRegisterAddress}",
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
            if (!_isConnected || !_modbusCommunication.IsConnected)
            {
                // Try to connect
                _isConnected = await _modbusCommunication.ConnectAsync(_modbusItem.Device.IpAddress);

                if (!_isConnected)
                {
                    throw new InvalidOperationException("Not connected to Modbus device");
                }
            }

            try
            {
                // Convert Modicon address to PDU address
                var pduAddress = ConvertModiconToPduAddress(_modbusItem.TemperatureRegisterAddress, out byte functionCode);
                
                // Use specified function code if not derived from address
                if (_modbusItem.TemperatureRegisterAddress < 100000)
                {
                    functionCode = _modbusItem.FunctionCode;
                }

                // Read temperature register
                ushort[] registers;
                
                if (functionCode == 3)
                {
                    registers = await _modbusCommunication.ReadHoldingRegistersAsync(
                        _modbusItem.UnitId,
                        pduAddress,
                        _modbusItem.RegisterCount);
                }
                else if (functionCode == 4)
                {
                    registers = await _modbusCommunication.ReadInputRegistersAsync(
                        _modbusItem.UnitId,
                        pduAddress,
                        _modbusItem.RegisterCount);
                }
                else
                {
                    throw new NotSupportedException($"Function code {functionCode} is not supported");
                }

                // Convert register values to temperature
                double temperature;
                
                if (_modbusItem.RegisterCount == 2)
                {
                    // 2 words = 32-bit float
                    temperature = ConvertRegistersToFloat(registers, _modbusItem.ByteOrder);
                }
                else if (_modbusItem.RegisterCount == 1)
                {
                    // 1 word = 16-bit integer (scaled by 10)
                    temperature = registers[0] / 10.0;
                }
                else
                {
                    throw new NotSupportedException($"RegisterCount {_modbusItem.RegisterCount} is not supported");
                }

                _modbusItem.TemperatureValue = temperature;

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
                // Connection lost, mark as disconnected
                _isConnected = false;
                throw new InvalidOperationException($"Modbus communication error: {ex.Message}", ex);
            }
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

            // Convert based on byte order
            switch (byteOrder)
            {
                case 0: // ABCD (Big-Endian)
                    bytes[0] = (byte)(registers[0] >> 8);
                    bytes[1] = (byte)(registers[0] & 0xFF);
                    bytes[2] = (byte)(registers[1] >> 8);
                    bytes[3] = (byte)(registers[1] & 0xFF);
                    break;

                case 1: // DCBA (Little-Endian)
                    bytes[0] = (byte)(registers[1] & 0xFF);
                    bytes[1] = (byte)(registers[1] >> 8);
                    bytes[2] = (byte)(registers[0] & 0xFF);
                    bytes[3] = (byte)(registers[0] >> 8);
                    break;

                case 2: // BADC (Big-Endian Byte Swap)
                    bytes[0] = (byte)(registers[0] & 0xFF);
                    bytes[1] = (byte)(registers[0] >> 8);
                    bytes[2] = (byte)(registers[1] & 0xFF);
                    bytes[3] = (byte)(registers[1] >> 8);
                    break;

                case 3: // CDAB (Little-Endian Byte Swap)
                    bytes[0] = (byte)(registers[1] >> 8);
                    bytes[1] = (byte)(registers[1] & 0xFF);
                    bytes[2] = (byte)(registers[0] >> 8);
                    bytes[3] = (byte)(registers[0] & 0xFF);
                    break;

                default:
                    throw new ArgumentException($"Invalid byte order: {byteOrder}");
            }

            // Convert bytes to float
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
            // Modicon address format:
            // 000001-099999: Coils (FC 01)
            // 100001-199999: Discrete Inputs (FC 02)
            // 300001-399999: Input Registers (FC 04)
            // 400001-499999: Holding Registers (FC 03)

            if (modiconAddress >= 400001 && modiconAddress <= 499999)
            {
                // Holding Registers
                functionCode = 3;
                return (ushort)(modiconAddress - 400001);
            }
            else if (modiconAddress >= 300001 && modiconAddress <= 399999)
            {
                // Input Registers
                functionCode = 4;
                return (ushort)(modiconAddress - 300001);
            }
            else if (modiconAddress >= 100001 && modiconAddress <= 199999)
            {
                // Discrete Inputs
                functionCode = 2;
                return (ushort)(modiconAddress - 100001);
            }
            else if (modiconAddress >= 1 && modiconAddress <= 99999)
            {
                // Coils
                functionCode = 1;
                return (ushort)(modiconAddress - 1);
            }
            else
            {
                // If not in Modicon format, treat as raw PDU address
                functionCode = 3; // Default to Holding Registers
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
            (_modbusCommunication as IDisposable)?.Dispose();
        }
    }
}
