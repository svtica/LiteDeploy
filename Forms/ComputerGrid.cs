// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    ComputerGrid.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-12
// Purpose: The computer grid, body of the main window: every enabled
//          computer of Active Directory (inventory of adcache.xml) and the
//          computers added (Add computers...: Active Directory search, subnet
//          scan, file or names typed; kept in updates.xml), with the state of
//          the winget packages of the package list (local state file). One row
//          per computer (OS, last logon, summary, pause and "outside business
//          hours only" flags) or, with "Show packages", one row per installed
//          package. Search box, status chips and "Include servers" (servers
//          hidden by default) filter the rows (ComputerFilter); the checks are
//          kept across filter changes. Check reads the versions on the checked
//          computers (every computer shown when none is checked); Update
//          checked upgrades the packages with an update of the checked
//          computers, or only the checked packages in the package view, after
//          one confirmation ("outside business hours only" computers are
//          skipped during business hours). The credential is offered at most
//          once per session. The work is done by LiteDeploy.Worker.exe run as
//          SYSTEM, a few computers at a time; each row shows the step in
//          progress. Actions (right-click or the Actions menu) apply to the
//          checked computers, else to the selected rows: Install (one package,
//          chosen explicitly, never part of Update checked or of the
//          schedules), pause, outside business hours only, computer card,
//          TeamViewer ID, remote desktop, command prompt, quick commands,
//          message to the logged-on users, Wake-on-LAN, logs, ping,
//          management console, admin share, deploy a file or command (PsExec,
//          DeployForm, run by the main window), export to Royal TS
//          (ComputerActions). A computer known under several spellings
//          (ComputerName) is one row. Splitter, columns and filters are kept
//          in the user settings; the grid always opens on one row per computer.
// -----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace LiteDeploy
{
    public partial class ComputerGrid : UserControl, IComputerActionHost
    {
        /// <summary>Computers processed at the same time.</summary>
        const int Parallelism = 4;

        /// <summary>One package to work on, on one computer.</summary>
        sealed class RowKey
        {
            public String Computer;
            public String PackageId;      // null: the computer has no package row yet
            public String Key { get { return (Computer + "|" + PackageId).ToLowerInvariant(); } }
        }

        sealed class Job
        {
            public String Computer;
            public List<UpdatePackage> Packages;
        }

        readonly UpdateStateStore store;
        readonly Object storeLock = new Object();
        /// <summary>Card, TeamViewer, remote desktop, command prompt, quick commands, logs, flags: shared with the main window.</summary>
        readonly ComputerActions actions;
        /// <summary>Send a message... (created here, after the quick commands).</summary>
        readonly ToolStripMenuItem messageItem = new ToolStripMenuItem();
        readonly List<Process> running = new List<Process>();
        /// <summary>Checked package rows (package view), by row key.</summary>
        readonly HashSet<String> checkedKeys = new HashSet<String>();
        /// <summary>Checked computers (computer view), kept across filter changes.</summary>
        readonly HashSet<String> checkedComputers = new HashSet<String>(ComputerName.Comparer);
        /// <summary>Step in progress, by row key (computer|package, or computer| for the computer).</summary>
        readonly Dictionary<String, String> live = new Dictionary<String, String>();
        readonly ListViewColumnSorter sorter = new ListViewColumnSorter { SortColumn = 0, Order = SortOrder.Ascending };
        readonly System.Windows.Forms.Timer searchTimer = new System.Windows.Forms.Timer { Interval = 250 };
        readonly Dictionary<ToolStripButton, ComputerStatusFilter> chips;

        List<UpdatePackage> packages;
        /// <summary>Every computer of the window (filters not applied), by name; rebuilt at each refresh.</summary>
        List<ComputerRow> rows = new List<ComputerRow>();
        Dictionary<String, ComputerRow> rowsByName = new Dictionary<String, ComputerRow>(ComputerName.Comparer);
        /// <summary>Rows shown (filters applied).</summary>
        List<ComputerRow> visible = new List<ComputerRow>();
        String computerColumnsLayout, packageColumnsLayout;
        /// <summary>The saved layout of the columns was applied to the view shown.</summary>
        bool columnsReady;
        volatile bool stopping;
        int pending;
        bool refreshing;
        bool loading = true;

        public ComputerGrid()
        {
            InitializeComponent();
            Localization.Apply(this);
            Theme.Apply(this);
            Theme.Apply(actionsButton.DropDown);
            Theme.Apply(rowMenu);
            if (Theme.IsDark)
            {
                searchBox.BackColor = Theme.Surface;
                searchBox.ForeColor = Theme.Text;
                searchBox.BorderStyle = BorderStyle.FixedSingle;
                includeServersCheck.ForeColor = Theme.Text;
            }

            chips = new Dictionary<ToolStripButton, ComputerStatusFilter>
            {
                { toUpdateFilter, ComputerStatusFilter.ToUpdate },
                { errorsFilter, ComputerStatusFilter.Errors },
                { upToDateFilter, ComputerStatusFilter.UpToDate },
                { notCheckedFilter, ComputerStatusFilter.NotChecked },
                { pausedFilter, ComputerStatusFilter.Paused },
                { nightOnlyFilter, ComputerStatusFilter.NightOnly },
                { staleFilter, ComputerStatusFilter.Stale },
                { hiddenFilter, ComputerStatusFilter.Hidden }
            };

            store = UpdateStateStore.Load(UpdateStateStore.DefaultPath);
            actions = new ComputerActions(this);
            messageItem.Text = Localization.T("ComputerGrid.messageItem.Text", "Send a message...");
            messageItem.ToolTipText = Localization.T("ComputerGrid.messageItem.ToolTipText", "A message box for the users logged on to the computers (replaces msg *)");
            messageItem.Click += (s, e) => actions.SendMessage(ActionTargets(), "");
            // In the right-click menu of the designer, after the quick commands (then moved with the other actions)
            rowMenu.Items.Insert(rowMenu.Items.IndexOf(quickCommandsItem) + 1, messageItem);
            RestoreSettings();
            LoadPackages();

            updatesList.ListViewItemSorter = sorter;
            updatesList.SmallImageList = StateImages();
            updatesList.ItemChecked += updatesList_ItemChecked;
            pingTimer.Tick += (s, e) => StartPing(false);
            Load += (s, e) => { StartPing(false); pingTimer.Start(); };
            searchTimer.Tick += (s, e) => { searchTimer.Stop(); RefreshList(); };
            ActiveDirectoryCache.InventoryChanged += ActiveDirectoryCache_InventoryChanged;
            UserSettings.Saved += UserSettings_Saved;
            Load += (s, e) => RestoreSplitter();
            ApplyDeploymentsVisibility();

            loading = false;
            RefreshList();
            SetIdleStatus();
        }

        /// <summary>Caption of the message boxes: the title of the window.</summary>
        String Caption
        {
            get
            {
                Form form = FindForm();
                return form == null || String.IsNullOrEmpty(form.Text) ? "Lite Deploy" : form.Text;
            }
        }

        /// <summary>Deploy a file or command... chosen (main window): the target computers.</summary>
        internal event Action<List<String>> DeployRequested;

        /// <summary>Ping... chosen (main window): the computers to ping.</summary>
        internal event Action<List<String>> PingRequested;

        /// <summary>The checked computers or the selection changed (deployment window: its targets).</summary>
        internal event EventHandler TargetsChanged;

        /// <summary>Items added to the status bar by the main window (daily web check).</summary>
        internal ToolStripItemCollection StatusItems { get { return statusStrip.Items; } }

        /// <summary>Standalone deployments (App > Options, hidden by default): the toolbar button and the action.</summary>
        internal void ApplyDeploymentsVisibility()
        {
            bool show = UserSettings.Current.ShowStandaloneDeployments;
            deployButton.Visible = deployItem.Visible = deploySeparator.Visible = show;
        }

        /// <summary>While a deployment runs, no other one starts.</summary>
        internal void SetDeployEnabled(bool enabled)
        {
            deployEnabled = enabled;
            deployButton.Enabled = enabled;
        }

        bool deployEnabled = true;

        /// <summary>Work in progress (check, update or install).</summary>
        internal bool Busy { get { return pending > 0; } }

        // ---- Settings ---------------------------------------------------------------

        int StaleDays { get { return UserSettings.Current.UpdatesStaleDays; } }

        void RestoreSettings()
        {
            UserSettings s = UserSettings.Current;
            includeServersCheck.Checked = s.IncludeServers;
            // The window always opens on one row per computer: "Show packages" is a detail view, not kept
            // (kept, it showed one row per package of every computer at each opening)
            byPackageButton.Checked = false;
            ComputerStatusFilter statuses = ComputerFilter.ParseStatuses(s.UpdatesStatusFilter);
            foreach (KeyValuePair<ToolStripButton, ComputerStatusFilter> chip in chips)
                chip.Key.Checked = (statuses & chip.Value) != 0;
            staleFilter.ToolTipText = Localization.F("ComputerGrid.StaleTip", "Last logon (Active Directory) older than {0} day(s), or never", StaleDays);
            computerColumnsLayout = s.UpdatesColumns ?? "";
            packageColumnsLayout = s.UpdatesPackageColumns ?? "";
            onlineOnlyButton.Checked = s.UpdatesOnlineOnly;
            profileName = s.UpdatesProfile ?? "";
            hiddenColumns = new HashSet<String>(WindowLayout.ParseNames(s.UpdatesHiddenColumns), StringComparer.OrdinalIgnoreCase);
        }

        // The splitter once the window has its size: the height of the details under the list
        void RestoreSplitter()
        {
            int details = UserSettings.Current.UpdatesSplitter;
            if (details <= 0) return;
            int distance = splitContainer.Height - splitContainer.SplitterWidth - details;
            int max = splitContainer.Height - splitContainer.SplitterWidth - splitContainer.Panel2MinSize;
            if (distance < splitContainer.Panel1MinSize || distance > max) return;
            try { splitContainer.SplitterDistance = distance; }
            catch (Exception) { }   // the default splitter
        }

        /// <summary>Filters, columns and splitter in the user settings (the main window adds its size and position).</summary>
        internal void SaveSettings(Action<UserSettings> more = null)
        {
            CaptureColumns();
            UserSettings s = UserSettings.Current.Clone();
            s.IncludeServers = includeServersCheck.Checked;
            s.UpdatesStatusFilter = ComputerFilter.FormatStatuses(Statuses);
            s.UpdatesColumns = computerColumnsLayout;
            s.UpdatesPackageColumns = packageColumnsLayout;
            s.UpdatesOnlineOnly = onlineOnlyButton.Checked;
            s.UpdatesHiddenColumns = String.Join(",", hiddenColumns.OrderBy(n => n, StringComparer.OrdinalIgnoreCase));
            Form form = FindForm();
            if (form == null || form.WindowState != FormWindowState.Minimized)
                s.UpdatesSplitter = splitContainer.Height - splitContainer.SplitterWidth - splitContainer.SplitterDistance;
            if (more != null) more(s);
            try { UserSettings.Save(s); }
            catch (Exception ex) { AppConsole.WriteLine("Software updates: could not save " + UserSettings.FilePath + ": " + ex.Message); }
        }

        // Width and position of the columns of the view shown, kept for the settings
        void CaptureColumns()
        {
            if (!columnsReady || updatesList.Columns.Count == 0) return;
            String layout = WindowLayout.FormatColumns(updatesList.Columns.Cast<ColumnHeader>()
                .Select(c => new ColumnLayout { Name = c.Name, Width = c.Width, Position = c.DisplayIndex }));
            if (updatesList.Columns.Contains(packageColumn)) packageColumnsLayout = layout;
            else computerColumnsLayout = layout;
        }

        void ApplyColumns(String layout)
        {
            Dictionary<String, ColumnLayout> saved = WindowLayout.ParseColumns(layout);
            List<ColumnHeader> columns = updatesList.Columns.Cast<ColumnHeader>().ToList();
            foreach (ColumnHeader c in columns)
            {
                ColumnLayout l;
                if (saved.TryGetValue(c.Name, out l)) c.Width = l.Width;
            }
            List<String> order = WindowLayout.Order(columns.Select(c => c.Name).ToList(), saved);
            // The State column, new to a saved layout, goes next to the Computer column (not last)
            if (!saved.ContainsKey(stateColumn.Name) && order.Remove(stateColumn.Name)) order.Insert(Math.Min(1, order.Count), stateColumn.Name);
            for (int i = 0; i < order.Count; i++)
            {
                ColumnHeader c = columns.First(x => x.Name == order[i]);
                try { c.DisplayIndex = i; } catch (Exception) { }
            }
        }

        // ---- Display ----------------------------------------------------------------

        /// <summary>One row per package (Show packages) instead of one row per computer.</summary>
        bool ByPackage { get { return byPackageButton.Checked; } }

        bool IncludeServers { get { return includeServersCheck.Checked; } }

        ComputerStatusFilter Statuses
        {
            get
            {
                ComputerStatusFilter result = ComputerStatusFilter.None;
                foreach (KeyValuePair<ToolStripButton, ComputerStatusFilter> chip in chips)
                    if (chip.Key.Checked) result |= chip.Value;
                return result;
            }
        }

        ComputerFilterState CurrentFilter()
        {
            return new ComputerFilterState
            {
                Search = searchBox.Text,
                Statuses = Statuses,
                IncludeServers = IncludeServers,
                StaleDays = StaleDays,
                OnlineOnly = onlineOnlyButton.Checked
            };
        }

        /// <summary>Every column of the computer view, in the default order; the Computer column is always shown.</summary>
        ColumnHeader[] ComputerColumns
        {
            get
            {
                return new[] { computerColumn, stateColumn, userColumn, osColumn, lastLogonColumn, statusColumn, updatesColumn, resultColumn,
                    lastScanColumn, changedColumn, pausedColumn, nightOnlyColumn,
                    manufacturerColumn, modelColumn, serialColumn, yearColumn, cpuColumn, coresColumn, ramColumn, diskColumn, diskFreeColumn };
            }
        }

        /// <summary>Columns of the computer view the user hid (Columns menu), by name; kept in the settings.</summary>
        HashSet<String> hiddenColumns = new HashSet<String>(StringComparer.OrdinalIgnoreCase);

        void SetColumns()
        {
            ColumnHeader[] wanted = ByPackage
                ? new[] { computerColumn, packageColumn, installedColumn, availableColumn, statusColumn, resultColumn, changedColumn }
                : ComputerColumns.Where(c => c == computerColumn || !hiddenColumns.Contains(c.Name)).ToArray();
            if (columnsReady && updatesList.Columns.Count == wanted.Length && wanted.Select((c, i) => updatesList.Columns[i] == c).All(b => b)) return;
            // The layout of the view left (not the columns of the designer, before the saved layout is applied)
            if (columnsReady) CaptureColumns();
            columnsReady = true;
            updatesList.Columns.Clear();
            updatesList.Columns.AddRange(wanted);
            ApplyColumns(ByPackage ? packageColumnsLayout : computerColumnsLayout);
            sorter.SortColumn = 0;
            sorter.Order = SortOrder.Ascending;
        }

        // Every computer of the window with its summary, from the Active Directory inventory and the saved state
        void BuildRows(Dictionary<String, ComputerUpdateState> states, Dictionary<String, ComputerSummary> summaries)
        {
            foreach (ComputerUpdateState c in store.Computers)
            {
                if (!String.IsNullOrEmpty(c.Name)) states[c.Name] = c;
            }
            List<ComputerRow> result = new List<ComputerRow>();
            foreach (KeyValuePair<String, AdComputer> entry in ComputerFilter.Universe(ActiveDirectoryCache.Inventory, store.Computers, null))
            {
                ComputerUpdateState state;
                states.TryGetValue(entry.Key, out state);
                ComputerSummary summary = ComputerSummary.Of(state, packages);
                summaries[entry.Key] = summary;
                ComputerRow row = ComputerRow.Build(entry.Key, entry.Value, state, summary, packages);
                row.Online = online.Get(entry.Key);
                result.Add(row);
            }
            rows = result;
            rowsByName = result.ToDictionary(r => r.Name, ComputerName.Comparer);
        }

        void RefreshList()
        {
            if (IsDisposed) return;
            // Kept across the rebuild: the selected rows and the first row shown
            HashSet<String> selectedKeys = new HashSet<String>(updatesList.SelectedItems.Cast<ListViewItem>().Select(i => ((RowKey)i.Tag).Key));
            String topKey = null;
            try { if (updatesList.TopItem != null) topKey = ((RowKey)updatesList.TopItem.Tag).Key; } catch (Exception) { }

            refreshing = true;
            ListViewItem top = null;
            updatesList.BeginUpdate();
            try
            {
                SetColumns();
                updatesList.Items.Clear();
                List<ListViewItem> items = new List<ListViewItem>();
                DateTime now = DateTime.Now;
                lock (storeLock)
                {
                    Dictionary<String, ComputerUpdateState> states = new Dictionary<String, ComputerUpdateState>(ComputerName.Comparer);
                    Dictionary<String, ComputerSummary> summaries = new Dictionary<String, ComputerSummary>(ComputerName.Comparer);
                    BuildRows(states, summaries);
                    visible = ComputerFilter.Apply(rows, CurrentFilter(), now);
                    foreach (ComputerRow row in visible)
                    {
                        ComputerUpdateState c;
                        states.TryGetValue(row.Name, out c);
                        ComputerSummary summary = summaries[row.Name];
                        if (!ByPackage || summary.Kind == ComputerSummaryKind.Paused || !AddPackageRows(items, row, c))
                            items.Add(ComputerItem(row, c, summary, now));
                    }
                }
                updatesList.Items.AddRange(items.ToArray());
                foreach (ListViewItem item in updatesList.Items)
                {
                    String key = ((RowKey)item.Tag).Key;
                    if (selectedKeys.Contains(key)) item.Selected = true;
                    if (key == topKey) top = item;
                }
            }
            finally
            {
                updatesList.EndUpdate();
            }
            if (top != null && updatesList.IsHandleCreated)
            {
                try { updatesList.TopItem = top; } catch (Exception) { }
            }
            refreshing = false;
            UpdateButtons();
            UpdateCounts();
            UpdateDetails();
        }

        // One row for the computer: its summary. Its checkbox selects the computer.
        ListViewItem ComputerItem(ComputerRow row, ComputerUpdateState c, ComputerSummary summary, DateTime now)
        {
            String computer = row.Name;
            String step = LiveStep(computer);
            String[] cells;
            if (ByPackage)
            {
                cells = new[] { computer, "", "", "", SummaryText(summary), ComputerResult(c, summary), Format(c?.LastChange) };
                if (step.Length > 0) cells[4] = step;
            }
            else
            {
                // The value of every column of the computer view, then the ones shown, in their order
                Dictionary<ColumnHeader, String> values = new Dictionary<ColumnHeader, String>
                {
                    { computerColumn, computer },
                    { stateColumn, OnlineText(row.Online) },
                    { userColumn, row.Users ?? "" },
                    { osColumn, row.Os ?? "" },
                    { lastLogonColumn, row.LastLogon.HasValue ? row.LastLogon.Value.ToString("yyyy-MM-dd") : "" },
                    { statusColumn, step.Length > 0 ? step : SummaryText(summary) },
                    { updatesColumn, UpdatesText(c, summary) },
                    { resultColumn, ComputerResult(c, summary) },
                    { lastScanColumn, Format(row.LastScan) },
                    { changedColumn, Format(c?.LastChange) },
                    { pausedColumn, row.Paused ? Localization.T("ComputerGrid.Paused", "Paused") : "" },
                    { nightOnlyColumn, row.NightOnly ? Localization.T("ComputerGrid.Yes", "yes") : "" },
                    { manufacturerColumn, c?.Manufacturer ?? "" },
                    { modelColumn, c?.Model ?? "" },
                    { serialColumn, c?.Serial ?? "" },
                    { yearColumn, InventoryTable.Year(c?.BiosDate) },
                    { cpuColumn, c?.Cpu ?? "" },
                    { coresColumn, InventoryTable.CoresText(c) },
                    { ramColumn, c?.RamGb ?? "" },
                    { diskColumn, c?.DiskGb ?? "" },
                    { diskFreeColumn, c?.DiskFreeGb ?? "" }
                };
                cells = updatesList.Columns.Cast<ColumnHeader>().Select(h => { String v; return values.TryGetValue(h, out v) ? v : ""; }).ToArray();
            }
            for (int i = 0; i < cells.Length; i++) cells[i] = ComputerName.OneLine(cells[i]);

            ListViewItem item = new ListViewItem(cells) { Tag = new RowKey { Computer = computer }, ToolTipText = RowTip(row), ImageIndex = OnlineImage(row.Online) };
            if (ComputerFilter.IsStale(row, now, StaleDays)) item.ForeColor = SystemColors.GrayText;
            switch (summary.Kind)
            {
                case ComputerSummaryKind.Paused: item.ForeColor = SystemColors.GrayText; break;
                case ComputerSummaryKind.Errors:
                case ComputerSummaryKind.Unreachable: item.ForeColor = Theme.ErrorColor; break;
                case ComputerSummaryKind.Outdated: item.ForeColor = Theme.WarningColor; break;
            }
            if (step.Length > 0) item.ForeColor = Theme.Accent;
            item.Checked = !ByPackage && checkedComputers.Contains(computer);
            return item;
        }

        // Tooltip of a row: why it is paused, outside business hours only, where it comes from
        String RowTip(ComputerRow row)
        {
            String tip = FlagsText(row);
            if (!String.IsNullOrEmpty(row.Ou))
                tip = Localization.F("ComputerGrid.OuTip", "OU: {0}", AdComputer.ShortOu(row.Ou)) + (tip.Length > 0 ? "\n" + tip : "");
            if (!String.IsNullOrEmpty(row.TeamViewerId))
                tip += (tip.Length > 0 ? "\n" : "") + TeamViewerText(row.TeamViewerId);
            return tip;
        }

        static String TeamViewerText(String id)
        {
            return Localization.F("ComputerGrid.TeamViewerTip", "TeamViewer ID: {0}", id);
        }

        // Pause reason, outside business hours only, not in Active Directory: one line each
        static String FlagsText(ComputerRow row)
        {
            List<String> lines = new List<String>();
            if (row.Paused)
                lines.Add(Localization.F("ComputerGrid.PausedTip", "Paused: {0}",
                    String.IsNullOrEmpty(row.PauseReason) ? Localization.T("ComputerGrid.NoReason", "(no reason given)") : row.PauseReason));
            if (row.NightOnly)
                lines.Add(Localization.F("ComputerGrid.NightOnlyTip", "Updated outside business hours only (business hours: {0} to {1}, Monday to Friday)",
                    UserSettings.Current.BusinessHoursStart, UserSettings.Current.BusinessHoursEnd));
            if (row.Hidden)
                lines.Add(Localization.T("ComputerGrid.HiddenTip", "Removed from the list (Hidden filter): never updated until shown in the list again"));
            if (!row.InAd)
                lines.Add(row.AddedByName
                    ? Localization.T("ComputerGrid.AddedByNameTip", "Not in Active Directory: added by name")
                    : Localization.T("ComputerGrid.NotInAdTip", "Not in Active Directory"));
            return String.Join("\n", lines);
        }

        // One row per package of the computer, except the packages that are not installed (and, when the
        // search names a package and not the computer, the other packages); false when none
        bool AddPackageRows(List<ListViewItem> items, ComputerRow row, ComputerUpdateState c)
        {
            if (c == null) return false;
            String computer = row.Name;
            String search = searchBox.Text.Trim();
            bool onlyMatching = search.Length > 0 && !ComputerFilter.MatchesSearch(new ComputerRow { Name = row.Name, Os = row.Os, Ou = row.Ou }, search);
            bool any = false;
            foreach (UpdatePackage p in packages)
            {
                PackageUpdateState s = c.Find(p.Id);
                if (s == null || s.Status == null || s.Status == WorkerProtocol.StatusNotInstalled) continue;
                if (onlyMatching && (p.DisplayName ?? "").IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0) continue;
                any = true;
                RowKey key = new RowKey { Computer = computer, PackageId = p.Id };
                String[] cells =
                {
                    computer,
                    p.DisplayName,
                    s.Installed ?? "",
                    s.Available ?? "",
                    StatusText(s.Status),
                    ResultText(s),
                    Format(c.LastChange)
                };
                String step;
                lock (live)
                {
                    // the package's own step ("" once it is done), else the step of its computer (connecting, started)
                    if (!live.TryGetValue(key.Key, out step) && !live.TryGetValue(new RowKey { Computer = computer }.Key, out step))
                        step = "";
                }
                if (step.Length > 0) cells[4] = step;
                for (int i = 0; i < cells.Length; i++) cells[i] = ComputerName.OneLine(cells[i]);
                ListViewItem item = new ListViewItem(cells) { Tag = key, ToolTipText = RowTip(row), ImageIndex = OnlineImage(row.Online) };
                if (s.Status == WorkerProtocol.StatusOutdated) item.ForeColor = Theme.WarningColor;
                if (s.Status == WorkerProtocol.StatusError || (s.Status == WorkerProtocol.StatusOutdated && s.LastResult == WorkerProtocol.ResultFailed)) item.ForeColor = Theme.ErrorColor;
                if (step.Length > 0) item.ForeColor = Theme.Accent;
                item.Checked = checkedKeys.Contains(key.Key);
                items.Add(item);
            }
            return any;
        }

        // Step in progress on a computer: the step of one of its packages, else its own step; "" when idle
        String LiveStep(String computer)
        {
            String prefix = (computer + "|").ToLowerInvariant();
            String own = new RowKey { Computer = computer }.Key;
            lock (live)
            {
                foreach (KeyValuePair<String, String> entry in live)
                {
                    if (entry.Key == own || !entry.Key.StartsWith(prefix, StringComparison.Ordinal) || entry.Value.Length == 0) continue;
                    UpdatePackage p = packages.FirstOrDefault(x => String.Equals(x.Id, entry.Key.Substring(prefix.Length), StringComparison.OrdinalIgnoreCase));
                    return p == null ? entry.Value : entry.Value + " (" + p.DisplayName + ")";
                }
                String step;
                return live.TryGetValue(own, out step) ? step : "";
            }
        }

        static String SummaryText(ComputerSummary s)
        {
            switch (s.Kind)
            {
                case ComputerSummaryKind.Paused: return Localization.T("ComputerGrid.Paused", "Paused");
                case ComputerSummaryKind.Unreachable: return Localization.T("ComputerGrid.Unreachable", "Error");
                case ComputerSummaryKind.Errors:
                    return s.Outdated.Count > 0
                        ? Localization.F("ComputerGrid.ErrorsAndUpdates", "{0} error(s), {1} update(s)", s.Errors.Count, s.Outdated.Count)
                        : Localization.F("ComputerGrid.Errors", "{0} error(s)", s.Errors.Count);
                case ComputerSummaryKind.Outdated: return Localization.F("ComputerGrid.UpdatesAvailable", "{0} update(s)", s.Outdated.Count);
                case ComputerSummaryKind.UpToDate: return Localization.T("ComputerGrid.UpToDate", "Up to date");
                default: return Localization.T("ComputerGrid.NotChecked", "Not checked");
            }
        }

        // "Firefox 120.0 > 121.0, 7-Zip 23.01 > 24.08"
        static String UpdatesText(ComputerUpdateState c, ComputerSummary s)
        {
            if (c == null) return "";
            return String.Join(", ", s.Outdated.Select(p =>
            {
                PackageUpdateState ps = c.Find(p.Id);
                return p.DisplayName + " " + ps.Installed + " → " + ps.Available;
            }));
        }

        // Pause reason, errors of the packages, or the error of the last run
        static String ComputerResult(ComputerUpdateState c, ComputerSummary s)
        {
            if (c == null) return "";
            if (s.Kind == ComputerSummaryKind.Paused) return c.PauseReason ?? "";
            // The errors of the last run ("id: message | ..."), else the failed updates still to do
            if (c.LastError != null) return c.LastError;
            return String.Join(" | ", s.Errors.Select(p => p.DisplayName + ": " + ResultText(c.Find(p.Id))));
        }

        void updatesList_ItemChecked(object sender, ItemCheckedEventArgs e)
        {
            if (refreshing) return;
            RowKey key = (RowKey)e.Item.Tag;
            if (key.PackageId == null)
            {
                if (ByPackage)
                {
                    // A computer row in the package view (nothing installed, not checked): nothing to update
                    if (e.Item.Checked) e.Item.Checked = false;
                    return;
                }
                if (e.Item.Checked) checkedComputers.Add(key.Computer);
                else checkedComputers.Remove(key.Computer);
            }
            else
            {
                if (e.Item.Checked) checkedKeys.Add(key.Key);
                else checkedKeys.Remove(key.Key);
            }
            UpdateButtons();
            UpdateCounts();
        }

        void byPackageButton_Click(object sender, EventArgs e)
        {
            RefreshList();
        }

        void updatesList_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyData == (Keys.Control | Keys.A))
            {
                selectAllButton_Click(sender, e);
                e.Handled = e.SuppressKeyPress = true;
            }
        }

        void updatesList_ColumnClick(object sender, ColumnClickEventArgs e)
        {
            if (e.Column == sorter.SortColumn)
                sorter.Order = sorter.Order == SortOrder.Ascending ? SortOrder.Descending : SortOrder.Ascending;
            else
            {
                sorter.SortColumn = e.Column;
                sorter.Order = SortOrder.Ascending;
            }
            updatesList.Sort();
        }

        void updatesList_DoubleClick(object sender, EventArgs e)
        {
            String computer = SelectedComputers().FirstOrDefault();
            if (computer != null) ShowCards(new List<String> { computer });
        }

        void updatesList_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (refreshing) return;
            UpdateButtons();
            UpdateDetails();
            OnTargetsChanged();
        }

        void OnTargetsChanged()
        {
            EventHandler handler = TargetsChanged;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        // What is known of the computer of the selected row, under the list
        void UpdateDetails()
        {
            String computer = SelectedComputers().FirstOrDefault();
            ComputerRow row;
            if (computer == null || !rowsByName.TryGetValue(computer, out row))
            {
                detailsBox.Text = Localization.T("ComputerGrid.DetailsHint",
                    "Select a computer to see its details. Double-click: computer card. Right-click: actions. F5: check, Ctrl+U: update, Ctrl+F: search.");
                return;
            }
            StringBuilder sb = new StringBuilder();
            sb.Append(row.Name);
            if (!String.IsNullOrEmpty(row.Os)) sb.Append(" - ").Append(row.Os).Append(String.IsNullOrEmpty(row.OsVersion) ? "" : " (" + row.OsVersion + ")");
            if (row.IsServer) sb.Append(" - ").Append(Localization.T("ComputerGrid.Server", "server"));
            sb.AppendLine();
            if (row.InAd)
                sb.AppendLine(Localization.F("ComputerGrid.DetailsDirectory", "Active Directory: {0}, last logon {1}",
                    String.IsNullOrEmpty(row.Ou) ? "?" : row.Ou,
                    row.LastLogon.HasValue ? row.LastLogon.Value.ToString("yyyy-MM-dd HH:mm") : Localization.T("ComputerGrid.Never", "never")));
            foreach (String line in FlagsText(row).Split('\n').Where(l => l.Length > 0)) sb.AppendLine(line);
            if (!String.IsNullOrEmpty(row.TeamViewerId)) sb.AppendLine(TeamViewerText(row.TeamViewerId));
            if (row.Online.HasValue || !String.IsNullOrEmpty(row.Users) || row.LastScan.HasValue)
                sb.AppendLine(Localization.F("ComputerGrid.DetailsState", "State: {0}; user: {1}; last scan: {2}",
                    row.Online.HasValue ? OnlineText(row.Online) : "?",
                    String.IsNullOrEmpty(row.Users) ? "?" : row.Users,
                    row.LastScan.HasValue ? Format(row.LastScan) : Localization.T("ComputerGrid.Never", "never")));
            lock (storeLock)
            {
                ComputerUpdateState c = store.Find(row.Name);
                if (c != null)
                {
                    if (c.LastError != null) sb.AppendLine(c.LastError);
                    foreach (UpdatePackage p in packages)
                    {
                        PackageUpdateState s = c.Find(p.Id);
                        if (s == null || s.Status == null || s.Status == WorkerProtocol.StatusNotInstalled) continue;
                        sb.Append("  ").Append(p.DisplayName).Append(": ").Append(StatusText(s.Status));
                        if (!String.IsNullOrEmpty(s.Installed)) sb.Append(" ").Append(s.Installed);
                        if (s.Status == WorkerProtocol.StatusOutdated && !String.IsNullOrEmpty(s.Available)) sb.Append(" → ").Append(s.Available);
                        String result = ResultText(s);
                        if (result.Length > 0) sb.Append(" - ").Append(result);
                        sb.AppendLine();
                    }
                }
            }
            detailsBox.Text = sb.ToString().TrimEnd();
        }

        static String Format(DateTime? time)
        {
            return time.HasValue ? time.Value.ToString("yyyy-MM-dd HH:mm") : "";
        }

        static String StatusText(String status)
        {
            switch (status)
            {
                case WorkerProtocol.StatusUpToDate: return Localization.T("ComputerGrid.UpToDate", "Up to date");
                case WorkerProtocol.StatusOutdated: return Localization.T("ComputerGrid.Outdated", "Update available");
                case WorkerProtocol.StatusNotInstalled: return Localization.T("ComputerGrid.NotInstalled", "Not installed");
                case WorkerProtocol.StatusError: return Localization.T("ComputerGrid.Error", "Error");
                default: return Localization.T("ComputerGrid.NotChecked", "Not checked");
            }
        }

        static String ResultText(PackageUpdateState s)
        {
            if (s == null || s.LastResult == null) return "";
            String when = Format(s.LastUpgrade);
            switch (s.LastResult)
            {
                case WorkerProtocol.ResultUpgraded: return Localization.F("ComputerGrid.Upgraded", "Updated {0}", when);
                case WorkerProtocol.ResultInstalled: return Localization.F("ComputerGrid.Installed", "Installed {0}", when);
                case WorkerProtocol.ResultFailed: return Localization.F("ComputerGrid.Failed", "Failed {0}: {1}", when, s.LastMessage);
                default: return s.LastMessage ?? "";
            }
        }

        void SetIdleStatus()
        {
            if (packages.Count == 0)
                statusLabel.Text = profileFound
                    ? Localization.T("ComputerGrid.NoPackagesInProfile", "No software in this profile: click \"Software catalog...\" or choose another profile.")
                    : Localization.F("ComputerGrid.ProfileMissing", "The profile {0} no longer exists: choose another profile (nothing is checked or updated meanwhile).", profileName);
            else if (ActiveDirectoryCache.Inventory.Count == 0 && rows.Count == 0)
                statusLabel.Text = Localization.T("ComputerGrid.NoComputers", "No computer: the Active Directory list is not loaded yet, or click \"Add computers...\".");
            else
                statusLabel.Text = Localization.F("ComputerGrid.IdleProfile", "{0} software in the profile {1} ({2} in the catalog).", packages.Count, ProfileDisplayName, catalog.Count);
        }

        void UpdateCounts()
        {
            int total = rows.Count(r => !r.Disabled);
            int checkedCount = ByPackage ? checkedKeys.Count : checkedComputers.Count;
            countsLabel.Text = Localization.F("ComputerGrid.Counts", "{0} shown / {1} total / {2} checked", visible.Count, total, checkedCount);
            OnTargetsChanged();
        }

        void UpdateButtons()
        {
            bool idle = pending == 0;
            bool anyTarget = (ByPackage ? checkedKeys.Count > 0 : checkedComputers.Count > 0) || updatesList.SelectedItems.Count > 0;
            checkButton.Enabled = idle && packages.Count > 0 && (visible.Count > 0 || checkedComputers.Count > 0);
            selectOutdatedButton.Enabled = selectAllButton.Enabled = selectNoneButton.Enabled = idle;
            updateButton.Enabled = idle && packages.Count > 0 && anyTarget;
            editPackagesButton.Enabled = profileButton.Enabled = idle;
            byPackageButton.Enabled = idle;
            stopButton.Enabled = !idle;
        }

        // ---- Filters ------------------------------------------------------------------

        void searchBox_TextChanged(object sender, EventArgs e)
        {
            searchTimer.Stop();
            searchTimer.Start();
        }

        void searchBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                searchTimer.Stop();
                RefreshList();
                updatesList.Focus();
                e.Handled = e.SuppressKeyPress = true;
            }
            else if (e.KeyCode == Keys.Down)
            {
                updatesList.Focus();
                if (updatesList.Items.Count > 0 && updatesList.SelectedItems.Count == 0) updatesList.Items[0].Selected = true;
                e.Handled = e.SuppressKeyPress = true;
            }
        }

        void statusFilter_CheckedChanged(object sender, EventArgs e)
        {
            if (!loading) RefreshList();
        }

        void includeServersCheck_CheckedChanged(object sender, EventArgs e)
        {
            if (loading || syncingSettings) return;
            SaveSettings();   // the setting is shared (the worker side and the computer list read it too)
            RefreshList();
        }

        bool syncingSettings;

        // Include servers changed elsewhere (computer list, options): same filter here
        void UserSettings_Saved(object sender, EventArgs e)
        {
            if (IsDisposed || includeServersCheck.Checked == UserSettings.Current.IncludeServers) return;
            syncingSettings = true;
            try { includeServersCheck.Checked = UserSettings.Current.IncludeServers; }
            finally { syncingSettings = false; }
            if (!loading) RefreshList();
        }

        // ---- Keyboard -------------------------------------------------------------------

        /// <summary>Shortcuts of the grid, from anywhere in the main window; true when handled.</summary>
        internal bool HandleShortcut(Keys keyData)
        {
            switch (keyData)
            {
                case Keys.Control | Keys.F:
                    searchBox.Focus();
                    searchBox.SelectAll();
                    return true;
                case Keys.F5:
                    if (checkButton.Enabled) checkButton_Click(this, EventArgs.Empty);
                    return true;
                case Keys.Shift | Keys.F5:
                    StartPing(true);
                    return true;
                case Keys.Control | Keys.U:
                    if (updateButton.Enabled) updateButton_Click(this, EventArgs.Empty);
                    return true;
                case Keys.Escape:
                    if (pending > 0)
                    {
                        ConfirmStop();
                        return true;
                    }
                    if (searchBox.Focused && searchBox.Text.Length > 0)
                    {
                        searchBox.Clear();
                        return true;
                    }
                    break;
            }
            return false;
        }

        // ---- Selection --------------------------------------------------------------

        void selectAllButton_Click(object sender, EventArgs e)
        {
            if (pending > 0) return;
            if (ByPackage)
            {
                foreach (ListViewItem item in updatesList.Items)
                {
                    RowKey key = (RowKey)item.Tag;
                    if (key.PackageId != null) checkedKeys.Add(key.Key);
                }
            }
            else
            {
                ComputerSelection.CheckVisible(checkedComputers, visible);
            }
            RefreshList();
        }

        void selectNoneButton_Click(object sender, EventArgs e)
        {
            checkedKeys.Clear();
            checkedComputers.Clear();
            RefreshList();
        }

        void selectOutdatedButton_Click(object sender, EventArgs e)
        {
            ComputerSelection.CheckOutdated(checkedComputers, visible);
            checkedKeys.Clear();
            lock (storeLock)
            {
                foreach (ComputerRow row in visible.Where(r => r.HasUpdates))
                {
                    foreach (UpdatePackage p in ComputerSummary.Of(store.Find(row.Name), packages).Outdated)
                        checkedKeys.Add(new RowKey { Computer = row.Name, PackageId = p.Id }.Key);
                }
            }
            RefreshList();
            if (checkedComputers.Count == 0)
                statusLabel.Text = Localization.T("ComputerGrid.NothingOutdated", "No update available: click \"Check\" first if the list is not up to date.");
        }

        /// <summary>Checked computers, or the computers of the checked package rows in the package view (servers only when included).</summary>
        List<String> CheckedComputers()
        {
            IEnumerable<String> names = ByPackage
                ? rows.Where(r => packages.Any(p => checkedKeys.Contains(new RowKey { Computer = r.Name, PackageId = p.Id }.Key))).Select(r => r.Name)
                : checkedComputers;
            return ComputerSelection.Targets(rows, names.ToList(), null, IncludeServers);
        }

        /// <summary>The computers an action applies to: the checked ones, else the selected rows.</summary>
        List<String> ActionTargets()
        {
            List<String> checkedNames = CheckedComputers();
            return checkedNames.Count > 0 ? checkedNames : ComputerSelection.Targets(rows, new List<String>(), SelectedComputers(), IncludeServers);
        }

        List<String> SelectedComputers()
        {
            return updatesList.SelectedItems.Cast<ListViewItem>().Select(i => ((RowKey)i.Tag).Computer)
                .Distinct(ComputerName.Comparer).ToList();
        }

        // ---- Account ----------------------------------------------------------------

        /// <summary>Asked at most once per session (until LiteDeploy is restarted), in one question.</summary>
        static bool credentialAsked;

        /// <summary>
        /// Before a check or an update run as the current user: one question, offering to add a credential
        /// when none is stored, or to use the stored one; the credential chosen becomes the default of the
        /// software updates (App > Options). False: the user cancelled the run.
        /// </summary>
        bool OfferCredential()
        {
            List<LiteSuite.StoredCredential> list;
            try { list = LiteSuite.CredentialStore.List().ToList(); }
            catch (Exception) { return true; }   // Credential Manager unavailable: the current user

            CredentialOffer offer = UpdateRunner.OfferFor(UpdateRunner.UpdateCredentialName, list.Count, credentialAsked);
            if (offer == CredentialOffer.None) return true;
            String me = Environment.UserDomainName + "\\" + Environment.UserName;
            String name = null;
            DialogResult answer;
            switch (offer)
            {
                case CredentialOffer.Add:
                    answer = MessageBox.Show(this,
                        Localization.F("ComputerGrid.OfferAddCredentialOnce", "No credential is stored: the computers are reached with your account ({0}). Add a credential now? It becomes the default of the software updates (App > Options).\n\nYes: add one. No: continue with your account.", me),
                        Caption, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                    if (answer == DialogResult.Cancel) return false;
                    credentialAsked = true;
                    if (answer == DialogResult.No) return true;
                    name = CredentialsForm.AddDialog(this);
                    if (name == null) return false;
                    break;
                case CredentialOffer.UseStored:
                    answer = MessageBox.Show(this,
                        Localization.F("ComputerGrid.OfferUseCredentialOnce", "No credential is chosen for the software updates: the computers are reached with your account ({0}). Use the stored credential \"{1}\"? It becomes the default (App > Options).\n\nYes: use it. No: continue with your account.", me, list[0].Name),
                        Caption, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                    if (answer == DialogResult.Cancel) return false;
                    credentialAsked = true;
                    if (answer == DialogResult.No) return true;
                    name = list[0].Name;
                    break;
                case CredentialOffer.ChooseInOptions:
                    answer = MessageBox.Show(this,
                        Localization.F("ComputerGrid.OfferChooseCredential", "No credential is chosen for the software updates: the computers are reached with your account ({0}). Choose a credential in App > Options now?\n\nYes: open the options. No: continue with your account.", me),
                        Caption, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                    if (answer == DialogResult.Cancel) return false;
                    credentialAsked = true;
                    if (answer == DialogResult.Yes)
                        using (OptionsForm options = new OptionsForm()) options.ShowDialog(this);
                    return true;
            }

            UpdateRunner.SessionCredential = name;
            UserSettings settings = UserSettings.Current.Clone();
            settings.UpdateCredential = name;
            try { UserSettings.Save(settings); }
            catch (Exception ex) { AppConsole.WriteLine("Software updates: could not save " + UserSettings.FilePath + ": " + ex.Message); }
            return true;
        }

        // ---- Runs -------------------------------------------------------------------

        void checkButton_Click(object sender, EventArgs e)
        {
            if (pending > 0) return;
            if (!OfferCredential()) return;
            LoadPackages();
            // The checked computers, or every computer shown when none is checked
            List<String> targets = CheckedComputers();
            if (targets.Count == 0) targets = visible.Select(r => r.Name).ToList();
            Start("scan", targets.Where(c => !IsPaused(c)).Select(c => new Job { Computer = c, Packages = packages.ToList() }).ToList());
        }

        void updateButton_Click(object sender, EventArgs e)
        {
            if (pending > 0) return;
            // Computer view: the packages with an update of each checked (else selected) computer.
            // Package view: on each computer, only its checked (else selected) package rows.
            HashSet<String> keys = new HashSet<String>(checkedKeys);
            if (ByPackage && keys.Count == 0)
            {
                foreach (ListViewItem item in updatesList.SelectedItems)
                {
                    RowKey key = (RowKey)item.Tag;
                    if (key.PackageId != null) keys.Add(key.Key);
                }
            }
            List<String> computers = ByPackage
                ? ComputerSelection.Targets(rows, rows.Where(r => packages.Any(p => keys.Contains(new RowKey { Computer = r.Name, PackageId = p.Id }.Key))).Select(r => r.Name).ToList(), null, IncludeServers)
                : ActionTargets();

            List<Job> jobs = new List<Job>();
            foreach (String computer in computers.Where(c => !IsPaused(c)))
            {
                List<UpdatePackage> selected;
                if (ByPackage)
                    selected = packages.Where(p => keys.Contains(new RowKey { Computer = computer, PackageId = p.Id }.Key)).ToList();
                else
                    lock (storeLock) selected = ComputerSummary.Of(store.Find(computer), packages).Outdated;
                if (selected.Count > 0) jobs.Add(new Job { Computer = computer, Packages = selected });
            }
            if (jobs.Count == 0)
            {
                statusLabel.Text = Localization.T("ComputerGrid.NothingToUpdate", "No update available on the checked computers: click \"Check\" first if the list is not up to date.");
                return;
            }

            String skipped;
            jobs = SkipNightOnly(jobs, out skipped);
            if (jobs.Count == 0)
            {
                statusLabel.Text = skipped;
                return;
            }
            String question = Localization.F("ComputerGrid.ConfirmUpdateProfile", "Profile {2}: update {0} package(s) on {1} computer(s)? Their applications are closed without warning before the update.",
                jobs.Sum(j => j.Packages.Count), jobs.Count, ProfileDisplayName);
            if (skipped != null) question += "\n\n" + skipped;
            if (MessageBox.Show(this, question, Caption, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;
            if (!OfferCredential()) return;
            Start("upgrade", jobs);
        }

        /// <summary>
        /// During business hours, the "outside business hours only" computers are left out (no extra question:
        /// the confirmation says it). <paramref name="skipped"/>: the sentence for the confirmation, null when none.
        /// </summary>
        List<Job> SkipNightOnly(List<Job> jobs, out String skipped)
        {
            skipped = null;
            if (!UserSettings.Current.IsBusinessHours(DateTime.Now)) return jobs;
            List<Job> night = jobs.Where(j => IsNightOnly(j.Computer)).ToList();
            if (night.Count == 0) return jobs;
            String names = String.Join(", ", night.Select(j => j.Computer).Take(10)) + (night.Count > 10 ? ", ..." : "");
            skipped = Localization.F("ComputerGrid.NightOnlySkipped",
                "{0} computer(s) updated outside business hours only are skipped now (business hours): {1}. To update them now, use \"Allow updates during business hours\" first.",
                night.Count, names);
            return jobs.Except(night).ToList();
        }

        // ---- Install (explicit, opt-in) ---------------------------------------------
        // Not part of "Update checked" nor of the schedules: one package, chosen in the row menu,
        // on the target computers where the last check says "notinstalled" (a module only when
        // its header declares "# Install: yes").

        // Install or update: every software of the active profile, with what can be done on the targets after
        // their last check (update where outdated, install where missing and allowed), else its state, greyed
        void BuildInstallMenu(List<String> targets)
        {
            foreach (ToolStripItem old in installItem.DropDownItems.Cast<ToolStripItem>().ToList()) old.Dispose();
            installItem.DropDownItems.Clear();
            HashSet<String> installable = new HashSet<String>(StringComparer.OrdinalIgnoreCase);
            if (packages.Any(p => p.IsModule))
            {
                foreach (UpdateModule m in UpdateModules.List().Where(m => m.Install)) installable.Add(m.Name);
            }
            List<PackageChoice> choices;
            lock (storeLock) choices = UpdateRunner.PackageChoices(targets, packages, store.Find, installable);
            bool any = false;
            foreach (PackageChoice choice in choices)
            {
                UpdatePackage package = choice.Package;
                if (choice.Update.Count > 0)
                {
                    List<String> computers = choice.Update;
                    ToolStripMenuItem item = new ToolStripMenuItem(
                        Localization.F("ComputerGrid.UpdatePackageItem", "{0}: update ({1} computer(s))", package.DisplayName, computers.Count));
                    item.Font = new Font(item.Font, FontStyle.Bold);
                    item.Click += (s, e) => UpdateOnePackage(package, computers);
                    installItem.DropDownItems.Add(item);
                    any = true;
                }
                if (choice.Install.Count > 0)
                {
                    List<String> computers = choice.Install;
                    ToolStripMenuItem item = new ToolStripMenuItem(
                        Localization.F("ComputerGrid.InstallPackageItem2", "{0}: install ({1} computer(s))", package.DisplayName, computers.Count));
                    item.Click += (s, e) => InstallPackage(package, computers);
                    installItem.DropDownItems.Add(item);
                    any = true;
                }
                if (choice.Update.Count == 0 && choice.Install.Count == 0)
                {
                    String state = choice.UpToDate > 0 && choice.NotChecked == 0 && choice.Other == 0
                        ? Localization.T("ComputerGrid.UpToDate", "Up to date")
                        : Localization.F("ComputerGrid.PackageState", "up to date: {0}, not checked: {1}, other: {2}", choice.UpToDate, choice.NotChecked, choice.Other);
                    installItem.DropDownItems.Add(new ToolStripMenuItem(package.DisplayName + " - " + state) { Enabled = false });
                }
            }
            if (choices.Count == 0)
                installItem.DropDownItems.Add(new ToolStripMenuItem(Localization.T("ComputerGrid.NoSoftwareInProfile", "No software in the profile")) { Enabled = false });
            Theme.Apply(installItem.DropDown);
            installItem.Enabled = choices.Count > 0 && pending == 0;
            installItem.ToolTipText = any ? null
                : Localization.T("ComputerGrid.NothingToInstallOrUpdate", "Nothing to update or install: the last check of these computers says the software of the profile is up to date (or not checked yet: Check first).");
        }

        // One software of the profile updated on the computers where it is outdated (the others of the profile are not touched)
        void UpdateOnePackage(UpdatePackage package, List<String> targets)
        {
            if (pending > 0) return;
            List<Job> jobs = targets.Where(c => !IsPaused(c))
                .Select(c => new Job { Computer = c, Packages = new List<UpdatePackage> { package } }).ToList();
            String skipped;
            jobs = SkipNightOnly(jobs, out skipped);
            if (jobs.Count == 0)
            {
                if (skipped != null) statusLabel.Text = skipped;
                return;
            }
            String question = Localization.F("ComputerGrid.ConfirmUpdateOne", "Update {0} on {1} computer(s)? The application is closed without warning before the update: {2}",
                package.DisplayName, jobs.Count, String.Join(", ", jobs.Select(j => j.Computer).Take(10)) + (jobs.Count > 10 ? ", ..." : ""));
            if (skipped != null) question += "\n\n" + skipped;
            if (MessageBox.Show(this, question, Caption, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;
            if (!OfferCredential()) return;
            Start("upgrade", jobs);
        }

        void InstallPackage(UpdatePackage package, List<String> targets)
        {
            if (pending > 0) return;
            List<Job> jobs = targets.Where(c => !IsPaused(c))
                .Select(c => new Job { Computer = c, Packages = new List<UpdatePackage> { package } }).ToList();
            String skipped;
            jobs = SkipNightOnly(jobs, out skipped);
            if (jobs.Count == 0)
            {
                if (skipped != null) statusLabel.Text = skipped;
                return;
            }

            String question = Localization.F("ComputerGrid.ConfirmInstall", "Install {0} on {1} computer(s)?\n\nIt is installed only where the check still says it is not installed: {2}",
                package.DisplayName, jobs.Count, String.Join(", ", jobs.Select(j => j.Computer).Take(10)) + (jobs.Count > 10 ? ", ..." : ""));
            if (skipped != null) question += "\n\n" + skipped;
            if (MessageBox.Show(this, question, Caption, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;
            if (!OfferCredential()) return;
            Start(InstallPolicy.Command, jobs);
        }

        void Start(String command, List<Job> jobs)
        {
            if (jobs.Count == 0 || packages.Count == 0)
            {
                SetIdleStatus();
                RefreshList();
                return;
            }
            // PsExec only for the PsExec method (WinRM by default)
            if (RemoteRunner.Prerequisite() != null)
            {
                MessageBox.Show(this,
                    Localization.T("ComputerGrid.MissingTools", "PsExec or LiteDeploy.Worker.exe is missing next to LiteDeploy.exe."),
                    Caption, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            stopping = false;
            pending = jobs.Count;
            UpdateButtons();
            progressBar.Maximum = jobs.Count;
            progressBar.Value = 0;
            UpdateProgressText();

            Queue<Job> queue = new Queue<Job>(jobs);
            for (int i = 0; i < Math.Min(Parallelism, jobs.Count); i++)
            {
                Thread t = new Thread(() => Work(queue, command)) { IsBackground = true, Name = "Software updates" };
                t.Start();
            }
        }

        void Work(Queue<Job> queue, String command)
        {
            while (true)
            {
                Job job;
                lock (queue)
                {
                    if (queue.Count == 0 || stopping) break;
                    job = queue.Dequeue();
                }

                SetLive(new RowKey { Computer = job.Computer }.Key, "▶ " + Localization.T("ComputerGrid.Connecting", "Connecting..."));
                UpdateRunResult result = UpdateRunner.Run(job.Computer, command, job.Packages,
                    p => { lock (running) running.Add(p); },
                    m => OnWorkerMessage(job.Computer, m));
                ClearLive(job.Computer);
                lock (running) running.RemoveAll(p => { try { return p.HasExited; } catch (InvalidOperationException) { return true; } });

                if (!stopping || result.Messages.Count > 0)
                {
                    lock (storeLock)
                    {
                        store.Apply(job.Computer, result.Messages, result.Failure, DateTime.Now);
                        try { store.SaveIfChanged(UpdateStateStore.DefaultPath); }
                        catch (Exception ex) { AppConsole.WriteLine("Software updates: could not save " + UpdateStateStore.DefaultPath + ": " + ex.Message); }
                    }
                }
                if (command == "upgrade")
                {
                    // Updated rows and computers are unchecked
                    foreach (UpdatePackage p in job.Packages)
                    {
                        String key = new RowKey { Computer = job.Computer, PackageId = p.Id }.Key;
                        lock (checkedKeys) checkedKeys.Remove(key);
                    }
                    lock (checkedKeys) checkedComputers.Remove(job.Computer);
                }
                Done();
            }

            // Jobs left in the queue after Stop
            lock (queue)
            {
                while (queue.Count > 0) { queue.Dequeue(); Done(); }
            }
        }

        // ---- Live display -------------------------------------------------------------

        void OnWorkerMessage(String computer, WorkerMessage m)
        {
            String key = new RowKey { Computer = computer, PackageId = m["id"] }.Key;
            switch (m.Type)
            {
                case "start":
                    SetLive(new RowKey { Computer = computer }.Key, "▶ " + Localization.T("ComputerGrid.Started", "Started"));
                    break;
                case WorkerProtocol.TypeProgress:
                    SetLive(key, "▶ " + StepText(m["step"]));
                    break;
                case WorkerProtocol.TypePackage:
                    // Shown at once, the state file is written when the computer is done
                    lock (storeLock) store.Apply(computer, new List<WorkerMessage> { m }, null, DateTime.Now);
                    SetLive(key, "");
                    break;
            }
        }

        static String StepText(String step)
        {
            switch (step)
            {
                case WorkerProtocol.StepChecking: return Localization.T("ComputerGrid.StepChecking", "Checking...");
                case WorkerProtocol.StepClosing: return Localization.T("ComputerGrid.StepClosing", "Closing the application...");
                case WorkerProtocol.StepUpgrading: return Localization.T("ComputerGrid.StepUpgrading", "Updating...");
                case WorkerProtocol.StepInstalling: return Localization.T("ComputerGrid.StepInstalling", "Installing...");
                default: return step;
            }
        }

        void SetLive(String key, String text)
        {
            lock (live)
            {
                if (text == null) live.Remove(key);
                else live[key] = text;
            }
            RefreshLater();
        }

        void ClearLive(String computer)
        {
            String prefix = (computer + "|").ToLowerInvariant();
            lock (live)
            {
                foreach (String k in live.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToList()) live.Remove(k);
            }
        }

        // Several messages can arrive at once: one refresh of the list at a time
        int refreshQueued;
        void RefreshLater()
        {
            if (IsDisposed || !IsHandleCreated || Interlocked.Exchange(ref refreshQueued, 1) == 1) return;
            try
            {
                BeginInvoke(new MethodInvoker(() =>
                {
                    Interlocked.Exchange(ref refreshQueued, 0);
                    RefreshList();
                }));
            }
            catch (InvalidOperationException) { }
        }

        void ActiveDirectoryCache_InventoryChanged(object sender, EventArgs e)
        {
            RefreshLater();
        }

        void Done()
        {
            int left = Interlocked.Decrement(ref pending);
            if (IsDisposed) return;
            try
            {
                BeginInvoke(new MethodInvoker(() =>
                {
                    progressBar.Value = Math.Min(progressBar.Maximum, progressBar.Maximum - left);
                    RefreshList();
                    if (left == 0) SetIdleStatus();
                    else UpdateProgressText();
                }));
            }
            catch (InvalidOperationException) { }   // window closed meanwhile
        }

        void UpdateProgressText()
        {
            statusLabel.Text = Localization.F("ComputerGrid.Running", "Working: {0} of {1} computer(s) done.", progressBar.Maximum - pending, progressBar.Maximum);
        }

        void stopButton_Click(object sender, EventArgs e)
        {
            ConfirmStop();
        }

        // Esc or Stop while working: stop after a confirmation
        void ConfirmStop()
        {
            if (pending == 0) return;
            if (MessageBox.Show(this, Localization.T("ComputerGrid.ConfirmStop", "Stop the work in progress?"), Caption,
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes)
                StopAll();
        }

        internal void StopAll()
        {
            stopping = true;
            lock (running)
            {
                foreach (Process p in running)
                {
                    try { if (!p.HasExited) p.Kill(); } catch (Exception) { }
                }
            }
        }

        /// <summary>LiteDeploy closes (the question was asked by the main window, which saves the settings): stops the work.</summary>
        internal void Shutdown()
        {
            pingTimer.Stop();
            if (pending > 0) StopAll();
            ActiveDirectoryCache.InventoryChanged -= ActiveDirectoryCache_InventoryChanged;
            UserSettings.Saved -= UserSettings_Saved;
            searchTimer.Stop();
        }

        // ---- Pause, outside business hours ----------------------------------------------

        bool IsPaused(String computer)
        {
            lock (storeLock) { ComputerUpdateState c = store.Find(computer); return c != null && c.Paused; }
        }

        bool IsNightOnly(String computer)
        {
            lock (storeLock) { ComputerUpdateState c = store.Find(computer); return c != null && c.NightOnly; }
        }

        // ---- Actions (right-click menu and Actions menu: the same items) -------------------

        ToolStripItem[] ActionItems
        {
            get
            {
                return new ToolStripItem[]
                {
                    targetsItem, targetsSeparator, installItem, installSeparator, pauseItem, resumeItem, nightOnlyItem, nightOnlyClearItem,
                    menuSeparator, cardItem, teamViewerItem, remoteDesktopItem, commandPromptItem, quickCommandsItem, messageItem,
                    processesItem, litePMItem, logsItem, wakeItem, pingItem, managementConsoleItem, adminShareItem, copyNameItem,
                    deploySeparator, deployItem, exportSeparator, inventoryItem, exportInventoryItem, royalTsItem, removeSeparator, removeItem, unhideItem
                };
            }
        }

        // The items move to the menu being opened (an item belongs to one menu at a time)
        void MoveActionsTo(ToolStripItemCollection target)
        {
            if (target.Contains(targetsItem)) return;
            target.AddRange(ActionItems);
        }

        /// <summary>Enables the actions for their targets; false when there is no target.</summary>
        bool PrepareActions()
        {
            List<String> targets = ActionTargets();
            bool idle = pending == 0;
            bool byCheck = CheckedComputers().Count > 0;
            targetsItem.Text = targets.Count == 0
                ? Localization.T("ComputerGrid.NoTargets", "No computer checked or selected")
                : byCheck
                    ? Localization.F("ComputerGrid.TargetsChecked", "{0} checked computer(s)", targets.Count)
                    : Localization.F("ComputerGrid.TargetsSelected", "{0} selected computer(s)", targets.Count);
            BuildInstallMenu(targets);
            pauseItem.Enabled = idle && targets.Any(c => !IsPaused(c));
            resumeItem.Enabled = idle && targets.Any(IsPaused);
            nightOnlyItem.Enabled = idle && targets.Any(c => !IsNightOnly(c));
            nightOnlyClearItem.Enabled = idle && targets.Any(IsNightOnly);
            cardItem.Enabled = logsItem.Enabled = targets.Count > 0;
            teamViewerItem.Enabled = remoteDesktopItem.Enabled = commandPromptItem.Enabled = quickCommandsItem.Enabled = messageItem.Enabled = targets.Count > 0;
            actions.FillQuickCommands(quickCommandsItem, ActionTargets);
            royalTsItem.Enabled = exportInventoryItem.Enabled = ExportTargets().Count > 0;
            inventoryItem.Enabled = idle && targets.Count > 0 && !readingInventory;
            processesItem.Enabled = litePMItem.Enabled = managementConsoleItem.Enabled = adminShareItem.Enabled = targets.Count == 1;
            pingItem.Enabled = copyNameItem.Enabled = targets.Count > 0;
            deployItem.Enabled = targets.Count > 0 && deployEnabled;
            lock (storeLock) wakeItem.Enabled = targets.Any(c => { ComputerUpdateState s = store.Find(c); return s != null && !String.IsNullOrEmpty(s.Macs); });
            bool anyShown = targets.Any(c => { ComputerRow r; return rowsByName.TryGetValue(c, out r) && !r.Hidden; });
            bool anyHidden = targets.Any(c => { ComputerRow r; return rowsByName.TryGetValue(c, out r) && r.Hidden; });
            removeItem.Visible = anyShown;
            unhideItem.Visible = anyHidden;
            removeSeparator.Visible = anyShown || anyHidden;
            removeItem.Enabled = idle && anyShown;
            unhideItem.Enabled = idle && anyHidden;
            return targets.Count > 0;
        }

        void rowMenu_Opening(object sender, System.ComponentModel.CancelEventArgs e)
        {
            MoveActionsTo(rowMenu.Items);
            e.Cancel = !PrepareActions();
        }

        void actionsButton_DropDownOpening(object sender, EventArgs e)
        {
            MoveActionsTo(actionsButton.DropDownItems);
            PrepareActions();
        }

        // ---- Computer card, TeamViewer, remote desktop, command prompt, Wake-on-LAN, logs (ComputerActions) ----

        void cardItem_Click(object sender, EventArgs e)
        {
            ShowCards(ActionTargets());
        }

        void ShowCards(List<String> targets)
        {
            actions.ShowCards(targets);
        }

        void teamViewerItem_Click(object sender, EventArgs e)
        {
            actions.ShowTeamViewer(ActionTargets());
        }

        void remoteDesktopItem_Click(object sender, EventArgs e)
        {
            actions.RemoteDesktop(ActionTargets());
        }

        void commandPromptItem_Click(object sender, EventArgs e)
        {
            actions.CommandPrompt(ActionTargets());
        }

        /// <summary>Export to Royal TS: the checked computers, else every computer shown.</summary>
        List<String> ExportTargets()
        {
            List<String> targets = CheckedComputers();
            return targets.Count > 0 ? targets : visible.Select(r => r.Name).ToList();
        }

        // ---- Hardware inventory --------------------------------------------------------------

        bool readingInventory;

        void inventoryItem_Click(object sender, EventArgs e)
        {
            ReadInventory(ActionTargets());
        }

        /// <summary>Reads the hardware of the computers (worker "info", a few at a time), kept in updates.xml.</summary>
        void ReadInventory(List<String> targets)
        {
            if (targets.Count == 0 || readingInventory) return;
            if (RemoteRunner.Prerequisite() != null)
            {
                MessageBox.Show(this, Localization.T("ComputerGrid.MissingTools", "PsExec or LiteDeploy.Worker.exe is missing next to LiteDeploy.exe."),
                    Caption, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            readingInventory = true;
            statusLabel.Text = Localization.F("ComputerGrid.ReadingInventory", "Reading the inventory of {0} computer(s)...", targets.Count);
            Queue<String> queue = new Queue<String>(targets);
            int left = Math.Min(8, targets.Count), read = 0, failed = 0;
            for (int i = 0; i < left; i++)
            {
                new Thread(() =>
                {
                    while (true)
                    {
                        String computer;
                        lock (queue) { if (queue.Count == 0) break; computer = queue.Dequeue(); }
                        UpdateRunResult r = UpdateRunner.Info(computer);
                        bool ok = r.Messages.Any(m => m.Type == "info");
                        if (ok)
                        {
                            lock (storeLock)
                            {
                                store.Apply(computer, r.Messages, null, DateTime.Now);
                                try { store.SaveIfChanged(UpdateStateStore.DefaultPath); }
                                catch (Exception ex) { AppConsole.WriteLine("Inventory: could not save " + UpdateStateStore.DefaultPath + ": " + ex.Message); }
                            }
                            Interlocked.Increment(ref read);
                        }
                        else
                        {
                            Interlocked.Increment(ref failed);
                            AppConsole.WriteLine(Localization.F("ComputerGrid.InventoryFailed", "Inventory of {0}: {1}", computer, r.Failure ?? "?"));
                        }
                        RefreshLater();
                    }
                    if (Interlocked.Decrement(ref left) > 0) return;
                    try
                    {
                        if (!IsDisposed && IsHandleCreated)
                            BeginInvoke(new MethodInvoker(() =>
                            {
                                readingInventory = false;
                                statusLabel.Text = Localization.F("ComputerGrid.InventoryDone", "Inventory read on {0} computer(s), {1} not reached (see the console).", read, failed);
                            }));
                    }
                    catch (InvalidOperationException) { }
                }) { IsBackground = true, Name = "Inventory" }.Start();
            }
        }

        void exportInventoryItem_Click(object sender, EventArgs e)
        {
            ExportInventory();
        }

        /// <summary>Export the inventory... (actions and Tools menu): the checked computers, else every computer shown.</summary>
        internal void ExportInventory()
        {
            List<String> targets = ExportTargets();
            if (targets.Count == 0) return;
            List<KeyValuePair<ComputerRow, ComputerUpdateState>> list = new List<KeyValuePair<ComputerRow, ComputerUpdateState>>();
            lock (storeLock)
            {
                foreach (String name in targets)
                {
                    ComputerRow row;
                    if (rowsByName.TryGetValue(name, out row)) list.Add(new KeyValuePair<ComputerRow, ComputerUpdateState>(row, store.Find(name)));
                }
            }
            InventoryTable table = InventoryTable.Build(list.OrderBy(p => p.Key.Name, StringComparer.OrdinalIgnoreCase));
            using (SaveFileDialog dialog = new SaveFileDialog())
            {
                dialog.Title = Localization.T("ComputerGrid.ExportInventoryTitle", "Export the inventory");
                dialog.Filter = Localization.T("ComputerGrid.ExportInventoryFilter", "Excel workbook (*.xlsx)|*.xlsx|CSV file, separator ; (*.csv)|*.csv");
                dialog.FileName = "LiteDeploy-inventory-" + DateTime.Now.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
                dialog.OverwritePrompt = true;
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    if (dialog.FilterIndex == 2 || dialog.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)) table.SaveCsv(dialog.FileName);
                    else table.SaveXlsx(dialog.FileName, Localization.T("ComputerGrid.InventorySheet", "Inventory"));
                    statusLabel.Text = Localization.F("ComputerGrid.InventoryExported", "Inventory of {0} computer(s) exported: {1}", table.Rows.Count, dialog.FileName);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, ex.Message, Caption, MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        void royalTsItem_Click(object sender, EventArgs e)
        {
            actions.ExportRoyalTs(ExportTargets());
        }

        // ---- IComputerActionHost ------------------------------------------------------------

        Form IComputerActionHost.Owner { get { return FindForm(); } }

        bool IComputerActionHost.Idle { get { return pending == 0; } }

        /// <summary>The store of this window, under its lock (the main window uses it too while this window is open).</summary>
        internal void WithStore(Action<UpdateStateStore> action, bool save)
        {
            lock (storeLock)
            {
                action(store);
                if (save) store.SaveIfChanged(UpdateStateStore.DefaultPath);
            }
        }

        void IComputerActionHost.WithStore(Action<UpdateStateStore> action, bool save)
        {
            WithStore(action, save);
        }

        ComputerRow IComputerActionHost.Row(String computer)
        {
            ComputerRow row;
            return rowsByName.TryGetValue(computer, out row) ? row : null;
        }

        void IComputerActionHost.SetStatus(String text)
        {
            // A check or an update under way owns the status line and the progress bar at the bottom: an action
            // on computers (quick command, card, message) has its own progress window and leaves them alone
            if (pending > 0) return;
            if (text == null) SetIdleStatus();
            else statusLabel.Text = text;
        }

        void IComputerActionHost.StoreChanged()
        {
            RefreshList();
        }

        /// <summary>The saved state changed elsewhere (main window): refresh the list.</summary>
        internal void StoreChanged()
        {
            if (!IsDisposed) RefreshList();
        }

        void processesItem_Click(object sender, EventArgs e)
        {
            String computer = ActionTargets().FirstOrDefault();
            if (computer == null) return;
            using (RemoteProcessesForm form = new RemoteProcessesForm(computer)) form.ShowDialog(this);
        }

        // LitePM connected to the computer through WMI, as the current user
        void litePMItem_Click(object sender, EventArgs e)
        {
            String computer = ActionTargets().FirstOrDefault();
            if (computer == null) return;
            ToolInfo litePM = ToolManager.List().FirstOrDefault(t => t.Id == ToolManager.LitePMId);
            if (litePM == null || litePM.Path == null)
            {
                MessageBox.Show(this, Localization.T("MainForm.LitePMMissing", "LitePM is not installed: install it in Tools > Manage Tools."), "LitePM", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            try { ToolManager.OpenLitePM(litePM, computer); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "LitePM", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        void logsItem_Click(object sender, EventArgs e)
        {
            actions.FetchLogs(ActionTargets());
        }

        void wakeItem_Click(object sender, EventArgs e)
        {
            actions.Wake(ActionTargets());
        }

        void pauseItem_Click(object sender, EventArgs e)
        {
            actions.Pause(ActionTargets());
        }

        void resumeItem_Click(object sender, EventArgs e)
        {
            actions.Resume(ActionTargets());
        }

        void nightOnlyItem_Click(object sender, EventArgs e)
        {
            actions.SetNightOnly(ActionTargets(), true);
        }

        void nightOnlyClearItem_Click(object sender, EventArgs e)
        {
            actions.SetNightOnly(ActionTargets(), false);
        }

        // Remove from the list: a computer added by name leaves the list; a computer of Active Directory is hidden
        // (kept in updates.xml: Hidden filter, Show in the list again), never updated by a schedule. Their other
        // saved state is kept.
        void removeItem_Click(object sender, EventArgs e)
        {
            List<String> added = new List<String>(), hidden = new List<String>();
            foreach (String c in ActionTargets())
            {
                ComputerRow r;
                if (!rowsByName.TryGetValue(c, out r) || r.Hidden) continue;
                if (r.AddedByName && !r.InAd) added.Add(c);
                else hidden.Add(c);
            }
            if (added.Count + hidden.Count == 0) return;
            String names = String.Join(", ", added.Concat(hidden).Take(10)) + (added.Count + hidden.Count > 10 ? ", ..." : "");
            if (MessageBox.Show(this, Localization.F("ComputerGrid.ConfirmRemove",
                    "Remove {0} computer(s) from the list? {1}\n\nThey are no longer shown nor updated. The computers of Active Directory stay available with the \"Hidden\" filter (Show in the list again).",
                    added.Count + hidden.Count, names), Caption, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;
            ChangeComputers(added, c => c.AddedByName = false);
            ChangeComputers(hidden, c => c.Hidden = true);
            foreach (String c in added.Concat(hidden)) checkedComputers.Remove(c);
            RefreshList();
        }

        void unhideItem_Click(object sender, EventArgs e)
        {
            List<String> hidden = ActionTargets().Where(c => { ComputerRow r; return rowsByName.TryGetValue(c, out r) && r.Hidden; }).ToList();
            ChangeComputers(hidden, c => c.Hidden = false);
            RefreshList();
        }

        void ChangeComputers(List<String> targets, Action<ComputerUpdateState> change)
        {
            actions.Change(targets, change);
        }

        // ---- Online state (ping) ------------------------------------------------------------
        // The computers the other filters show are pinged when the grid opens, then every 5 minutes and on
        // demand (Refresh state, Shift+F5). "Online only" (on by default) hides the computers that did not
        // answer; a computer not pinged yet stays shown.

        readonly OnlineStatus online = new OnlineStatus();
        readonly System.Windows.Forms.Timer pingTimer = new System.Windows.Forms.Timer { Interval = 5 * 60 * 1000 };

        const int ImageUnknown = 0, ImageOnline = 1, ImageOffline = 2;

        static int OnlineImage(bool? state)
        {
            return state == true ? ImageOnline : state == false ? ImageOffline : ImageUnknown;
        }

        static String OnlineText(bool? state)
        {
            return state == true ? Localization.T("ComputerGrid.Online", "Online")
                : state == false ? Localization.T("ComputerGrid.Offline", "Offline")
                : "";
        }

        // A dot per state: grey (not pinged), green (answers), red (does not answer)
        ImageList StateImages()
        {
            ImageList images = new ImageList { ColorDepth = ColorDepth.Depth32Bit, ImageSize = new Size(12, 12) };
            foreach (Color color in new[] { Color.Silver, Color.FromArgb(46, 160, 67), Color.FromArgb(215, 58, 73) })
            {
                Bitmap dot = new Bitmap(12, 12);
                using (Graphics g = Graphics.FromImage(dot))
                using (SolidBrush brush = new SolidBrush(color))
                {
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    g.FillEllipse(brush, 2, 2, 8, 8);
                }
                images.Images.Add(dot);
            }
            components.Add(images);
            return images;
        }

        /// <summary>Pings the computers the other filters show (offline ones included); <paramref name="asked"/>: by the user (status line).</summary>
        void StartPing(bool asked)
        {
            if (IsDisposed) return;
            ComputerFilterState filter = CurrentFilter();
            filter.OnlineOnly = false;
            List<String> names = ComputerFilter.Apply(rows, filter, DateTime.Now).Select(r => r.Name).ToList();
            if (names.Count == 0) return;
            bool started = online.PingAll(names, (c, up) => RefreshLater(), () =>
            {
                try
                {
                    if (!IsDisposed && IsHandleCreated)
                        BeginInvoke(new MethodInvoker(() =>
                        {
                            refreshStateButton.Enabled = true;
                            if (pending == 0)
                                statusLabel.Text = Localization.F("ComputerGrid.PingDone", "Ping: {0} of {1} computer(s) answer.",
                                    names.Count(n => online.Get(n) == true), names.Count);
                        }));
                }
                catch (InvalidOperationException) { }
            });
            if (!started) return;
            refreshStateButton.Enabled = false;
            if (asked && pending == 0)
                statusLabel.Text = Localization.F("ComputerGrid.Pinging", "Ping of {0} computer(s)...", names.Count);
        }

        void refreshStateButton_Click(object sender, EventArgs e)
        {
            StartPing(true);
        }

        void onlineOnlyButton_CheckedChanged(object sender, EventArgs e)
        {
            if (!loading) RefreshList();
        }

        // Columns menu: one item per column of the computer view (Computer always shown)
        void columnsButton_DropDownOpening(object sender, EventArgs e)
        {
            while (columnsButton.DropDownItems.Count > 0)
            {
                ToolStripItem old = columnsButton.DropDownItems[0];
                columnsButton.DropDownItems.RemoveAt(0);
                old.Dispose();
            }
            foreach (ColumnHeader column in ComputerColumns.Where(c => c != computerColumn))
            {
                ColumnHeader c = column;
                ToolStripMenuItem item = new ToolStripMenuItem(c.Text) { Checked = !hiddenColumns.Contains(c.Name), CheckOnClick = true };
                item.CheckedChanged += (s, a) =>
                {
                    if (item.Checked) hiddenColumns.Remove(c.Name);
                    else hiddenColumns.Add(c.Name);
                    RefreshList();
                };
                columnsButton.DropDownItems.Add(item);
            }
            columnsButton.DropDownItems.Add(new ToolStripSeparator());
            columnsButton.DropDownItems.Add(Localization.T("ComputerGrid.AllColumns", "Show every column"), null, (s, a) => { hiddenColumns.Clear(); RefreshList(); });
            Theme.Apply(columnsButton.DropDown);
        }

        // ---- Add computers -----------------------------------------------------------------

        /// <summary>Computer list window: created the first time it is opened, then kept (it keeps its search results).</summary>
        static ComputerListForm computerList;

        void addButton_Click(object sender, EventArgs e)
        {
            ShowAddComputers();
        }

        /// <summary>Add computers... (also in the Tools menu): the computers chosen are added and checked.</summary>
        internal void ShowAddComputers()
        {
            if (computerList == null || computerList.IsDisposed) computerList = new ComputerListForm();
            List<String> picked = computerList.Pick(FindForm());
            if (picked != null && picked.Count > 0) AddComputers(picked, true);
        }

        /// <summary>
        /// Adds computers to the list: the ones not in Active Directory are kept in updates.xml ("added by name");
        /// <paramref name="check"/>: the computers given become checked (the other checks are kept).
        /// </summary>
        internal void AddComputers(IEnumerable<String> names, bool check)
        {
            List<String> added = new List<String>(), invalid = new List<String>(), known = new List<String>();
            lock (storeLock)
            {
                foreach (String part in names ?? Enumerable.Empty<String>())
                {
                    String name = ComputerFilter.NormalizeName(part);
                    if (name == null) { if (!String.IsNullOrWhiteSpace(part)) invalid.Add(part.Trim()); continue; }
                    ComputerRow row;
                    if (rowsByName.TryGetValue(name, out row) && (row.InAd || row.AddedByName))
                    {
                        // A computer removed from the list (hidden) comes back when it is added again
                        if (row.Hidden) { store.GetOrAdd(row.Name).Hidden = false; added.Add(row.Name); }
                        else known.Add(row.Name);
                        continue;
                    }
                    store.GetOrAdd(name).AddedByName = true;
                    added.Add(name);
                }
                if (added.Count > 0)
                {
                    try { store.SaveIfChanged(UpdateStateStore.DefaultPath); }
                    catch (Exception ex) { MessageBox.Show(this, ex.Message, Caption, MessageBoxButtons.OK, MessageBoxIcon.Error); }
                }
            }
            if (check)
            {
                foreach (String name in added.Concat(known)) checkedComputers.Add(name);
            }
            RefreshList();
            int hidden = added.Concat(known).Count(n => !visible.Any(r => ComputerName.Same(r.Name, n)));
            statusLabel.Text = Localization.F("ComputerGrid.Added", "{0} computer(s) added, {1} already in the list, {2} hidden by the filters.", added.Count, known.Count, hidden);
            if (invalid.Count > 0)
                MessageBox.Show(this, Localization.F("ComputerGrid.InvalidNames", "Not a computer name: {0}", String.Join(", ", invalid)), Caption, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        // ---- Selection used by the main window (ping, deployment) -------------------------------

        /// <summary>The computers an action applies to: the checked ones, else the selected rows.</summary>
        internal List<String> Targets()
        {
            return ActionTargets();
        }

        /// <summary>Ping: the checked computers, else every computer shown.</summary>
        internal List<String> PingTargets()
        {
            List<String> targets = CheckedComputers();
            return targets.Count > 0 ? targets : visible.Select(r => r.Name).ToList();
        }

        /// <summary>Checks or unchecks computers (after a ping).</summary>
        internal void SetChecked(IEnumerable<String> names, bool check)
        {
            foreach (String name in names ?? Enumerable.Empty<String>())
            {
                if (check) checkedComputers.Add(name);
                else checkedComputers.Remove(name);
            }
            RefreshList();
        }

        /// <summary>
        /// Takes computers out of the work (after a ping): unchecked, and no longer shown when they were only
        /// added by name (a computer of Active Directory stays in the list).
        /// </summary>
        internal void RemoveFromList(IEnumerable<String> names)
        {
            List<String> list = (names ?? Enumerable.Empty<String>()).ToList();
            foreach (String name in list) checkedComputers.Remove(name);
            List<String> added = list.Where(c => { ComputerRow r; return rowsByName.TryGetValue(c, out r) && r.AddedByName && !r.InAd; }).ToList();
            if (added.Count > 0) ChangeComputers(added, c => c.AddedByName = false);
            RefreshList();
        }

        void pingItem_Click(object sender, EventArgs e)
        {
            Action<List<String>> handler = PingRequested;
            if (handler != null) handler(ActionTargets());
        }

        void deployItem_Click(object sender, EventArgs e)
        {
            Action<List<String>> handler = DeployRequested;
            if (handler != null) handler(ActionTargets());
        }

        void managementConsoleItem_Click(object sender, EventArgs e)
        {
            String computer = ActionTargets().FirstOrDefault();
            if (computer != null) RemoteActions.ManagementConsole(computer);
        }

        void adminShareRootItem_Click(object sender, EventArgs e) { OpenAdminShare(""); }
        void publicDesktopItem_Click(object sender, EventArgs e) { OpenAdminShare(System.IO.Path.Combine("Users", "Public", "Desktop")); }
        void startMenuItem_Click(object sender, EventArgs e) { OpenAdminShare(System.IO.Path.Combine("ProgramData", "Microsoft", "Windows", "Start Menu")); }

        void OpenAdminShare(String folder)
        {
            String computer = ActionTargets().FirstOrDefault();
            if (computer != null) RemoteActions.OpenAdminShare(computer, folder);
        }

        void copyNameItem_Click(object sender, EventArgs e)
        {
            List<String> targets = ActionTargets();
            if (targets.Count == 0) return;
            try { Clipboard.SetText(String.Join(Environment.NewLine, targets)); }
            catch (Exception ex) { statusLabel.Text = ex.Message; }
        }

        void scheduleButton_Click(object sender, EventArgs e)
        {
            List<String> targets = CheckedComputers();
            if (targets.Count == 0) targets = visible.Select(r => r.Name).ToList();
            using (SchedulesForm schedules = new SchedulesForm(targets))
            {
                schedules.ShowDialog(this);
            }
        }

        // ---- Package list ----------------------------------------------------------

        void editPackagesButton_Click(object sender, EventArgs e)
        {
            ShowCatalog();
        }

        /// <summary>Software catalog... (tool bar and Tools menu): the catalog and its profiles; refused while work is in progress.</summary>
        internal void ShowCatalog()
        {
            if (pending > 0) return;
            using (PackageListForm editor = new PackageListForm(UpdateRunner.LoadPackages(), SoftwareProfiles.Load()))
            {
                if (editor.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    UpdateRunner.SavePackages(editor.Packages);
                    editor.Profiles.KeepOnly(editor.Packages);
                    editor.Profiles.Save();
                    // A renamed profile stays the active one, and the one of its schedules
                    String active = profileName;
                    foreach (KeyValuePair<String, String> r in editor.Renamed)
                    {
                        if (String.Equals(active, r.Key, StringComparison.OrdinalIgnoreCase)) active = r.Value;
                        UpdateSchedules.RenameProfile(r.Key, r.Value);
                    }
                    if (active != profileName) SetProfile(active);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, ex.Message, Caption, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
            }
            LoadPackages();
            RefreshList();
            SetIdleStatus();
        }

        // ---- Profile of the software -----------------------------------------------------
        // Check, Update checked and Install work on the packages of the active profile only (faster, and
        // no update of the whole catalog by mistake). No drop-down list on the main window: the profile
        // changes through a dialog only, never with a turn of the mouse wheel.

        /// <summary>Every package of the catalog (packages.txt).</summary>
        List<UpdatePackage> catalog = new List<UpdatePackage>();
        /// <summary>Active profile; empty: the whole catalog. Kept in the settings.</summary>
        String profileName = "";
        bool profileFound = true;

        String ProfileDisplayName
        {
            get { return String.IsNullOrWhiteSpace(profileName) ? Localization.T("ComputerGrid.AllSoftware", "All") : profileName; }
        }

        /// <summary>The catalog, the profiles and the packages of the active profile (a missing profile: none).</summary>
        void LoadPackages()
        {
            catalog = UpdateRunner.LoadPackages();
            packages = SoftwareProfiles.Load().Filter(catalog, profileName, out profileFound);
            profileButton.Text = Localization.F("ComputerGrid.ProfileButton", "Profile: {0}", ProfileDisplayName);
            profileButton.ForeColor = profileFound ? (Theme.IsDark ? Theme.Text : SystemColors.ControlText) : Theme.ErrorColor;
        }

        void SetProfile(String name)
        {
            profileName = name ?? "";
            UserSettings s = UserSettings.Current.Clone();
            s.UpdatesProfile = profileName;
            try { UserSettings.Save(s); }
            catch (Exception ex) { AppConsole.WriteLine("Software updates: could not save " + UserSettings.FilePath + ": " + ex.Message); }
        }

        /// <summary>The active profile (schedules created from the main window start with it).</summary>
        internal String ActiveProfile { get { return profileName; } }

        void profileButton_Click(object sender, EventArgs e)
        {
            if (pending > 0) return;
            String chosen = ProfilePickerForm.Pick(FindForm(), UpdateRunner.LoadPackages(), SoftwareProfiles.Load(), profileName);
            if (chosen == null || String.Equals(chosen, profileName, StringComparison.Ordinal)) return;
            SetProfile(chosen);
            // Checked package rows of another profile are not kept
            checkedKeys.Clear();
            LoadPackages();
            RefreshList();
            SetIdleStatus();
        }
    }
}
