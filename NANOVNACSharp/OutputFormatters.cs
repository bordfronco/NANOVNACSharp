using System;
using System.Globalization;
using System.IO;
using System.Numerics;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace NANOVNACSharp
{
    /// <summary>
    /// Produces structured JSON and CSV output from measurement data,
    /// matching the v1.0 schema defined by the Python NanoVNA H4 script.
    /// </summary>
    public static class OutputFormatters
    {
        /// <summary>
        /// Build a structured JSON string from measurement data, conforming
        /// to the v1.0 schema.
        ///
        /// Includes version, timestamp, device info, sweep parameters,
        /// measurement arrays, optional threshold results, and overall
        /// pass/fail status.
        /// </summary>
        /// <param name="measurement">Complete measurement data.</param>
        /// <param name="evaluation">Threshold evaluation result, or null if none.</param>
        /// <returns>Formatted JSON string.</returns>
        public static string ToJson(MeasurementData measurement, EvaluationResult evaluation = null)
        {
            JObject doc = new JObject
            {
                ["version"] = "1.0",
                ["timestamp"] = measurement.Timestamp.ToString("o"),
                ["device"] = measurement.Device,
                ["sweep"] = new JObject
                {
                    ["start_hz"] = measurement.Frequencies[0],
                    ["stop_hz"] = measurement.Frequencies[measurement.Frequencies.Length - 1],
                    ["points"] = measurement.Frequencies.Length,
                    ["port"] = measurement.Port
                },
                ["measurement"] = new JObject
                {
                    ["frequencies_hz"] = new JArray(measurement.Frequencies),
                    ["s_real"] = new JArray(measurement.SReal),
                    ["s_imag"] = new JArray(measurement.SImag),
                    ["s_mag_db"] = new JArray(measurement.SMagDb),
                    ["vswr"] = new JArray(measurement.VSWR),
                    ["impedance_real"] = new JArray(measurement.ImpedanceReal),
                    ["impedance_imag"] = new JArray(measurement.ImpedanceImag),
                    ["impedance_mag"] = new JArray(measurement.ImpedanceMag)
                }
            };

            if (evaluation != null)
            {
                JObject thresholds = new JObject();

                if (evaluation.MaxVSWR != null)
                {
                    thresholds["max_vswr"] = ThresholdToJson(evaluation.MaxVSWR);
                }
                if (evaluation.MinReturnLoss != null)
                {
                    thresholds["min_return_loss_db"] = ThresholdToJson(evaluation.MinReturnLoss);
                }
                if (evaluation.MaxInsertionLoss != null)
                {
                    thresholds["max_insertion_loss_db"] = ThresholdToJson(evaluation.MaxInsertionLoss);
                }

                doc["thresholds"] = thresholds;
                doc["result"] = evaluation.Result;
                doc["exit_code"] = evaluation.ExitCode;
            }
            else
            {
                doc["result"] = "PASS";
                doc["exit_code"] = Constants.EXIT_SUCCESS;
            }

            return doc.ToString(Formatting.Indented);
        }

        /// <summary>
        /// Write measurement data to a CSV file with metadata header comments.
        ///
        /// The header includes timestamp, device, sweep range, and port info
        /// as comment lines (prefixed with '#'). Columns are:
        /// Frequency_Hz, S_Real, S_Imag, S_Mag_dB, VSWR, Z_Real_Ohms,
        /// Z_Imag_Ohms, Z_Mag_Ohms.
        /// </summary>
        /// <param name="filePath">Output CSV file path.</param>
        /// <param name="measurement">Complete measurement data.</param>
        public static void WriteCsv(string filePath, MeasurementData measurement)
        {
            filePath = ValidateFilePath(filePath);
            using (StreamWriter writer = new StreamWriter(filePath))
            {
                writer.WriteLine("# NanoVNA H4 Measurement Data");
                writer.WriteLine("# Timestamp: {0}", measurement.Timestamp.ToString("o"));
                writer.WriteLine("# Device: {0}", measurement.Device);
                writer.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "# Sweep: {0:F0} - {1:F0} Hz, {2} points",
                    measurement.Frequencies[0],
                    measurement.Frequencies[measurement.Frequencies.Length - 1],
                    measurement.Frequencies.Length));
                writer.WriteLine("# Port: {0}", measurement.Port);
                writer.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "# Z0: {0:F1} ohms", measurement.Z0));

                // Header row
                writer.WriteLine("Frequency_Hz,S_Real,S_Imag,S_Mag_dB,VSWR,Z_Real_Ohms,Z_Imag_Ohms,Z_Mag_Ohms");

                // Data rows
                for (int i = 0; i < measurement.Frequencies.Length; i++)
                {
                    writer.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "{0},{1},{2},{3},{4},{5},{6},{7}",
                        measurement.Frequencies[i],
                        measurement.SReal[i],
                        measurement.SImag[i],
                        measurement.SMagDb[i],
                        measurement.VSWR[i],
                        measurement.ImpedanceReal[i],
                        measurement.ImpedanceImag[i],
                        measurement.ImpedanceMag[i]));
                }
            }
        }

        /// <summary>
        /// Convert a <see cref="ThresholdResult"/> to a JSON object.
        /// </summary>
        internal static string ValidateFilePath(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                throw new ArgumentException("File path cannot be null or empty.", "filePath");
            if (filePath.IndexOf('\0') >= 0)
                throw new ArgumentException("File path contains invalid characters.", "filePath");
            string fullPath = Path.GetFullPath(filePath);
            if (fullPath.StartsWith(@"\\"))
                throw new ArgumentException("UNC paths are not permitted.", "filePath");
            return fullPath;
        }

        private static JObject ThresholdToJson(ThresholdResult tr)
        {
            return new JObject
            {
                ["limit"] = tr.Limit,
                ["worst"] = tr.Worst,
                ["worst_freq_hz"] = tr.WorstFrequencyHz,
                ["pass"] = tr.Pass
            };
        }
    }
}
