// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    UpdateSummary.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-17
// Purpose: One line per computer in the grid: from the saved state of
//          a computer and the package list, its kind (not checked, paused,
//          unreachable, updates available, errors, up to date), the packages
//          with an update and the packages in error. Packages that are not
//          installed are left out: they are neither an update nor an error.
//          Pure logic, unit-tested.
// -----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Linq;

namespace LiteDeploy
{
    public enum ComputerSummaryKind
    {
        NotChecked,
        Paused,
        /// <summary>The last run failed on the computer (unreachable, worker error); the versions known are older.</summary>
        Unreachable,
        Errors,
        Outdated,
        UpToDate
    }

    internal sealed class ComputerSummary
    {
        public ComputerSummaryKind Kind;
        /// <summary>Installed packages with an update available, in the order of the package list.</summary>
        public List<UpdatePackage> Outdated = new List<UpdatePackage>();
        /// <summary>Packages whose check or last update failed.</summary>
        public List<UpdatePackage> Errors = new List<UpdatePackage>();
        public int UpToDate;
        public int NotInstalled;

        public static ComputerSummary Of(ComputerUpdateState c, IList<UpdatePackage> packages)
        {
            ComputerSummary s = new ComputerSummary();
            if (c == null) { s.Kind = ComputerSummaryKind.NotChecked; return s; }
            if (c.Paused) { s.Kind = ComputerSummaryKind.Paused; return s; }

            bool anyState = false;
            foreach (UpdatePackage p in packages)
            {
                PackageUpdateState ps = c.Find(p.Id);
                if (ps == null || ps.Status == null) continue;
                anyState = true;
                if (ps.Status == WorkerProtocol.StatusOutdated) s.Outdated.Add(p);
                else if (ps.Status == WorkerProtocol.StatusUpToDate) s.UpToDate++;
                else if (ps.Status == WorkerProtocol.StatusNotInstalled) s.NotInstalled++;
                // A failed update counts while the update is still needed
                if (ps.Status == WorkerProtocol.StatusError || (ps.Status == WorkerProtocol.StatusOutdated && ps.LastResult == WorkerProtocol.ResultFailed))
                    s.Errors.Add(p);
            }

            if (!anyState)
                s.Kind = c.LastError != null ? ComputerSummaryKind.Unreachable : ComputerSummaryKind.NotChecked;
            else if (s.Errors.Count > 0)
                s.Kind = ComputerSummaryKind.Errors;
            else if (c.LastError != null)
                s.Kind = ComputerSummaryKind.Unreachable;   // last run failed: the versions shown are older
            else if (s.Outdated.Count > 0)
                s.Kind = ComputerSummaryKind.Outdated;
            else
                s.Kind = ComputerSummaryKind.UpToDate;
            return s;
        }
    }
}
