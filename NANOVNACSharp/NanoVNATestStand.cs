using System;
using System.Numerics;

namespace NANOVNACSharp
{
    /// <summary>
    /// High-level facade for NI TestStand integration.
    ///
    /// Exposes simple-type methods (bool, double[], string) that TestStand
    /// can call directly without needing to handle Complex types. All serial
    /// operations are protected by a lock for thread safety.
    /// </summary>
    public class NanoVNATestStand : IDisposable
    {
        private NanoVNA _nv;
        private readonly object _lock = new object();
        private bool _disposed;

        /// <summary>
        /// The most recent measurement data, or null if no measurement has been taken.
        /// </summary>
        public MeasurementData LastMeasurement { get; private set; }

        /// <summary>
        /// The most recent threshold evaluation result, or null.
        /// </summary>
        public EvaluationResult LastEvaluation { get; private set; }

        /// <summary>
        /// Whether the device is currently connected.
        /// </summary>
        public bool IsConnected
        {
            get { return _nv != null; }
        }

        /// <summary>
        /// Auto-detect the NanoVNA H4 COM port.
        /// </summary>
        /// <returns>The detected COM port name (e.g. "COM3").</returns>
        public string DetectPort()
        {
            return PortDetector.GetPort();
        }

        /// <summary>
        /// Connect to the NanoVNA H4 on the specified COM port.
        /// If <paramref name="comPort"/> is null or empty, auto-detects the port.
        /// </summary>
        /// <param name="comPort">COM port name, or null for auto-detect.</param>
        public void Connect(string comPort = null)
        {
            lock (_lock)
            {
                if (_nv != null)
                    Disconnect();

                _nv = new NanoVNA(string.IsNullOrEmpty(comPort) ? null : comPort);
                _nv.Open();

                // Warm-up: discard first data read so the device has time to
                // complete its initial sweep after being plugged in.
                try
                {
                    _nv.FetchFrequencies();
                    _nv.Data(0);
                }
                catch { /* ignore warm-up errors */ }
            }
        }

        /// <summary>
        /// Connect to the device and return a status string for diagnostics.
        /// Returns "OK" on success or the exception message on failure.
        /// </summary>
        /// <param name="comPort">COM port name, or null for auto-detect.</param>
        /// <returns>"OK" on success, or the error message on failure.</returns>
        public string ConnectWithStatus(string comPort = null)
        {
            try
            {
                Connect(comPort);
                return "OK";
            }
            catch (Exception ex)
            {
                return ex.GetType().Name + ": " + ex.Message;
            }
        }

        /// <summary>
        /// Disconnect from the NanoVNA H4.
        /// </summary>
        public void Disconnect()
        {
            lock (_lock)
            {
                if (_nv != null)
                {
                    _nv.Close();
                    _nv.Dispose();
                    _nv = null;
                }
            }
        }

        /// <summary>
        /// Perform a measurement and evaluate against thresholds.
        ///
        /// Connects to the device (if not already connected), configures the sweep,
        /// acquires data, computes all derived quantities, evaluates thresholds,
        /// and stores results in <see cref="LastMeasurement"/> and <see cref="LastEvaluation"/>.
        /// </summary>
        /// <param name="startHz">Sweep start frequency in Hz.</param>
        /// <param name="stopHz">Sweep stop frequency in Hz.</param>
        /// <param name="points">Number of sweep points.</param>
        /// <param name="port">Measurement port (0 = S11, 1 = S21).</param>
        /// <param name="maxVSWR">Maximum allowed VSWR, or 0 to skip.</param>
        /// <param name="minRL">Minimum return loss in dB, or 0 to skip.</param>
        /// <param name="maxIL">Maximum insertion loss in dB, or 0 to skip.</param>
        /// <param name="freqRangeStart">Threshold sub-band start in Hz, or 0 for full range.</param>
        /// <param name="freqRangeStop">Threshold sub-band stop in Hz, or 0 for full range.</param>
        /// <param name="z0">Reference impedance in ohms.</param>
        /// <returns>True if all thresholds pass (or no thresholds set), false otherwise.</returns>
        public bool MeasureAndEvaluate(
            double startHz = 1e6,
            double stopHz = 900e6,
            int points = 101,
            int port = 0,
            double maxVSWR = 0,
            double minRL = 0,
            double maxIL = 0,
            double freqRangeStart = 0,
            double freqRangeStop = 0,
            double z0 = 50.0)
        {
            lock (_lock)
            {
                EnsureConnected();

                // Configure and acquire
                Complex[] sData = AcquireData(startHz, stopHz, points, port);

                // Build measurement data
                LastMeasurement = BuildMeasurementData(sData, port, z0);

                // Evaluate thresholds
                double? maxVswrParam = maxVSWR > 0 ? (double?)maxVSWR : null;
                double? minRlParam = minRL > 0 ? (double?)minRL : null;
                double? maxIlParam = maxIL > 0 ? (double?)maxIL : null;
                double? freqStart = freqRangeStart > 0 ? (double?)freqRangeStart : null;
                double? freqStop = freqRangeStop > 0 ? (double?)freqRangeStop : null;

                if (maxVswrParam.HasValue || minRlParam.HasValue || maxIlParam.HasValue)
                {
                    LastEvaluation = ThresholdEvaluator.Evaluate(
                        LastMeasurement.Frequencies, sData,
                        maxVswrParam, minRlParam, maxIlParam,
                        freqStart, freqStop);
                }
                else
                {
                    LastEvaluation = new EvaluationResult
                    {
                        Result = "PASS",
                        ExitCode = Constants.EXIT_SUCCESS
                    };
                }

                return LastEvaluation.Result == "PASS";
            }
        }

