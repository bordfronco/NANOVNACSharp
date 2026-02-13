using System;
using System.Numerics;

namespace NANOVNACSharp
{
    /// <summary>
    /// Static math utilities replacing NumPy/SciPy functions used by the
    /// Python NanoVNA script: linspace, complex-array operations, windowing,
    /// FFT, and RF computations.
    /// </summary>
    public static class MathHelpers
    {
        /// <summary>
        /// Generate an array of evenly spaced values between <paramref name="start"/>
        /// and <paramref name="stop"/> (inclusive).
        /// </summary>
        /// <param name="start">First value.</param>
        /// <param name="stop">Last value.</param>
        /// <param name="count">Number of values to generate.</param>
        /// <returns>Array of <paramref name="count"/> evenly spaced doubles.</returns>
        public static double[] Linspace(double start, double stop, int count)
        {
            if (count < 1)
                return new double[0];
            if (count == 1)
                return new double[] { start };

            double[] result = new double[count];
            double step = (stop - start) / (count - 1);
            for (int i = 0; i < count; i++)
                result[i] = start + i * step;
            return result;
        }

        /// <summary>
        /// Compute the magnitude (absolute value) of each element in a complex array.
        /// </summary>
        /// <param name="data">Complex input array.</param>
        /// <returns>Array of magnitudes.</returns>
        public static double[] Abs(Complex[] data)
        {
            double[] result = new double[data.Length];
            for (int i = 0; i < data.Length; i++)
                result[i] = data[i].Magnitude;
            return result;
        }

        /// <summary>
        /// Compute the phase angle (in radians) of each element in a complex array.
        /// </summary>
        /// <param name="data">Complex input array.</param>
        /// <returns>Array of phase angles in radians.</returns>
        public static double[] Angle(Complex[] data)
        {
            double[] result = new double[data.Length];
            for (int i = 0; i < data.Length; i++)
                result[i] = data[i].Phase;
            return result;
        }

        /// <summary>
        /// Unwrap phase angles by changing absolute jumps greater than pi to their
        /// 2*pi complement, matching numpy.unwrap behaviour.
        /// </summary>
        /// <param name="phase">Array of phase angles in radians.</param>
        /// <returns>Unwrapped phase array.</returns>
        public static double[] Unwrap(double[] phase)
        {
            if (phase.Length == 0)
                return new double[0];

            double[] result = new double[phase.Length];
            result[0] = phase[0];
            for (int i = 1; i < phase.Length; i++)
            {
                double diff = phase[i] - phase[i - 1];
                // Normalize diff to (-pi, pi]
                diff = diff - 2.0 * Math.PI * Math.Round(diff / (2.0 * Math.PI));
                result[i] = result[i - 1] + diff;
            }
            return result;
        }

        /// <summary>
        /// Convert an array of radians to degrees.
        /// </summary>
        /// <param name="radians">Input angles in radians.</param>
        /// <returns>Angles in degrees.</returns>
        public static double[] RadToDeg(double[] radians)
        {
            double[] result = new double[radians.Length];
            for (int i = 0; i < radians.Length; i++)
                result[i] = radians[i] * (180.0 / Math.PI);
            return result;
        }

        /// <summary>
        /// Compute log magnitude in dB: 20 * log10(|x|).
        /// </summary>
        /// <param name="data">Complex input array.</param>
        /// <returns>Magnitude in dB.</returns>
        public static double[] LogMagnitude(Complex[] data)
        {
            double[] result = new double[data.Length];
            for (int i = 0; i < data.Length; i++)
            {
                double mag = data[i].Magnitude;
                if (mag == 0) mag = 1e-30;
                result[i] = 20.0 * Math.Log10(mag);
            }
            return result;
        }

        /// <summary>
        /// Compute linear magnitude of each element in a complex array.
        /// </summary>
        /// <param name="data">Complex input array.</param>
        /// <returns>Array of linear magnitudes.</returns>
        public static double[] LinearMagnitude(Complex[] data)
        {
            return Abs(data);
        }

