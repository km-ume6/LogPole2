using System;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace LP2DTP.Common.Services
{
    /// <summary>
    /// Manages Modbus TCP connections per IP address to prevent parallel access conflicts
    /// </summary>
    public class ModbusTcpConnectionManager
    {
        private static readonly Lazy<ModbusTcpConnectionManager> _instance = new(() => new ModbusTcpConnectionManager());
        private readonly ConcurrentDictionary<string, ConnectionInfo> _connections = new();
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _semaphores = new();

        public static ModbusTcpConnectionManager Instance => _instance.Value;

        private ModbusTcpConnectionManager() { }

        /// <summary>
        /// Execute a Modbus operation with exclusive access to the IP address
        /// </summary>
        public async Task<T> ExecuteAsync<T>(string ipAddress, int port, Func<TcpClient, NetworkStream, ushort, Task<T>> operation, int timeoutMs = 5000)
        {
            var semaphore = _semaphores.GetOrAdd(ipAddress, _ => new SemaphoreSlim(1, 1));

            await semaphore.WaitAsync();
            try
            {
                var connectionInfo = await GetOrCreateConnectionAsync(ipAddress, port, timeoutMs);

                if (!connectionInfo.IsConnected)
                {
                    throw new InvalidOperationException($"Cannot connect to {ipAddress}:{port}");
                }

                var transactionId = connectionInfo.GetNextTransactionId();

                try
                {
                    return await operation(connectionInfo.TcpClient, connectionInfo.NetworkStream, transactionId);
                }
                catch
                {
                    RemoveAndDisposeConnection(ipAddress, port);
                    throw;
                }
            }
            finally
            {
                semaphore.Release();
            }
        }

        /// <summary>
        /// Check if connection is alive
        /// </summary>
        public async Task<bool> CheckConnectionAsync(string ipAddress, int port = 502, int timeoutMs = 5000)
        {
            var semaphore = _semaphores.GetOrAdd(ipAddress, _ => new SemaphoreSlim(1, 1));

            await semaphore.WaitAsync();
            try
            {
                var key = GetConnectionKey(ipAddress, port);

                if (_connections.TryGetValue(key, out var connectionInfo))
                {
                    if (connectionInfo.IsConnected)
                    {
                        return true;
                    }

                    _connections.TryRemove(key, out _);
                    connectionInfo.Dispose();
                }

                var testClient = new TcpClient();
                testClient.SendTimeout = timeoutMs;
                testClient.ReceiveTimeout = timeoutMs;

                try
                {
                    await testClient.ConnectAsync(ipAddress, port);
                    testClient.Close();
                    return true;
                }
                catch
                {
                    return false;
                }
            }
            finally
            {
                semaphore.Release();
            }
        }

        /// <summary>
        /// Close connection for specific IP address
        /// </summary>
        public async Task CloseConnectionAsync(string ipAddress, int port = 502)
        {
            var semaphore = _semaphores.GetOrAdd(ipAddress, _ => new SemaphoreSlim(1, 1));

            await semaphore.WaitAsync();
            try
            {
                RemoveAndDisposeConnection(ipAddress, port);
            }
            finally
            {
                semaphore.Release();
            }
        }

        /// <summary>
        /// Close all connections
        /// </summary>
        public void CloseAllConnections()
        {
            foreach (var connectionInfo in _connections.Values)
            {
                try
                {
                    connectionInfo.Dispose();
                }
                catch
                {
                    // Ignore errors during cleanup
                }
            }

            _connections.Clear();
        }

        private async Task<ConnectionInfo> GetOrCreateConnectionAsync(string ipAddress, int port, int timeoutMs)
        {
            var key = GetConnectionKey(ipAddress, port);

            if (_connections.TryGetValue(key, out var connectionInfo) && connectionInfo.IsConnected)
            {
                return connectionInfo;
            }

            if (connectionInfo != null)
            {
                _connections.TryRemove(key, out _);
                connectionInfo.Dispose();
            }

            var newConnectionInfo = new ConnectionInfo();
            var tcpClient = new TcpClient();
            tcpClient.SendTimeout = timeoutMs;
            tcpClient.ReceiveTimeout = timeoutMs;

            try
            {
                await tcpClient.ConnectAsync(ipAddress, port);
                var networkStream = tcpClient.GetStream();

                newConnectionInfo.Initialize(tcpClient, networkStream);
                _connections[key] = newConnectionInfo;

                return newConnectionInfo;
            }
            catch
            {
                tcpClient.Dispose();
                throw;
            }
        }

        private string GetConnectionKey(string ipAddress, int port)
        {
            return $"{ipAddress}:{port}";
        }

        private void RemoveAndDisposeConnection(string ipAddress, int port)
        {
            var key = GetConnectionKey(ipAddress, port);
            if (_connections.TryRemove(key, out var connectionInfo))
            {
                connectionInfo.Dispose();
            }
        }

        private class ConnectionInfo : IDisposable
        {
            private TcpClient? _tcpClient;
            private NetworkStream? _networkStream;
            private ushort _transactionId;
            private readonly object _lock = new();
            private bool _disposed;

            public TcpClient TcpClient => _tcpClient ?? throw new InvalidOperationException("Not connected");
            public NetworkStream NetworkStream => _networkStream ?? throw new InvalidOperationException("Not connected");

            public bool IsConnected
            {
                get
                {
                    lock (_lock)
                    {
                        if (_disposed || _tcpClient == null || !_tcpClient.Connected)
                        {
                            return false;
                        }

                        try
                        {
                            var socket = _tcpClient.Client;
                            return !(socket.Poll(0, SelectMode.SelectRead) && socket.Available == 0);
                        }
                        catch
                        {
                            return false;
                        }
                    }
                }
            }

            public void Initialize(TcpClient tcpClient, NetworkStream networkStream)
            {
                lock (_lock)
                {
                    _tcpClient = tcpClient;
                    _networkStream = networkStream;
                    _transactionId = 0;
                }
            }

            public ushort GetNextTransactionId()
            {
                lock (_lock)
                {
                    _transactionId++;
                    if (_transactionId == 0) // Wrap around, skip 0
                    {
                        _transactionId = 1;
                    }
                    return _transactionId;
                }
            }

            public void Dispose()
            {
                lock (_lock)
                {
                    if (_disposed)
                    {
                        return;
                    }

                    try
                    {
                        _networkStream?.Dispose();
                        _tcpClient?.Dispose();
                    }
                    catch
                    {
                        // Ignore errors during cleanup
                    }

                    _networkStream = null;
                    _tcpClient = null;
                    _disposed = true;
                }
            }
        }
    }
}
