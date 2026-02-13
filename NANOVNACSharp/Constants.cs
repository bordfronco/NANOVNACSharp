namespace NANOVNACSharp
{
    /// <summary>
    /// Device constants, exit codes, and default values for the NanoVNA H4.
    /// </summary>
    public static class Constants
    {
        /// <summary>USB Vendor ID for the NanoVNA H4 (STMicroelectronics).</summary>
        public const int VID = 0x0483;

        /// <summary>USB Product ID for the NanoVNA H4.</summary>
        public const int PID = 0x5740;

        /// <summary>Reference level for gamma calculation (1 &lt;&lt; 9 = 512).</summary>
        public const int REF_LEVEL = 1 << 9;

        /// <summary>Device screen width in pixels.</summary>
        public const int SCREEN_WIDTH = 480;

        /// <summary>Device screen height in pixels.</summary>
        public const int SCREEN_HEIGHT = 320;

        /// <summary>Default number of sweep points.</summary>
        public const int DEFAULT_POINTS = 101;

        /// <summary>Default sweep start frequency in Hz.</summary>
        public const double DEFAULT_START_HZ = 1e6;

        /// <summary>Default sweep stop frequency in Hz.</summary>
        public const double DEFAULT_STOP_HZ = 1.5e9;

        /// <summary>Maximum points per hardware scan segment.</summary>
        public const int SEGMENT_LENGTH = 101;

        /// <summary>Serial prompt string indicating end of device response.</summary>
        public const string PROMPT = "ch>";

        // ----- Exit codes -----

        /// <summary>Exit code: success / all thresholds pass.</summary>
        public const int EXIT_SUCCESS = 0;

        /// <summary>Exit code: one or more thresholds exceeded (measurement fail).</summary>
        public const int EXIT_THRESHOLD_FAIL = 1;

        /// <summary>Exit code: device error (COM port / serial).</summary>
        public const int EXIT_DEVICE_ERROR = 2;

        /// <summary>Exit code: argument error.</summary>
        public const int EXIT_ARGUMENT_ERROR = 3;
    }
}