        /// <summary>
        /// Perform a measurement and return the result as a JSON string
        /// conforming to the v1.0 schema.
        /// </summary>
        /// <param name="startHz">Sweep start frequency in Hz.</param>
        /// <param name="stopHz">Sweep stop frequency in Hz.</param>
        /// <param name="points">Number of sweep points.</param>
        /// <param name="port">Measurement port (0 = S11, 1 = S21).</param>
        /// <param name="maxVSWR">Maximum allowed VSWR, or 0 to skip.</param>
        /// <param name="minRL">Minimum return loss in dB, or 0 to skip.</param>
        /// <param name="maxIL">Maximum insertion loss in dB, or 0 to skip.</param>
        /// <param name="freqRangeStart">Threshold sub-band start in Hz, or 0 for full range.</param>
        /// <param name="freqRangeStop">Threshold sub-band stop in Hz, or 0 for full range.</param>
        /// <param name="z0">Reference impedance in ohms.</param>
        /// <returns>JSON string with measurement data and evaluation results.</returns>
        public string MeasureToJson(
            double startHz = 1e6,
            double stopHz = 900e6,
            int points = 101,
            int port = 0,
            double maxVSWR = 0,
            double minRL = 0,
            double maxIL = 0,
            double freqRangeStart = 0,
            double freqRangeStop = 0,
            double z0 = 50.0)
        {
            MeasureAndEvaluate(startHz, stopHz, points, port,
                maxVSWR, minRL, maxIL, freqRangeStart, freqRangeStop, z0);

            return OutputFormatters.ToJson(LastMeasurement, LastEvaluation);
        }

        /// <summary>
        /// Perform a measurement and save results to a CSV file.
        /// </summary>
        /// <param name="filePath">Output CSV file path.</param>
        /// <param name="startHz">Sweep start frequency in Hz.</param>
        /// <param name="stopHz">Sweep stop frequency in Hz.</param>
        /// <param name="points">Number of sweep points.</param>
        /// <param name="port">Measurement port (0 = S11, 1 = S21).</param>
        /// <param name="z0">Reference impedance in ohms.</param>
        public void MeasureToCsv(
            string filePath,
            double startHz = 1e6,
            double stopHz = 900e6,
            int points = 101,
            int port = 0,
            double z0 = 50.0)
        {
            lock (_lock)
            {
                EnsureConnected();

                Complex[] sData = AcquireData(startHz, stopHz, points, port);
                LastMeasurement = BuildMeasurementData(sData, port, z0);
                OutputFormatters.WriteCsv(filePath, LastMeasurement);
            }
        }

        /// <summary>
        /// Perform a measurement and save results to a Touchstone S1P file.
        /// </summary>
        /// <param name="filePath">Output .s1p file path.</param>
        /// <param name="startHz">Sweep start frequency in Hz.</param>
        /// <param name="stopHz">Sweep stop frequency in Hz.</param>
        /// <param name="points">Number of sweep points.</param>
        /// <param name="port">Measurement port (0 = S11, 1 = S21).</param>
        /// <param name="z0">Reference impedance in ohms.</param>
        public void MeasureToTouchstone(
            string filePath,
            double startHz = 1e6,
            double stopHz = 900e6,
            int points = 101,
            int port = 0,
            double z0 = 50.0)
        {
            lock (_lock)
            {
                EnsureConnected();

                Complex[] sData = AcquireData(startHz, stopHz, points, port);
                LastMeasurement = BuildMeasurementData(sData, port, z0);
                TouchstoneWriter.WriteS1P(filePath, _nv.Frequencies, sData, z0);
            }
        }

