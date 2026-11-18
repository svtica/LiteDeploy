// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    SoftwareUpdatesForm.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-12
// Purpose: Software Updates window, self-sufficient and non-modal: every
//          enabled computer of Active Directory (inventory of adcache.xml),
//          the computers added by name (updates.xml) and those of the main
//          window's list, with the state of the winget packages of the
//          package list (local state file). One row per computer (OS, last
//          logon, summary, pause and "outside business hours only" flags) or,
//          with "Show packages", one row per installed package. Search box,
//          status chips, "Include servers" (servers hidden by default) and the
//          selection of the main window filter the rows (ComputerFilter); the
//          checks are kept across filter changes. Check reads the versions on
//          the checked computers (every computer shown when none is checked);
//          Update checked upgrades the packages with an update of the checked
//          computers, or only the checked packages in the package view, after
//          one confirmation ("outside business hours only" computers are
//          skipped during business hours). The credential is offered at most
//          once per session. The work is done by LiteDeploy.Worker.exe run as
//          SYSTEM, a few computers at a time; each row shows the step in
//          progress. Actions (right-click or the Actions menu) apply to the
//          checked computers, else to the selected rows: Install (one package,
//          chosen explicitly, never part of Update checked or of the
//          schedules), pause, outside business hours only, computer card,
//          Wake-on-LAN, logs. Size, splitter, columns, view and filters are
//          kept in the user settings.
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
    public partial class SoftwareUpdatesForm : Form
    {
        /// <summary>Computers processed at the same time.</summary>
        const int Parallelism = 4;

        public const String ViewComputers = "computers";
        public const String ViewPackages = "packages";

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
        readonly List<Process> running = new List<Process>();
        /// <summary>Checked package rows (package view), by row key.</summary>
        readonly HashSet<String> checkedKeys = new HashSet<String>();
        /// <summary>Checked computers (computer view), kept across filter changes.</summary>
        readonly HashSet<String> checkedComputers = new HashSet<String>(StringComparer.OrdinalIgnoreCase);
        /// <summary>Step in progress, by row key (computer|package, or computer| for the computer).</summary>
        readonly Dictionary<String, String> live = new Dictionary<String, String>();
        /// <summary>Computers of the main window's list (shown even when they are not in Active Directory).</summary>
        readonly HashSet<String> listed = new HashSet<String>(StringComparer.OrdinalIgnoreCase);
        /// <summary>Computers checked in the main window when the window was opened (filter); null: none.</summary>
        HashSet<String> selection;
        readonly ListViewColumnSorter sorter = new ListViewColumnSorter { SortColumn = 0, Order = SortOrder.Ascending };
        readonly System.Windows.Forms.Timer searchTimer = new System.Windows.Forms.Timer { Interval = 250 };
        readonly Dictionary<ToolStripButton, ComputerStatusFilter> chips;

        List<UpdatePackage> packages;
        /// <summary>Every computer of the window (filters not applied), by name; rebuilt at each refresh.</summary>
        List<ComputerRow> rows = new List<ComputerRow>();
        Dictionary<String, ComputerRow> rowsByName = new Dictionary<String, ComputerRow>(StringComparer.OrdinalIgnoreCase);
        /// <summary>Rows shown (filters applied).</summary>
        List<ComputerRow> visible = new List<ComputerRow>();
        String computerColumnsLayout, packageColumnsLayout;
        /// <summary>The saved layout of the columns was applied to the view shown.</summary>
        bool columnsReady;
        volatile bool stopping;
        int pending;
        bool refreshing;
        bool loading = true;

        /// <param name="listedComputers">Computers of the main window's list: shown even when not in Active Directory.</param>
        /// <param name="checkedInMain">Computers checked in the main window: the window opens filtered to them (removable).</param>
        public SoftwareUpdatesForm(IEnumerable<String> listedComputers, IEnumerable<String> checkedInMain)
        {
            InitializeComponent();
            Localization.Apply(this);
            Theme.Apply(this);
            Theme.Apply(actionsButton.DropDown);
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
                { staleFilter, ComputerStatusFilter.Stale }
            };

            store = UpdateStateStore.Load(UpdateStateStore.DefaultPath);
            packages = UpdateRunner.LoadPackages();
            AddListedComputers(listedComputers);
            SetSelectionFilter(checkedInMain);
            RestoreSettings();

            updatesList.ListViewItemSorter = sorter;
            updatesList.ItemChecked += updatesList_ItemChecked;
            searchTimer.Tick += (s, e) => { searchTimer.Stop(); RefreshList(); };
            ActiveDirectoryCache.InventoryChanged += ActiveDirectoryCache_InventoryChanged;
            UserSettings.Saved += UserSettings_Saved;
            Load += (s, e) => RestoreSplitter();

            loading = false;
            RefreshList();
            SetIdleStatus();
        }

        /// <summary>Work in progress (check, update or install).</summary>
        internal bool Busy { get { return pending > 0; } }

        /// <summary>Adds computers of the main window's list (they are shown even when they are not in Active Directory).</summary>
        public void AddListedComputers(IEnumerable<String> names)
        {
            foreach (String n in names ?? Enumerable.Empty<String>())
            {
                if (!String.IsNullOrWhiteSpace(n)) listed.Add(n.Trim());
            }
            if (!loading) RefreshList();
        }

        /// <summary>
        /// Filters the window to these computers (the ones checked in the main window); empty or null: removes
        /// the filter. The "Selection" button removes it too.
        /// </summary>
        public void SetSelectionFilter(IEnumerable<String> names)
        {
            List<String> list = (names ?? Enumerable.Empty<String>()).Where(n => !String.IsNullOrWhiteSpace(n)).Select(n => n.Trim()).ToList();
            selection = list.Count == 0 ? null : new HashSet<String>(list, StringComparer.OrdinalIgnoreCase);
            selectionFilterButton.Visible = selection != null;
            if (selection != null)
                selectionFilterButton.Text = Localization.F("SoftwareUpdatesForm.SelectionFilter", "Selection: {0} computer(s) ×", selection.Count);
            if (!loading) RefreshList();
        }

        // ---- Settings ---------------------------------------------------------------

        int StaleDays { get { return UserSettings.Current.UpdatesStaleDays; } }

        void RestoreSettings()
        {
            UserSettings s = UserSettings.Current;
            includeServersCheck.Checked = s.IncludeServers;
            byPackageButton.Checked = s.UpdatesView == ViewPackages;
            ComputerStatusFilter statuses = ComputerFilter.ParseStatuses(s.UpdatesStatusFilter);
            foreach (KeyValuePair<ToolStripButton, ComputerStatusFilter> chip in chips)
                chip.Key.Checked = (statuses & chip.Value) != 0;
            staleFilter.ToolTipText = Localization.F("SoftwareUpdatesForm.StaleTip", "Last logon (Active Directory) older than {0} day(s), or never", StaleDays);
            computerColumnsLayout = s.UpdatesColumns ?? "";
            packageColumnsLayout = s.UpdatesPackageColumns ?? "";

            Rectangle bounds;
            if (WindowLayout.TryParseBounds(s.UpdatesWindowBounds, MinimumSize, out bounds)
                && WindowLayout.IsVisibleOn(bounds, Screen.AllScreens.Select(sc => sc.WorkingArea)))
            {
                StartPosition = FormStartPosition.Manual;
                Bounds = bounds;
            }
            if (s.UpdatesWindowMaximized) WindowState = FormWindowState.Maximized;
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

        void SaveSettings()
        {
            CaptureColumns();
            UserSettings s = UserSettings.Current.Clone();
            s.IncludeServers = includeServersCheck.Checked;
            s.UpdatesView = ByPackage ? ViewPackages : ViewComputers;
            s.UpdatesStatusFilter = ComputerFilter.FormatStatuses(Statuses);
            s.UpdatesColumns = computerColumnsLayout;
            s.UpdatesPackageColumns = packageColumnsLayout;
            Rectangle normal = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
            if (normal.Width >= MinimumSize.Width && normal.Height >= MinimumSize.Height) s.UpdatesWindowBounds = WindowLayout.FormatBounds(normal);
            s.UpdatesWindowMaximized = WindowState == FormWindowState.Maximized;
            if (WindowState != FormWindowState.Minimized)
                s.UpdatesSplitter = splitContainer.Height - splitContainer.SplitterWidth - splitContainer.SplitterDistance;
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
                Selection = selection
            };
        }

        void SetColumns()
        {
            ColumnHeader[] wanted = ByPackage
                ? new[] { computerColumn, packageColumn, installedColumn, availableColumn, statusColumn, resultColumn, changedColumn }
                : new[] { computerColumn, osColumn, lastLogonColumn, statusColumn, updatesColumn, resultColumn, changedColumn, pausedColumn, nightOnlyColumn };
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
            foreach (KeyValuePair<String, AdComputer> entry in ComputerFilter.Universe(ActiveDirectoryCache.Inventory, store.Computers, listed))
            {
                ComputerUpdateState state;
                states.TryGetValue(entry.Key, out state);
                ComputerSummary summary = ComputerSummary.Of(state, packages);
                summaries[entry.Key] = summary;
                result.Add(ComputerRow.Build(entry.Key, entry.Value, state, summary, packages));
            }
            rows = result;
            rowsByName = result.ToDictionary(r => r.Name, StringComparer.OrdinalIgnoreCase);
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
                    Dictionary<String, ComputerUpdateState> states = new Dictionary<String, ComputerUpdateState>(StringComparer.OrdinalIgnoreCase);
                    Dictionary<String, ComputerSummary> summaries = new Dictionary<String, ComputerSummary>(StringComparer.OrdinalIgnoreCase);
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
            String[] cells = ByPackage
                ? new[] { computer, "", "", "", SummaryText(summary), ComputerResult(c, summary), Format(c?.LastChange) }
                : new[]
                {
                    computer,
                    row.Os ?? "",
                    row.LastLogon.HasValue ? row.LastLogon.Value.ToString("yyyy-MM-dd") : "",
                    SummaryText(summary),
                    UpdatesText(c, summary),
                    ComputerResult(c, summary),
                    Format(c?.LastChange),
                    row.Paused ? Localization.T("SoftwareUpdatesForm.Paused", "Paused") : "",
                    row.NightOnly ? Localization.T("SoftwareUpdatesForm.Yes", "yes") : ""
                };
            int statusCell = ByPackage ? 4 : 3;
            if (step.Length > 0) cells[statusCell] = step;

            ListViewItem item = new ListViewItem(cells) { Tag = new RowKey { Computer = computer }, ToolTipText = RowTip(row) };
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
                tip = Localization.F("SoftwareUpdatesForm.OuTip", "OU: {0}", AdComputer.ShortOu(row.Ou)) + (tip.Length > 0 ? "\n" + tip : "");
            return tip;
        }

        // Pause reason, outside business hours only, not in Active Directory: one line each
        static String FlagsText(ComputerRow row)
        {
            List<String> lines = new List<String>();
            if (row.Paused)
                lines.Add(Localization.F("SoftwareUpdatesForm.PausedTip", "Paused: {0}",
                    String.IsNullOrEmpty(row.PauseReason) ? Localization.T("SoftwareUpdatesForm.NoReason", "(no reason given)") : row.PauseReason));
            if (row.NightOnly)
                lines.Add(Localization.F("SoftwareUpdatesForm.NightOnlyTip", "Updated outside business hours only (business hours: {0} to {1}, Monday to Friday)",
                    UserSettings.Current.BusinessHoursStart, UserSettings.Current.BusinessHoursEnd));
            if (!row.InAd)
                lines.Add(row.AddedByName
                    ? Localization.T("SoftwareUpdatesForm.AddedByNameTip", "Not in Active Directory: added by name")
                    : Localization.T("SoftwareUpdatesForm.NotInAdTip", "Not in Active Directory"));
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
                ListViewItem item = new ListViewItem(cells) { Tag = key, ToolTipText = RowTip(row) };
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
                case ComputerSummaryKind.Paused: return Localization.T("SoftwareUpdatesForm.Paused", "Paused");
                case ComputerSummaryKind.Unreachable: return Localization.T("SoftwareUpdatesForm.Unreachable", "Error");
                case ComputerSummaryKind.Errors:
                    return s.Outdated.Count > 0
                        ? Localization.F("SoftwareUpdatesForm.ErrorsAndUpdates", "{0} error(s), {1} update(s)", s.Errors.Count, s.Outdated.Count)
                        : Localization.F("SoftwareUpdatesForm.Errors", "{0} error(s)", s.Errors.Count);
                case ComputerSummaryKind.Outdated: return Localization.F("SoftwareUpdatesForm.UpdatesAvailable", "{0} update(s)", s.Outdated.Count);
                case ComputerSummaryKind.UpToDate: return Localization.T("SoftwareUpdatesForm.UpToDate", "Up to date");
                default: return Localization.T("SoftwareUpdatesForm.NotChecked", "Not checked");
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
        }

        // What is known of the computer of the selected row, under the list
        void UpdateDetails()
        {
            String computer = SelectedComputers().FirstOrDefault();
            ComputerRow row;
            if (computer == null || !rowsByName.TryGetValue(computer, out row))
            {
                detailsBox.Text = Localization.T("SoftwareUpdatesForm.DetailsHint",
                    "Select a computer to see its details. Double-click: computer card. Right-click: actions. F5: check, Ctrl+U: update, Ctrl+F: search.");
                return;
            }
            StringBuilder sb = new StringBuilder();
            sb.Append(row.Name);
            if (!String.IsNullOrEmpty(row.Os)) sb.Append(" - ").Append(row.Os).Append(String.IsNullOrEmpty(row.OsVersion) ? "" : " (" + row.OsVersion + ")");
            if (row.IsServer) sb.Append(" - ").Append(Localization.T("SoftwareUpdatesForm.Server", "server"));
            sb.AppendLine();
            if (row.InAd)
                sb.AppendLine(Localization.F("SoftwareUpdatesForm.DetailsDirectory", "Active Directory: {0}, last logon {1}",
                    String.IsNullOrEmpty(row.Ou) ? "?" : row.Ou,
                    row.LastLogon.HasValue ? row.LastLogon.Value.ToString("yyyy-MM-dd HH:mm") : Localization.T("SoftwareUpdatesForm.Never", "never")));
            foreach (String line in FlagsText(row).Split('\n').Where(l => l.Length > 0)) sb.AppendLine(line);
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
                case WorkerProtocol.StatusUpToDate: return Localization.T("SoftwareUpdatesForm.UpToDate", "Up to date");
                case WorkerProtocol.StatusOutdated: return Localization.T("SoftwareUpdatesForm.Outdated", "Update available");
                case WorkerProtocol.StatusNotInstalled: return Localization.T("SoftwareUpdatesForm.NotInstalled", "Not installed");
                case WorkerProtocol.StatusError: return Localization.T("SoftwareUpdatesForm.Error", "Error");
                default: return Localization.T("SoftwareUpdatesForm.NotChecked", "Not checked");
            }
        }

        static String ResultText(PackageUpdateState s)
        {
            if (s == null || s.LastResult == null) return "";
            String when = Format(s.LastUpgrade);
            switch (s.LastResult)
            {
                case WorkerProtocol.ResultUpgraded: return Localization.F("SoftwareUpdatesForm.Upgraded", "Updated {0}", when);
                case WorkerProtocol.ResultInstalled: return Localization.F("SoftwareUpdatesForm.Installed", "Installed {0}", when);
                case WorkerProtocol.ResultFailed: return Localization.F("SoftwareUpdatesForm.Failed", "Failed {0}: {1}", when, s.LastMessage);
                default: return s.LastMessage ?? "";
            }
        }

        void SetIdleStatus()
        {
            if (packages.Count == 0)
                statusLabel.Text = Localization.T("SoftwareUpdatesForm.NoPackages", "The package list is empty: click \"Package list...\".");
            else if (ActiveDirectoryCache.Inventory.Count == 0 && rows.Count == 0)
                statusLabel.Text = Localization.T("SoftwareUpdatesForm.NoComputers", "No computer: the Active Directory list is not loaded yet, or click \"Add...\".");
            else
                statusLabel.Text = Localization.F("SoftwareUpdatesForm.IdlePackages", "{0} package(s) in the package list.", packages.Count);
        }

        void UpdateCounts()
        {
            int total = rows.Count(r => !r.Disabled);
            int checkedCount = ByPackage ? checkedKeys.Count : checkedComputers.Count;
            countsLabel.Text = Localization.F("SoftwareUpdatesForm.Counts", "{0} shown / {1} total / {2} checked", visible.Count, total, checkedCount);
        }

        void UpdateButtons()
        {
            bool idle = pending == 0;
            bool anyTarget = (ByPackage ? checkedKeys.Count > 0 : checkedComputers.Count > 0) || updatesList.SelectedItems.Count > 0;
            checkButton.Enabled = idle && packages.Count > 0 && (visible.Count > 0 || checkedComputers.Count > 0);
            selectOutdatedButton.Enabled = selectAllButton.Enabled = selectNoneButton.Enabled = idle;
            updateButton.Enabled = idle && packages.Count > 0 && anyTarget;
            editPackagesButton.Enabled = idle;
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

        void selectionFilterButton_Click(object sender, EventArgs e)
        {
            SetSelectionFilter(null);
        }

        // ---- Keyboard -------------------------------------------------------------------

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
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
            return base.ProcessCmdKey(ref msg, keyData);
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
                statusLabel.Text = Localization.T("SoftwareUpdatesForm.NothingOutdated", "No update available: click \"Check\" first if the list is not up to date.");
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
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
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
                        Localization.F("SoftwareUpdatesForm.OfferAddCredentialOnce", "No credential is stored: the computers are reached with your account ({0}). Add a credential now? It becomes the default of the software updates (App > Options).\n\nYes: add one. No: continue with your account.", me),
                        Text, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                    if (answer == DialogResult.Cancel) return false;
                    credentialAsked = true;
                    if (answer == DialogResult.No) return true;
                    name = CredentialsForm.AddDialog(this);
                    if (name == null) return false;
                    break;
                case CredentialOffer.UseStored:
                    answer = MessageBox.Show(this,
                        Localization.F("SoftwareUpdatesForm.OfferUseCredentialOnce", "No credential is chosen for the software updates: the computers are reached with your account ({0}). Use the stored credential \"{1}\"? It becomes the default (App > Options).\n\nYes: use it. No: continue with your account.", me, list[0].Name),
                        Text, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                    if (answer == DialogResult.Cancel) return false;
                    credentialAsked = true;
                    if (answer == DialogResult.No) return true;
                    name = list[0].Name;
                    break;
                case CredentialOffer.ChooseInOptions:
                    answer = MessageBox.Show(this,
                        Localization.F("SoftwareUpdatesForm.OfferChooseCredential", "No credential is chosen for the software updates: the computers are reached with your account ({0}). Choose a credential in App > Options now?\n\nYes: open the options. No: continue with your account.", me),
                        Text, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
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
            packages = UpdateRunner.LoadPackages();
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
                statusLabel.Text = Localization.T("SoftwareUpdatesForm.NothingToUpdate", "No update available on the checked computers: click \"Check\" first if the list is not up to date.");
                return;
            }

            String skipped;
            jobs = SkipNightOnly(jobs, out skipped);
            if (jobs.Count == 0)
            {
                statusLabel.Text = skipped;
                return;
            }
            String question = Localization.F("SoftwareUpdatesForm.ConfirmUpdate", "Update {0} package(s) on {1} computer(s)? Their applications are closed without warning before the update.",
                jobs.Sum(j => j.Packages.Count), jobs.Count);
            if (skipped != null) question += "\n\n" + skipped;
            if (MessageBox.Show(this, question, Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
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
            skipped = Localization.F("SoftwareUpdatesForm.NightOnlySkipped",
                "{0} computer(s) updated outside business hours only are skipped now (business hours): {1}. To update them now, use \"Allow updates during business hours\" first.",
                night.Count, names);
            return jobs.Except(night).ToList();
        }

        // ---- Install (explicit, opt-in) ---------------------------------------------
        // Not part of "Update checked" nor of the schedules: one package, chosen in the row menu,
        // on the target computers where the last check says "notinstalled" (a module only when
        // its header declares "# Install: yes").

        void BuildInstallMenu(List<String> targets)
        {
            installItem.DropDownItems.Clear();
            HashSet<String> installable = new HashSet<String>(StringComparer.OrdinalIgnoreCase);
            if (packages.Any(p => p.IsModule))
            {
                foreach (UpdateModule m in UpdateModules.List().Where(m => m.Install)) installable.Add(m.Name);
            }
            List<KeyValuePair<UpdatePackage, List<String>>> candidates;
            lock (storeLock) candidates = UpdateRunner.InstallCandidates(targets, packages, store.Find, installable);
            foreach (KeyValuePair<UpdatePackage, List<String>> entry in candidates)
            {
                UpdatePackage package = entry.Key;
                List<String> computers = entry.Value;
                ToolStripMenuItem item = new ToolStripMenuItem(
                    Localization.F("SoftwareUpdatesForm.InstallPackageItem", "{0} ({1} computer(s))", package.DisplayName, computers.Count));
                item.Click += (s, e) => InstallPackage(package, computers);
                installItem.DropDownItems.Add(item);
            }
            Theme.Apply(installItem.DropDown);
            installItem.Enabled = candidates.Count > 0 && pending == 0;
            installItem.ToolTipText = candidates.Count > 0 ? null
                : Localization.T("SoftwareUpdatesForm.NothingToInstall", "No package to install: the last check of the selected computers says every package is installed, or the module does not declare the Install action.");
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

            String question = Localization.F("SoftwareUpdatesForm.ConfirmInstall", "Install {0} on {1} computer(s)?\n\nIt is installed only where the check still says it is not installed: {2}",
                package.DisplayName, jobs.Count, String.Join(", ", jobs.Select(j => j.Computer).Take(10)) + (jobs.Count > 10 ? ", ..." : ""));
            if (skipped != null) question += "\n\n" + skipped;
            if (MessageBox.Show(this, question, Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
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
                    Localization.T("SoftwareUpdatesForm.MissingTools", "PsExec or LiteDeploy.Worker.exe is missing next to LiteDeploy.exe."),
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
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

                SetLive(new RowKey { Computer = job.Computer }.Key, "▶ " + Localization.T("SoftwareUpdatesForm.Connecting", "Connecting..."));
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
                    SetLive(new RowKey { Computer = computer }.Key, "▶ " + Localization.T("SoftwareUpdatesForm.Started", "Started"));
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
                case WorkerProtocol.StepChecking: return Localization.T("SoftwareUpdatesForm.StepChecking", "Checking...");
                case WorkerProtocol.StepClosing: return Localization.T("SoftwareUpdatesForm.StepClosing", "Closing the application...");
                case WorkerProtocol.StepUpgrading: return Localization.T("SoftwareUpdatesForm.StepUpgrading", "Updating...");
                case WorkerProtocol.StepInstalling: return Localization.T("SoftwareUpdatesForm.StepInstalling", "Installing...");
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
            statusLabel.Text = Localization.F("SoftwareUpdatesForm.Running", "Working: {0} of {1} computer(s) done.", progressBar.Maximum - pending, progressBar.Maximum);
        }

        void stopButton_Click(object sender, EventArgs e)
        {
            ConfirmStop();
        }

        // Esc or Stop while working: stop after a confirmation
        void ConfirmStop()
        {
            if (pending == 0) return;
            if (MessageBox.Show(this, Localization.T("SoftwareUpdatesForm.ConfirmStop", "Stop the work in progress?"), Text,
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

        /// <summary>Closes with LiteDeploy (the question was asked by the main window): stops the work.</summary>
        internal void CloseWithoutQuestion()
        {
            closeConfirmed = true;
            Close();
        }

        bool closeConfirmed;

        void SoftwareUpdatesForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (pending > 0 && !closeConfirmed && e.CloseReason == CloseReason.UserClosing)
            {
                if (MessageBox.Show(this, Localization.T("SoftwareUpdatesForm.ConfirmClose", "Work is in progress. Stop it and close?"), Text,
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                {
                    e.Cancel = true;
                    return;
                }
            }
            if (pending > 0) StopAll();
            SaveSettings();
        }

        void SoftwareUpdatesForm_FormClosed(object sender, FormClosedEventArgs e)
        {
            ActiveDirectoryCache.InventoryChanged -= ActiveDirectoryCache_InventoryChanged;
            UserSettings.Saved -= UserSettings_Saved;
            searchTimer.Dispose();
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
                    menuSeparator, cardItem, processesItem, litePMItem, logsItem, wakeItem, removeSeparator, removeItem
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
                ? Localization.T("SoftwareUpdatesForm.NoTargets", "No computer checked or selected")
                : byCheck
                    ? Localization.F("SoftwareUpdatesForm.TargetsChecked", "{0} checked computer(s)", targets.Count)
                    : Localization.F("SoftwareUpdatesForm.TargetsSelected", "{0} selected computer(s)", targets.Count);
            BuildInstallMenu(targets);
            pauseItem.Enabled = idle && targets.Any(c => !IsPaused(c));
            resumeItem.Enabled = idle && targets.Any(IsPaused);
            nightOnlyItem.Enabled = idle && targets.Any(c => !IsNightOnly(c));
            nightOnlyClearItem.Enabled = idle && targets.Any(IsNightOnly);
            cardItem.Enabled = logsItem.Enabled = targets.Count > 0;
            processesItem.Enabled = litePMItem.Enabled = targets.Count == 1;
            lock (storeLock) wakeItem.Enabled = targets.Any(c => { ComputerUpdateState s = store.Find(c); return s != null && !String.IsNullOrEmpty(s.Macs); });
            bool anyAdded = targets.Any(c => { ComputerRow r; return rowsByName.TryGetValue(c, out r) && r.AddedByName && !r.InAd; });
            removeItem.Visible = removeSeparator.Visible = anyAdded;
            removeItem.Enabled = idle && anyAdded;
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

        // ---- Computer card, Wake-on-LAN, logs ------------------------------------------

        void cardItem_Click(object sender, EventArgs e)
        {
            ShowCards(ActionTargets());
        }

        // The cards of the computers (read a few at a time), in one window that can be copied
        void ShowCards(List<String> targets)
        {
            if (targets.Count == 0) return;
            Dictionary<String, String> directory = targets.ToDictionary(c => c, DirectoryText, StringComparer.OrdinalIgnoreCase);
            statusLabel.Text = targets.Count == 1
                ? Localization.F("SoftwareUpdatesForm.ReadingCard", "Reading the computer card of {0}...", targets[0])
                : Localization.F("SoftwareUpdatesForm.ReadingCards", "Reading the computer cards of {0} computer(s)...", targets.Count);
            Cursor = Cursors.WaitCursor;
            new Thread(() =>
            {
                String[] cards = new String[targets.Count];
                int next = -1;
                List<Thread> readers = new List<Thread>();
                for (int t = 0; t < Math.Min(Parallelism, targets.Count); t++)
                {
                    Thread reader = new Thread(() =>
                    {
                        int i;
                        while ((i = Interlocked.Increment(ref next)) < targets.Count)
                            cards[i] = ReadCard(targets[i], directory[targets[i]]);
                    }) { IsBackground = true };
                    readers.Add(reader);
                    reader.Start();
                }
                foreach (Thread reader in readers) reader.Join();
                if (IsDisposed) return;
                try
                {
                    BeginInvoke(new MethodInvoker(() =>
                    {
                        Cursor = Cursors.Default;
                        SetIdleStatus();
                        String title = targets.Count == 1
                            ? Localization.F("SoftwareUpdatesForm.CardTitle", "Computer card - {0}", targets[0])
                            : Localization.F("SoftwareUpdatesForm.CardsTitle", "Computer cards - {0} computer(s)", targets.Count);
                        TextViewForm.ShowText(this, title, String.Join("\n\n", cards));
                        RefreshList();
                    }));
                }
                catch (InvalidOperationException) { }   // window closed meanwhile
            }) { IsBackground = true }.Start();
        }

        // What Active Directory says of a computer (first lines of its card)
        String DirectoryText(String computer)
        {
            ComputerRow row;
            if (!rowsByName.TryGetValue(computer, out row) || !row.InAd)
                return Localization.T("SoftwareUpdatesForm.NotInAdTip", "Not in Active Directory");
            return Localization.F("SoftwareUpdatesForm.CardDirectory", "Operating system (AD): {0}\nOU: {1}\nLast logon (AD): {2}",
                String.IsNullOrEmpty(row.Os) ? "?" : row.Os + (String.IsNullOrEmpty(row.OsVersion) ? "" : " (" + row.OsVersion + ")"),
                String.IsNullOrEmpty(row.Ou) ? "?" : row.Ou,
                row.LastLogon.HasValue ? row.LastLogon.Value.ToString("yyyy-MM-dd HH:mm") : Localization.T("SoftwareUpdatesForm.Never", "never"));
        }

        // Runs on a reader thread
        String ReadCard(String computer, String directory)
        {
            UpdateRunResult r = UpdateRunner.Info(computer);
            WorkerMessage info = r.Messages.FirstOrDefault(m => m.Type == "info");
            StringBuilder sb = new StringBuilder();
            sb.Append("== ").Append(computer).Append(" ==\n").Append(directory).Append('\n');
            if (info == null)
            {
                sb.Append(Localization.F("SoftwareUpdatesForm.CardFailed", "Not read: {0}", r.Failure ?? "?"));
                return sb.ToString();
            }
            lock (storeLock)
            {
                store.Apply(computer, r.Messages, null, DateTime.Now);
                try { store.SaveIfChanged(UpdateStateStore.DefaultPath); } catch (Exception) { }
            }
            String none = Localization.T("SoftwareUpdatesForm.CardNone", "(none)");
            sb.Append(Localization.F("SoftwareUpdatesForm.Card",
                "Logged-on users: {0}\nLast restart: {1}\nRestart pending: {2}\nWindows: {3}\nType: {7}\nFree space on C: {4}\nAdapters (Wake-on-LAN): {5}\nIPv4 addresses: {6}",
                Empty(info["users"], none), info["lastboot"], info["pendingrestart"] == "yes" ? Localization.T("SoftwareUpdatesForm.Yes", "yes") : Localization.T("SoftwareUpdatesForm.No", "no"),
                info["os"], info["freec"], Empty(info["macs"], none), Empty(info["ips"], none),
                Empty(ComputerUpdateState.OsTypeText(info["ostype"]), Localization.T("SoftwareUpdatesForm.CardUnknown", "(unknown)"))));
            return sb.ToString();
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
            List<String> targets = ActionTargets();
            if (targets.Count == 0) return;
            statusLabel.Text = targets.Count == 1
                ? Localization.F("SoftwareUpdatesForm.FetchingLogs", "Getting the logs of {0}...", targets[0])
                : Localization.F("SoftwareUpdatesForm.FetchingLogsMany", "Getting the logs of {0} computer(s)...", targets.Count);
            Cursor = Cursors.WaitCursor;
            new Thread(() =>
            {
                String folder = null;
                int total = 0, fetched = 0;
                List<String> errors = new List<String>();
                foreach (String computer in targets)
                {
                    try
                    {
                        int count;
                        folder = RemoteLogs.Fetch(computer, out count);
                        total += count;
                        fetched++;
                    }
                    catch (Exception ex) { errors.Add(computer + ": " + ex.Message); }
                }
                if (IsDisposed) return;
                try
                {
                    BeginInvoke(new MethodInvoker(() =>
                    {
                        Cursor = Cursors.Default;
                        if (fetched == 0)
                        {
                            SetIdleStatus();
                            if (targets.Count == 1) MessageBox.Show(this, errors.FirstOrDefault() ?? "?", targets[0], MessageBoxButtons.OK, MessageBoxIcon.Warning);
                            else TextViewForm.ShowText(this, Text, String.Join("\n", errors));
                            return;
                        }
                        // One computer: its folder; several: the folder of every computer
                        String open = targets.Count == 1 ? folder : ScheduleRunner.LogDir;
                        statusLabel.Text = targets.Count == 1
                            ? Localization.F("SoftwareUpdatesForm.LogsFetched", "{0} log(s) of {1} copied to {2}.", total, targets[0], folder)
                            : Localization.F("SoftwareUpdatesForm.LogsFetchedMany", "Logs of {0} of {1} computer(s) copied to {2}.", fetched, targets.Count, open);
                        if (errors.Count > 0) TextViewForm.ShowText(this, Text, String.Join("\n", errors));
                        try { using (Process.Start(new ProcessStartInfo(open) { UseShellExecute = true })) { } } catch (Exception) { }
                    }));
                }
                catch (InvalidOperationException) { }
            }) { IsBackground = true }.Start();
        }

        static String Empty(String value, String none)
        {
            return String.IsNullOrEmpty(value) ? none : value;
        }

        void wakeItem_Click(object sender, EventArgs e)
        {
            int computersWoken = 0;
            lock (storeLock)
            {
                foreach (String computer in ActionTargets())
                {
                    ComputerUpdateState c = store.Find(computer);
                    if (c == null || String.IsNullOrEmpty(c.Macs)) continue;
                    if (WakeOnLan.Wake(c.Macs, c.Ips) > 0) computersWoken++;
                }
            }
            statusLabel.Text = Localization.F("SoftwareUpdatesForm.WakeSent", "Wake-on-LAN sent to {0} computer(s). They can take a few minutes to start.", computersWoken);
        }

        void pauseItem_Click(object sender, EventArgs e)
        {
            List<String> targets = ActionTargets();
            if (targets.Count == 0) return;
            String reason = Prompt.Show(this, Text,
                Localization.F("SoftwareUpdatesForm.PauseReason", "Reason of the pause ({0} computer(s)):", targets.Count), "");
            if (reason == null) return;
            ChangeComputers(targets, c => { c.Paused = true; c.PauseReason = reason.Length > 0 ? reason : null; });
        }

        void resumeItem_Click(object sender, EventArgs e)
        {
            ChangeComputers(ActionTargets(), c => { c.Paused = false; c.PauseReason = null; });
        }

        void nightOnlyItem_Click(object sender, EventArgs e)
        {
            ChangeComputers(ActionTargets(), c => c.NightOnly = true);
        }

        void nightOnlyClearItem_Click(object sender, EventArgs e)
        {
            ChangeComputers(ActionTargets(), c => c.NightOnly = false);
        }

        // Computers added by name: no longer shown (the rest of their saved state is kept)
        void removeItem_Click(object sender, EventArgs e)
        {
            List<String> added = ActionTargets().Where(c => { ComputerRow r; return rowsByName.TryGetValue(c, out r) && r.AddedByName && !r.InAd; }).ToList();
            ChangeComputers(added, c => c.AddedByName = false);
            foreach (String c in added) checkedComputers.Remove(c);
            RefreshList();
        }

        void ChangeComputers(List<String> targets, Action<ComputerUpdateState> change)
        {
            if (targets.Count == 0) return;
            lock (storeLock)
            {
                foreach (String computer in targets) change(store.GetOrAdd(computer));
                try { store.SaveIfChanged(UpdateStateStore.DefaultPath); }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error); }
            }
            RefreshList();
        }

        // ---- Add by name ------------------------------------------------------------------

        void addButton_Click(object sender, EventArgs e)
        {
            String typed = Prompt.Show(this, Text,
                Localization.T("SoftwareUpdatesForm.AddPrompt", "Computers to add (names separated by spaces or commas):"), "");
            if (String.IsNullOrWhiteSpace(typed)) return;
            List<String> added = new List<String>(), invalid = new List<String>(), known = new List<String>();
            lock (storeLock)
            {
                foreach (String part in typed.Split(new[] { ' ', ',', ';', '\t' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    String name = ComputerFilter.NormalizeName(part);
                    if (name == null) { invalid.Add(part); continue; }
                    ComputerRow row;
                    if (rowsByName.TryGetValue(name, out row) && (row.InAd || row.AddedByName)) { known.Add(row.Name); continue; }
                    store.GetOrAdd(name).AddedByName = true;
                    added.Add(name);
                }
                if (added.Count > 0)
                {
                    try { store.SaveIfChanged(UpdateStateStore.DefaultPath); }
                    catch (Exception ex) { MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error); }
                }
            }
            // The new computers are shown even when the window is filtered to a selection
            if (selection != null) foreach (String name in added.Concat(known)) selection.Add(name);
            RefreshList();
            int hidden = added.Count(n => !visible.Any(r => String.Equals(r.Name, n, StringComparison.OrdinalIgnoreCase)));
            statusLabel.Text = Localization.F("SoftwareUpdatesForm.Added", "{0} computer(s) added, {1} already in the list, {2} hidden by the filters.", added.Count, known.Count, hidden);
            if (invalid.Count > 0)
                MessageBox.Show(this, Localization.F("SoftwareUpdatesForm.InvalidNames", "Not a computer name: {0}", String.Join(", ", invalid)), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
            using (PackageListForm editor = new PackageListForm(packages))
            {
                if (editor.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    UpdateRunner.SavePackages(editor.Packages);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
            }
            packages = UpdateRunner.LoadPackages();
            RefreshList();
            SetIdleStatus();
        }
    }
}
