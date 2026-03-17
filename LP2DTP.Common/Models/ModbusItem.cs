namespace LP2DTP.Common.Models
{
    /// <summary>
    /// Modbus TCP device item
    /// </summary>
    public class ModbusItem
    {
        /// <summary>
        /// Polling device information
        /// </summary>
        public PollingItem Device { get; set; } = new PollingItem();

        /// <summary>
        /// Modbus slave/unit ID
        /// </summary>
        public byte UnitId { get; set; } = 1;

        /// <summary>
        /// Temperature register address (supports Modicon addressing like 300001, 400001)
        /// </summary>
        public uint TemperatureRegisterAddress { get; set; }

        /// <summary>
        /// Modbus function code (default: 03 - Read Holding Registers)
        /// </summary>
        public byte FunctionCode { get; set; } = 3;

        /// <summary>
        /// Number of registers to read per value (2 for 32-bit float)
        /// </summary>
        public ushort RegisterCount { get; set; } = 2;

        /// <summary>
        /// Byte order for float conversion (0=ABCD, 1=DCBA, 2=BADC, 3=CDAB)
        /// </summary>
        public byte ByteOrder { get; set; } = 0;

        /// <summary>
        /// Temperature value (°C)
        /// </summary>
        public double TemperatureValue { get; set; }

        /// <summary>
        /// Item type
        /// </summary>
        public ModbusItemType ItemType { get; set; } = ModbusItemType.Keyence;

        /// <summary>
        /// Is enabled
        /// </summary>
        public bool IsEnabled { get; set; } = true;

        /// <summary>
        /// Default temperature value (°C) used when communication fails
        /// </summary>
        public double DefaultTemperatureValue { get; set; } = double.MinValue;
    }
}
