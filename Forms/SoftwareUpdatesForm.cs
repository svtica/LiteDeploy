// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    SoftwareUpdatesForm.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-12
// Purpose: Software Updates window: state of the winget packages of the package
//          list on the computers (from the local state file), one row per
//          computer (its summary: updates, errors) or, with "Show packages",
//          one row per installed package. Check reads the versions on the
//          checked computers (every computer when none is checked); Update
//          checked upgrades the packages with an update of the checked
//          computers, or only the checked packages in the package view.
//          Before the first run of a session as the current user, offers to
//          add or choose a credential and to make it the default. The work is done by
//          LiteDeploy.Worker.exe run as SYSTEM by PsExec, a few computers at a
//          time. Paused computers are skipped; "outside business hours only"
//          computers are updated during business hours only on request. Each
//          row shows the step in progress while its computer is processed.
//          Right-click > Install: one package, chosen explicitly, on the selected
//          computers where the last check says it is not installed (modules:
//          only those that declare "# Install: yes"); never part of Update
//          checked or of the schedules.
// -----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace LiteDeploy
{
    public partial class SoftwareUpdatesForm : Form
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

        readonly List<String> computers;
        readonly UpdateStateStore store;
        readonly Object storeLock = new Object();
        readonly List<Process> running = new List<Process>();
        /// <summary>Checked package rows (package view), by row key.</summary>
        readonly HashSet<String> checkedKeys = new HashSet<String>();
        /// <summary>Checked computers (computer view), lower case.</summary>
        readonly HashSet<String> checkedComputers = new HashSet<String>();
        /// <summary>Step in progress, by row key (computer|package, or computer| for the computer).</summary>
        readonly Dictionary<String, String> live = new Dictionary<String, String>();
        List<UpdatePackage> packages;
        volatile bool stopping;
        int pending;
        bool refreshing;

        public SoftwareUpdatesForm(IEnumerable<String> computers)
        {
            InitializeComponent();
            Localization.Apply(this);
            Theme.Apply(this);
            this.computers = computers.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            store = UpdateStateStore.Load(UpdateStateStore.DefaultPath);
            packages = UpdateRunner.LoadPackages();
            updatesList.ItemChecked += updatesList_ItemChecked;
            RefreshList();
            SetIdleStatus();
        }

        // ---- Display ----------------------------------------------------------------

        /// <summary>One row per package (Show packages) instead of one row per computer.</summary>
        bool ByPackage { get { return byPackageButton.Checked; } }

        void SetColumns()
        {
            ColumnHeader[] wanted = ByPackage
                ? new[] { computerColumn, packageColumn, installedColumn, availableColumn, statusColumn, resultColumn, changedColumn }
                : new[] { computerColumn, statusColumn, updatesColumn, resultColumn, changedColumn };
            if (updatesList.Columns.Count == wanted.Length && wanted.Select((c, i) => updatesList.Columns[i] == c).All(b => b)) return;
            updatesList.Columns.Clear();
            updatesList.Columns.AddRange(wanted);
        }

        void RefreshList()
        {
            refreshing = true;
            updatesList.BeginUpdate();
            SetColumns();
            updatesList.Items.Clear();
            lock (storeLock)
            {
                foreach (String computer in computers)
                {
                    ComputerUpdateState c = store.Find(computer);
                    ComputerSummary summary = ComputerSummary.Of(c, packages);
                    if (!ByPackage || summary.Kind == ComputerSummaryKind.Paused || !AddPackageRows(computer, c))
                        AddComputerRow(computer, c, summary);
                }
            }
            updatesList.EndUpdate();
            refreshing = false;
            UpdateButtons();
        }

        // One row for the computer: its summary. Its checkbox selects the computer.
        void AddComputerRow(String computer, ComputerUpdateState c, ComputerSummary summary)
        {
            String step = LiveStep(computer);
            String[] cells = ByPackage
                ? new[] { computer, "", "", "", SummaryText(summary), ComputerResult(c, summary), Format(c?.LastChange) }
                : new[] { computer, SummaryText(summary), UpdatesText(c, summary), ComputerResult(c, summary), Format(c?.LastChange) };
            int statusCell = ByPackage ? 4 : 1;
            if (step.Length > 0) cells[statusCell] = step;

            ListViewItem item = new ListViewItem(cells) { Tag = new RowKey { Computer = computer } };
            switch (summary.Kind)
            {
                case ComputerSummaryKind.Paused: item.ForeColor = SystemColors.GrayText; break;
                case ComputerSummaryKind.Errors:
                case ComputerSummaryKind.Unreachable: item.ForeColor = Theme.ErrorColor; break;
                case ComputerSummaryKind.Outdated: item.ForeColor = Theme.WarningColor; break;
            }
            if (step.Length > 0) item.ForeColor = Theme.Accent;
            item.Checked = !ByPackage && checkedComputers.Contains(computer.ToLowerInvariant());
            updatesList.Items.Add(item);
        }

        // One row per package of the computer, except the packages that are not installed; false when none
        bool AddPackageRows(String computer, ComputerUpdateState c)
        {
            if (c == null) return false;
            bool any = false;
            foreach (UpdatePackage p in packages)
            {
                PackageUpdateState s = c.Find(p.Id);
                if (s == null || s.Status == null || s.Status == WorkerProtocol.StatusNotInstalled) continue;
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
                ListViewItem item = new ListViewItem(cells) { Tag = key };
                if (s.Status == WorkerProtocol.StatusOutdated) item.ForeColor = Theme.WarningColor;
                if (s.Status == WorkerProtocol.StatusError || (s.Status == WorkerProtocol.StatusOutdated && s.LastResult == WorkerProtocol.ResultFailed)) item.ForeColor = Theme.ErrorColor;
                if (step.Length > 0) item.ForeColor = Theme.Accent;
                item.Checked = checkedKeys.Contains(key.Key);
                updatesList.Items.Add(item);
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
                if (e.Item.Checked) checkedComputers.Add(key.Computer.ToLowerInvariant());
                else checkedComputers.Remove(key.Computer.ToLowerInvariant());
            }
            else
            {
                if (e.Item.Checked) checkedKeys.Add(key.Key);
                else checkedKeys.Remove(key.Key);
            }
            UpdateButtons();
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
            statusLabel.Text = packages.Count == 0
                ? Localization.T("SoftwareUpdatesForm.NoPackages", "The package list is empty: click \"Package list...\".")
                : Localization.F("SoftwareUpdatesForm.Idle", "{0} computer(s), {1} package(s).", computers.Count, packages.Count);
        }

        void UpdateButtons()
        {
            bool idle = pending == 0;
            checkButton.Enabled = idle && packages.Count > 0 && computers.Count > 0;
            selectOutdatedButton.Enabled = selectAllButton.Enabled = selectNoneButton.Enabled = idle;
            updateButton.Enabled = idle && (ByPackage ? checkedKeys.Count > 0 : checkedComputers.Count > 0);
            editPackagesButton.Enabled = idle;
            byPackageButton.Enabled = idle;
            stopButton.Enabled = !idle;
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
                foreach (String computer in computers.Where(c => !IsPaused(c))) checkedComputers.Add(computer.ToLowerInvariant());
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
            checkedKeys.Clear();
            checkedComputers.Clear();
            lock (storeLock)
            {
                foreach (String computer in computers)
                {
                    ComputerSummary summary = ComputerSummary.Of(store.Find(computer), packages);
                    if (summary.Outdated.Count == 0) continue;
                    checkedComputers.Add(computer.ToLowerInvariant());
                    foreach (UpdatePackage p in summary.Outdated)
                        checkedKeys.Add(new RowKey { Computer = computer, PackageId = p.Id }.Key);
                }
            }
            RefreshList();
            if (checkedKeys.Count == 0)
                statusLabel.Text = Localization.T("SoftwareUpdatesForm.NothingOutdated", "No update available: click \"Check\" first if the list is not up to date.");
        }

        // Computers checked in the view shown (in the package view, the computers of the checked packages)
        List<String> CheckedComputers()
        {
            return computers.Where(c => ByPackage
                    ? packages.Any(p => checkedKeys.Contains(new RowKey { Computer = c, PackageId = p.Id }.Key))
                    : checkedComputers.Contains(c.ToLowerInvariant()))
                .ToList();
        }

        // ---- Account ----------------------------------------------------------------

        /// <summary>Asked once per session (the question comes back at each start of LiteDeploy).</summary>
        static bool credentialAsked;

        /// <summary>
        /// Before a check or an update run as the current user: offers to add a credential when none
        /// is stored (or to use the stored one), then to make it the default (App > Options).
        /// False: the user cancelled the run.
        /// </summary>
        bool OfferCredential()
        {
            int stored;
            List<LiteSuite.StoredCredential> list;
            try { list = LiteSuite.CredentialStore.List().ToList(); stored = list.Count; }
            catch (Exception) { return true; }   // Credential Manager unavailable: the current user

            CredentialOffer offer = UpdateRunner.OfferFor(UpdateRunner.UpdateCredentialName, stored, credentialAsked);
            if (offer == CredentialOffer.None) return true;
            credentialAsked = true;
            String me = Environment.UserDomainName + "\\" + Environment.UserName;
            String name = null;
            DialogResult answer;
            switch (offer)
            {
                case CredentialOffer.Add:
                    answer = MessageBox.Show(this,
                        Localization.F("SoftwareUpdatesForm.OfferAddCredential", "No credential is stored: the computers are reached with your account ({0}). Add a credential now (Tools > Credentials)?\n\nYes: add one. No: continue with your account.", me),
                        Text, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                    if (answer == DialogResult.Cancel) return false;
                    if (answer == DialogResult.No) return true;
                    name = CredentialsForm.AddDialog(this);
                    if (name == null) return false;
                    break;
                case CredentialOffer.UseStored:
                    answer = MessageBox.Show(this,
                        Localization.F("SoftwareUpdatesForm.OfferUseCredential", "No credential is chosen for the software updates: the computers are reached with your account ({0}). Use the stored credential \"{1}\"?\n\nYes: use it. No: continue with your account.", me, list[0].Name),
                        Text, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                    if (answer == DialogResult.Cancel) return false;
                    if (answer == DialogResult.No) return true;
                    name = list[0].Name;
                    break;
                case CredentialOffer.ChooseInOptions:
                    answer = MessageBox.Show(this,
                        Localization.F("SoftwareUpdatesForm.OfferChooseCredential", "No credential is chosen for the software updates: the computers are reached with your account ({0}). Choose a credential in App > Options now?\n\nYes: open the options. No: continue with your account.", me),
                        Text, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                    if (answer == DialogResult.Cancel) return false;
                    if (answer == DialogResult.Yes)
                        using (OptionsForm options = new OptionsForm()) options.ShowDialog(this);
                    return true;
            }

            answer = MessageBox.Show(this,
                Localization.F("SoftwareUpdatesForm.OfferDefaultCredential", "Use \"{0}\" by default for the software updates (App > Options)?\n\nYes: by default. No: until LiteDeploy is closed.", name),
                Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (answer == DialogResult.Yes)
            {
                UserSettings settings = UserSettings.Current.Clone();
                settings.UpdateCredential = name;
                try { UserSettings.Save(settings); return true; }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error); }
            }
            UpdateRunner.SessionCredential = name;
            return true;
        }

        // ---- Runs -------------------------------------------------------------------

        void checkButton_Click(object sender, EventArgs e)
        {
            if (!OfferCredential()) return;
            packages = UpdateRunner.LoadPackages();
            // The checked computers, or every computer when none is checked
            List<String> targets = CheckedComputers();
            if (targets.Count == 0) targets = computers;
            Start("scan", targets.Where(c => !IsPaused(c)).Select(c => new Job { Computer = c, Packages = packages.ToList() }).ToList());
        }

        void updateButton_Click(object sender, EventArgs e)
        {
            // Computer view: the packages with an update of each checked computer.
            // Package view: on each computer, only its checked packages.
            List<Job> jobs = new List<Job>();
            foreach (String computer in CheckedComputers().Where(c => !IsPaused(c)))
            {
                List<UpdatePackage> selected;
                if (ByPackage)
                    selected = packages.Where(p => checkedKeys.Contains(new RowKey { Computer = computer, PackageId = p.Id }.Key)).ToList();
                else
                    lock (storeLock) selected = ComputerSummary.Of(store.Find(computer), packages).Outdated;
                if (selected.Count > 0) jobs.Add(new Job { Computer = computer, Packages = selected });
            }
            if (jobs.Count == 0)
            {
                statusLabel.Text = Localization.T("SoftwareUpdatesForm.NothingToUpdate", "No update available on the checked computers: click \"Check\" first if the list is not up to date.");
                return;
            }

            jobs = AskNightOnly(jobs, false);
            if (jobs == null || jobs.Count == 0) return;

            if (MessageBox.Show(this,
                    Localization.F("SoftwareUpdatesForm.ConfirmUpdate", "Update {0} package(s) on {1} computer(s)? Their applications are closed without warning before the update.",
                        jobs.Sum(j => j.Packages.Count), jobs.Count),
                    Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;
            Start("upgrade", jobs);
        }

        /// <summary>
        /// "Outside business hours only" computers, during business hours: only on request (Yes: keep
        /// them, No: skip them). Null: cancelled.
        /// </summary>
        List<Job> AskNightOnly(List<Job> jobs, bool install)
        {
            List<Job> night = jobs.Where(j => IsNightOnly(j.Computer)).ToList();
            if (night.Count == 0 || !UserSettings.Current.IsBusinessHours(DateTime.Now)) return jobs;
            String names = String.Join(", ", night.Select(j => j.Computer).Take(10));
            String question = install
                ? Localization.F("SoftwareUpdatesForm.NightOnlyInstallNow", "{0} computer(s) are updated outside business hours only ({1}). Install on them now anyway?\n\nYes: install now. No: skip them.", night.Count, names)
                : Localization.F("SoftwareUpdatesForm.NightOnlyNow", "{0} computer(s) are updated outside business hours only ({1}). Update them now anyway?\n\nYes: update them now. No: skip them.", night.Count, names);
            DialogResult answer = MessageBox.Show(this, question, Text, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (answer == DialogResult.Cancel) return null;
            return answer == DialogResult.No ? jobs.Except(night).ToList() : jobs;
        }

        // ---- Install (explicit, opt-in) ---------------------------------------------
        // Not part of "Update checked" nor of the schedules: one package, chosen in the row menu,
        // on the selected computers where the last check says "notinstalled" (a module only when
        // its header declares "# Install: yes").

        void BuildInstallMenu(List<String> selected)
        {
            installItem.DropDownItems.Clear();
            HashSet<String> installable = new HashSet<String>(StringComparer.OrdinalIgnoreCase);
            if (packages.Any(p => p.IsModule))
            {
                foreach (UpdateModule m in UpdateModules.List().Where(m => m.Install)) installable.Add(m.Name);
            }
            List<KeyValuePair<UpdatePackage, List<String>>> candidates;
            lock (storeLock) candidates = UpdateRunner.InstallCandidates(selected, packages, store.Find, installable);
            foreach (KeyValuePair<UpdatePackage, List<String>> entry in candidates)
            {
                UpdatePackage package = entry.Key;
                List<String> targets = entry.Value;
                ToolStripMenuItem item = new ToolStripMenuItem(
                    Localization.F("SoftwareUpdatesForm.InstallPackageItem", "{0} ({1} computer(s))", package.DisplayName, targets.Count));
                item.Click += (s, e) => InstallPackage(package, targets);
                installItem.DropDownItems.Add(item);
            }
            Theme.Apply(installItem.DropDown);
            installItem.Enabled = candidates.Count > 0;
            installItem.ToolTipText = candidates.Count > 0 ? null
                : Localization.T("SoftwareUpdatesForm.NothingToInstall", "No package to install: the last check of the selected computers says every package is installed, or the module does not declare the Install action.");
        }

        void InstallPackage(UpdatePackage package, List<String> targets)
        {
            if (pending > 0) return;
            List<Job> jobs = targets.Where(c => !IsPaused(c))
                .Select(c => new Job { Computer = c, Packages = new List<UpdatePackage> { package } }).ToList();
            jobs = AskNightOnly(jobs, true);
            if (jobs == null || jobs.Count == 0) return;

            if (MessageBox.Show(this,
                    Localization.F("SoftwareUpdatesForm.ConfirmInstall", "Install {0} on {1} computer(s)?\n\nIt is installed only where the check still says it is not installed: {2}",
                        package.DisplayName, jobs.Count, String.Join(", ", jobs.Select(j => j.Computer).Take(10)) + (jobs.Count > 10 ? ", ..." : "")),
                    Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;
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

                SetLive(new RowKey { Computer = job.Computer }.Key, "\u25B6 " + Localization.T("SoftwareUpdatesForm.Connecting", "Connecting..."));
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
                    lock (checkedKeys) checkedComputers.Remove(job.Computer.ToLowerInvariant());
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
                    SetLive(new RowKey { Computer = computer }.Key, "\u25B6 " + Localization.T("SoftwareUpdatesForm.Started", "Started"));
                    break;
                case WorkerProtocol.TypeProgress:
                    SetLive(key, "\u25B6 " + StepText(m["step"]));
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
            if (IsDisposed || Interlocked.Exchange(ref refreshQueued, 1) == 1) return;
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
            StopAll();
        }

        void StopAll()
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

        void SoftwareUpdatesForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (pending > 0)
            {
                if (MessageBox.Show(this, Localization.T("SoftwareUpdatesForm.ConfirmClose", "Work is in progress. Stop it and close?"), Text,
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                {
                    e.Cancel = true;
                    return;
                }
                StopAll();
            }
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

        List<String> SelectedComputers()
        {
            return updatesList.SelectedItems.Cast<ListViewItem>().Select(i => ((RowKey)i.Tag).Computer)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        void rowMenu_Opening(object sender, System.ComponentModel.CancelEventArgs e)
        {
            List<String> selected = SelectedComputers();
            if (selected.Count == 0 || pending > 0) { e.Cancel = true; return; }
            BuildInstallMenu(selected);
            pauseItem.Enabled = selected.Any(c => !IsPaused(c));
            resumeItem.Enabled = selected.Any(IsPaused);
            nightOnlyItem.Checked = selected.All(IsNightOnly);
            cardItem.Enabled = processesItem.Enabled = litePMItem.Enabled = logsItem.Enabled = selected.Count == 1;
            lock (storeLock) wakeItem.Enabled = selected.Any(c => { ComputerUpdateState s = store.Find(c); return s != null && !String.IsNullOrEmpty(s.Macs); });
        }

        // ---- Computer card, Wake-on-LAN ------------------------------------------------

        void cardItem_Click(object sender, EventArgs e)
        {
            String computer = SelectedComputers().FirstOrDefault();
            if (computer == null) return;
            statusLabel.Text = Localization.F("SoftwareUpdatesForm.ReadingCard", "Reading the computer card of {0}...", computer);
            Cursor = Cursors.WaitCursor;
            new Thread(() =>
            {
                UpdateRunResult r = UpdateRunner.Info(computer);
                WorkerMessage info = r.Messages.FirstOrDefault(m => m.Type == "info");
                if (info != null)
                {
                    lock (storeLock)
                    {
                        store.Apply(computer, r.Messages, null, DateTime.Now);
                        try { store.SaveIfChanged(UpdateStateStore.DefaultPath); } catch (Exception) { }
                    }
                }
                if (IsDisposed) return;
                BeginInvoke(new MethodInvoker(() =>
                {
                    Cursor = Cursors.Default;
                    SetIdleStatus();
                    if (info == null)
                    {
                        MessageBox.Show(this, r.Failure ?? "?", computer, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                    String none = Localization.T("SoftwareUpdatesForm.CardNone", "(none)");
                    String text = Localization.F("SoftwareUpdatesForm.Card",
                        "Logged-on users: {0}\nLast restart: {1}\nRestart pending: {2}\nWindows: {3}\nFree space on C: {4}\nAdapters (Wake-on-LAN): {5}\nIPv4 addresses: {6}",
                        Empty(info["users"], none), info["lastboot"], info["pendingrestart"] == "yes" ? Localization.T("SoftwareUpdatesForm.Yes", "yes") : Localization.T("SoftwareUpdatesForm.No", "no"),
                        info["os"], info["freec"], Empty(info["macs"], none), Empty(info["ips"], none));
                    MessageBox.Show(this, text, Localization.F("SoftwareUpdatesForm.CardTitle", "Computer card - {0}", computer), MessageBoxButtons.OK, MessageBoxIcon.Information);
                }));
            }) { IsBackground = true }.Start();
        }

        void processesItem_Click(object sender, EventArgs e)
        {
            String computer = SelectedComputers().FirstOrDefault();
            if (computer == null) return;
            using (RemoteProcessesForm form = new RemoteProcessesForm(computer)) form.ShowDialog(this);
        }

        // LitePM connected to the computer through WMI, as the current user
        void litePMItem_Click(object sender, EventArgs e)
        {
            String computer = SelectedComputers().FirstOrDefault();
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
            String computer = SelectedComputers().FirstOrDefault();
            if (computer == null) return;
            statusLabel.Text = Localization.F("SoftwareUpdatesForm.FetchingLogs", "Getting the logs of {0}...", computer);
            Cursor = Cursors.WaitCursor;
            new Thread(() =>
            {
                String folder = null, error = null;
                int count = 0;
                try { folder = RemoteLogs.Fetch(computer, out count); }
                catch (Exception ex) { error = ex.Message; }
                if (IsDisposed) return;
                BeginInvoke(new MethodInvoker(() =>
                {
                    Cursor = Cursors.Default;
                    if (error != null)
                    {
                        SetIdleStatus();
                        MessageBox.Show(this, error, computer, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                    statusLabel.Text = Localization.F("SoftwareUpdatesForm.LogsFetched", "{0} log(s) of {1} copied to {2}.", count, computer, folder);
                    try { using (Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true })) { } } catch (Exception) { }
                }));
            }) { IsBackground = true }.Start();
        }

        static String Empty(String value, String none)
        {
            return String.IsNullOrEmpty(value) ? none : value;
        }

        void wakeItem_Click(object sender, EventArgs e)
        {
            int computersWoken = 0, packets = 0;
            lock (storeLock)
            {
                foreach (String computer in SelectedComputers())
                {
                    ComputerUpdateState c = store.Find(computer);
                    if (c == null || String.IsNullOrEmpty(c.Macs)) continue;
                    int sent = WakeOnLan.Wake(c.Macs, c.Ips);
                    if (sent > 0) { computersWoken++; packets += sent; }
                }
            }
            statusLabel.Text = Localization.F("SoftwareUpdatesForm.WakeSent", "Wake-on-LAN sent to {0} computer(s). They can take a few minutes to start.", computersWoken);
        }

        void pauseItem_Click(object sender, EventArgs e)
        {
            List<String> selected = SelectedComputers();
            String reason = Prompt.Show(this, Text,
                Localization.F("SoftwareUpdatesForm.PauseReason", "Reason of the pause ({0} computer(s)):", selected.Count), "");
            if (reason == null) return;
            ChangeComputers(selected, c => { c.Paused = true; c.PauseReason = reason.Length > 0 ? reason : null; });
        }

        void resumeItem_Click(object sender, EventArgs e)
        {
            ChangeComputers(SelectedComputers(), c => { c.Paused = false; c.PauseReason = null; });
        }

        void nightOnlyItem_Click(object sender, EventArgs e)
        {
            List<String> selected = SelectedComputers();
            bool value = !selected.All(IsNightOnly);
            ChangeComputers(selected, c => c.NightOnly = value);
        }

        void ChangeComputers(List<String> selected, Action<ComputerUpdateState> change)
        {
            lock (storeLock)
            {
                foreach (String computer in selected) change(store.GetOrAdd(computer));
                try { store.SaveIfChanged(UpdateStateStore.DefaultPath); }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error); }
            }
            RefreshList();
        }

        void scheduleButton_Click(object sender, EventArgs e)
        {
            using (SchedulesForm schedules = new SchedulesForm(computers))
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
