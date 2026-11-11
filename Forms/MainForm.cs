// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    MainForm.cs
// Author:  LiteDeploy contributors
// Date:    2025-05-30
// Purpose: Main window: target computers, file or command to run, PsExec
//          options, console.
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
            priority.SelectedIndex = 2;
            UpdateComputerCounts();
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
        private void remoteToHost_Click(object sender, EventArgs e) { RemoteActions.RemoteDesktop(hostName.Text); }
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
                    if (selectedComputers.SelectedIndex > -1)
                    {
                        hostName.Text = selectedComputers.Items[selectedComputers.SelectedIndex].ToString();
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

        private void pingAllToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ShowPingAll(false);
        }

        private void pingAllCheckedToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ShowPingAll(true);
        }

        // Ping window: created for each use and disposed when closed
        private void ShowPingAll(bool checkedOnly)
        {
            using (PingAllForm pingAllWindow = new PingAllForm())
            {
                pingAllWindow.loadSelectedComputers(checkedOnly);
                pingAllWindow.firstInit = true;
                pingAllWindow.ShowDialog();
            }

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
            if (ProcessManager.processing)
            {
                DialogResult result = MessageBox.Show(Localization.T("MainForm.ConfirmClose", "A deployment is currently in progress. Are you sure you want to close the application?"), Localization.T("MainForm.ConfirmClose.Title", "Confirm Close"), MessageBoxButtons.YesNo, MessageBoxIcon.Question);

                if (result == System.Windows.Forms.DialogResult.No)
                {               
                    e.Cancel = true;
                    return;
                }
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
        }

                  
    }
}