        /// <summary>
        /// Generate a Blackman window of the specified length.
        /// Matches numpy.blackman(N).
        /// </summary>
        /// <param name="n">Window length.</param>
        /// <returns>Blackman window coefficients.</returns>
        public static double[] BlackmanWindow(int n)
        {
            if (n <= 0)
                return new double[0];
            if (n == 1)
                return new double[] { 1.0 };

            double[] window = new double[n];
            for (int i = 0; i < n; i++)
            {
                window[i] = 0.42
                    - 0.5 * Math.Cos(2.0 * Math.PI * i / (n - 1))
                    + 0.08 * Math.Cos(4.0 * Math.PI * i / (n - 1));
            }
            return window;
        }

        /// <summary>
        /// Perform a 1-D convolution in "same" mode (output length equals input length),
        /// matching numpy.convolve(x, kernel, mode='same').
        /// </summary>
        /// <param name="x">Input array.</param>
        /// <param name="kernel">Convolution kernel.</param>
        /// <returns>Convolution result with length equal to <paramref name="x"/>.</returns>
        public static double[] ConvolveSame(double[] x, double[] kernel)
        {
            int n = x.Length;
            int k = kernel.Length;
            int fullLen = n + k - 1;

            // Full convolution
            double[] full = new double[fullLen];
            for (int i = 0; i < fullLen; i++)
            {
                double sum = 0;
                for (int j = 0; j < k; j++)
                {
                    int xi = i - j;
                    if (xi >= 0 && xi < n)
                        sum += x[xi] * kernel[j];
                }
                full[i] = sum;
            }

            // Extract "same" portion (centered)
            int offset = (k - 1) / 2;
            double[] result = new double[n];
            Array.Copy(full, offset, result, 0, n);
            return result;
        }

        /// <summary>
        /// Compute the inverse FFT magnitude of a windowed complex signal,
        /// zero-padded to <paramref name="nfft"/> points.
        /// Used for TDR (time-domain reflectometry) computation.
        /// </summary>
        /// <param name="data">Complex frequency-domain data.</param>
        /// <param name="window">Window coefficients (same length as <paramref name="data"/>).</param>
        /// <param name="nfft">FFT size (should be a power of 2).</param>
        /// <returns>Magnitude of the inverse FFT result.</returns>
        public static double[] InverseFFTMagnitude(Complex[] data, double[] window, int nfft)
        {
            // Apply window and zero-pad
            Complex[] padded = new Complex[nfft];
            int len = Math.Min(data.Length, window.Length);
            for (int i = 0; i < len; i++)
                padded[i] = data[i] * window[i];

            // IFFT = conj(FFT(conj(x))) / N
            Complex[] conjugated = new Complex[nfft];
            for (int i = 0; i < nfft; i++)
                conjugated[i] = Complex.Conjugate(padded[i]);

            Complex[] fftResult = FFT(conjugated, nfft);

            double[] magnitude = new double[nfft];
            for (int i = 0; i < nfft; i++)
                magnitude[i] = Complex.Conjugate(fftResult[i]).Magnitude / nfft;

            return magnitude;
        }

        /// <summary>
        /// Compute the radix-2 Cooley-Tukey FFT of a complex array.
        /// Array length must be a power of 2.
        /// </summary>
        /// <param name="x">Input complex array.</param>
        /// <param name="n">Transform size (power of 2).</param>
        /// <returns>FFT result.</returns>
        public static Complex[] FFT(Complex[] x, int n)
        {
            // Ensure power of 2
            Complex[] a = new Complex[n];
            Array.Copy(x, a, Math.Min(x.Length, n));

            // Bit-reversal permutation
            int bits = (int)Math.Log(n, 2);
            for (int i = 0; i < n; i++)
            {
                int j = BitReverse(i, bits);
                if (j > i)
                {
                    Complex temp = a[i];
                    a[i] = a[j];
                    a[j] = temp;
                }
            }

            // Cooley-Tukey iterative FFT
            for (int len = 2; len <= n; len *= 2)
            {
                double angle = -2.0 * Math.PI / len;
                Complex wn = new Complex(Math.Cos(angle), Math.Sin(angle));
                for (int i = 0; i < n; i += len)
                {
                    Complex w = Complex.One;
                    for (int j = 0; j < len / 2; j++)
                    {
                        Complex u = a[i + j];
                        Complex v = a[i + j + len / 2] * w;
                        a[i + j] = u + v;
                        a[i + j + len / 2] = u - v;
                        w *= wn;
                    }
                }
            }

            return a;
        }

