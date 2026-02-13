using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO.Ports;
using System.Numerics;
using System.Text;

namespace NANOVNACSharp
{
    /// <summary>
    /// Core interface for communicating with a NanoVNA H4 vector network analyzer
    /// over USB serial.
    ///
    /// Provides methods for configuring sweeps, reading S-parameter data,
    /// computing derived quantities (impedance, VSWR, return loss, TDR),
    /// and capturing the device screen.
    /// </summary>
    public class NanoVNA : IDisposable
    {
        private SerialPort _serial;
        private double[] _frequencies;
        private bool _disposed;

        /// <summary>
        /// Serial device path (e.g. "COM3").
        /// </summary>
        public string Dev { get; private set; }

        /// <summary>
        /// Number of sweep points.
        /// </summary>
        public int Points { get; set; }

        /// <summary>
        /// Current array of sweep frequency points in Hz.
        /// </summary>
        public double[] Frequencies
        {
            get { return _frequencies; }
        }

        /// <summary>
        /// Initialise a NanoVNA connection.
        /// </summary>
        /// <param name="dev">
        /// Serial device path. If null, auto-detected via <see cref="PortDetector.GetPort"/>.
        /// </param>
        public NanoVNA(string dev = null)
        {
            Dev = dev ?? PortDetector.GetPort();
            _serial = null;
            _frequencies = null;
            Points = Constants.DEFAULT_POINTS;
        }

        /// <summary>
        /// Open the serial connection to the device.
        /// If a connection is already open this method does nothing.
        /// </summary>
        public void Open()
        {
            if (_serial == null)
            {
                _serial = new SerialPort(Dev);
                _serial.Open();
            }
        }

        /// <summary>
        /// Close the serial connection to the device.
        /// Safe to call even if the connection is already closed.
        /// </summary>
        public void Close()
        {
            if (_serial != null)
            {
                if (_serial.IsOpen)
                {
                    try { _serial.DiscardInBuffer(); } catch { }
                    try { _serial.DiscardOutBuffer(); } catch { }
                    _serial.Close();
                }
                _serial.Dispose();
                _serial = null;
                // Allow USB driver time to fully release the port
                System.Threading.Thread.Sleep(1000);
            }
        }

        /// <summary>
        /// Send a command string to the NanoVNA over serial.
        ///
        /// Opens the serial port if it is not already open, writes the encoded
        /// command, and discards the initial empty-line echo.
        /// </summary>
        /// <param name="cmd">
        /// The command to send (should include trailing '\r').
        /// </param>
        public void SendCommand(string cmd)
        {
            Open();
            byte[] bytes = Encoding.ASCII.GetBytes(cmd);
            _serial.Write(bytes, 0, bytes.Length);
            _serial.ReadLine(); // discard empty echo line
        }

        /// <summary>
        /// Read response data from the device until the "ch>" prompt is received.
        ///
        /// Reads characters one at a time, accumulating lines until the
        /// prompt string is detected.
        /// </summary>
        /// <returns>The raw multi-line response text (excluding the prompt).</returns>
        public string FetchData()
        {
            StringBuilder result = new StringBuilder();
            StringBuilder line = new StringBuilder();

            while (true)
            {
                int b = _serial.ReadByte();
                if (b < 0)
                    break;

                char c = (char)b;

                if (c == '\r')
                    continue; // ignore CR

                line.Append(c);

                if (c == '\n')
                {
                    result.Append(line.ToString());
                    line.Clear();
                    continue;
                }

                if (line.Length >= 3 &&
                    line[line.Length - 3] == 'c' &&
                    line[line.Length - 2] == 'h' &&
                    line[line.Length - 1] == '>')
                {
                    // Stop on prompt
                    break;
                }
            }

            return result.ToString();
        }

        /// <summary>
        /// Set the sweep frequency range and number of points.
        /// </summary>
        /// <param name="start">Start frequency in Hz.</param>
        /// <param name="stop">Stop frequency in Hz.</param>
        /// <param name="points">
        /// Number of sweep points. If null, uses current <see cref="Points"/> value.
        /// </param>
        public void SetFrequencies(double start = 1e6, double stop = 900e6, int? points = null)
        {
            if (points.HasValue)
                Points = points.Value;
            _frequencies = MathHelpers.Linspace(start, stop, Points);
        }

