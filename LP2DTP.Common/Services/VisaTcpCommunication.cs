using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace LP2DTP.Common.Services
{
    /// <summary>
    /// VISA communication implementation using TCP/IP SCPI
    /// </summary>
    public class VisaTcpCommunication : IVisaCommunication, IDisposable
    {
        private TcpClient? _tcpClient;
        private NetworkStream? _networkStream;
        private StreamReader? _streamReader;
        private StreamWriter? _streamWriter;
        private string _ipAddress = string.Empty;
        private int _port = 5025; // Default SCPI port
        private int _timeout = 5000; // 5 seconds timeout

        public bool IsConnected => _tcpClient?.Connected ?? false;

        /// <summary>
        /// Open connection to device
        /// </summary>
        /// <param name="resourceName">Format: "TCPIP::192.168.1.100::5025::SOCKET" or "192.168.1.100"</param>
        public async Task<bool> OpenAsync(string resourceName)
        {
            try
            {
                // Parse resource name
                ParseResourceName(resourceName);

                // Close existing connection if any
                await CloseAsync();

                // Create new TCP connection
                _tcpClient = new TcpClient();
                _tcpClient.SendTimeout = _timeout;
                _tcpClient.ReceiveTimeout = _timeout;

                await _tcpClient.ConnectAsync(_ipAddress, _port);

                _networkStream = _tcpClient.GetStream();
                _streamReader = new StreamReader(_networkStream, Encoding.ASCII);
                _streamWriter = new StreamWriter(_networkStream, Encoding.ASCII)
                {
                    AutoFlush = true
                };

                return true;
            }
            catch (Exception)
            {
                await CloseAsync();
                return false;
            }
        }

        /// <summary>
        /// Close connection
        /// </summary>
        public async Task CloseAsync()
        {
            try
            {
                if (_streamWriter != null)
                {
                    await _streamWriter.DisposeAsync();
                    _streamWriter = null;
                }

                if (_streamReader != null)
                {
                    _streamReader.Dispose();
                    _streamReader = null;
                }

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

        /// <summary>
        /// Write command to device
        /// </summary>
        public async Task WriteAsync(string command)
        {
            if (!IsConnected || _streamWriter == null)
            {
                throw new InvalidOperationException("Not connected to device");
            }

            if (string.IsNullOrWhiteSpace(command))
            {
                throw new ArgumentException("Command cannot be empty", nameof(command));
            }

            await _streamWriter.WriteLineAsync(command);
        }

        /// <summary>
        /// Read response from device
        /// </summary>
        public async Task<string> ReadAsync()
        {
            if (!IsConnected || _streamReader == null)
            {
                throw new InvalidOperationException("Not connected to device");
            }

            var response = await _streamReader.ReadLineAsync();
            return response?.Trim() ?? string.Empty;
        }

        /// <summary>
        /// Query device (Write + Read)
        /// </summary>
        public async Task<string> QueryAsync(string command)
        {
            await WriteAsync(command);
            await Task.Delay(50); // Small delay for device to process
            return await ReadAsync();
        }

        private void ParseResourceName(string resourceName)
        {
            if (string.IsNullOrWhiteSpace(resourceName))
            {
                throw new ArgumentException("Resource name cannot be empty", nameof(resourceName));
            }

            // Parse VISA resource string format: "TCPIP::192.168.1.100::5025::SOCKET"
            if (resourceName.StartsWith("TCPIP", StringComparison.OrdinalIgnoreCase))
            {
                var parts = resourceName.Split(new[] { "::" }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2)
                {
                    _ipAddress = parts[1];
                    if (parts.Length >= 3 && int.TryParse(parts[2], out int port))
                    {
                        _port = port;
                    }
                }
            }
            else
            {
                // Simple IP address format
                var parts = resourceName.Split(':');
                _ipAddress = parts[0];
                if (parts.Length > 1 && int.TryParse(parts[1], out int port))
                {
                    _port = port;
                }
            }

            if (string.IsNullOrWhiteSpace(_ipAddress))
            {
                throw new ArgumentException("Invalid resource name format", nameof(resourceName));
            }
        }

        public void Dispose()
        {
            try
            {
                CloseAsync().ConfigureAwait(false).GetAwaiter().GetResult();
            }
            catch
            {
                // Ignore dispose errors
            }
        }
    }
}