        /// <summary>
        /// Reverse the bits of an integer, considering the specified bit width.
        /// </summary>
        private static int BitReverse(int value, int bits)
        {
            int result = 0;
            for (int i = 0; i < bits; i++)
            {
                result = (result << 1) | (value & 1);
                value >>= 1;
            }
            return result;
        }

        /// <summary>
        /// Compute VSWR from complex S-parameter (reflection coefficient) data.
        /// VSWR = (1 + |gamma|) / (1 - |gamma|).
        /// Magnitude is clamped to [0, 0.9999] to avoid division by zero.
        /// </summary>
        /// <param name="sData">Complex reflection coefficient values.</param>
        /// <returns>VSWR values at each point.</returns>
        public static double[] ComputeVSWR(Complex[] sData)
        {
            double[] result = new double[sData.Length];
            for (int i = 0; i < sData.Length; i++)
            {
                double mag = Clip(sData[i].Magnitude, 0, 0.9999);
                result[i] = (1.0 + mag) / (1.0 - mag);
            }
            return result;
        }

        /// <summary>
        /// Compute return loss in dB from complex S-parameter data.
        /// Return loss = -20 * log10(|gamma|). Larger positive value = better match.
        /// </summary>
        /// <param name="sData">Complex reflection coefficient values.</param>
        /// <returns>Return loss in positive dB.</returns>
        public static double[] ComputeReturnLoss(Complex[] sData)
        {
            double[] result = new double[sData.Length];
            for (int i = 0; i < sData.Length; i++)
            {
                double mag = sData[i].Magnitude;
                if (mag == 0) mag = 1e-30;
                result[i] = -20.0 * Math.Log10(mag);
            }
            return result;
        }

        /// <summary>
        /// Compute insertion loss in dB from complex S21 data.
        /// Insertion loss = -20 * log10(|S21|). Smaller value = lower loss.
        /// </summary>
        /// <param name="sData">Complex S21 (transmission) values.</param>
        /// <returns>Insertion loss in positive dB.</returns>
        public static double[] ComputeInsertionLoss(Complex[] sData)
        {
            // Same formula as return loss
            return ComputeReturnLoss(sData);
        }

        /// <summary>
        /// Convert reflection coefficient (gamma) to impedance.
        /// Z = Z0 * (1 + gamma) / (1 - gamma).
        /// </summary>
        /// <param name="sData">Complex reflection coefficient data.</param>
        /// <param name="z0">Reference impedance in ohms.</param>
        /// <returns>Complex impedance values.</returns>
        public static Complex[] ComputeImpedance(Complex[] sData, double z0)
        {
            Complex[] result = new Complex[sData.Length];
            for (int i = 0; i < sData.Length; i++)
                result[i] = z0 * (Complex.One + sData[i]) / (Complex.One - sData[i]);
            return result;
        }

        /// <summary>
        /// Clamp a value to the range [min, max].
        /// </summary>
        /// <param name="value">Input value.</param>
        /// <param name="min">Minimum allowed value.</param>
        /// <param name="max">Maximum allowed value.</param>
        /// <returns>Clamped value.</returns>
        public static double Clip(double value, double min, double max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        /// <summary>
        /// Compute propagation delay at each frequency point.
        /// delay = -unwrap(angle(s)) / (2 * pi * freq).
        /// </summary>
        /// <param name="sData">Complex S-parameter data.</param>
        /// <param name="frequencies">Frequency array in Hz.</param>
        /// <returns>Delay in seconds at each point.</returns>
        public static double[] ComputeDelay(Complex[] sData, double[] frequencies)
        {
            double[] phase = Unwrap(Angle(sData));
            double[] result = new double[sData.Length];
            for (int i = 0; i < sData.Length; i++)
            {
                double f = frequencies[i];
                if (f == 0) f = 1e-30;
                result[i] = -phase[i] / (2.0 * Math.PI * f);
            }
            return result;
        }

        /// <summary>
        /// Compute group delay as the numerical derivative of unwrapped phase.
        /// Matches: numpy.convolve(numpy.unwrap(numpy.angle(x)), [1, -1], mode='same').
        /// </summary>
        /// <param name="sData">Complex S-parameter data.</param>
        /// <returns>Group delay (phase derivative) at each point.</returns>
        public static double[] ComputeGroupDelay(Complex[] sData)
        {
            double[] phase = Unwrap(Angle(sData));
            return ConvolveSame(phase, new double[] { 1.0, -1.0 });
        }
    }
}
