// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    ComputerFilter.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-18
// Purpose: Rows of the computer view of Software Updates (Active Directory
//          inventory + saved update state) and the pure logic around them:
//          building a row, server and "offline/stale" classification, the
//          filters (search, status chips, servers, disabled accounts, the
//          selection of the main window) and the checking of rows (select
//          all visible, select outdated, targets of an action). Unit-tested.
// -----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Linq;

namespace LiteDeploy
{
    /// <summary>Status chips of Software Updates. Several chips: a row matching any of them is shown.</summary>
    [Flags]
    public enum ComputerStatusFilter
    {
        None = 0,
        /// <summary>Updates available (not paused).</summary>
        ToUpdate = 1,
        /// <summary>Package errors, or the last run failed (unreachable).</summary>
        Errors = 2,
        UpToDate = 4,
        NotChecked = 8,
        Paused = 16,
        /// <summary>Updated outside business hours only.</summary>
        NightOnly = 32,
        /// <summary>Last logon (Active Directory) older than the stale delay, or never.</summary>
        Stale = 64
    }

    /// <summary>One computer of the computer view: what the filters, the sort and the columns need.</summary>
    public sealed class ComputerRow
    {
        public String Name;
        /// <summary>Operating system shown: the one read on the computer, else the one of Active Directory.</summary>
        public String Os;
        public String OsVersion;
        /// <summary>OU (distinguished name of the container) from Active Directory; null: unknown.</summary>
        public String Ou;
        public DateTime? LastLogon;
        /// <summary>Known by Active Directory (false: added by name, or from the list of the main window).</summary>
        public bool InAd;
        public bool Disabled;
        public bool IsServer;
        public bool AddedByName;
        public ComputerSummaryKind Kind = ComputerSummaryKind.NotChecked;
        public int UpdateCount;
        public bool Paused;
        public String PauseReason;
        public bool NightOnly;
        /// <summary>Display names of the packages known on the computer (installed, outdated or in error): searched too.</summary>
        public List<String> PackageNames = new List<String>();

        /// <summary>
        /// A row from what is known of a computer: Active Directory (may be null), the saved state (may be null)
        /// and its summary. The operating system read on the computer (OsCaption, OsType) wins over Active Directory.
        /// </summary>
        internal static ComputerRow Build(String name, AdComputer ad, ComputerUpdateState state, ComputerSummary summary, IEnumerable<UpdatePackage> packages)
        {
            ComputerRow row = new ComputerRow { Name = name };
            if (ad != null)
            {
                row.InAd = true;
                row.Os = ad.OperatingSystem;
                row.OsVersion = ad.OperatingSystemVersion;
                row.Ou = ad.Ou;
                row.LastLogon = ad.LastLogon;
                row.Disabled = ad.Disabled;
            }
            if (state != null)
            {
                if (!String.IsNullOrWhiteSpace(state.OsCaption)) { row.Os = state.OsCaption.Trim(); row.OsVersion = null; }
                row.Paused = state.Paused;
                row.PauseReason = state.PauseReason;
                row.NightOnly = state.NightOnly;
                row.AddedByName = state.AddedByName;
                if (packages != null)
                {
                    foreach (UpdatePackage p in packages)
                    {
                        PackageUpdateState ps = state.Find(p.Id);
                        if (ps != null && ps.Status != null && ps.Status != WorkerProtocol.StatusNotInstalled) row.PackageNames.Add(p.DisplayName);
                    }
                }
            }
            row.IsServer = IsServerKind(state == null ? null : state.OsType, row.Os);
            if (summary != null)
            {
                row.Kind = summary.Kind;
                row.UpdateCount = summary.Outdated.Count;
            }
            return row;
        }

        /// <summary>
        /// Server or not: the kind read on the computer when known ("server", "domaincontroller" or a kind
        /// containing "server": yes, any other kind: no), else the name of the operating system (AdComputer.IsServerOs).
        /// </summary>
        public static bool IsServerKind(String osType, String os)
        {
            if (!String.IsNullOrWhiteSpace(osType))
                return osType.IndexOf("server", StringComparison.OrdinalIgnoreCase) >= 0
                    || osType.IndexOf("domaincontroller", StringComparison.OrdinalIgnoreCase) >= 0;
            return AdComputer.IsServerOs(os);
        }

        /// <summary>Has updates that Update checked would install (not paused).</summary>
        public bool HasUpdates { get { return !Paused && UpdateCount > 0; } }
    }