        /// <summary>
        /// Set the hardware sweep start and stop frequencies.
        /// </summary>
        /// <param name="start">Start frequency in Hz, or null to skip.</param>
        /// <param name="stop">Stop frequency in Hz, or null to skip.</param>
        public void SetSweep(double? start, double? stop)
        {
            if (start.HasValue)
                SendCommand(string.Format("sweep start {0}\r", (long)start.Value));
            if (stop.HasValue)
                SendCommand(string.Format("sweep stop {0}\r", (long)stop.Value));
        }

        /// <summary>
        /// Set a single CW frequency on the device.
        /// </summary>
        /// <param name="freq">Frequency in Hz, or null to skip.</param>
        public void SetFrequency(double? freq)
        {
            if (freq.HasValue)
                SendCommand(string.Format("freq {0}\r", (long)freq.Value));
        }

        /// <summary>
        /// Select the measurement port on the device.
        /// </summary>
        /// <param name="port">Port number (0 or 1), or null to skip.</param>
        public void SetPort(int? port)
        {
            if (port.HasValue)
                SendCommand(string.Format("port {0}\r", port.Value));
        }

        /// <summary>
        /// Set the device IF gain.
        /// </summary>
        /// <param name="gain">Gain value applied to both channels, or null to skip.</param>
        public void SetGain(int? gain)
        {
            if (gain.HasValue)
                SendCommand(string.Format("gain {0} {1}\r", gain.Value, gain.Value));
        }

        /// <summary>
        /// Set the device frequency offset.
        /// </summary>
        /// <param name="offset">Offset value, or null to skip.</param>
        public void SetOffset(int? offset)
        {
            if (offset.HasValue)
                SendCommand(string.Format("offset {0}\r", offset.Value));
        }

        /// <summary>
        /// Set the device output power level.
        /// </summary>
        /// <param name="strength">Power level index, or null to skip.</param>
        public void SetStrength(int? strength)
        {
            if (strength.HasValue)
                SendCommand(string.Format("power {0}\r", strength.Value));
        }

