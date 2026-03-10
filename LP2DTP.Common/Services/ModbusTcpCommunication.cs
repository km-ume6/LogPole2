using System;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace LP2DTP.Common.Services
{
    /// <summary>
    /// Modbus TCP communication implementation using connection manager
    /// </summary>
    public class ModbusTcpCommunication : IModbusCommunication, IDisposable
    {
        private string _ipAddress = string.Empty;
        private int _port = 502;
        private int _timeout = 5000;
        private bool _disposed = false;
        private readonly ModbusTcpConnectionManager _connectionManager = ModbusTcpConnectionManager.Instance;

        public bool IsConnected => !_disposed && !string.IsNullOrEmpty(_ipAddress);

        public async Task<bool> ConnectAsync(string ipAddress, int port = 502)
        {
            try
            {
                ThrowIfDisposed();

                _ipAddress = ipAddress;
                _port = port;

                return await _connectionManager.CheckConnectionAsync(_ipAddress, _port, _timeout);
            }
            catch
            {
                return false;
            }
        }

        public async Task DisconnectAsync()
        {
            if (!string.IsNullOrEmpty(_ipAddress))
            {
                await _connectionManager.CloseConnectionAsync(_ipAddress, _port);
            }
        }

        public async Task<ushort[]> ReadHoldingRegistersAsync(byte unitId, ushort startAddress, ushort count)
        {
            return await ReadRegistersAsync(unitId, 0x03, startAddress, count);
        }

        public async Task<ushort[]> ReadInputRegistersAsync(byte unitId, ushort startAddress, ushort count)
        {
            return await ReadRegistersAsync(unitId, 0x04, startAddress, count);
        }

        private async Task<ushort[]> ReadRegistersAsync(byte unitId, byte functionCode, ushort startAddress, ushort count)
        {
            ThrowIfDisposed();

            if (string.IsNullOrEmpty(_ipAddress))
            {
                throw new InvalidOperationException("Not connected to Modbus device");
            }

            return await _connectionManager.ExecuteAsync(_ipAddress, _port, async (tcpClient, stream, transactionId) =>
            {
                // Build Modbus TCP request
                var request = new byte[12];
                
                // MBAP Header
                request[0] = (byte)(transactionId >> 8);      // Transaction ID High
                request[1] = (byte)(transactionId & 0xFF);    // Transaction ID Low
                request[2] = 0x00;                             // Protocol ID High (0 = Modbus)
                request[3] = 0x00;                             // Protocol ID Low
                request[4] = 0x00;                             // Length High
                request[5] = 0x06;                             // Length Low (6 bytes following)
                
                // PDU
                request[6] = unitId;                           // Unit ID
                request[7] = functionCode;                     // Function Code
                request[8] = (byte)(startAddress >> 8);        // Start Address High
                request[9] = (byte)(startAddress & 0xFF);      // Start Address Low
                request[10] = (byte)(count >> 8);              // Quantity High
                request[11] = (byte)(count & 0xFF);            // Quantity Low

                // Send request
                await stream.WriteAsync(request, 0, request.Length);

                // Read response
                var header = new byte[9];
                var bytesRead = await stream.ReadAsync(header, 0, 9);
                
                if (bytesRead < 9)
                {
                    throw new InvalidOperationException("Incomplete Modbus response header");
                }

                // Verify transaction ID
                var responseTransactionId = (ushort)((header[0] << 8) | header[1]);
                if (responseTransactionId != transactionId)
                {
                    throw new InvalidOperationException($"Transaction ID mismatch: expected {transactionId}, got {responseTransactionId}");
                }

                // Check for exception
                if ((header[7] & 0x80) != 0)
                {
                    var exceptionCode = header[8];
                    throw new InvalidOperationException(
                        $"Modbus exception: {exceptionCode} ({GetModbusExceptionDescription(exceptionCode)}) " +
                        $"[Unit={unitId}, FC={functionCode}, Start={startAddress}, Count={count}]");
                }

                var byteCount = header[8];
                var data = new byte[byteCount];
                bytesRead = await stream.ReadAsync(data, 0, byteCount);

                if (bytesRead < byteCount)
                {
                    throw new InvalidOperationException(
                        $"Incomplete Modbus response data [Unit={unitId}, FC={functionCode}, Start={startAddress}, Count={count}, Expected={byteCount}, Actual={bytesRead}]");
                }

                // Convert bytes to ushort array
                var result = new ushort[count];
                for (int i = 0; i < count; i++)
                {
                    result[i] = (ushort)((data[i * 2] << 8) | data[i * 2 + 1]);
                }

                return result;
            }, _timeout);
        }

        private static string GetModbusExceptionDescription(byte exceptionCode)
        {
            return exceptionCode switch
            {
                1 => "Illegal Function",
                2 => "Illegal Data Address",
                3 => "Illegal Data Value",
                4 => "Slave Device Failure",
                5 => "Acknowledge",
                6 => "Slave Device Busy",
                8 => "Memory Parity Error",
                10 => "Gateway Path Unavailable",
                11 => "Gateway Target Device Failed to Respond",
                _ => "Unknown Exception"
            };
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(GetType().Name);
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
            {
                return;
            }

            if (disposing && !string.IsNullOrEmpty(_ipAddress))
            {
                _ = DisconnectAsync();
            }

            _disposed = true;
        }
    }
}
