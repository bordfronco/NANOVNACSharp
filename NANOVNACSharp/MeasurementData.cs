using System;
using System.Numerics;

namespace NANOVNACSharp
{
    /// <summary>
    /// Holds a complete set of measurement results from a NanoVNA sweep.
    /// </summary>
    public class MeasurementData
    {
        /// <summary>Sweep frequency points in Hz.</summary>
        public double[] Frequencies { get; set; }

        /// <summary>Complex S-parameter data.</summary>
        public Complex[] SData { get; set; }

        /// <summary>Real parts of the S-parameter data.</summary>
        public double[] SReal { get; set; }

        /// <summary>Imaginary parts of the S-parameter data.</summary>
        public double[] SImag { get; set; }

        /// <summary>S-parameter magnitude in dB.</summary>
        public double[] SMagDb { get; set; }

        /// <summary>VSWR values at each frequency point.</summary>
        public double[] VSWR { get; set; }

        /// <summary>Complex impedance at each frequency point.</summary>
        public Complex[] Impedance { get; set; }

        /// <summary>Resistance (real part of impedance) in ohms.</summary>
        public double[] ImpedanceReal { get; set; }

        /// <summary>Reactance (imaginary part of impedance) in ohms.</summary>
        public double[] ImpedanceImag { get; set; }

        /// <summary>Impedance magnitude in ohms.</summary>
        public double[] ImpedanceMag { get; set; }

        /// <summary>Measurement port (0 = S11, 1 = S21).</summary>
        public int Port { get; set; }

        /// <summary>Serial device path (e.g. "COM3").</summary>
        public string Device { get; set; }

        /// <summary>Reference impedance in ohms.</summary>
        public double Z0 { get; set; } = 50.0;

        /// <summary>UTC timestamp of the measurement.</summary>
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// Result of evaluating a single pass/fail threshold.
    /// </summary>
    public class ThresholdResult
    {
        /// <summary>The threshold limit value.</summary>
        public double Limit { get; set; }

        /// <summary>The worst measured value.</summary>
        public double Worst { get; set; }

        /// <summary>Frequency (Hz) at which the worst value occurred.</summary>
        public double WorstFrequencyHz { get; set; }

        /// <summary>Whether this threshold passed.</summary>
        public bool Pass { get; set; }
    }

    /// <summary>
    /// Aggregate result of all threshold evaluations.
    /// </summary>
    public class EvaluationResult
    {
        /// <summary>VSWR threshold result, or null if not evaluated.</summary>
        public ThresholdResult MaxVSWR { get; set; }

        /// <summary>Return-loss threshold result, or null if not evaluated.</summary>
        public ThresholdResult MinReturnLoss { get; set; }

        /// <summary>Insertion-loss threshold result, or null if not evaluated.</summary>
        public ThresholdResult MaxInsertionLoss { get; set; }

        /// <summary>"PASS" or "FAIL".</summary>
        public string Result { get; set; }

        /// <summary>Exit code: 0 for pass, 1 for fail.</summary>
        public int ExitCode { get; set; }
    }
}