        /// <summary>
        /// Fetch a raw sample buffer from the device.
        /// </summary>
        /// <param name="buffer">Buffer index to dump (default 0).</param>
        /// <returns>Array of int16 sample values.</returns>
        public short[] FetchBuffer(int buffer = 0)
        {
            SendCommand(string.Format("dump {0}\r", buffer));
            string data = FetchData();
            List<short> values = new List<short>();
            foreach (string line in data.Split('\n'))
            {
                string trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed))
                    continue;
                foreach (string hex in trimmed.Split(' '))
                {
                    if (!string.IsNullOrEmpty(hex))
                        values.Add((short)Convert.ToInt32(hex, 16));
                }
            }
            return values.ToArray();
        }

        /// <summary>
        /// Fetch raw I/Q waveform data from the device.
        /// </summary>
        /// <param name="freq">If specified, set CW frequency before reading.</param>
        /// <returns>Tuple of (I channel, Q channel) int16 arrays.</returns>
        public Tuple<short[], short[]> FetchRawWave(double? freq = null)
        {
            if (freq.HasValue)
            {
                SetFrequency(freq);
                System.Threading.Thread.Sleep(50);
            }

            SendCommand("dump 0\r");
            string data = FetchData();
            List<short> all = new List<short>();
            foreach (string line in data.Split('\n'))
            {
                string trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed))
                    continue;
                foreach (string hex in trimmed.Split(' '))
                {
                    if (!string.IsNullOrEmpty(hex))
                        all.Add((short)Convert.ToInt32(hex, 16));
                }
            }

            // Split into even (I) and odd (Q) interleaved samples
            List<short> iChannel = new List<short>();
            List<short> qChannel = new List<short>();
            for (int i = 0; i < all.Count; i++)
            {
                if (i % 2 == 0)
                    iChannel.Add(all[i]);
                else
                    qChannel.Add(all[i]);
            }
            return Tuple.Create(iChannel.ToArray(), qChannel.ToArray());
        }

        /// <summary>
        /// Fetch a processed data array from the device.
        /// </summary>
        /// <param name="sel">Data array selector (0 = S11, 1 = S21, etc.).</param>
        /// <returns>Complex array of measurement values.</returns>
        public Complex[] FetchArray(int sel)
        {
            SendCommand(string.Format("data {0}\r", sel));
            string data = FetchData();
            List<double> values = new List<double>();
            foreach (string line in data.Split('\n'))
            {
                string trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed))
                    continue;
                foreach (string s in trimmed.Split(' '))
                {
                    double v;
                    if (double.TryParse(s, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out v))
                        values.Add(v);
                }
            }

            Complex[] result = new Complex[values.Count / 2];
            for (int i = 0; i < result.Length; i++)
                result[i] = new Complex(values[i * 2], values[i * 2 + 1]);
            return result;
        }

        /// <summary>
        /// Fetch a single gamma (reflection coefficient) value from the device.
        /// </summary>
        /// <param name="freq">If specified, set CW frequency before reading.</param>
        /// <returns>The reflection coefficient as a complex number.</returns>
        public Complex FetchGamma(double? freq = null)
        {
            if (freq.HasValue)
                SetFrequency(freq);

            SendCommand("gamma\r");
            string data = _serial.ReadLine();
            string[] parts = data.Trim().Split(' ');
            double real = double.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture);
            double imag = double.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture);
            return new Complex(real, imag) / Constants.REF_LEVEL;
        }

        /// <summary>
        /// Fetch processed S-parameter data from the device.
        ///
        /// Uses robust parsing that skips non-numeric lines.
        /// </summary>
        /// <param name="array">Data array index (0 for S11, 1 for S21).</param>
        /// <returns>Complex S-parameter values.</returns>
        public Complex[] Data(int array = 0)
        {
            SendCommand(string.Format("data {0}\r", array));
            string data = FetchData();
            List<Complex> result = new List<Complex>();
            foreach (string line in data.Split('\n'))
            {
                string trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed))
                    continue;

                string[] parts = trimmed.Split(' ');
                if (parts.Length >= 2)
                {
                    double re, im;
                    if (double.TryParse(parts[0], System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out re) &&
                        double.TryParse(parts[1], System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out im))
                    {
                        result.Add(new Complex(re, im));
                    }
                    // Skip non-numeric lines (matches Python try/except behaviour)
                }
            }
            return result.ToArray();
        }

        /// <summary>
        /// Get current frequency array from device.
        ///
        /// Sends the "sweep" command which returns "start stop points".
        /// Parses the response and sets <see cref="Frequencies"/> and
        /// <see cref="Points"/> accordingly. Falls back to default 1 MHz - 900 MHz
        /// range if parsing fails.
        /// </summary>
        public void FetchFrequencies()
        {
            SendCommand("sweep\r");
            string data = FetchData();
            foreach (string line in data.Split('\n'))
            {
                string trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed))
                    continue;

                string[] parts = trimmed.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 3)
                {
                    double start, stop;
                    int points;
                    if (double.TryParse(parts[0], System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out start) &&
                        double.TryParse(parts[1], System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out stop) &&
                        int.TryParse(parts[2], out points))
                    {
                        _frequencies = MathHelpers.Linspace(start, stop, points);
                        Points = points;
                        return;
                    }
                }
            }

            // Fallback to defaults
            if (_frequencies == null)
                _frequencies = MathHelpers.Linspace(Constants.DEFAULT_START_HZ,
                    Constants.DEFAULT_STOP_HZ, Points);
        }

        /// <summary>
        /// Send a scan command to the device.
        /// </summary>
        /// <param name="start">Start frequency in Hz.</param>
        /// <param name="stop">Stop frequency in Hz.</param>
        /// <param name="points">Number of points. If null, omitted from the command.</param>
        public void SendScan(double start = 1e6, double stop = 900e6, int? points = null)
        {
            if (points.HasValue)
                SendCommand(string.Format("scan {0} {1} {2}\r", (long)start, (long)stop, points.Value));
            else
                SendCommand(string.Format("scan {0} {1}\r", (long)start, (long)stop));
        }

        /// <summary>
        /// Perform a segmented scan across the full frequency range.
        ///
        /// Splits the frequency range into segments of up to 101 points each,
        /// performs a scan for each segment, and concatenates the results.
        /// </summary>
        /// <returns>
        /// Tuple of two Complex arrays: (S11 data, S21 data).
        /// </returns>
        public Tuple<Complex[], Complex[]> Scan()
        {
            List<Complex> array0 = new List<Complex>();
            List<Complex> array1 = new List<Complex>();

            if (_frequencies == null)
                FetchFrequencies();

            double[] freqs = _frequencies;
            int offset = 0;

            while (offset < freqs.Length)
            {
                int remaining = freqs.Length - offset;
                int length = Math.Min(Constants.SEGMENT_LENGTH, remaining);

                double segStart = freqs[offset];
                double segStop = freqs[offset + length - 1];

                SendScan(segStart, segStop, length);
                array0.AddRange(Data(0));
                array1.AddRange(Data(1));

                offset += Constants.SEGMENT_LENGTH;
            }

            Resume();
            return Tuple.Create(array0.ToArray(), array1.ToArray());
        }

        /// <summary>
        /// Resume continuous sweep on the device.
        /// </summary>
        public void Resume()
        {
            SendCommand("resume\r");
        }

        /// <summary>
        /// Pause continuous sweep on the device.
        /// </summary>
        public void Pause()
        {
            SendCommand("pause\r");
        }

        /// <summary>
        /// Capture the current device display as a Bitmap.
        ///
        /// Reads the raw 480x320 16-bit RGB565 framebuffer from the device
        /// and converts it to a 32-bit ARGB Bitmap.
        /// </summary>
        /// <returns>The captured screen image.</returns>
        public Bitmap Capture()
        {
            SendCommand("capture\r");
            int totalPixels = Constants.SCREEN_WIDTH * Constants.SCREEN_HEIGHT;
            int totalBytes = totalPixels * 2;
            byte[] buffer = new byte[totalBytes];
            int bytesRead = 0;

            while (bytesRead < totalBytes)
            {
                int read = _serial.Read(buffer, bytesRead, totalBytes - bytesRead);
                if (read <= 0)
                    throw new InvalidOperationException("Failed to read capture data from device.");
                bytesRead += read;
            }

            // Convert RGB565 (big-endian) to ARGB
            Bitmap bmp = new Bitmap(Constants.SCREEN_WIDTH, Constants.SCREEN_HEIGHT, PixelFormat.Format32bppArgb);
            int idx = 0;
            for (int y = 0; y < Constants.SCREEN_HEIGHT; y++)
            {
                for (int x = 0; x < Constants.SCREEN_WIDTH; x++)
                {
                    // Big-endian 16-bit read
                    ushort pixel = (ushort)((buffer[idx] << 8) | buffer[idx + 1]);
                    idx += 2;

                    // RGB565 -> 8-bit components
                    int r = ((pixel >> 11) & 0x1F) << 3;
                    int g = ((pixel >> 5) & 0x3F) << 2;
                    int b = (pixel & 0x1F) << 3;

                    bmp.SetPixel(x, y, Color.FromArgb(255, r, g, b));
                }
            }

            return bmp;
        }

        /// <summary>
        /// Capture the device screen and save it to a file.
        /// </summary>
        /// <param name="filePath">Output image file path (format inferred from extension).</param>
        public void CaptureToFile(string filePath)
        {
            using (Bitmap bmp = Capture())
            {
                bmp.Save(filePath);
            }
        }

        // ------------------------------------------------------------------
        // Data computation methods (no plotting - return arrays)
        // ------------------------------------------------------------------

        /// <summary>
        /// Convert reflection coefficient (gamma) to impedance.
        /// Z = Z0 * (1 + gamma) / (1 - gamma).
        /// </summary>
        /// <param name="sData">Complex reflection coefficient data.</param>
        /// <param name="z0">Reference impedance in ohms (default 50).</param>
        /// <returns>Complex impedance values.</returns>
        public Complex[] Impedance(Complex[] sData, double z0 = 50.0)
        {
            return MathHelpers.ComputeImpedance(sData, z0);
        }

        /// <summary>
        /// Compute log magnitude in dB from S-parameter data.
        /// </summary>
        /// <param name="sData">Complex S-parameter data.</param>
        /// <returns>Magnitude in dB at each frequency point.</returns>
        public double[] GetLogMagnitude(Complex[] sData)
        {
            return MathHelpers.LogMagnitude(sData);
        }

        /// <summary>
        /// Compute linear magnitude from S-parameter data.
        /// </summary>
        /// <param name="sData">Complex S-parameter data.</param>
        /// <returns>Linear magnitude at each frequency point.</returns>
        public double[] GetLinearMagnitude(Complex[] sData)
        {
            return MathHelpers.LinearMagnitude(sData);
        }

        /// <summary>
        /// Compute phase in degrees from S-parameter data.
        /// </summary>
        /// <param name="sData">Complex S-parameter data.</param>
        /// <param name="unwrap">If true, unwrap the phase.</param>
        /// <returns>Phase in degrees at each frequency point.</returns>
        public double[] GetPhase(Complex[] sData, bool unwrap = false)
        {
            double[] angle = MathHelpers.Angle(sData);
            if (unwrap)
                angle = MathHelpers.Unwrap(angle);
            return MathHelpers.RadToDeg(angle);
        }

        /// <summary>
        /// Compute propagation delay from S-parameter data.
        /// </summary>
        /// <param name="sData">Complex S-parameter data.</param>
        /// <returns>Delay in seconds at each frequency point.</returns>
        public double[] GetDelay(Complex[] sData)
        {
            return MathHelpers.ComputeDelay(sData, _frequencies);
        }

        /// <summary>
        /// Compute group delay from S-parameter data.
        /// </summary>
        /// <param name="sData">Complex S-parameter data.</param>
        /// <returns>Group delay at each frequency point.</returns>
        public double[] GetGroupDelay(Complex[] sData)
        {
            return MathHelpers.ComputeGroupDelay(sData);
        }

        /// <summary>
        /// Compute VSWR from reflection coefficient data.
        /// </summary>
        /// <param name="sData">Complex reflection coefficient data.</param>
        /// <returns>VSWR at each frequency point.</returns>
        public double[] GetVSWR(Complex[] sData)
        {
            return MathHelpers.ComputeVSWR(sData);
        }

        /// <summary>
        /// Compute time-domain reflectometry (TDR) response.
        ///
        /// Applies a Blackman window and computes the inverse FFT to convert
        /// frequency-domain data to the time domain.
        /// </summary>
        /// <param name="sData">Complex S-parameter data.</param>
        /// <param name="nfft">FFT size (default 256).</param>
        /// <returns>
        /// Tuple of (time axis in seconds, magnitude array).
        /// </returns>
        public Tuple<double[], double[]> GetTDR(Complex[] sData, int nfft = 256)
        {
            double[] window = MathHelpers.BlackmanWindow(sData.Length);
            double[] magnitude = MathHelpers.InverseFFTMagnitude(sData, window, nfft);

            double timeSpan = 1.0 / (_frequencies[1] - _frequencies[0]);
            double[] timeAxis = MathHelpers.Linspace(0, timeSpan, nfft);

            return Tuple.Create(timeAxis, magnitude);
        }

        /// <summary>
        /// Send a raw command and return the response text.
        /// </summary>
        /// <param name="command">Command string (without trailing '\r').</param>
        /// <returns>Response text from the device.</returns>
        public string SendRawCommand(string command)
        {
            SendCommand(command + "\r");
            return FetchData();
        }

        // ------------------------------------------------------------------
        // Static computation helpers
        // ------------------------------------------------------------------

        /// <summary>
        /// Compute VSWR from complex S-parameter data (static helper).
        /// </summary>
        public static double[] ComputeVSWR(Complex[] sData)
        {
            return MathHelpers.ComputeVSWR(sData);
        }

        /// <summary>
        /// Compute return loss in dB from complex S-parameter data (static helper).
        /// </summary>
        public static double[] ComputeReturnLoss(Complex[] sData)
        {
            return MathHelpers.ComputeReturnLoss(sData);
        }

        /// <summary>
        /// Compute insertion loss in dB from complex S21 data (static helper).
        /// </summary>
        public static double[] ComputeInsertionLoss(Complex[] sData)
        {
            return MathHelpers.ComputeInsertionLoss(sData);
        }

        /// <summary>
        /// Dispose of the serial connection and release resources.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Dispose of managed and unmanaged resources.
        /// </summary>
        /// <param name="disposing">True if called from Dispose(), false from finalizer.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                    Close();
                _disposed = true;
            }
        }

        /// <summary>
        /// Finalizer to ensure serial port is closed.
        /// </summary>
        ~NanoVNA()
        {
            Dispose(false);
        }
    }
}
