using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using SKYNET.Steamworks;

namespace SKYNET
{
    public static class Common
    {
        private struct LASTINPUTINFO
        {
            public uint cbSize;

            public uint dwTime;
        }

        private static bool ConsoleEnabled;

        /// <summary>
        /// Name of the emulator data folder created next to the game executable.
        /// </summary>
        public const string DataFolderName = "D2MAX";

        private const string LegacyDataFolderName = "SKYNET";

        private static int dataFolderMigrated;

        /// <summary>
        /// Builds a path inside the emulator data folder, migrating the legacy
        /// folder the first time it is used so existing installs keep their
        /// configuration, storage and caches.
        /// </summary>
        public static string DataPath(params string[] parts)
        {
            string root = Path.Combine(GetPath(), DataFolderName);
            MigrateLegacyDataFolder(root);

            if (parts == null || parts.Length == 0)
            {
                return root;
            }

            string combined = root;
            foreach (var part in parts)
            {
                combined = Path.Combine(combined, part);
            }

            return combined;
        }

        private static void MigrateLegacyDataFolder(string root)
        {
            if (Interlocked.Exchange(ref dataFolderMigrated, 1) == 1)
            {
                return;
            }

            try
            {
                if (Directory.Exists(root))
                {
                    return;
                }

                string legacy = Path.Combine(GetPath(), LegacyDataFolderName);
                if (Directory.Exists(legacy))
                {
                    Directory.Move(legacy, root);
                }
            }
            catch
            {
                // A failed migration is not fatal: the new folder is created empty.
            }
        }

        public static bool LogToFile { get; set; }

        public static DateTime LoadTime { get; set; } = DateTime.Now;


        public static IntPtr GetObjectPtr(object Obj)
        {
            IntPtr zero = IntPtr.Zero;
            GCHandle value = GCHandle.Alloc(Obj, GCHandleType.WeakTrackResurrection);
            zero = Marshal.ReadIntPtr(GCHandle.ToIntPtr(value));
            value.Free();
            return zero;
        }

        public static void EnsureDirectoryExists(string filePath, bool isFile = false)
        {
            if (string.IsNullOrEmpty(filePath))
            {
                return;
            }
            filePath = filePath.Trim().Replace("\0", string.Empty);
            if (string.IsNullOrEmpty(filePath))
            {
                return;
            }
            try
            {
                string text = (isFile ? Path.GetDirectoryName(filePath) : filePath);
                if (Path.IsPathRooted(filePath))
                {
                    text = text.Trim();
                    if (!Directory.Exists(text))
                    {
                        Directory.CreateDirectory(text);
                    }
                }
            }
            catch (Exception)
            {
            }
        }

        public static void Show(object msg)
        {
            MessageBox.Show(msg.ToString());
        }

        /// <summary>
        /// Attaches a console to the host process for log output.
        ///
        /// AllocConsole creates a window that Windows brings to the front, and
        /// since it belongs to the game's own process the game ends up behind
        /// it — which is why the game looked like it started in the background.
        /// The console is therefore shown without activation and the window
        /// that had the focus gets it back. Standard output is rebound too:
        /// the CLR caches a null stdout from before the console existed, so
        /// Console.Write would otherwise go nowhere.
        /// </summary>
        public static void ActiveConsoleOutput()
        {
            if (ConsoleEnabled)
            {
                return;
            }

            ConsoleEnabled = true;

            IntPtr previousForeground = GetForegroundWindow();

            if (!AllocConsole())
            {
                return;
            }

            try
            {
                var stdout = Console.OpenStandardOutput();
                var writer = new StreamWriter(stdout) { AutoFlush = true };
                Console.SetOut(writer);
                Console.SetError(writer);
            }
            catch
            {
            }

            try
            {
                IntPtr console = GetConsoleWindow();
                if (console != IntPtr.Zero)
                {
                    ShowWindow(console, SW_SHOWNOACTIVATE);
                }

                if (previousForeground != IntPtr.Zero)
                {
                    SetForegroundWindow(previousForeground);
                }
            }
            catch
            {
            }
        }

        private const int SW_SHOWNOACTIVATE = 4;

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AllocConsole();

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetConsoleWindow();

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetForegroundWindow(IntPtr window);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ShowWindow(IntPtr window, int command);

        public static ulong GenerateSteamID()
        {
            return (ulong)CSteamID.CreateOne();
        }

        public static string GetPath()
        {
            using (Process currentProcess = Process.GetCurrentProcess())
            {
                return new FileInfo(currentProcess.MainModule.FileName).Directory?.FullName;
            }
        }

        public static bool Is64Bit()
        {
            return IntPtr.Size == 8;
        }

        public static int MilisecondTime()
        {
            return (DateTime.Now - LoadTime).Milliseconds;
        }

        public static uint ToUnixTime(DateTime t)
        {
            return (uint)new DateTimeOffset(t).ToUnixTimeSeconds();
        }

        public static IPAddress GetIPAddress(uint IP)
        {
            return new IPAddress(new byte[4]
            {
            (byte)(IP >> 24),
            (byte)(IP >> 16),
            (byte)(IP >> 8),
            (byte)IP
            });
        }

        public static int GetInactiveTime()
        {
            LASTINPUTINFO plii = default(LASTINPUTINFO);
            checked
            {
                plii.cbSize = (uint)Marshal.SizeOf((object)plii);
                plii.dwTime = 0u;
                return GetLastInputInfo(ref plii) ? ((int)Math.Round((double)((unchecked((long)(Environment.TickCount & 0x7FFFFFFF)) - unchecked((long)plii.dwTime)) & 0x7FFFFFFF & 0x7FFFFFFF) / 1000.0)) : 0;
            }
        }

        public static TimeSpan? GetInactiveTimeSpan()
        {
            LASTINPUTINFO plii = default(LASTINPUTINFO);
            checked
            {
                plii.cbSize = (uint)Marshal.SizeOf((object)plii);
                plii.dwTime = 0u;
                return (!GetLastInputInfo(ref plii)) ? null : new TimeSpan?(TimeSpan.FromMilliseconds(unchecked((long)Environment.TickCount) - unchecked((long)plii.dwTime)));
            }
        }

        [DllImport("user32.dll")]
        private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);
    }
}
