using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace SKYNET.Helpers
{
    /// <summary>
    /// Log sink for the emulator.
    ///
    /// Every process that loads the DLL gets its own file (`steam_api.&lt;pid&gt;.log`)
    /// opened for append with sharing enabled, so concurrent processes never
    /// truncate or lock each other out. Repeated lines are collapsed into a
    /// counter instead of being dropped.
    /// </summary>
    public class Log
    {
        public static bool Initialized;

        private const long MaxFileBytes = 32 * 1024 * 1024;

        private static readonly List<string> buffered = new List<string>();
        private static readonly object file_lock = new object();
        private static string lastMsg;
        private static int repeatCount;
        private static string fileName;
        private static StreamWriter writer;
        private static DateTime lastDiskFlush;

        private static readonly TimeSpan DiskFlushInterval = TimeSpan.FromSeconds(1);

        public static string FileName
        {
            get
            {
                lock (file_lock)
                {
                    return fileName;
                }
            }
        }

        public static void Initialize()
        {
            lock (file_lock)
            {
                if (Initialized)
                {
                    return;
                }

                var logPath = Common.DataPath();
                Common.EnsureDirectoryExists(logPath);
                fileName = Path.Combine(logPath, BuildLogName());

                if (!TryOpenWriter())
                {
                    return;
                }

                Initialized = true;

                // A header makes an otherwise empty file meaningful: it proves
                // the process reached Log.Initialize and says which process it
                // is, so a folder full of per-PID files can be told apart.
                buffered.Insert(0, $" Log: opened {fileName} at {DateTime.Now:yyyy-MM-dd HH:mm:ss} by {ProcessDescription()}");
                FlushBuffered();
            }
        }

        /// <summary>
        /// The log name carries an identity so two processes sharing the game
        /// folder (the client and a dedicated server, or two launches) never
        /// write to the same file. D2MAX_LOG_SUFFIX (or the legacy
        /// SKYNET_LOG_SUFFIX) overrides the default PID.
        /// </summary>
        private static string BuildLogName()
        {
            var suffix = Environment.GetEnvironmentVariable("D2MAX_LOG_SUFFIX");
            if (string.IsNullOrWhiteSpace(suffix))
            {
                suffix = Environment.GetEnvironmentVariable("SKYNET_LOG_SUFFIX");
            }

            if (string.IsNullOrWhiteSpace(suffix))
            {
                try
                {
                    suffix = Process.GetCurrentProcess().Id.ToString(CultureInfo.InvariantCulture);
                }
                catch
                {
                    suffix = Guid.NewGuid().ToString("N").Substring(0, 8);
                }
            }

            foreach (var invalid in Path.GetInvalidFileNameChars())
            {
                suffix = suffix.Replace(invalid, '_');
            }

            return $"steam_api.{suffix}.log";
        }

        internal static void AppEnd(string formatted)
        {
            try
            {
                lock (file_lock)
                {
                    if (formatted == lastMsg)
                    {
                        repeatCount++;
                        return;
                    }

                    if (repeatCount > 0)
                    {
                        buffered.Add($" (previous line repeated x{repeatCount})");
                        repeatCount = 0;
                    }

                    lastMsg = formatted;
                    buffered.Add(formatted);

                    if (Initialized)
                    {
                        FlushBuffered();
                    }
                }
            }
            catch
            {
            }
        }

        private static string ProcessDescription()
        {
            try
            {
                using (var process = Process.GetCurrentProcess())
                {
                    return $"{process.ProcessName} pid={process.Id} ({(IntPtr.Size == 8 ? "x64" : "x86")})";
                }
            }
            catch
            {
                return "unknown process";
            }
        }

        private static void FlushBuffered()
        {
            if (!Initialized || buffered.Count == 0)
            {
                return;
            }

            if (writer == null && !TryOpenWriter())
            {
                // Keep the newest lines only: an unwritable log must not grow
                // the buffer without bound.
                TrimBuffer();
                return;
            }

            try
            {
                foreach (var line in buffered)
                {
                    writer.Write(DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture));
                    writer.WriteLine(line);
                }

                buffered.Clear();
                FlushToDisk();
                RollIfTooLarge();
            }
            catch
            {
                CloseWriter();
                TrimBuffer();
            }
        }

        /// <summary>
        /// AutoFlush only pushes the bytes into the file handle; Windows does
        /// not update the directory entry until the handle is closed, so a log
        /// being written looked like a 0-byte file to anyone inspecting it
        /// while the game ran. Forcing the buffers out fixes that, rate-limited
        /// because it is a full FlushFileBuffers call.
        /// </summary>
        private static void FlushToDisk()
        {
            var now = DateTime.UtcNow;
            if ((now - lastDiskFlush) < DiskFlushInterval)
            {
                return;
            }

            lastDiskFlush = now;

            try
            {
                writer.Flush();
                (writer.BaseStream as FileStream)?.Flush(true);
            }
            catch
            {
            }
        }

        private static void TrimBuffer()
        {
            const int MaxBufferedLines = 4096;
            if (buffered.Count > MaxBufferedLines)
            {
                buffered.RemoveRange(0, buffered.Count - MaxBufferedLines);
            }
        }

        private static bool TryOpenWriter()
        {
            try
            {
                var stream = new FileStream(
                    fileName,
                    FileMode.Append,
                    FileAccess.Write,
                    FileShare.ReadWrite | FileShare.Delete);
                writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
                return true;
            }
            catch
            {
                writer = null;
                return false;
            }
        }

        private static void RollIfTooLarge()
        {
            try
            {
                if (writer.BaseStream.Length < MaxFileBytes)
                {
                    return;
                }

                CloseWriter();
                var rolled = fileName + ".1";
                if (File.Exists(rolled))
                {
                    File.Delete(rolled);
                }
                File.Move(fileName, rolled);
                TryOpenWriter();
            }
            catch
            {
                CloseWriter();
            }
        }

        private static void CloseWriter()
        {
            try
            {
                writer?.Dispose();
            }
            catch
            {
            }
            finally
            {
                writer = null;
            }
        }

        /// <summary>
        /// Flushes any pending repeat counter and releases the file handle.
        /// </summary>
        public static void Shutdown()
        {
            lock (file_lock)
            {
                if (repeatCount > 0)
                {
                    buffered.Add($" (previous line repeated x{repeatCount})");
                    repeatCount = 0;
                }

                lastDiskFlush = DateTime.MinValue;
                FlushBuffered();
                CloseWriter();
                Initialized = false;
            }
        }
    }
}
