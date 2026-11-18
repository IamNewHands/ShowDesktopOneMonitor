using System;
using System.Threading;

namespace ShowDesktopOneMonitor
{
    /// <summary>
    /// Keeps the app to a single running copy per user session. A named mutex is used
    /// instead of scanning for processes: the OS releases it when the process dies, so a
    /// crashed copy can never lock the app out.
    /// </summary>
    internal static class SingleInstance
    {
        // The "Local\" prefix scopes the name to the current logon session, so an RDP
        // session and the console session can each run their own copy while a second
        // launch inside one session is still refused.
        private const string MutexName = @"Local\ShowDesktopOneMonitor.SingleInstance.v1";

        // Held for the lifetime of the process. The handle is never closed explicitly:
        // the OS closes it on exit, which is exactly when the name must become free.
        private static Mutex HeldMutex { get; set; }

        /// <summary>
        /// True when this process is the only running copy; false when another copy
        /// already holds the name.
        /// </summary>
        public static bool TryAcquire ()
        {
            try {
                bool createdNew;
                Mutex mutex = new Mutex(false, MutexName, out createdNew);
                if (createdNew) {
                    HeldMutex = mutex;
                    return true;
                }

                mutex.Dispose();
                return false;
            }
            catch (UnauthorizedAccessException) {
                // The running copy was started with higher privileges (the Task Scheduler
                // auto-start runs elevated), so its mutex cannot be opened from a normal
                // process. A copy is running all the same.
                return false;
            }
            catch (Exception ex) {
                // The guard itself must never keep the app from starting.
                Diagnostics.Write("single-instance check failed", ex);
                return true;
            }
        }
    }
}
