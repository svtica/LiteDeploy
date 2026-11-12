// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    SoftwareUpdatesForm.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-12
// Purpose: Software Updates window: state of the winget packages of the package
//          list on the computers (from the local state file). Check reads the
//          versions on every computer; the rows to update are checked (one
//          click selects every outdated row) and Update checked upgrades, on
//          each computer, only its checked packages. The work is done by
//          LiteDeploy.Worker.exe run as SYSTEM by PsExec, a few computers at a
//          time. Paused computers are skipped; "outside business hours only"
//          computers are updated during business hours only on request. Each
//          row shows the step in progress while its computer is processed.
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
        readonly HashSet<String> checkedKeys = new HashSet<String>();
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

        void RefreshList()
        {
            refreshing = true;
            updatesList.BeginUpdate();
            updatesList.Items.Clear();
            lock (storeLock)
            {
                foreach (String computer in computers)
                {
                    ComputerUpdateState c = store.Find(computer);
                    if (c != null && c.Paused)
                    {
                        AddRow(new RowKey { Computer = computer }, new[] { computer, "", "", "", Localization.T("SoftwareUpdatesForm.Paused", "Paused"), c.PauseReason ?? "", Format(c.LastChange) }, null);
                        updatesList.Items[updatesList.Items.Count - 1].ForeColor = SystemColors.GrayText;
                        continue;
                    }
                    if (packages.Count == 0 || c == null || (c.LastError != null && !packages.Any(p => c.Find(p.Id) != null)))
                    {
                        String status = c == null ? Localization.T("SoftwareUpdatesForm.NotChecked", "Not checked")
                                      : c.LastError != null ? Localization.T("SoftwareUpdatesForm.Unreachable", "Error") : "";
                        AddRow(new RowKey { Computer = computer }, new[] { computer, "", "", "", status, c?.LastError ?? "", Format(c?.LastChange) }, null);
                        continue;
                    }
                    foreach (UpdatePackage p in packages)
                    {
                        PackageUpdateState s = c.Find(p.Id);
                        AddRow(new RowKey { Computer = computer, PackageId = p.Id }, new[]
                        {
                            computer,
                            p.DisplayName,
                            s?.Installed ?? "",
                            s?.Available ?? "",
                            StatusText(s?.Status),
                            ResultText(s),
                            Format(c.LastChange)
                        }, s);
                    }
                }
            }
            updatesList.EndUpdate();
            refreshing = false;
            UpdateButtons();
        }

        void AddRow(RowKey key, String[] cells, PackageUpdateState s)
        {
            // Step in progress (live) instead of the saved status
            String step;
            lock (live)
            {
                // the package's own step ("" once it is done), else the step of its computer (connecting, started)
                if (!live.TryGetValue(key.Key, out step) && !live.TryGetValue(new RowKey { Computer = key.Computer }.Key, out step))
                    step = "";
                if (step.Length > 0) cells[4] = step;
            }
            ListViewItem item = new ListViewItem(cells) { Tag = key };
            if (s != null && s.Status == WorkerProtocol.StatusOutdated) item.ForeColor = Theme.WarningColor;
            if (s != null && (s.Status == WorkerProtocol.StatusError || s.LastResult == WorkerProtocol.ResultFailed)) item.ForeColor = Theme.ErrorColor;
            if (cells[4] != null && cells[4].StartsWith("\u25B6", StringComparison.Ordinal)) item.ForeColor = Theme.Accent;
            // Only a package that is installed can be updated
            item.Checked = key.PackageId != null && checkedKeys.Contains(key.Key);
            updatesList.Items.Add(item);
        }

        void updatesList_ItemChecked(object sender, ItemCheckedEventArgs e)
        {
            if (refreshing) return;
            RowKey key = (RowKey)e.Item.Tag;
            if (key.PackageId == null)
            {
                // A computer without package rows: check it to update... nothing; keep unchecked
                if (e.Item.Checked) e.Item.Checked = false;
                return;
            }
            if (e.Item.Checked) checkedKeys.Add(key.Key);
            else checkedKeys.Remove(key.Key);
            UpdateButtons();
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
            selectOutdatedButton.Enabled = idle;
            updateButton.Enabled = idle && checkedKeys.Count > 0;
            editPackagesButton.Enabled = idle;
            stopButton.Enabled = !idle;
        }

        // ---- Selection --------------------------------------------------------------

        void selectOutdatedButton_Click(object sender, EventArgs e)
        {
            checkedKeys.Clear();
            lock (storeLock)
            {
                foreach (String computer in computers)
                {
                    ComputerUpdateState c = store.Find(computer);
                    if (c == null) continue;
                    foreach (UpdatePackage p in packages)
                    {
                        PackageUpdateState s = c.Find(p.Id);
                        if (s != null && s.Status == WorkerProtocol.StatusOutdated)
                            checkedKeys.Add(new RowKey { Computer = computer, PackageId = p.Id }.Key);
                    }
                }
            }
            RefreshList();
            if (checkedKeys.Count == 0)
                statusLabel.Text = Localization.T("SoftwareUpdatesForm.NothingOutdated", "No update available: click \"Check\" first if the list is not up to date.");
        }

        // ---- Runs -------------------------------------------------------------------

        void checkButton_Click(object sender, EventArgs e)
        {
            packages = UpdateRunner.LoadPackages();
            Start("scan", computers.Where(c => !IsPaused(c)).Select(c => new Job { Computer = c, Packages = packages.ToList() }).ToList());
        }

        void updateButton_Click(object sender, EventArgs e)
        {
            // On each computer, only its checked packages
            List<Job> jobs = new List<Job>();
            foreach (String computer in computers.Where(c => !IsPaused(c)))
            {
                List<UpdatePackage> selected = packages
                    .Where(p => checkedKeys.Contains(new RowKey { Computer = computer, PackageId = p.Id }.Key))
                    .ToList();
                if (selected.Count > 0) jobs.Add(new Job { Computer = computer, Packages = selected });
            }
            if (jobs.Count == 0) return;

            // "Outside business hours only" computers, during business hours: only on request
            List<Job> night = jobs.Where(j => IsNightOnly(j.Computer)).ToList();
            if (night.Count > 0 && UserSettings.Current.IsBusinessHours(DateTime.Now))
            {
                DialogResult answer = MessageBox.Show(this,
                    Localization.F("SoftwareUpdatesForm.NightOnlyNow", "{0} computer(s) are updated outside business hours only ({1}). Update them now anyway?\n\nYes: update them now. No: skip them.",
                        night.Count, String.Join(", ", night.Select(j => j.Computer).Take(10))),
                    Text, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (answer == DialogResult.Cancel) return;
                if (answer == DialogResult.No) jobs = jobs.Except(night).ToList();
                if (jobs.Count == 0) return;
            }

            if (MessageBox.Show(this,
                    Localization.F("SoftwareUpdatesForm.ConfirmUpdate", "Update {0} package(s) on {1} computer(s)? Their applications are closed without warning before the update.",
                        jobs.Sum(j => j.Packages.Count), jobs.Count),
                    Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;
            Start("upgrade", jobs);
        }

        void Start(String command, List<Job> jobs)
        {
            if (jobs.Count == 0 || packages.Count == 0)
            {
                SetIdleStatus();
                RefreshList();
                return;
            }
            if (PsExecLocator.Path == null || !File.Exists(UpdateRunner.WorkerPath))
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
                    // Updated rows are unchecked
                    foreach (UpdatePackage p in job.Packages)
                    {
                        String key = new RowKey { Computer = job.Computer, PackageId = p.Id }.Key;
                        lock (checkedKeys) checkedKeys.Remove(key);
                    }
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
            pauseItem.Enabled = selected.Any(c => !IsPaused(c));
            resumeItem.Enabled = selected.Any(IsPaused);
            nightOnlyItem.Checked = selected.All(IsNightOnly);
            cardItem.Enabled = selected.Count == 1;
            lock (storeLock) wakeItem.Enabled = selected.Any(c => { ComputerUpdateState s = store.Find(c); return s != null && !String.IsNullOrEmpty(s.Macs); });
        }

        // ---- Computer card, Wake-on-LAN (MAJ-Logiciels panel) ----------------------------

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