        /// <summary>
        /// Capture the device screen and save it to a file.
        /// </summary>
        /// <param name="filePath">Output image file path.</param>
        /// <returns>True on success.</returns>
        public bool CaptureScreen(string filePath)
        {
            lock (_lock)
            {
                EnsureConnected();
                _nv.CaptureToFile(filePath);
                return true;
            }
        }

        /// <summary>
        /// Send a raw command to the device and return the response.
        /// </summary>
        /// <param name="command">Command string (without trailing '\r').</param>
        /// <returns>Response text from the device.</returns>
        public string SendRawCommand(string command)
        {
            lock (_lock)
            {
                EnsureConnected();
                return _nv.SendRawCommand(command);
            }
        }

        // ------------------------------------------------------------------
        // Simple-type accessor methods for TestStand
        // ------------------------------------------------------------------

        /// <summary>
        /// Get the frequency array from the last measurement.
        /// </summary>
        /// <returns>Frequency values in Hz, or empty array if no measurement.</returns>
        public double[] GetFrequencies()
        {
            return LastMeasurement != null ? LastMeasurement.Frequencies : new double[0];
        }

        /// <summary>
        /// Get VSWR values from the last measurement.
        /// </summary>
        /// <returns>VSWR array, or empty array if no measurement.</returns>
        public double[] GetVSWR()
        {
            return LastMeasurement != null ? LastMeasurement.VSWR : new double[0];
        }

        /// <summary>
        /// Get S-parameter magnitude in dB from the last measurement.
        /// </summary>
        /// <returns>Magnitude array in dB, or empty array if no measurement.</returns>
        public double[] GetMagnitudeDb()
        {
            return LastMeasurement != null ? LastMeasurement.SMagDb : new double[0];
        }

        /// <summary>
        /// Get resistance (real part of impedance) from the last measurement.
        /// </summary>
        /// <returns>Resistance array in ohms, or empty array if no measurement.</returns>
        public double[] GetResistance()
        {
            return LastMeasurement != null ? LastMeasurement.ImpedanceReal : new double[0];
        }

        /// <summary>
        /// Get reactance (imaginary part of impedance) from the last measurement.
        /// </summary>
        /// <returns>Reactance array in ohms, or empty array if no measurement.</returns>
        public double[] GetReactance()
        {
            return LastMeasurement != null ? LastMeasurement.ImpedanceImag : new double[0];
        }

        /// <summary>
        /// Get the overall result string ("PASS" or "FAIL") from the last evaluation.
        /// </summary>
        /// <returns>Result string, or "NO_DATA" if no evaluation.</returns>
        public string GetResult()
        {
            return LastEvaluation != null ? LastEvaluation.Result : "NO_DATA";
        }

        /// <summary>
        /// Get the exit code from the last evaluation.
        /// </summary>
        /// <returns>Exit code (0=pass, 1=fail), or -1 if no evaluation.</returns>
        public int GetExitCode()
        {
            return LastEvaluation != null ? LastEvaluation.ExitCode : -1;
        }

        /// <summary>
        /// Get the S-parameter real parts from the last measurement.
        /// </summary>
        /// <returns>Real part array, or empty array if no measurement.</returns>
        public double[] GetSReal()
        {
            return LastMeasurement != null ? LastMeasurement.SReal : new double[0];
        }

        /// <summary>
        /// Get the S-parameter imaginary parts from the last measurement.
        /// </summary>
        /// <returns>Imaginary part array, or empty array if no measurement.</returns>
        public double[] GetSImag()
        {
            return LastMeasurement != null ? LastMeasurement.SImag : new double[0];
        }

        /// <summary>
        /// Get the impedance magnitude from the last measurement.
        /// </summary>
        /// <returns>Impedance magnitude array in ohms, or empty array if no measurement.</returns>
        public double[] GetImpedanceMag()
        {
            return LastMeasurement != null ? LastMeasurement.ImpedanceMag : new double[0];
        }

        /// <summary>
        /// Diagnostic method that returns a summary of the last measurement as a string.
        /// Useful for debugging in TestStand when array returns are hard to inspect.
        /// </summary>
        /// <returns>Multi-line summary string with array lengths and first 3 values of each.</returns>
        public string GetDiagnostics()
        {
            if (LastMeasurement == null)
                return "NO MEASUREMENT DATA";

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("=== Last Measurement Diagnostics ===");
            sb.AppendLine("Device: " + (LastMeasurement.Device ?? "null"));
            sb.AppendLine("Port: " + LastMeasurement.Port);
            sb.AppendLine("Z0: " + LastMeasurement.Z0);

            Action<string, double[]> dump = (name, arr) =>
            {
                if (arr == null)
                {
                    sb.AppendLine(name + ": null");
                    return;
                }
                sb.Append(name + " [" + arr.Length + "]: ");
                for (int i = 0; i < Math.Min(3, arr.Length); i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append(arr[i].ToString("F4"));
                }
                if (arr.Length > 3) sb.Append(" ...");
                sb.AppendLine();
            };

            dump("Frequencies", LastMeasurement.Frequencies);
            dump("VSWR", LastMeasurement.VSWR);
            dump("SMagDb", LastMeasurement.SMagDb);
            dump("ImpedanceReal", LastMeasurement.ImpedanceReal);
            dump("ImpedanceImag", LastMeasurement.ImpedanceImag);
            dump("ImpedanceMag", LastMeasurement.ImpedanceMag);
            dump("SReal", LastMeasurement.SReal);
            dump("SImag", LastMeasurement.SImag);

            if (LastEvaluation != null)
            {
                sb.AppendLine("Result: " + LastEvaluation.Result);
                sb.AppendLine("ExitCode: " + LastEvaluation.ExitCode);
            }

            return sb.ToString();
        }

