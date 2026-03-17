namespace LP2DTP.Common.Models
{
    /// <summary>
    /// VISA device item
    /// </summary>
    public class VisaItem
    {
        /// <summary>
        /// Polling device information
        /// </summary>
        public PollingItem Device { get; set; } = new PollingItem();

        /// <summary>
        /// Command A
        /// </summary>
        public string CommandCurr { get; set; } = string.Empty;

        /// <summary>
        /// Command V
        /// </summary>
        public string CommandVolt { get; set; } = string.Empty;

        /// <summary>
        /// Current value (A)
        /// </summary>
        public double CurrentValue { get; set; }

        /// <summary>
        /// Voltage value (V)
        /// </summary>
        public double VoltageValue { get; set; }

        /// <summary>
        /// Item type
        /// </summary>
        public VisaItemType ItemType { get; set; } = VisaItemType.Device;

        /// <summary>
        /// Is enabled
        /// </summary>
        public bool IsEnabled { get; set; } = true;

        /// <summary>
        /// Default current value (A) used when communication fails
        /// </summary>
        public double DefaultCurrentValue { get; set; } = -999.0;

        /// <summary>
        /// Default voltage value (V) used when communication fails
        /// </summary>
        public double DefaultVoltageValue { get; set; } = -999.0;
    }
}
