// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    RemoteRunProgress.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-23
// Purpose: Texts of the progress window of the actions run on several
//          computers (RemoteRunForm: quick commands, computer cards,
//          messages): the status line (done, running, elapsed time) and the
//          line of a computer left out by Cancel. Pure logic, unit-tested.
// -----------------------------------------------------------------------------
using System;
using System.Collections.Generic;

namespace LiteDeploy
{
    internal static class RemoteRunProgress
    {
        /// <summary>Elapsed time as m:ss (h:mm:ss from one hour).</summary>
        public static String Elapsed(TimeSpan elapsed)
        {
            if (elapsed < TimeSpan.Zero) elapsed = TimeSpan.Zero;
            return elapsed.TotalHours >= 1
                ? String.Format("{0}:{1:00}:{2:00}", (int)elapsed.TotalHours, elapsed.Minutes, elapsed.Seconds)
                : String.Format("{0}:{1:00}", (int)elapsed.TotalMinutes, elapsed.Seconds);
        }

        /// <summary>The status line: computers done of the total, running, elapsed time; or how the run ended.</summary>
        public static String StatusText(int done, int running, int total, TimeSpan elapsed, bool cancelling, bool finished)
        {
            String time = Elapsed(elapsed);
            if (finished)
                return done < total
                    ? Localization.F("RemoteRunForm.Cancelled", "Cancelled: {0} of {1} computer(s) done ({2}).", done, total, time)
                    : Localization.F("RemoteRunForm.Finished", "Done: {0} computer(s) ({1}).", total, time);
            if (cancelling)
                return Localization.F("RemoteRunForm.Cancelling", "Cancelling: waiting for {0} computer(s) under way ({1})...", running, time);
            return Localization.F("RemoteRunForm.Running", "{0} of {1} computer(s) done, {2} under way ({3})...", done, total, running, time);
        }

        /// <summary>The results in the order of the targets; a computer left out by Cancel gets a line saying so.</summary>
        public static String[] Complete(IList<String> targets, IList<String> results)
        {
            String[] all = new String[targets.Count];
            for (int i = 0; i < targets.Count; i++)
            {
                String r = results != null && i < results.Count ? results[i] : null;
                all[i] = r ?? "== " + targets[i] + " ==\n" + Localization.T("RemoteRunForm.NotRun", "Not run (cancelled).");
            }
            return all;
        }
    }
}