        // ------------------------------------------------------------------
        // Private helpers
        // ------------------------------------------------------------------

        /// <summary>
        /// Ensure the device is connected, throwing if not.
        /// </summary>
        private void EnsureConnected()
        {
            if (_nv == null)
                throw new InvalidOperationException(
                    "Not connected. Call Connect() first.");
        }

        /// <summary>
        /// Configure the sweep and acquire S-parameter data from the device.
        /// </summary>
        private Complex[] AcquireData(double startHz, double stopHz, int points, int port)
        {
            _nv.SetFrequencies(startHz, stopHz, points);

            Complex[] sData;

            if (points > Constants.SEGMENT_LENGTH)
            {
                // Segmented scan for large point counts
                var scanResult = _nv.Scan();
                sData = port == 0 ? scanResult.Item1 : scanResult.Item2;
            }
            else
            {
                _nv.SetSweep(startHz, stopHz);
                // Wait for device to complete sweep after reconfiguring
                System.Threading.Thread.Sleep(200);
                _nv.FetchFrequencies();
                sData = _nv.Data(port);

                // Retry once if device returned incomplete data (happens on
                // first run after plugging in, before initial sweep finishes)
                if (sData.Length != points)
                {
                    System.Threading.Thread.Sleep(500);
                    sData = _nv.Data(port);
                }

                _nv.FetchFrequencies();
            }

            return sData;
        }

        /// <summary>
        /// Build a complete <see cref="MeasurementData"/> from raw S-parameter data.
        /// </summary>
        private MeasurementData BuildMeasurementData(Complex[] sData, int port, double z0)
        {
            Complex[] impedance = MathHelpers.ComputeImpedance(sData, z0);
            double[] vswr = MathHelpers.ComputeVSWR(sData);
            double[] magDb = MathHelpers.LogMagnitude(sData);

            double[] sReal = new double[sData.Length];
            double[] sImag = new double[sData.Length];
            double[] zReal = new double[impedance.Length];
            double[] zImag = new double[impedance.Length];
            double[] zMag = new double[impedance.Length];

            for (int i = 0; i < sData.Length; i++)
            {
                sReal[i] = sData[i].Real;
                sImag[i] = sData[i].Imaginary;
            }

            for (int i = 0; i < impedance.Length; i++)
            {
                zReal[i] = impedance[i].Real;
                zImag[i] = impedance[i].Imaginary;
                zMag[i] = impedance[i].Magnitude;
            }

            // Use _nv.Frequencies if it matches sData length, otherwise
            // regenerate from the sweep range to guarantee array alignment.
            double[] freqs = _nv.Frequencies;
            if (freqs == null || freqs.Length != sData.Length)
                freqs = MathHelpers.Linspace(
                    _nv.Frequencies != null && _nv.Frequencies.Length > 0 ? _nv.Frequencies[0] : 1e6,
                    _nv.Frequencies != null && _nv.Frequencies.Length > 0 ? _nv.Frequencies[_nv.Frequencies.Length - 1] : 900e6,
                    sData.Length);

            return new MeasurementData
            {
                Frequencies = freqs,
                SData = sData,
                SReal = sReal,
                SImag = sImag,
                SMagDb = magDb,
                VSWR = vswr,
                Impedance = impedance,
                ImpedanceReal = zReal,
                ImpedanceImag = zImag,
                ImpedanceMag = zMag,
                Port = port,
                Device = _nv.Dev,
                Z0 = z0,
                Timestamp = DateTime.UtcNow
            };
        }

        /// <summary>
        /// Dispose of the connection and release resources.
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
                    Disconnect();
                _disposed = true;
            }
        }

        /// <summary>
        /// Finalizer to ensure device is disconnected.
        /// </summary>
        ~NanoVNATestStand()
        {
            Dispose(false);
        }
    }
}
