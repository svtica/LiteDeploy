// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    MainForm.cs
// Author:  LiteDeploy contributors
// Date:    2025-05-30
// Purpose: Main window: the computer grid (ComputerGrid: Active Directory
//          inventory and computers added, states, filters, checks, software
//          updates and actions), the console under it, and the App, Tools and
//          Help menus. Runs the standalone deployments of the grid (DeployForm,
//          PsExec, process reports), the ping of its computers, the internal
//          scheduler and the daily web check. Size and position are kept in the
//          user settings.
// -----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace LiteDeploy
{
    public partial class MainForm : Form, IPingTarget
    {
        // Child windows used by the deployment threads: created with the main window, on the UI thread
        static private ProcessReportsForm processConsole = new ProcessReportsForm();
        static public DeploymentProgressForm prepareWindow = new DeploymentProgressForm();

        public static SplashForm screen;

        /// <summary>Deploy a file or command: created on first use, then kept (its fields stay).</summary>
        private DeployForm deployWindow;

        public MainForm()
        {
            InitializeComponent();
            Localization.Apply(this);
            Theme.Apply(this);
            RestoreWindowBounds();
            ApplyDeploymentsVisibility();

            grid.DeployRequested += targets => ShowDeploy();
            grid.PingRequested += ShowPing;
            grid.TargetsChanged += (s, e) => { if (deployWindow != null && deployWindow.Visible) deployWindow.RefreshTargets(); };

            Hide();
        }

        private void MainForm_Load(object sender, EventArgs e)
        {
            screen = new SplashForm(this);
            screen.ShowDialog();
            screen.Dispose();
            screen = null;

            MainFormAccess.Init(this);

            LocalNetworkSettings.CheckForLocalDNSServers();
            AppConsole.Init(consoleDisplay, this);
            StartInternalScheduler();
        }

        // ---- Size and position ----------------------------------------------------------

        void RestoreWindowBounds()
        {
            UserSettings s = UserSettings.Current;
            Rectangle bounds;
            if (WindowLayout.TryParseBounds(s.UpdatesWindowBounds, MinimumSize, out bounds)
                && WindowLayout.IsVisibleOn(bounds, Screen.AllScreens.Select(sc => sc.WorkingArea)))
            {
                StartPosition = FormStartPosition.Manual;
                Bounds = bounds;
            }
            if (s.UpdatesWindowMaximized) WindowState = FormWindowState.Maximized;
        }

        void SaveSettings()
        {
            Rectangle normal = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
            bool maximized = WindowState == FormWindowState.Maximized;
            Size minimum = MinimumSize;
            grid.SaveSettings(s =>
            {
                if (normal.Width >= minimum.Width && normal.Height >= minimum.Height) s.UpdatesWindowBounds = WindowLayout.FormatBounds(normal);
                s.UpdatesWindowMaximized = maximized;
            });
        }

        // ---- Keyboard: the shortcuts of the grid from anywhere in the window --------------

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (grid.HandleShortcut(keyData)) return true;
            return base.ProcessCmdKey(ref msg, keyData);
        }

        // ---- Deployment (file or command through PsExec) ------------------------------------

        private void deployToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ShowDeploy();
        }

        private void ShowDeploy()
        {
            if (deployWindow == null || deployWindow.IsDisposed)
                deployWindow = new DeployForm(grid.Targets);
            deployWindow.ShowFor(this);
        }

        /// <summary>The password typed in the deployment window, hidden in the console; null when none.</summary>
        internal String SecretToMask()
        {
            DeployForm window = deployWindow;
            return window == null || window.IsDisposed ? null : window.SecretToMask();
        }

        private delegate void ClearAllLists();
        private void clearLists()
        {
            if (processConsole.processList.InvokeRequired)
            {
                ClearAllLists a = new ClearAllLists(clearLists);
                Invoke(a);
            }

            else
            {
                processConsole.processList.Items.Clear();
                ProcessReportsForm.logEntries.Clear();
                ProcessManager.processList.Clear();
                ProcessManager.displayList.Clear();
                ProcessManager.outputList.Clear();
                ProcessManager.deployNameList.Clear();
            }
        }

        /// <summary>
        /// Starts a deployment (DeployForm): one PsExec process per computer, the MSI preparation when needed,
        /// the progress in the process reports window.
        /// </summary>
        internal void StartDeployment(List<String> computers, Func<String, PsExecOptions> options, String executableOrCommand)
        {
            if (ProcessManager.processing) return;
            clearLists();

            foreach (String computer in computers)
            {
                PsExecOptions o = options(computer);
                ProcessManager.deployNameList.Add(computer);
                ProcessManager.AddProcess(PsExecCommandBuilder.BuildArguments(o), PsExecCommandBuilder.DisplayArguments(o));
            }

            bool MSIPrepCancelled = false;
            if (processConsole.MSIDeployment)
            {
                // A form shown with ShowDialog is only hidden when closed: dispose it.
                using (DeploymentPrepForm MSIPrep = new DeploymentPrepForm())
                {
                    if (MSIPrep.ShowDialog(this) == DialogResult.Cancel)
                        MSIPrepCancelled = true;
                }

                processConsole.MSIprogressBar.Maximum = ProcessManager.deployNameList.Count;
            }

            if (!MSIPrepCancelled)
            {
                processConsole.PrepareForDeployment();
                processConsole.Show();

                ProcessReportsForm.currentExecutableOrCommand = executableOrCommand;
                ProcessManager.StartProcessing();

                processConsole.AddLogEntry(ProcessReportsForm.DeploymentHost, Localization.T("MainForm.DeploymentStarted", "Deployment started."));

                ControlBox = false;
                exitToolStripMenuItem.Enabled = false;
            }

            else
            {
                processConsole.AddLogEntry(ProcessReportsForm.DeploymentHost, Localization.T("MainForm.DeploymentCancelled", "Deployment cancelled."));
            }
        }

        private void killAllProcessesToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ProcessManager.KillAll();
            prepareWindow.Hide();
            reEnableExitOps();
        }

        private delegate void reEnableExitOpsDel();
        public void reEnableExitOps()
        {
            if (this.InvokeRequired)
            {
                reEnableExitOpsDel a = new reEnableExitOpsDel(reEnableExitOps);
                Invoke(a);
            }

            else
            {
                ControlBox = true;
                SetDeployEnabled(true);
                exitToolStripMenuItem.Enabled = true;
            }
        }

        private void killCurrentProcessToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ProcessManager.KillCurrentProcess();
        }

        // The deployment threads disable and enable the start of a deployment
        private delegate void SetDeployEnabledDel(bool enabled);
        void SetDeployEnabled(bool enabled)
        {
            if (InvokeRequired)
            {
                Invoke(new SetDeployEnabledDel(SetDeployEnabled), enabled);
                return;
            }
            grid.SetDeployEnabled(enabled);
            deployToolStripMenuItem.Enabled = enabled;
            if (deployWindow != null && !deployWindow.IsDisposed) deployWindow.SetStartEnabled(enabled);
        }

        public void DisableStart()
        {
            SetDeployEnabled(false);
        }

        public void EnableStart()
        {
            SetDeployEnabled(true);
        }

        delegate void ShowConsoleDel();
        public void ShowProcessConsole()
        {
            if (processConsole.InvokeRequired)
            {
                ShowConsoleDel a = new ShowConsoleDel(ShowProcessConsole);
                Invoke(a);
            }
            else
            {
                processConsole.clearSelect_Click(null, null);

                if (!processConsole.Visible)
                {
                    processConsole.Show();
                }

                else if (processConsole.WindowState == FormWindowState.Minimized)
                {
                    processConsole.WindowState = FormWindowState.Normal;
                }

                else
                {
                    processConsole.Focus();
                }
            }
        }

        private void openProcessConsoleToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ShowProcessConsole();
        }

        private delegate bool IsPConsoleVis();
        public bool IsProcessConsoleVisible()
        {
            if (processConsole.InvokeRequired)
            {
                IsPConsoleVis a = new IsPConsoleVis(IsProcessConsoleVisible);
                return (bool)Invoke(a);
            }

            return processConsole.Visible;
        }

        public ProcessReportsForm PConsoleAccess()
        {
            return processConsole;
        }

        // ---- Computers ----------------------------------------------------------------------

        private void addComputersToolStripMenuItem_Click(object sender, EventArgs e)
        {
            grid.ShowAddComputers();
        }

        // Ping window: created on first use, then reused. Creating one per use leaked about
        // three GDI objects per ping run (fonts and icon of the designer), measured by the soak test.
        private PingAllForm pingAllWindow;

        private void pingAllToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ShowPing(grid.PingTargets());
        }

        private void ShowPing(List<String> computers)
        {
            if (pingAllWindow == null)
                pingAllWindow = new PingAllForm();
            pingAllWindow.firstInit = true;
            pingAllWindow.SetComputers(computers, this);
            pingAllWindow.loadSelectedComputers();
            pingAllWindow.ShowDialog(this);
        }

        void IPingTarget.SetChecked(IEnumerable<String> computers, bool check)
        {
            grid.SetChecked(computers, check);
        }

        void IPingTarget.RemoveFromList(IEnumerable<String> computers)
        {
            grid.RemoveFromList(computers);
        }

        // ---- App and Help menus ---------------------------------------------------------------

        private void exitToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Close();
        }

        private void aboutToolStripMenuItem_Click(object sender, EventArgs e)
        {
            using (AboutForm about = new AboutForm())
            {
                about.ShowDialog(this);
            }
        }

        private void clearConsoleToolStripMenuItem_Click(object sender, EventArgs e)
        {
            AppConsole.Clear();
        }

        private void changeLogToolStripMenuItem_Click(object sender, EventArgs e)
        {
            using (ChangeLogForm clog = new ChangeLogForm())
            {
                clog.ShowDialog(this);
            }
        }

        private void optionsToolStripMenuItem1_Click(object sender, EventArgs e)
        {
            using (OptionsForm SetWin = new OptionsForm())
            {
                SetWin.ShowDialog(this);
            }
            ApplyDeploymentsVisibility();
        }

        // Standalone deployments (App > Options, hidden by default): Tools menu and actions of the grid
        void ApplyDeploymentsVisibility()
        {
            deployToolStripMenuItem.Visible = UserSettings.Current.ShowStandaloneDeployments;
            grid.ApplyDeploymentsVisibility();
        }

        // ---- Update of LiteDeploy (AppUpdater) -------------------------------------------

        // Set when LiteDeploy closes for its update: the closing question was already asked
        bool closingForUpdate;

        private async void checkForUpdatesToolStripMenuItem_Click(object sender, EventArgs e)
        {
            checkForUpdatesToolStripMenuItem.Enabled = false;
            try
            {
                await AppUpdater.RunAsync(this, true);
            }
            finally
            {
                if (!IsDisposed) checkForUpdatesToolStripMenuItem.Enabled = true;
            }
        }

        // Start-up check (App > Options), in the background once the window is shown
        private async void MainForm_Shown(object sender, EventArgs e)
        {
            if (!UserSettings.Current.CheckUpdatesOnStartup) return;
            await AppUpdater.RunAsync(this, false);
        }

        /// <summary>Before the update closes LiteDeploy: confirmation when deployments, software updates or scheduled updates are running.</summary>
        internal bool ConfirmCloseForUpdate()
        {
            bool schedules;
            lock (runningSchedules) schedules = runningSchedules.Count > 0;
            if (!ProcessManager.processing && !schedules && !grid.Busy) return true;
            return MessageBox.Show(this, Localization.T("MainForm.UpdateWhileBusy", "A deployment or a scheduled update is in progress: closing LiteDeploy for its update stops it.\n\nInstall the update now anyway?"),
                                   Localization.T("MainForm.ConfirmClose.Title", "Confirm Close"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) == DialogResult.Yes;
        }

        /// <summary>Closes LiteDeploy once its update script runs (it waits for this process to exit).</summary>
        internal void CloseForUpdate()
        {
            closingForUpdate = true;
            Close();
            // Closing the main window ends the application; should it stay open, exit anyway
            // (the script stops LiteDeploy after 60 s otherwise)
            if (!IsDisposed && Visible) Application.Exit();
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (ProcessManager.processing && !closingForUpdate)
            {
                DialogResult result = MessageBox.Show(this, Localization.T("MainForm.ConfirmClose", "A deployment is currently in progress. Are you sure you want to close the application?"), Localization.T("MainForm.ConfirmClose.Title", "Confirm Close"), MessageBoxButtons.YesNo, MessageBoxIcon.Question);

                if (result == System.Windows.Forms.DialogResult.No)
                {
                    e.Cancel = true;
                    return;
                }
            }

            // The work of the grid (check, update, install) stops with LiteDeploy, after a question
            if (grid.Busy && !closingForUpdate
                && MessageBox.Show(this, Localization.T("ComputerGrid.ConfirmClose", "Work is in progress. Stop it and close?"),
                    Localization.T("MainForm.ConfirmClose.Title", "Confirm Close"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            {
                e.Cancel = true;
                return;
            }

            SaveSettings();
            grid.Shutdown();
            ProcessManager.KillAll();
            ActiveDirectoryCache.StopUpdate();
        }

        // ---- Tools menu -------------------------------------------------------------------------

        // Web versions of the products of the shared cache (module instances with a WingetId)
        private void webVersionsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            using (WebVersionsForm webVersions = new WebVersionsForm())
            {
                webVersions.ShowDialog(this);
            }
        }

        // Scheduled updates: the checked computers, else every computer shown, as the default targets
        private void schedulesToolStripMenuItem_Click(object sender, EventArgs e)
        {
            using (SchedulesForm schedules = new SchedulesForm(grid.PingTargets()))
            {
                schedules.ShowDialog(this);
            }
        }

        private void credentialsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            using (CredentialsForm credentials = new CredentialsForm())
            {
                credentials.ShowDialog(this);
            }
        }

        private void manageToolsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            using (ToolsForm tools = new ToolsForm())
            {
                tools.ShowDialog(this);
            }
        }

        // LitePM: installed by Tools > Manage Tools (or provided by LiteTask)
        private void litePMToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ToolInfo litePM = ToolManager.List().FirstOrDefault(t => t.Id == ToolManager.LitePMId);
            if (litePM == null || litePM.Path == null)
            {
                MessageBox.Show(this, Localization.T("MainForm.LitePMMissing", "LitePM is not installed: install it in Tools > Manage Tools."), "LitePM", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            try { ToolManager.Open(litePM); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "LitePM", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        // ---- Internal scheduler: the schedules due while LiteDeploy is open --------------

        readonly DateTime startedAt = DateTime.Now;
        readonly HashSet<String> runningSchedules = new HashSet<String>(StringComparer.OrdinalIgnoreCase);
        System.Windows.Forms.Timer schedulerTimer;

        void StartInternalScheduler()
        {
            schedulerTimer = new System.Windows.Forms.Timer { Interval = 60000 };
            schedulerTimer.Tick += (s, e) => { RunDueSchedules(); ClearWebCheckStatus(); RunDueWebCheck(); };
            schedulerTimer.Start();
        }

        // Daily web version check (Tools > Web versions...): report only, unless the automatic
        // download is on; whatever the engine of the update schedules, while LiteDeploy is open
        void RunDueWebCheck()
        {
            UserSettings settings = UserSettings.Current;
            if (String.IsNullOrWhiteSpace(settings.WebCheckTime)) return;
            DateTime? last = WebVersionResults.Load(WebVersionResults.DefaultPath).LastScheduledCheck;
            if (!WebVersionChecker.IsDailyCheckDue(settings.WebCheckTime, last, DateTime.Now, startedAt)) return;
            if (!WebVersionChecker.TryBegin()) return;
            bool autoDownload = settings.WebAutoDownload;
            ShowWebCheckStatus(Localization.F("MainForm.WebCheckStatus", "Daily web check: {0}", Localization.T("WebVersionsForm.Checking", "Checking...")));
            webCheckWatch.Restart();
            WebVersionOperation phase = WebVersionOperation.Check;
            // Status line only (no dialog): the item and its step, elapsed time and time left of the current phase
            IProgress<WebVersionProgress> progress = new Progress<WebVersionProgress>(p =>
            {
                if (IsDisposed || p.Step == WebVersionStep.Done) return;
                if (p.Operation != phase) { phase = p.Operation; webCheckWatch.Restart(); }
                ShowWebCheckStatus(Localization.F("MainForm.WebCheckStatus", "Daily web check: {0}", WebVersionProgressText.StatusText(p, webCheckWatch.Elapsed)));
            });
            new Thread(() =>
            {
                String summary;
                try { summary = WebVersionChecker.RunScheduled(autoDownload, progress); }
                catch (Exception ex) { summary = ex.Message; WebVersionChecker.Log("Daily web check failed: " + ex.Message); }
                finally { WebVersionChecker.End(); }
                AppConsole.WriteLine(summary);
                try
                {
                    if (!IsDisposed && IsHandleCreated)
                        BeginInvoke(new MethodInvoker(() =>
                        {
                            if (IsDisposed) return;
                            ShowWebCheckStatus(summary);
                            webCheckStatusUntil = DateTime.Now.AddMinutes(10);
                        }));
                }
                catch (InvalidOperationException) { }
            }) { IsBackground = true, Name = "Web versions daily check" }.Start();
        }

        // Status bar text of the daily web check (added to the status bar of the grid the first time);
        // the summary stays 10 minutes after the end
        ToolStripStatusLabel webCheckStatus;
        readonly System.Diagnostics.Stopwatch webCheckWatch = new System.Diagnostics.Stopwatch();
        DateTime? webCheckStatusUntil;

        void ShowWebCheckStatus(String text)
        {
            if (webCheckStatus == null)
            {
                webCheckStatus = new ToolStripStatusLabel { TextAlign = ContentAlignment.MiddleRight };
                grid.StatusItems.Add(webCheckStatus);
            }
            webCheckStatusUntil = null;
            webCheckStatus.Text = text;
            webCheckStatus.ToolTipText = text;
        }

        void ClearWebCheckStatus()
        {
            if (webCheckStatus == null || !webCheckStatusUntil.HasValue || DateTime.Now < webCheckStatusUntil.Value) return;
            webCheckStatus.Text = "";
            webCheckStatus.ToolTipText = "";
            webCheckStatusUntil = null;
        }

        void RunDueSchedules()
        {
            if (UserSettings.Current.SchedulerEngine != UpdateSchedules.Internal) return;
            DateTime now = DateTime.Now;
            // Only what became due since LiteDeploy started: an update missed during the night is
            // not started the next morning, during business hours
            foreach (UpdateSchedule s in UpdateSchedules.Load().Items.Where(x => x.IsDue(now, startedAt)))
            {
                lock (runningSchedules)
                {
                    if (!runningSchedules.Add(s.Name)) continue;
                }
                AppConsole.WriteLine(Localization.F("MainForm.ScheduleStarted", "Scheduled update \"{0}\" started.", s.Name));
                UpdateSchedule schedule = s;
                new Thread(() =>
                {
                    try { ScheduleRunner.Run(schedule); }
                    finally { lock (runningSchedules) runningSchedules.Remove(schedule.Name); }
                    AppConsole.WriteLine(Localization.F("MainForm.ScheduleFinished", "Scheduled update \"{0}\" finished (see the logs of Tools > Scheduled Updates).", schedule.Name));
                }) { IsBackground = true, Name = "Schedule " + s.Name }.Start();
            }
        }
    }
}
