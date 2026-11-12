// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    SoftwareUpdatesForm.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-12
// Purpose: Software Updates window: state of the winget packages of the package
//          list on the checked computers (from the local state file), check
//          and update through LiteDeploy.Worker.exe run as SYSTEM by PsExec,
//          a few computers at a time.
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

        readonly List<String> computers;
        readonly UpdateStateStore store;
        readonly Object storeLock = new Object();
        readonly List<Process> running = new List<Process>();
        List<UpdatePackage> packages;
        volatile bool stopping;
        int pending;

        public SoftwareUpdatesForm(IEnumerable<String> computers)
        {
            InitializeComponent();
            Localization.Apply(this);
            this.computers = computers.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            store = UpdateStateStore.Load(UpdateStateStore.DefaultPath);
            packages = UpdateRunner.LoadPackages();
            RefreshList();
            SetIdleStatus();
        }

        // ---- Display ----------------------------------------------------------------

        void RefreshList()
        {
            updatesList.BeginUpdate();
            updatesList.Items.Clear();
            lock (storeLock)
            {
                foreach (String computer in computers)
                {
                    ComputerUpdateState c = store.Find(computer);
                    if (packages.Count == 0 || c == null)
                    {
                        ListViewItem item = new ListViewItem(new[] { computer, "", "", "", c == null ? Localization.T("SoftwareUpdatesForm.NotChecked", "Not checked") : "", c?.LastError ?? "", Format(c?.LastChange) });
                        updatesList.Items.Add(item);
                        continue;
                    }
                    if (c.LastError != null && !packages.Any(p => c.Find(p.Id) != null))
                    {
                        updatesList.Items.Add(new ListViewItem(new[] { computer, "", "", "", Localization.T("SoftwareUpdatesForm.Unreachable", "Error"), c.LastError, Format(c.LastChange) }));
                        continue;
                    }
                    foreach (UpdatePackage p in packages)
                    {
                        PackageUpdateState s = c.Find(p.Id);
                        ListViewItem item = new ListViewItem(new[]
                        {
                            computer,
                            p.DisplayName,
                            s?.Installed ?? "",
                            s?.Available ?? "",
                            StatusText(s?.Status),
                            ResultText(s),
                            Format(c.LastChange)
                        });
                        if (s != null && s.Status == WorkerProtocol.StatusOutdated) item.ForeColor = System.Drawing.Color.DarkOrange;
                        if (s != null && (s.Status == WorkerProtocol.StatusError || s.LastResult == WorkerProtocol.ResultFailed)) item.ForeColor = System.Drawing.Color.Firebrick;
                        updatesList.Items.Add(item);
                    }
                }
            }
            updatesList.EndUpdate();
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

        // ---- Runs -------------------------------------------------------------------

        void checkButton_Click(object sender, EventArgs e)
        {
            Start("scan");
        }

        void updateButton_Click(object sender, EventArgs e)
        {
            if (MessageBox.Show(this,
                    Localization.F("SoftwareUpdatesForm.ConfirmUpdate", "Update the outdated packages on {0} computer(s)? Their applications are closed without warning before the update.", computers.Count),
                    Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;
            Start("upgrade");
        }

        void Start(String command)
        {
            packages = UpdateRunner.LoadPackages();
            if (packages.Count == 0 || computers.Count == 0)
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
            pending = computers.Count;
            SetRunning(true);
            progressBar.Maximum = computers.Count;
            progressBar.Value = 0;
            UpdateProgressText();

            List<UpdatePackage> list = packages.ToList();
            Queue<String> queue = new Queue<String>(computers);
            for (int i = 0; i < Math.Min(Parallelism, computers.Count); i++)
            {
                Thread t = new Thread(() => Work(queue, command, list)) { IsBackground = true, Name = "Software updates" };
                t.Start();
            }
        }

        void Work(Queue<String> queue, String command, List<UpdatePackage> list)
        {
            while (true)
            {
                String computer;
                lock (queue)
                {
                    if (queue.Count == 0 || stopping) break;
                    computer = queue.Dequeue();
                }

                UpdateRunResult result = UpdateRunner.Run(computer, command, list, p => { lock (running) running.Add(p); });
                lock (running) running.RemoveAll(p => { try { return p.HasExited; } catch (InvalidOperationException) { return true; } });

                if (!stopping || result.Messages.Count > 0)
                {
                    lock (storeLock)
                    {
                        store.Apply(computer, result.Messages, result.Failure, DateTime.Now);
                        try { store.SaveIfChanged(UpdateStateStore.DefaultPath); }
                        catch (Exception ex) { AppConsole.WriteLine("Software updates: could not save " + UpdateStateStore.DefaultPath + ": " + ex.Message); }
                    }
                }
                Done();
            }

            // Computers left in the queue after Stop
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
                    if (left == 0)
                    {
                        SetRunning(false);
                        SetIdleStatus();
                    }
                    else
                    {
                        UpdateProgressText();
                    }
                }));
            }
            catch (InvalidOperationException) { }   // window closed meanwhile
        }

        void UpdateProgressText()
        {
            statusLabel.Text = Localization.F("SoftwareUpdatesForm.Running", "Working: {0} of {1} computer(s) done.", progressBar.Maximum - pending, progressBar.Maximum);
        }

        void SetRunning(bool isRunning)
        {
            checkButton.Enabled = !isRunning;
            updateButton.Enabled = !isRunning;
            editPackagesButton.Enabled = !isRunning;
            stopButton.Enabled = isRunning;
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
            String path = UpdateRunner.UserPackagesPath;
            try
            {
                if (!File.Exists(path))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    if (File.Exists(UpdateRunner.DefaultPackagesPath)) File.Copy(UpdateRunner.DefaultPackagesPath, path);
                    else File.WriteAllText(path, "# winget package Id | processes to close | label\r\n");
                }
                // Not waited for: the list is read again when this window is activated
                using (Process.Start("notepad.exe", PsExecCommandLineQuote(path))) { }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);
            if (pending > 0) return;
            List<UpdatePackage> reloaded = UpdateRunner.LoadPackages();
            if (reloaded.Select(p => p.ToArgument()).SequenceEqual(packages.Select(p => p.ToArgument()))) return;
            packages = reloaded;
            RefreshList();
            SetIdleStatus();
        }

        static String PsExecCommandLineQuote(String path)
        {
            return LiteSuite.PsExecCommandLine.QuoteArgument(path, true);
        }
    }
}
