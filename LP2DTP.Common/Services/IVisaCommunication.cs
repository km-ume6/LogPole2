using System;
using System.Threading.Tasks;

namespace LP2DTP.Common.Services
{
    /// <summary>
    /// Interface for VISA communication
    /// </summary>
    public interface IVisaCommunication
    {
        /// <summary>
        /// Open connection to device
        /// </summary>
        Task<bool> OpenAsync(string resourceName);

        /// <summary>
        /// Close connection
        /// </summary>
        Task CloseAsync();

        /// <summary>
        /// Write command to device
        /// </summary>
        Task WriteAsync(string command);

        /// <summary>
        /// Read response from device
        /// </summary>
        Task<string> ReadAsync();

        /// <summary>
        /// Query device (Write + Read)
        /// </summary>
        Task<string> QueryAsync(string command);

        /// <summary>
        /// Check if connected
        /// </summary>
        bool IsConnected { get; }
    }
}
