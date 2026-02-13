using System;
using System.Numerics;

namespace NANOVNACSharp
{
    /// <summary>
    /// Evaluates measurement data against pass/fail thresholds for VSWR,
    /// return loss, and insertion loss. Supports optional frequency sub-band
    /// filtering.
    /// </summary>
    public static class ThresholdEvaluator
    {
        /// <summary>
        /// Evaluate S-parameter data against pass/fail thresholds.
        ///
        /// Computes VSWR, return loss, and/or insertion loss and checks each
        /// against the specified limits. If a frequency range is provided, only
        /// the sub-band within that range is evaluated.
        /// </summary>
        /// <param name="frequencies">Frequency array in Hz.</param>
        /// <param name="sData">Complex S-parameter data.</param>
        /// <param name="maxVSWR">Maximum allowed VSWR, or null to skip.</param>
        /// <param name="minReturnLoss">Minimum return loss in positive dB, or null to skip.</param>
        /// <param name="maxInsertionLoss">Maximum insertion loss in dB, or null to skip.</param>
        /// <param name="freqRangeStart">Start of frequency sub-band in Hz, or null for full range.</param>
        /// <param name="freqRangeStop">Stop of frequency sub-band in Hz, or null for full range.</param>
        /// <returns>An <see cref="EvaluationResult"/> containing per-threshold results and overall verdict.</returns>
        public static EvaluationResult Evaluate(
            double[] frequencies,
            Complex[] sData,
            double? maxVSWR = null,
            double? minReturnLoss = null,
            double? maxInsertionLoss = null,
            double? freqRangeStart = null,
            double? freqRangeStop = null)
        {
            // Apply frequency range mask if specified
            double[] freqs;
            Complex[] s;

            if (freqRangeStart.HasValue && freqRangeStop.HasValue)
            {
                int count = 0;
                for (int i = 0; i < frequencies.Length; i++)
                {
                    if (frequencies[i] >= freqRangeStart.Value && frequencies[i] <= freqRangeStop.Value)
                        count++;
                }

                freqs = new double[count];
                s = new Complex[count];
                int idx = 0;
                for (int i = 0; i < frequencies.Length; i++)
                {
                    if (frequencies[i] >= freqRangeStart.Value && frequencies[i] <= freqRangeStop.Value)
                    {
                        freqs[idx] = frequencies[i];
                        s[idx] = sData[i];
                        idx++;
                    }
                }
            }
            else
            {
                freqs = frequencies;
                s = sData;
            }

            var result = new EvaluationResult();
            bool allPass = true;

            if (maxVSWR.HasValue)
            {
                double[] vswrVals = MathHelpers.ComputeVSWR(s);
                int worstIdx = ArgMax(vswrVals);
                double worstVal = vswrVals[worstIdx];
                bool passed = worstVal <= maxVSWR.Value;
                if (!passed) allPass = false;

                result.MaxVSWR = new ThresholdResult
                {
                    Limit = maxVSWR.Value,
                    Worst = worstVal,
                    WorstFrequencyHz = freqs[worstIdx],
                    Pass = passed
                };
            }

            if (minReturnLoss.HasValue)
            {
                double[] rlVals = MathHelpers.ComputeReturnLoss(s);
                int worstIdx = ArgMin(rlVals);
                double worstVal = rlVals[worstIdx];
                bool passed = worstVal >= minReturnLoss.Value;
                if (!passed) allPass = false;

                result.MinReturnLoss = new ThresholdResult
                {
                    Limit = minReturnLoss.Value,
                    Worst = worstVal,
                    WorstFrequencyHz = freqs[worstIdx],
                    Pass = passed
                };
            }

            if (maxInsertionLoss.HasValue)
            {
                double[] ilVals = MathHelpers.ComputeInsertionLoss(s);
                int worstIdx = ArgMax(ilVals);
                double worstVal = ilVals[worstIdx];
                bool passed = worstVal <= maxInsertionLoss.Value;
                if (!passed) allPass = false;

                result.MaxInsertionLoss = new ThresholdResult
                {
                    Limit = maxInsertionLoss.Value,
                    Worst = worstVal,
                    WorstFrequencyHz = freqs[worstIdx],
                    Pass = passed
                };
            }

            result.Result = allPass ? "PASS" : "FAIL";
            result.ExitCode = allPass ? Constants.EXIT_SUCCESS : Constants.EXIT_THRESHOLD_FAIL;
            return result;
        }

        /// <summary>
        /// Return the index of the maximum value in the array.
        /// </summary>
        private static int ArgMax(double[] values)
        {
            int idx = 0;
            for (int i = 1; i < values.Length; i++)
            {
                if (values[i] > values[idx])
                    idx = i;
            }
            return idx;
        }

        /// <summary>
        /// Return the index of the minimum value in the array.
        /// </summary>
        private static int ArgMin(double[] values)
        {
            int idx = 0;
            for (int i = 1; i < values.Length; i++)
            {
                if (values[i] < values[idx])
                    idx = i;
            }
            return idx;
        }
    }
}
