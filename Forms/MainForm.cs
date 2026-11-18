// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    MainForm.cs
// Author:  LiteDeploy contributors
// Date:    2025-05-30
// Purpose: Main window: target computers, file or command to run, PsExec
//          options, console. The right-click menu of the computer list has
//          the actions of Software Updates (ComputerActions) on the checked
//          computers, else on the selected one.
// -----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Net;
using System.Threading;
using System.IO;
using System.DirectoryServices;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Xml.Serialization;
using System.Configuration;

namespace LiteDeploy
{
    public partial class MainForm : Form
    {
        // Child windows used by the deployment threads: created with the main window, on the UI thread
        static private ProcessReportsForm processConsole = new ProcessReportsForm();
        static public DeploymentProgressForm prepareWindow = new DeploymentProgressForm();

        // Computer list window: created the first time it is opened, then kept (it keeps its search results)
        static private ComputerListForm listMaker;

        //Static Reference to Start Button
        static Button StartRef = null;

        // Relative working path (if set)
        public String workPath = "";

        public static SplashForm screen;

        public MainForm()
        {            
            InitializeComponent();
            Localization.Apply(this);
            AddComputerActions();
            Theme.Apply(this);

            // Computer counters in the status bar follow the list (they were refreshed by a 100 ms timer)
            selectedComputers.ItemCheck += (s, e) => BeginInvoke(new MethodInvoker(UpdateComputerCounts));

            Hide();
        }

        /// <summary>Total and checked computers, in the status bar. Call after changing the computer list.</summary>
        public void UpdateComputerCounts()
        {
            if (InvokeRequired)
            {
                BeginInvoke(new MethodInvoker(UpdateComputerCounts));
                return;
            }

            totalComps.Text = selectedComputers.Items.Count.ToString();
            selComps.Text = selectedComputers.CheckedItems.Count.ToString();
        }

        private ComputerListForm ComputerList
        {
            get
            {
                if (listMaker == null || listMaker.IsDisposed)
                {
                    listMaker = new ComputerListForm();
                    listMaker.initUseList(selectedComputers);
                }

                return listMaker;
            }
        }


        private void MainForm_Load(object sender, EventArgs e)
        {
            screen = new SplashForm(this);
            screen.ShowDialog();
            screen.Dispose();
            screen = null;

            StartRef = startDeploy;
            MainFormAccess.Init(this);

            LocalNetworkSettings.CheckForLocalDNSServers();
            AppConsole.Init(consoleDisplay, this);         
            username.Text = Environment.UserDomainName + "\\" + Environment.UserName;
            username.Select(username.Text.Length, 0);
            ApplyDeploymentsVisibility();
            priority.SelectedIndex = 2;
            UpdateComputerCounts();
            StartInternalScheduler();
        }        

        private void selectFileToExecute_Click(object sender, EventArgs e)
        {
            openExecutable.ShowDialog();
        }

        private void checkAll_Click(object sender, EventArgs e)
        {
            for (int i = 0; i < selectedComputers.Items.Count; i++)
                selectedComputers.SetItemChecked(i, true);
        }

        private void clearAllChecked_Click(object sender, EventArgs e)
        {
            for (int i = 0; i < selectedComputers.Items.Count; i++)
                selectedComputers.SetItemChecked(i, false);
        }

        private void openExecutable_FileOk(object sender, CancelEventArgs e)
        {
            PConsoleAccess().ClearMSI();
            executable.Text = openExecutable.FileName;            
        }

