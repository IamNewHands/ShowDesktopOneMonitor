using System;
using System.IO;

namespace ShowDesktopOneMonitor
{
    /// <summary>
    /// Remembers which build ran last, so a freshly installed build can tell that it was
    /// just updated.
    ///
    /// The updater's "--updated" switch alone was not enough. It is a one-way contract
    /// across a version boundary, so the release that introduced the switch could never
    /// have received it - updating *to* v1.1.4 showed nothing at all. Comparing the running
    /// version with the version that ran last needs no cooperation from the outgoing build,
    /// and it also survives the elevated fallback path in the updater, where the switch does
    /// not make it through the command line.
    /// </summary>
    internal static class UpdateState
    {
        private static string StatePath => Path.Combine(Diagnostics.LogDirectory, "last-run-version.txt");

        /// <summary>
        /// Records the running version and reports whether a different build ran last,
        /// i.e. this process is the first start after an update. The record is written
        /// immediately so a crash or a forced exit cannot make the notice repeat.
        /// </summary>
        public static bool TakeJustUpdated ()
        {
            try {
                string previous = File.Exists(StatePath) ? File.ReadAllText(StatePath).Trim() : null;
                File.WriteAllText(StatePath, BuildInfo.Version);
                Diagnostics.Write("UpdateState: previous run version="
                    + (string.IsNullOrEmpty(previous) ? "<none>" : previous)
                    + ", this run=" + BuildInfo.Version);
                return !string.IsNullOrEmpty(previous)
                    && !string.Equals(previous, BuildInfo.Version, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex) {
                Diagnostics.Write("UpdateState.TakeJustUpdated failed", ex);
                return false;
            }
        }
    }
}
