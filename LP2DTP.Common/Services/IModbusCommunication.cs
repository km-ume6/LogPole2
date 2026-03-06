using System.Threading.Tasks;

namespace LP2DTP.Common.Services
{
    /// <summary>
    /// Interface for Modbus TCP communication
    /// </summary>
    public interface IModbusCommunication
    {
        /// <summary>
        /// Connect to Modbus TCP device
        /// </summary>
        Task<bool> ConnectAsync(string ipAddress, int port = 502);

        /// <summary>
        /// Disconnect from device
        /// </summary>
        Task DisconnectAsync();

        /// <summary>
        /// Read holding registers (Function Code 03)
        /// </summary>
        Task<ushort[]> ReadHoldingRegistersAsync(byte unitId, ushort startAddress, ushort count);

        /// <summary>
        /// Read input registers (Function Code 04)
        /// </summary>
        Task<ushort[]> ReadInputRegistersAsync(byte unitId, ushort startAddress, ushort count);

        /// <summary>
        /// Check if connected
        /// </summary>
        bool IsConnected { get; }
    }
}
