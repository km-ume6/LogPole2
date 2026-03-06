using System;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace LP2DTP.Common.Services
{
    /// <summary>
    /// Modbus TCP communication implementation
    /// </summary>
    public class ModbusTcpCommunication : IModbusCommunication, IDisposable
    {
        private TcpClient? _tcpClient;
        private NetworkStream? _networkStream;
        private string _ipAddress = string.Empty;
        private int _port = 502;
        private int _timeout = 5000;
        private ushort _transactionId = 0;
        private readonly object _lockObject = new object();

        public bool IsConnected => _tcpClient?.Connected ?? false;

        public async Task<bool> ConnectAsync(string ipAddress, int port = 502)
        {
            try
            {
                _ipAddress = ipAddress;
                _port = port;

                await DisconnectAsync();

                _tcpClient = new TcpClient();
                _tcpClient.SendTimeout = _timeout;
                _tcpClient.ReceiveTimeout = _timeout;

                await _tcpClient.ConnectAsync(_ipAddress, _port);
                _networkStream = _tcpClient.GetStream();

                return true;
            }
            catch
            {
                await DisconnectAsync();
                return false;
            }
        }

        public async Task DisconnectAsync()
        {
            try
            {
                if (_networkStream != null)
                {
                    await _networkStream.DisposeAsync();
                    _networkStream = null;
                }

                if (_tcpClient != null)
                {
                    _tcpClient.Close();
                    _tcpClient.Dispose();
                    _tcpClient = null;
                }
            }
            catch
            {
                // Ignore errors during cleanup
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
            if (!IsConnected || _networkStream == null)
            {
                throw new InvalidOperationException("Not connected to Modbus device");
            }

            lock (_lockObject)
            {
                _transactionId++;
            }

            // Build Modbus TCP request
            var request = new byte[12];
            
            // MBAP Header
            request[0] = (byte)(_transactionId >> 8);      // Transaction ID High
            request[1] = (byte)(_transactionId & 0xFF);    // Transaction ID Low
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
            await _networkStream.WriteAsync(request, 0, request.Length);

            // Read response
            var header = new byte[9];
            var bytesRead = await _networkStream.ReadAsync(header, 0, 9);
            
            if (bytesRead < 9)
            {
                throw new InvalidOperationException("Incomplete Modbus response header");
            }

            // Verify transaction ID
            var responseTransactionId = (ushort)((header[0] << 8) | header[1]);
            if (responseTransactionId != _transactionId)
            {
                throw new InvalidOperationException("Transaction ID mismatch");
            }

            // Check for exception
            if ((header[7] & 0x80) != 0)
            {
                var exceptionCode = header[8];
                throw new InvalidOperationException($"Modbus exception: {exceptionCode}");
            }

            var byteCount = header[8];
            var data = new byte[byteCount];
            bytesRead = await _networkStream.ReadAsync(data, 0, byteCount);

            if (bytesRead < byteCount)
            {
                throw new InvalidOperationException("Incomplete Modbus response data");
            }

            // Convert bytes to ushort array
            var result = new ushort[count];
            for (int i = 0; i < count; i++)
            {
                result[i] = (ushort)((data[i * 2] << 8) | data[i * 2 + 1]);
            }

            return result;
        }

        public void Dispose()
        {
            DisconnectAsync().ConfigureAwait(false).GetAwaiter().GetResult();
        }
    }
}
