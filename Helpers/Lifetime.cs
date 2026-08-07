using System;
using System.Threading;

namespace SKYNET.Helpers
{
    /// <summary>
    /// Process-wide shutdown signal for every emulator background loop.
    ///
    /// Without it the long-poll loops keep blocking in the middle of an HTTP
    /// call while the game is tearing the CLR down, which is what leaves a
    /// `dota2.exe` behind that never finishes exiting (and makes the next
    /// launch stay in the background, since Source 2 refuses a second
    /// instance).
    /// </summary>
    public static class Lifetime
    {
        private static readonly CancellationTokenSource Source = new CancellationTokenSource();

        public static CancellationToken Token => Source.Token;

        public static bool IsShuttingDown => Source.IsCancellationRequested;

        public static void Shutdown()
        {
            try
            {
                Source.Cancel();
            }
            catch
            {
            }
        }

        /// <summary>
        /// Sleeps unless shutdown is requested. Returns false once the process
        /// is shutting down, so loops can `if (!Lifetime.Sleep(ms)) return;`.
        /// </summary>
        public static bool Sleep(int milliseconds)
        {
            if (IsShuttingDown)
            {
                return false;
            }

            try
            {
                return !Token.WaitHandle.WaitOne(milliseconds);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Waits on a signal, returning false when shutdown is requested first.
        /// </summary>
        public static bool Wait(WaitHandle signal, int milliseconds)
        {
            if (IsShuttingDown)
            {
                return false;
            }

            try
            {
                return WaitHandle.WaitAny(new[] { signal, Token.WaitHandle }, milliseconds) != 1;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Joins a worker thread with a small budget. Shutdown must never cost
        /// the game more than a few hundred milliseconds per worker.
        /// </summary>
        public static void JoinBriefly(Thread thread, int milliseconds = 250)
        {
            try
            {
                thread?.Join(milliseconds);
            }
            catch
            {
            }
        }
    }
}