    /// <summary>State of the filters of the computer view.</summary>
    public sealed class ComputerFilterState
    {
        /// <summary>Contained (case-insensitive) in the name, the operating system, the OU or a package name.</summary>
        public String Search = "";
        public ComputerStatusFilter Statuses = ComputerStatusFilter.None;
        public bool IncludeServers;
        public int StaleDays = 30;
        /// <summary>Only these computers (checked in the main window; case-insensitive set); null: no such filter.</summary>
        public HashSet<String> Selection;
    }

    public static class ComputerFilter
    {
        /// <summary>
        /// Computers of the window, with their Active Directory entry (null when not in AD): the Active Directory
        /// inventory, the computers added by name (updates.xml), and the computers of the main window's list.
        /// Without any Active Directory inventory (no domain), every computer of updates.xml too. Sorted by name.
        /// </summary>
        public static List<KeyValuePair<String, AdComputer>> Universe(IEnumerable<AdComputer> ad, IEnumerable<ComputerUpdateState> saved, IEnumerable<String> listed)
        {
            Dictionary<String, AdComputer> result = new Dictionary<String, AdComputer>(StringComparer.OrdinalIgnoreCase);
            foreach (AdComputer c in ad ?? Enumerable.Empty<AdComputer>())
            {
                if (c != null && !String.IsNullOrWhiteSpace(c.Name)) result[c.Name.Trim()] = c;
            }
            bool noDirectory = result.Count == 0;
            foreach (ComputerUpdateState s in saved ?? Enumerable.Empty<ComputerUpdateState>())
            {
                if (s == null || String.IsNullOrWhiteSpace(s.Name) || result.ContainsKey(s.Name.Trim())) continue;
                if (s.AddedByName || noDirectory) result[s.Name.Trim()] = null;
            }
            foreach (String name in listed ?? Enumerable.Empty<String>())
            {
                if (!String.IsNullOrWhiteSpace(name) && !result.ContainsKey(name.Trim())) result[name.Trim()] = null;
            }
            return result.OrderBy(e => e.Key, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>
        /// A computer name typed in "Add": trimmed, without leading backslashes (a DNS name is kept as typed);
        /// null when it cannot be a computer name (empty, spaces, or characters NetBIOS and DNS names do not allow).
        /// </summary>
        public static String NormalizeName(String typed)
        {
            if (typed == null) return null;
            String name = typed.Trim().TrimStart((char)0x5C, '/').Trim();
            if (name.Length == 0 || name.Length > 255) return null;
            foreach (char ch in name)
            {
                if (!(Char.IsLetterOrDigit(ch) || ch == '-' || ch == '_' || ch == '.')) return null;
            }
            return name;
        }

        /// <summary>
        /// Offline or stale: an Active Directory computer whose last logon is older than <paramref name="days"/>
        /// days, or that never logged on. A computer unknown to Active Directory is never stale (nothing to tell).
        /// days &lt;= 0: never stale.
        /// </summary>
        public static bool IsStale(ComputerRow row, DateTime now, int days)
        {
            if (row == null || !row.InAd || days <= 0) return false;
            return !row.LastLogon.HasValue || row.LastLogon.Value < now.AddDays(-days);
        }

        /// <summary>True when the row has one of the statuses of the filter (or the filter has none).</summary>
        public static bool MatchesStatus(ComputerRow row, ComputerStatusFilter statuses, DateTime now, int staleDays)
        {
            if (statuses == ComputerStatusFilter.None) return true;
            if ((statuses & ComputerStatusFilter.ToUpdate) != 0 && row.HasUpdates) return true;
            if ((statuses & ComputerStatusFilter.Errors) != 0 && (row.Kind == ComputerSummaryKind.Errors || row.Kind == ComputerSummaryKind.Unreachable)) return true;
            if ((statuses & ComputerStatusFilter.UpToDate) != 0 && row.Kind == ComputerSummaryKind.UpToDate) return true;
            if ((statuses & ComputerStatusFilter.NotChecked) != 0 && row.Kind == ComputerSummaryKind.NotChecked && !row.Paused) return true;
            if ((statuses & ComputerStatusFilter.Paused) != 0 && row.Paused) return true;
            if ((statuses & ComputerStatusFilter.NightOnly) != 0 && row.NightOnly) return true;
            if ((statuses & ComputerStatusFilter.Stale) != 0 && IsStale(row, now, staleDays)) return true;
            return false;
        }

        /// <summary>The search text in the name, the operating system, the OU or a package name (empty: every row).</summary>
        public static bool MatchesSearch(ComputerRow row, String search)
        {
            if (String.IsNullOrWhiteSpace(search)) return true;
            String s = search.Trim();
            return Contains(row.Name, s) || Contains(row.Os, s) || Contains(row.Ou, s)
                || row.PackageNames.Any(p => Contains(p, s));
        }

        /// <summary>
        /// Every filter at once: disabled accounts are never shown, servers only with IncludeServers, then
        /// the selection, the search and the status chips.
        /// </summary>
        public static bool Matches(ComputerRow row, ComputerFilterState filter, DateTime now)
        {
            if (row == null || row.Disabled) return false;
            if (row.IsServer && !filter.IncludeServers) return false;
            if (filter.Selection != null && !filter.Selection.Contains(row.Name)) return false;
            if (!MatchesSearch(row, filter.Search)) return false;
            return MatchesStatus(row, filter.Statuses, now, filter.StaleDays);
        }

        public static List<ComputerRow> Apply(IEnumerable<ComputerRow> rows, ComputerFilterState filter, DateTime now)
        {
            return rows.Where(r => Matches(r, filter, now)).ToList();
        }

        /// <summary>Names of the status chips, for the settings ("ToUpdate,Errors").</summary>
        public static String FormatStatuses(ComputerStatusFilter statuses)
        {
            return String.Join(",", Enum.GetValues(typeof(ComputerStatusFilter)).Cast<ComputerStatusFilter>()
                .Where(f => f != ComputerStatusFilter.None && (statuses & f) == f).Select(f => f.ToString()));
        }

        /// <summary>Status chips read from the settings; unknown names are ignored.</summary>
        public static ComputerStatusFilter ParseStatuses(String text)
        {
            ComputerStatusFilter result = ComputerStatusFilter.None;
            if (String.IsNullOrWhiteSpace(text)) return result;
            foreach (String part in text.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                ComputerStatusFilter f;
                if (Enum.TryParse(part.Trim(), true, out f) && Enum.IsDefined(typeof(ComputerStatusFilter), f)) result |= f;
            }
            return result;
        }

        static bool Contains(String text, String part)
        {
            return text != null && text.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }

    /// <summary>Checked computers of the computer view: kept across filter changes, case-insensitive.</summary>
    public static class ComputerSelection
    {
        /// <summary>Select all: checks every visible row (the rows hidden by the filters keep their state).</summary>
        public static void CheckVisible(ISet<String> checkedNames, IEnumerable<ComputerRow> visible)
        {
            foreach (ComputerRow r in visible) checkedNames.Add(r.Name);
        }

        /// <summary>Select outdated: exactly the visible rows with updates (paused rows excluded).</summary>
        public static void CheckOutdated(ISet<String> checkedNames, IEnumerable<ComputerRow> visible)
        {
            checkedNames.Clear();
            foreach (ComputerRow r in visible.Where(r => r.HasUpdates)) checkedNames.Add(r.Name);
        }

        /// <summary>
        /// Computers an action applies to: the checked ones (even hidden by the search or the status chips,
        /// since the checks are kept on purpose), else the selected ones. Never a disabled account, never a
        /// server unless the servers are included, never a computer that is no longer in the list.
        /// </summary>
        public static List<String> Targets(IEnumerable<ComputerRow> all, ICollection<String> checkedNames, IEnumerable<String> selectedNames, bool includeServers)
        {
            Dictionary<String, ComputerRow> byName = new Dictionary<String, ComputerRow>(StringComparer.OrdinalIgnoreCase);
            foreach (ComputerRow r in all) byName[r.Name] = r;
            IEnumerable<String> source = checkedNames.Count > 0 ? (IEnumerable<String>)checkedNames : selectedNames ?? Enumerable.Empty<String>();
            List<String> result = new List<String>();
            HashSet<String> seen = new HashSet<String>(StringComparer.OrdinalIgnoreCase);
            foreach (String name in source)
            {
                ComputerRow r;
                if (!byName.TryGetValue(name, out r) || r.Disabled || (r.IsServer && !includeServers) || !seen.Add(r.Name)) continue;
                result.Add(r.Name);
            }
            result.Sort(StringComparer.OrdinalIgnoreCase);
            return result;
        }
    }
}
