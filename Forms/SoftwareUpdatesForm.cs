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
//          time.
// -----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Diagnostics;
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
            ListViewItem item = new ListViewItem(cells) { Tag = key };
            if (s != null && s.Status == WorkerProtocol.StatusOutdated) item.ForeColor = Theme.WarningColor;
            if (s != null && (s.Status == WorkerProtocol.StatusError || s.LastResult == WorkerProtocol.ResultFailed)) item.ForeColor = Theme.ErrorColor;
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
            Start("scan", computers.Select(c => new Job { Computer = c, Packages = packages.ToList() }).ToList());
        }

        void updateButton_Click(object sender, EventArgs e)
        {
            // On each computer, only its checked packages
            List<Job> jobs = new List<Job>();
            foreach (String computer in computers)
            {
                List<UpdatePackage> selected = packages
                    .Where(p => checkedKeys.Contains(new RowKey { Computer = computer, PackageId = p.Id }.Key))
                    .ToList();
                if (selected.Count > 0) jobs.Add(new Job { Computer = computer, Packages = selected });
            }
            if (jobs.Count == 0) return;

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

                UpdateRunResult result = UpdateRunner.Run(job.Computer, command, job.Packages, p => { lock (running) running.Add(p); });
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
