using System;
using System.IO.Ports;
using System.Management;
using System.Text.RegularExpressions;

namespace NANOVNACSharp
{
    /// <summary>
    /// Detects the NanoVNA H4 COM port by querying WMI for the
    /// USB VID/PID (0x0483 / 0x5740).
    /// </summary>
    public static class PortDetector
    {
        /// <summary>
        /// Auto-detect the NanoVNA H4 serial port by USB VID/PID.
        ///
        /// Queries WMI Win32_PnPEntity for a device whose PnPDeviceID contains
        /// "VID_0483&amp;PID_5740" and extracts the COM port number from the
        /// device Name field.
        /// </summary>
        /// <returns>The COM port name (e.g. "COM3").</returns>
        /// <exception cref="InvalidOperationException">
        /// Thrown when no matching device is found.
        /// </exception>
        public static string GetPort()
        {
            string vidPid = string.Format("VID_{0:X4}&PID_{1:X4}",
                Constants.VID, Constants.PID);

            using (var searcher = new ManagementObjectSearcher(
                "SELECT Name, PNPDeviceID FROM Win32_PnPEntity WHERE PNPDeviceID LIKE '%" + vidPid + "%'"))
            {
                foreach (ManagementObject obj in searcher.Get())
                {
                    string name = obj["Name"] as string;
                    if (name != null)
                    {
                        // Extract COMx from the Name field, e.g. "STMicroelectronics Virtual COM Port (COM3)"
                        Match match = Regex.Match(name, @"\(COM(\d+)\)");
                        if (match.Success)
                            return "COM" + match.Groups[1].Value;
                    }
                }
            }

            throw new InvalidOperationException(
                "NanoVNA H4 device not found (VID=0x0483, PID=0x5740).");
        }

        /// <summary>
        /// Get all available serial port names on the system.
        /// </summary>
        /// <returns>Array of port names (e.g. ["COM1", "COM3"]).</returns>
        public static string[] GetAvailablePorts()
        {
            return SerialPort.GetPortNames();
        }
    }
}
