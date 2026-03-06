namespace LP2DTP.Common.Models
{
    /// <summary>
    /// Polling device information
    /// </summary>
    public class PollingItem
    {
        /// <summary>
        /// Device name
        /// </summary>
        public string MachineName { get; set; } = string.Empty;

        /// <summary>
        /// Unit name
        /// </summary>
        public string UnitName { get; set; } = string.Empty;

        /// <summary>
        /// IPv4 address
        /// </summary>
        public string IpAddress { get; set; } = string.Empty;
    }
}