        // PsExec options of the main window for one target computer (PsExecCommandBuilder)
        private PsExecOptions buildPsExecOptions(String pc)
        {
            PsExecOptions o = new PsExecOptions();
            o.Computer = pc;
            o.Interactive = interactive.Checked;
            o.Session = (int)isessionNum.Value;
            o.DontWait = wait.Checked;
            o.WorkingDirectory = relWorkingPath.Checked ? workPath : null;
            o.CopyExecutable = copy.Checked;
            o.OverwriteExisting = forceCopy.Checked;
            o.ConnectionTimeout = (int)timeOut.Value;
            o.UseCredentials = credentials.Checked;
            o.UserName = username.Text;
            o.Password = password.Text;
            o.RunAsSystem = systemAccount.Checked;
            o.PriorityIndex = priority.SelectedIndex;
            o.Executable = executable.Text;
            o.Arguments = arguments.Text.Replace("%computername%", pc);
            return o;
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

        private void startDeploy_Click(object sender, EventArgs e)
        {
            clearLists();

            // PsExec is optional (software updates use WinRM by default): only the standalone
            // deployments need it, so it is asked for here
            if (PsExecLocator.Path == null && !Program.OfferPsExecDownload())
            {
                MessageBox.Show(Localization.T("MainForm.PsExecNeeded", "The standalone deployments need PsExec: download it in Tools > Manage Tools, or put PsExec.exe next to LiteDeploy.exe."),
                    Localization.T("Program.PsExecMissing.Title", "File Missing"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (executable.Text.Length == 0 && arguments.Text.Length == 0)
            {
                MessageBox.Show(Localization.T("MainForm.NothingToExecute", "You have not selected a file to execute or provided a command."), Localization.T("MainForm.NothingToExecute.Title", "Nothing to Execute"), MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                selectFileToExecute.Focus();
                return;
            }

            if (credentials.Checked)
            {
                if (username.Text.Length < 1)
                {
                    MessageBox.Show(Localization.T("MainForm.UserNameMissing", "Please enter your user name."), Localization.T("MainForm.CredentialsMissing.Title", "Credentials Missing"), MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                    username.Focus();
                    return;
                }

                else if (password.Text.Length < 1)
                {
                    MessageBox.Show(Localization.T("MainForm.PasswordMissing", "Please enter your password."), Localization.T("MainForm.CredentialsMissing.Title", "Credentials Missing"), MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                    password.Focus();
                    return;
                }
            }

            if (selectedComputers.CheckedItems.Count == 0)
            {
                MessageBox.Show(Localization.T("MainForm.NoComputerChecked", "Please check at least one computer from the computer list."), Localization.T("MainForm.NoComputerChecked.Title", "Computer Not Selected"), MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                selectedComputers.Focus();
                return;
            }

            for (int i = 0; i < selectedComputers.CheckedItems.Count; i++)
            {
                String computer = selectedComputers.CheckedItems[i].ToString();
                PsExecOptions options = buildPsExecOptions(computer);
                ProcessManager.deployNameList.Add(computer);
                ProcessManager.AddProcess(PsExecCommandBuilder.BuildArguments(options), PsExecCommandBuilder.DisplayArguments(options));
            }

            bool MSIPrepCancelled = false;
            if (processConsole.MSIDeployment)
            {
                // A form shown with ShowDialog is only hidden when closed: dispose it.
                using (DeploymentPrepForm MSIPrep = new DeploymentPrepForm())
                {
                    DialogResult result = MSIPrep.ShowDialog();

                    if (result == System.Windows.Forms.DialogResult.Cancel)
                    {
                        MSIPrepCancelled = true;
                    }
                }

                processConsole.MSIprogressBar.Maximum = ProcessManager.deployNameList.Count;                
            }

            if (!MSIPrepCancelled)
            {
                processConsole.PrepareForDeployment();
                processConsole.Show();

                ProcessReportsForm.currentExecutableOrCommand = (executable.Text.Length > 0 ? executable.Text + " " + arguments.Text : arguments.Text);
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

        private void clearExec_Click(object sender, EventArgs e)
        {
            executable.Text = "";
            arguments.Text = "";
            processConsole.ClearMSI();
        }

        private void systemAccount_CheckedChanged(object sender, EventArgs e)
        {
            if (systemAccount.Checked)
            {
                credentials.Enabled = false;
                password.Enabled = false;
                username.Enabled = false;
            }

            else
            {
                credentials.Enabled = true;
                password.Enabled = true;
                username.Enabled = true;
            }
        }

        // Saved... : fills the user name and password from a stored credential
        // (Tools > Credentials, shared with LiteTask and the software updates)
        private ContextMenuStrip savedCredentialsMenu;

        private void savedCredentialButton_Click(object sender, EventArgs e)
        {
            if (savedCredentialsMenu == null)
            {
                savedCredentialsMenu = new ContextMenuStrip();
                components.Add(savedCredentialsMenu);
            }
            while (savedCredentialsMenu.Items.Count > 0)
            {
                ToolStripItem old = savedCredentialsMenu.Items[0];
                savedCredentialsMenu.Items.RemoveAt(0);
                old.Dispose();
            }

            List<LiteSuite.StoredCredential> stored = new List<LiteSuite.StoredCredential>();
            try { stored = LiteSuite.CredentialStore.List().ToList(); }
            catch (Exception) { }
            foreach (LiteSuite.StoredCredential c in stored)
            {
                String name = c.Name;
                savedCredentialsMenu.Items.Add(name + " (" + c.UserName + ")", null, (s, a) => UseStoredCredential(name));
            }
            if (stored.Count == 0)
                savedCredentialsMenu.Items.Add(Localization.T("MainForm.NoSavedCredential", "(none)")).Enabled = false;
            savedCredentialsMenu.Items.Add(new ToolStripSeparator());
            savedCredentialsMenu.Items.Add(Localization.T("MainForm.ManageCredentials", "Manage credentials..."), null, credentialsToolStripMenuItem_Click);
            Theme.Apply(savedCredentialsMenu);
            savedCredentialsMenu.Show(savedCredentialButton, 0, savedCredentialButton.Height);
        }

        private void UseStoredCredential(String name)
        {
            LiteSuite.StoredCredential c = null;
            try { c = LiteSuite.CredentialStore.Read(name); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, name, MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            if (c == null) return;
            username.Text = c.UserName;
            password.Text = c.Password;
            credentials.Checked = true;
        }

        private void credentials_CheckedChanged(object sender, EventArgs e)
        {
            if (credentials.Checked)
                systemAccount.Enabled = false;

            else
                systemAccount.Enabled = true;
        }

        private void customListToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ComputerListForm list = ComputerList;
            list.selected.BeginUpdate();
            list.selected.Items.Clear();
            foreach (String o in selectedComputers.Items)
            {
                list.selected.Items.Add(o, o, 0);
            }
            list.selected.EndUpdate();
            list.ShowDialog();
            UpdateComputerCounts();
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
                startDeploy.Enabled = true;
                exitToolStripMenuItem.Enabled = true;
            }
        }

        // Same as "Stop Current Process"
        private void stopPsExecToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ProcessManager.KillCurrentProcess();
        }

        private void exitToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Close();
        }

        private void killCurrentProcessToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ProcessManager.KillCurrentProcess();            
        }

        private void aboutToolStripMenuItem_Click(object sender, EventArgs e)
        {
            using (AboutForm about = new AboutForm())
            {
                about.ShowDialog();
            }
        }

        private void clearConsoleToolStripMenuItem_Click(object sender, EventArgs e)
        {
            AppConsole.Clear();
        }

        private void executable_TextChanged(object sender, EventArgs e)
        {
            if (executable.Text != "")
            {
                if (File.Exists(executable.Text))
                {
                    FileInfo info = new FileInfo(executable.Text);

                    if (info.Extension == ".msi" || info.Extension == ".mst" )
                    {
                        using (MsiOptionsForm MSIWindow = new MsiOptionsForm(openExecutable.FileName))
                        {
                            DialogResult result = MSIWindow.ShowDialog();
                            arguments.Text = MSIWindow.GetCommandString();
                            executable.Text = "C:\\Windows\\System32\\msiexec.exe";
                            wait.Checked = true;
                            copy.Checked = false;
                            forceCopy.Checked = false;
                            interactive.Checked = false;
                            relWorkingPath.Checked = false;
                            PConsoleAccess().MSIStatProductName.Text = MSIWindow.MSIProductName.Text;
                            PConsoleAccess().MSIStatProductVersion.Text = MSIWindow.MSIPVersion.Text;
                        }
                    }
                }
            }
        }

        // Right-click menu on a computer: shared with the process reports (RemoteActions)
        private void pingHost_Click(object sender, EventArgs e) { RemoteActions.Ping(hostName.Text); }
        private void remoteToHost_Click(object sender, EventArgs e) { computerActions.RemoteDesktop(ActionTargets()); }
        private void mangementConsoleToolStripMenuItem_Click(object sender, EventArgs e) { RemoteActions.ManagementConsole(hostName.Text); }
        private void openAdminShareToolStripMenuItem_Click(object sender, EventArgs e) { RemoteActions.OpenAdminShare(hostName.Text, ""); }
        private void publicDesktopToolStripMenuItem1_Click(object sender, EventArgs e) { RemoteActions.OpenAdminShare(hostName.Text, "Users\\Public\\Desktop"); }
        private void publicStartMenuToolStripMenuItem_Click(object sender, EventArgs e) { RemoteActions.OpenAdminShare(hostName.Text, "ProgramData\\Microsoft\\Windows\\Start Menu"); }

        private void selectedComputers_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                if (selectedComputers.Items.Count > 0)
                {
                    // The item under the mouse becomes the selected one (the actions on one computer)
                    int index = selectedComputers.IndexFromPoint(e.Location);
                    if (index >= 0) selectedComputers.SelectedIndex = index;
                    if (selectedComputers.SelectedIndex > -1 || selectedComputers.CheckedItems.Count > 0)
                    {
                        hostName.Text = selectedComputers.SelectedIndex > -1 ? selectedComputers.Items[selectedComputers.SelectedIndex].ToString() : "";
                        PrepareComputerActions();
                        contextMenu.Show(MousePosition);
                    }
                }
            }
        }

        private void aboutToolStripMenuItem_Click_1(object sender, EventArgs e)
        {
            aboutToolStripMenuItem_Click(sender, e);
        }

        private void changeLogToolStripMenuItem_Click(object sender, EventArgs e)
        {
            using (ChangeLogForm clog = new ChangeLogForm())
            {
                clog.ShowDialog();
            }
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

        /// <summary>Before the update closes LiteDeploy: confirmation when deployments or scheduled updates are running.</summary>
        internal bool ConfirmCloseForUpdate()
        {
            bool schedules;
            lock (runningSchedules) schedules = runningSchedules.Count > 0;
            if (!ProcessManager.processing && !schedules) return true;
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

        private void pingAllToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ShowPingAll(false);
        }

        private void pingAllCheckedToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ShowPingAll(true);
        }

        // Software Updates window: opened once and kept (non-modal)
        static private SoftwareUpdatesForm updatesWindow;

        // Software updates: every computer of Active Directory and of the list; filtered to the checked
        // computers when some are checked (the filter can be removed in the window)
        private void softwareUpdatesToolStripMenuItem_Click(object sender, EventArgs e)
        {
            List<String> listed = selectedComputers.Items.Cast<object>().Select(o => o.ToString()).ToList();
            List<String> checkedNames = selectedComputers.CheckedItems.Cast<object>().Select(o => o.ToString()).ToList();
            if (updatesWindow == null || updatesWindow.IsDisposed)
            {
                updatesWindow = new SoftwareUpdatesForm(listed, checkedNames);
                updatesWindow.Show();
                return;
            }
            updatesWindow.AddListedComputers(listed);
            if (checkedNames.Count > 0) updatesWindow.SetSelectionFilter(checkedNames);
            if (updatesWindow.WindowState == FormWindowState.Minimized) updatesWindow.WindowState = FormWindowState.Normal;
            updatesWindow.Activate();
        }

        // Web versions of the products of the shared cache (module instances with a WingetId)
        private void webVersionsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            using (WebVersionsForm webVersions = new WebVersionsForm())
            {
                webVersions.ShowDialog(this);
            }
        }

        private void schedulesToolStripMenuItem_Click(object sender, EventArgs e)
        {
            IEnumerable<object> source = selectedComputers.CheckedItems.Count > 0
                ? selectedComputers.CheckedItems.Cast<object>()
                : selectedComputers.Items.Cast<object>();
            using (SchedulesForm schedules = new SchedulesForm(source.Select(o => o.ToString())))
            {
                schedules.ShowDialog(this);
            }
        }

        // ---- Internal scheduler: the schedules due while LiteDeploy is open --------------

        readonly DateTime startedAt = DateTime.Now;
        readonly HashSet<String> runningSchedules = new HashSet<String>(StringComparer.OrdinalIgnoreCase);
        System.Windows.Forms.Timer schedulerTimer;

        void StartInternalScheduler()
        {
            schedulerTimer = new System.Windows.Forms.Timer { Interval = 60000 };
            schedulerTimer.Tick += (s, e) => { RunDueSchedules(); RunDueWebCheck(); };
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
            new Thread(() =>
            {
                String summary;
                try { summary = WebVersionChecker.RunScheduled(autoDownload); }
                catch (Exception ex) { summary = ex.Message; WebVersionChecker.Log("Daily web check failed: " + ex.Message); }
                finally { WebVersionChecker.End(); }
                AppConsole.WriteLine(summary);
            }) { IsBackground = true, Name = "Web versions daily check" }.Start();
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
                MessageBox.Show(Localization.T("MainForm.LitePMMissing", "LitePM is not installed: install it in Tools > Manage Tools."), "LitePM", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            try { ToolManager.Open(litePM); }
            catch (Exception ex) { MessageBox.Show(ex.Message, "LitePM", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        // Ping window: created on first use, then reused. Creating one per use leaked about
        // three GDI objects per ping run (fonts and icon of the designer), measured by the soak test.
        private PingAllForm pingAllWindow;

        private void ShowPingAll(bool checkedOnly)
        {
            if (pingAllWindow == null)
                pingAllWindow = new PingAllForm();
            pingAllWindow.firstInit = true;
            pingAllWindow.loadSelectedComputers(checkedOnly);
            pingAllWindow.ShowDialog();

            UpdateComputerCounts();
        }

        private void restartToolStripMenuItem_Click(object sender, EventArgs e)
        {
            PConsoleAccess().ClearMSI();
            SetOpsForRestartShutdownCmd();
            arguments.Text = "shutdown -r -t 0";
            MessageBox.Show(Localization.T("MainForm.CommandReady", "Click \"Start Deploy\" to execute command."), Localization.T("MainForm.RestartReady.Title", "Restart Command Ready"), MessageBoxButtons.OK, MessageBoxIcon.Information);
            startDeploy.Focus();
        }

        private void shutdownToolStripMenuItem_Click(object sender, EventArgs e)
        {
            PConsoleAccess().ClearMSI();
            SetOpsForRestartShutdownCmd();
            arguments.Text = "shutdown -s -t 0";
            MessageBox.Show(Localization.T("MainForm.CommandReady", "Click \"Start Deploy\" to execute command."), Localization.T("MainForm.ShutdownReady.Title", "Shutdown Command Ready"), MessageBoxButtons.OK, MessageBoxIcon.Information);
            startDeploy.Focus();
        }

        private void SetOpsForRestartShutdownCmd()
        {
            PConsoleAccess().ClearMSI();
            credentials.Checked = false;
            systemAccount.Checked = true;
            timeOut.Value = 2;
            wait.Checked = true;
            copy.Checked = false;
            forceCopy.Checked = false;
            executable.Clear();
        }


        private void relWorkingPath_CheckedChanged(object sender, EventArgs e)
        {
            if (relWorkingPath.Checked)
            {
                using (WorkingDirectoryForm dialog = new WorkingDirectoryForm(this, workPath))
                {
                    dialog.ShowDialog();
                }
            }
        }

        private void interactive_CheckedChanged(object sender, EventArgs e)
        {
            if (interactive.Checked)
                MessageBox.Show(Localization.T("MainForm.InteractiveAccount", "Interactive process requires a local account on the remote machine. Make sure to use the proper credentials."),
                                Localization.T("MainForm.InteractiveAccount.Title", "Interactive Process"), MessageBoxButtons.OK, MessageBoxIcon.Information);

        }

        private delegate void DisableStartDel();
        public void DisableStart()
        {
            if (StartRef.InvokeRequired)
            {
                DisableStartDel a = new DisableStartDel(DisableStart);
                Invoke(a);
            }

            else
            {
                StartRef.Enabled = false;
            }
        }

        private delegate void EnableStartDel();
        public void EnableStart()
        {
            if (StartRef.InvokeRequired)
            {
                EnableStartDel a = new EnableStartDel(EnableStart);
                Invoke(a);
            }

            else
            {
                StartRef.Enabled = true;
            }
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (ProcessManager.processing && !closingForUpdate)
            {
                DialogResult result = MessageBox.Show(Localization.T("MainForm.ConfirmClose", "A deployment is currently in progress. Are you sure you want to close the application?"), Localization.T("MainForm.ConfirmClose.Title", "Confirm Close"), MessageBoxButtons.YesNo, MessageBoxIcon.Question);

                if (result == System.Windows.Forms.DialogResult.No)
                {
                    e.Cancel = true;
                    return;
                }
            }

            // The Software Updates window closes with LiteDeploy: its work stops (after a question)
            if (updatesWindow != null && !updatesWindow.IsDisposed)
            {
                if (updatesWindow.Busy && !closingForUpdate
                    && MessageBox.Show(this, Localization.T("SoftwareUpdatesForm.ConfirmClose", "Work is in progress. Stop it and close?"),
                        Localization.T("MainForm.ConfirmClose.Title", "Confirm Close"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                {
                    e.Cancel = true;
                    return;
                }
                updatesWindow.CloseWithoutQuestion();
            }

            ProcessManager.KillAll();
            ActiveDirectoryCache.StopUpdate();
        }

        private void removeToolStripMenuItem_Click(object sender, EventArgs e)
        {
            selectedComputers.Items.RemoveAt(selectedComputers.SelectedIndex);
            UpdateComputerCounts();
        }

        private void copyToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Clipboard.SetText(selectedComputers.Items[selectedComputers.SelectedIndex].ToString());
        }

        private void ListEditButton_Click( object sender, EventArgs e )
        {
            customListToolStripMenuItem_Click( sender, e );
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
                Invoke(a);
            }

            else
            {
                return processConsole.Visible;
            }

            return false;
        }

        public ProcessReportsForm PConsoleAccess()
        {
            return processConsole;
        }

        private void optionsToolStripMenuItem1_Click(object sender, EventArgs e)
        {
            using (OptionsForm SetWin = new OptionsForm())
            {
                SetWin.ShowDialog();
            }
            ApplyDeploymentsVisibility();
        }

        // ---- Computer actions of the right-click menu (shared with Software Updates) ------------
        // They apply to the checked computers, else to the selected one; the first items (ping,
        // management console, admin share, copy, remove) stay on the selected computer.

        private ComputerActions computerActions;
        private ToolStripMenuItem actionTargetsItem, cardActionItem, teamViewerActionItem, commandPromptActionItem, quickCommandsActionItem,
            wakeActionItem, logsActionItem, pauseActionItem, resumeActionItem, nightOnlyActionItem, nightOnlyClearActionItem, royalTsActionItem;

        private void AddComputerActions()
        {
            computerActions = new ComputerActions(new MainActionHost(this));
            // Same texts as the Software Updates window
            actionTargetsItem = new ToolStripMenuItem { Enabled = false };
            cardActionItem = ActionItem(Localization.T("SoftwareUpdatesForm.cardItem.Text", "Computer card..."), () => computerActions.ShowCards(ActionTargets()));
            teamViewerActionItem = ActionItem(Localization.T("SoftwareUpdatesForm.teamViewerItem.Text", "TeamViewer ID..."), () => computerActions.ShowTeamViewer(ActionTargets()));
            commandPromptActionItem = ActionItem(Localization.T("SoftwareUpdatesForm.commandPromptItem.Text", "Command prompt on the computer"), () => computerActions.CommandPrompt(ActionTargets()));
            commandPromptActionItem.ToolTipText = Localization.T("SoftwareUpdatesForm.commandPromptItem.ToolTipText",
                "A console attached to the computer: PsExec (command prompt as SYSTEM) or WinRM (PowerShell session), as chosen in App > Options");
            quickCommandsActionItem = new ToolStripMenuItem(Localization.T("SoftwareUpdatesForm.quickCommandsItem.Text", "Quick commands"));
            wakeActionItem = ActionItem(Localization.T("SoftwareUpdatesForm.wakeItem.Text", "Wake up (Wake-on-LAN)"), () => computerActions.Wake(ActionTargets()));
            logsActionItem = ActionItem(Localization.T("SoftwareUpdatesForm.logsItem.Text", "Get the logs"), () => computerActions.FetchLogs(ActionTargets()));
            pauseActionItem = ActionItem(Localization.T("SoftwareUpdatesForm.pauseItem.Text", "Pause these computers..."), () => computerActions.Pause(ActionTargets()));
            resumeActionItem = ActionItem(Localization.T("SoftwareUpdatesForm.resumeItem.Text", "Resume"), () => computerActions.Resume(ActionTargets()));
            nightOnlyActionItem = ActionItem(Localization.T("SoftwareUpdatesForm.nightOnlyItem.Text", "Update outside business hours only"), () => computerActions.SetNightOnly(ActionTargets(), true));
            nightOnlyClearActionItem = ActionItem(Localization.T("SoftwareUpdatesForm.nightOnlyClearItem.Text", "Allow updates during business hours"), () => computerActions.SetNightOnly(ActionTargets(), false));
            royalTsActionItem = ActionItem(Localization.T("SoftwareUpdatesForm.royalTsItem.Text", "Export to Royal TS..."), () => computerActions.ExportRoyalTs(ExportTargets()));
            royalTsActionItem.ToolTipText = Localization.T("MainForm.RoyalTsTip", "Remote Desktop connections of the checked computers, else of every computer of the list (CSV or RoyalJSON)");

            // After "Open Admin Share", before "Copy" and "Remove"
            int at = contextMenu.Items.IndexOf(openAdminShareToolStripMenuItem) + 1;
            contextMenu.Items.Insert(at, new ToolStripSeparator());
            contextMenu.Items.Insert(at + 1, actionTargetsItem);
            ToolStripItem[] items =
            {
                cardActionItem, teamViewerActionItem, commandPromptActionItem, quickCommandsActionItem, wakeActionItem, logsActionItem,
                new ToolStripSeparator(), pauseActionItem, resumeActionItem, nightOnlyActionItem, nightOnlyClearActionItem,
                new ToolStripSeparator(), royalTsActionItem
            };
            for (int i = 0; i < items.Length; i++) contextMenu.Items.Insert(at + 2 + i, items[i]);
        }

        private static ToolStripMenuItem ActionItem(String text, Action click)
        {
            ToolStripMenuItem item = new ToolStripMenuItem(text);
            item.Click += (s, e) => click();
            return item;
        }

        /// <summary>The computers the shared actions apply to: the checked ones, else the selected one.</summary>
        private List<String> ActionTargets()
        {
            return ComputerActions.Targets(selectedComputers.CheckedItems.Cast<object>().Select(o => o.ToString()),
                selectedComputers.SelectedIndex > -1 ? new[] { selectedComputers.Items[selectedComputers.SelectedIndex].ToString() } : new String[0]);
        }

        /// <summary>Export to Royal TS: the checked computers, else every computer of the list.</summary>
        private List<String> ExportTargets()
        {
            return ComputerActions.Targets(selectedComputers.CheckedItems.Cast<object>().Select(o => o.ToString()),
                selectedComputers.Items.Cast<object>().Select(o => o.ToString()));
        }

        private void PrepareComputerActions()
        {
            List<String> targets = ActionTargets();
            bool one = selectedComputers.SelectedIndex > -1;
            bool idle = updatesWindow == null || updatesWindow.IsDisposed || !updatesWindow.Busy;
            pingHost.Enabled = mangementConsoleToolStripMenuItem.Enabled = openAdminShareToolStripMenuItem.Enabled = one;
            copyToolStripMenuItem.Enabled = removeToolStripMenuItem.Enabled = one;
            actionTargetsItem.Text = selectedComputers.CheckedItems.Count > 0
                ? Localization.F("SoftwareUpdatesForm.TargetsChecked", "{0} checked computer(s)", targets.Count)
                : Localization.F("SoftwareUpdatesForm.TargetsSelected", "{0} selected computer(s)", targets.Count);
            bool any = targets.Count > 0;
            remoteToHost.Enabled = cardActionItem.Enabled = teamViewerActionItem.Enabled = commandPromptActionItem.Enabled = any;
            quickCommandsActionItem.Enabled = logsActionItem.Enabled = any;
            computerActions.FillQuickCommands(quickCommandsActionItem, ActionTargets);
            wakeActionItem.Enabled = any && computerActions.CanWake(targets);
            pauseActionItem.Enabled = idle && targets.Any(c => !computerActions.IsPaused(c));
            resumeActionItem.Enabled = idle && targets.Any(computerActions.IsPaused);
            nightOnlyActionItem.Enabled = idle && targets.Any(c => !computerActions.IsNightOnly(c));
            nightOnlyClearActionItem.Enabled = idle && targets.Any(computerActions.IsNightOnly);
            royalTsActionItem.Enabled = selectedComputers.Items.Count > 0;
            Theme.Apply(contextMenu);
        }

        /// <summary>
        /// The main window as the host of the shared actions: the saved state is the one of the Software
        /// Updates window while it is open (one store in memory), else updates.xml read and written here.
        /// </summary>
        private sealed class MainActionHost : IComputerActionHost
        {
            static readonly Object fileLock = new Object();
            readonly MainForm form;

            public MainActionHost(MainForm form)
            {
                this.form = form;
            }

            public Form Owner { get { return form; } }

            public bool Idle { get { return updatesWindow == null || updatesWindow.IsDisposed || !updatesWindow.Busy; } }

            public void WithStore(Action<UpdateStateStore> action, bool save)
            {
                SoftwareUpdatesForm window = updatesWindow;
                if (window != null && !window.IsDisposed)
                {
                    window.WithStore(action, save);
                    return;
                }
                lock (fileLock)
                {
                    UpdateStateStore store = UpdateStateStore.Load(UpdateStateStore.DefaultPath);
                    action(store);
                    if (save) store.SaveIfChanged(UpdateStateStore.DefaultPath);
                }
            }

            public ComputerRow Row(String computer)
            {
                AdComputer ad = null;
                try { ad = ActiveDirectoryCache.Inventory.ToList().FirstOrDefault(c => c != null && ComputerName.Same(c.Name, computer)); }
                catch (Exception) { }   // inventory being refreshed
                ComputerUpdateState state = null;
                WithStore(store => state = store.Find(computer), false);
                return ComputerRow.Build(computer, ad, state, null, null);
            }

            public void SetStatus(String text)
            {
                if (text != null) AppConsole.WriteLine(text);
            }

            public void StoreChanged()
            {
                SoftwareUpdatesForm window = updatesWindow;
                if (window != null) window.StoreChanged();
            }
        }

        // Standalone deployments (App > Options, hidden by default): the deployment group and the
        // restart/shutdown commands that fill it. Hidden, the console takes the freed space.
        private Rectangle consoleBounds, consoleLabelBounds;

        private void ApplyDeploymentsVisibility()
        {
            bool show = UserSettings.Current.ShowStandaloneDeployments;
            if (consoleBounds.IsEmpty)
            {
                consoleBounds = consoleDisplay.Bounds;
                consoleLabelBounds = label3.Bounds;
            }
            groupBox4.Visible = show;
            restartToolStripMenuItem.Visible = show;
            shutdownToolStripMenuItem.Visible = show;
            toolStripSeparator3.Visible = show;
            if (show)
            {
                label3.Bounds = consoleLabelBounds;
                consoleDisplay.Bounds = consoleBounds;
            }
            else
            {
                int top = groupBox4.Top;
                label3.Location = new Point(consoleLabelBounds.X, top + 3);
                consoleDisplay.SetBounds(consoleBounds.X, top + 21, consoleBounds.Width, consoleBounds.Bottom - (top + 21));
            }
        }

                  
    }
}
