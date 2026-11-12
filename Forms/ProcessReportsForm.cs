// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    ProcessReportsForm.cs
// Author:  LiteDeploy contributors
// Date:    2025-05-30
// Purpose: Process reports: PsExec output per computer, parsed log, MSI
//          deployment status.
// -----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.IO;
using System.Threading;
using System.Net.NetworkInformation;
using System.Diagnostics;

namespace LiteDeploy
{
    public partial class ProcessReportsForm : Form
    {
        /// <summary>Host name used for the lines about the whole deployment (not a computer).</summary>
        public const String DeploymentHost = "Deployment";

        /// <summary>Line sent by ProcessManager when the user stops the current process (not translated: it is matched).</summary>
        public const String ManualAbortSignal = "Process manually aborted by user.";

        static String DeploymentHostDisplay
        {
            get { return Localization.T("ProcessReportsForm.DeploymentHost", "Deployment"); }
        }
        public static List<LogEntry> logEntries = new List<LogEntry>();
        public static String currentExecutableOrCommand = "";
        private ListViewColumnSorter lvwColumnSorter;

        // Stat Objects
        private int MSIOK = 0;
        private int MSIWARN = 0;
        private int MSIERR = 0;
        private int MSIWAIT = 0;
        private int MSIERRCODE = 0;
        private Object StatSetLock;

        // MSI Logging
        public String MSILoggingDir = "";
        public String MSIProductName = "";
        public bool MSIDeployment = false;
        private List<String> MSILogFiles;
        public bool MSIDeploymentStillRunning = false;
        private bool pollingActive = false;
        private Object pollingLock;

        public ProcessReportsForm()
        {
            InitializeComponent();
            Localization.Apply(this);
            Theme.Apply(this);
            lvwColumnSorter = new ListViewColumnSorter();
            log.ListViewItemSorter = lvwColumnSorter;
            MSILogFiles = new List<String>();
            pollingLock = new Object();
            StatSetLock = new Object();
        }

        public void ClearMSI()
        {
            //MSILoggingDir = "";
            MSIDeployment = false;
            MSILogFiles.Clear();
            ActualMSILog.Text = "";
            LogFileLocation.Text = "";
            MSIOK = 0;
            MSIWARN = 0;
            MSIERR = 0;
            MSIWAIT = 0;
            MSICountTable.Items[0].SubItems[1].Text = "0";
            MSICountTable.Items[1].SubItems[1].Text = "0";
            MSICountTable.Items[2].SubItems[1].Text = "0";
            MSIStatProductName.Text = "";
            MSIStatProductVersion.Text = "";            
        }

        private void processList_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (processList.SelectedIndex > -1 && processList.SelectedIndex < ProcessManager.outputList.Count)
            {
                try
                {
                    console.Text = ProcessManager.outputList[processList.SelectedIndex];
                }

                catch
                {
                    MessageBox.Show(Localization.T("ProcessReportsForm.OutputReadError", "Error opening the output file."), Localization.T("ProcessReportsForm.OutputReadError.Title", "File Read Error"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        public void clearSelect_Click(object sender, EventArgs e)
        {
            processList.SelectedIndex = -1;
            console.Text = "";
        }

        private delegate void AddProcessToListDel(String s);
        public void AddProcessToList(String s)
        {
            if (processList.InvokeRequired)
            {
                AddProcessToListDel a = new AddProcessToListDel(AddProcessToList);
                object[] args = new object[] { s };
                Invoke(a, args);
            }

            else
            {
                processList.Items.Add(s);
            }
        }

        private void ProcessReportsForm_VisibleChanged(object sender, EventArgs e)
        {
        }

        private void MSIProcessStarted(String HostName)
        {
            MSIStatSet(HostName, Localization.T("ProcessReportsForm.MsiWaiting", "Process has been started. Waiting for process to finish."), "WAITING");

            if (!MSILogFiles.Contains(MSILoggingDir + "\\" + HostName) )
                MSILogFiles.Add(MSILoggingDir.Replace("%computername%", HostName) + "\\" + HostName);
        }

        private void MSIProcessCouldNotBeStarted(String HostName)
        {
            MSIStatSet(HostName, Localization.T("ProcessReportsForm.MsiNotStarted", "Process could not be started."), "FAIL");
        }

        private delegate void AddLogEntryDel(String Host, String S);
        public void AddLogEntry(String Host, String S)
        {
            if (log.InvokeRequired)
            {
                AddLogEntryDel a = new AddLogEntryDel(AddLogEntry);
                object[] args = new object[] { Host, S };
                Invoke(a, args);
            }

            else
            {
                List<LogEntry> queueToAdd = new List<LogEntry>();
                StringReader reader = new StringReader(S);
                string currentLine = reader.ReadLine();
                while (currentLine != null)
                {
                    if (Host == DeploymentHost)
                    {
                        queueToAdd.Add(new LogEntry(DateTime.Now, Host, S, S, LogEntry.IconDeployment));
                    }

                    else if (currentLine.Contains("started on " + Host))
                    {
                        queueToAdd.Add(new LogEntry(DateTime.Now, Host, S, Localization.F("ProcessReportsForm.Started", "{0} started with process ID {1}", MSIDeployment ? "MSI - " + MSIProductName : currentExecutableOrCommand, currentLine.Substring(currentLine.LastIndexOf(' ') + 1, currentLine.Length - currentLine.LastIndexOf(' ') - 1)), LogEntry.IconStarted));

                        if (MSIDeployment)
                            MSIProcessStarted(Host);
                    }

                    else if (currentLine.Contains("Timeout accessing"))
                    {
                        queueToAdd.Add(new LogEntry(DateTime.Now, Host, S, Localization.T("ProcessReportsForm.Timeout", "Connection timed out.")));

                        if (MSIDeployment)
                            MSIProcessCouldNotBeStarted(Host);
                    }

                    else if (currentLine.Contains("is an incompatible version"))
                    {
                        queueToAdd.Add(new LogEntry(DateTime.Now, Host, S, Localization.F("ProcessReportsForm.IncompatiblePsExec", "Incompatible PsExec version. {0} {1}", reader.ReadLine(), reader.ReadLine())));
                    }

                    else if (currentLine.Contains("Couldn't access " + Host))
                    {
                        queueToAdd.Add(new LogEntry(DateTime.Now, Host, S, Localization.T("ProcessReportsForm.Unreachable", "Host is inaccessible. Could not start PsExec service on remote computer."), LogEntry.IconUnreachable));

                        if (MSIDeployment)
                            MSIProcessCouldNotBeStarted(Host);
                    }

                    else if (currentLine.Contains("unknown user name or bad password"))
                    {
                        queueToAdd.Add(new LogEntry(DateTime.Now, Host, S, Localization.T("ProcessReportsForm.BadPassword", "Authentication failed. Invalid username or password."), LogEntry.IconAuthentication));

                        if (MSIDeployment)
                            MSIProcessCouldNotBeStarted(Host);
                    }

                    else if (currentLine == ManualAbortSignal)
                    {
                        queueToAdd.Add(new LogEntry(DateTime.Now, Host, S, Localization.T("ProcessReportsForm.ManualAbort", "Process manually aborted by user.")));
                    }

                    else if (currentLine.Contains("Logon failure: account currently disabled."))
                    {
                        queueToAdd.Add(new LogEntry(DateTime.Now, Host, S, Localization.T("ProcessReportsForm.AccountDisabled", "Authentication failed. The user account is disabled."), LogEntry.IconAuthentication));

                        if (MSIDeployment)
                            MSIProcessCouldNotBeStarted(Host);
                    }

                    else if (currentLine == "Access is denied.")
                    {
                        queueToAdd.Add(new LogEntry(DateTime.Now, Host, S, Localization.T("ProcessReportsForm.AccessDenied", "Unable to establish connection. Access was denied.")));

                        if (MSIDeployment)
                            MSIProcessCouldNotBeStarted(Host);
                    }

                    else if (currentLine.Contains("cannot find the path specified"))
                    {
                        queueToAdd.Add(new LogEntry(DateTime.Now, Host, S, Localization.T("ProcessReportsForm.PathNotFound", "Process could not be started. Specified path not found.")));

                        if (MSIDeployment)
                            MSIProcessCouldNotBeStarted(Host);
                    }

                    currentLine = reader.ReadLine();
                }

                if (queueToAdd.Count == 0)
                {
                    queueToAdd.Add(new LogEntry(DateTime.Now, Host, S, Localization.T("ProcessReportsForm.NoLogData", "No log data found")));

                    if (MSIDeployment)
                        MSIProcessCouldNotBeStarted(Host);
                }

                UpdateLog(queueToAdd);
            }
        }

        private void UpdateLog(List<LogEntry> queueToAdd)
        {
            for (int i = queueToAdd.Count - 1; i >= 0; i--)
            {
                LogEntry e = queueToAdd[i];
                log.Items.Add( e.LoggedTime.ToLongTimeString() + " " + e.LoggedTime.ToShortDateString());
                int count = log.Items.Count;
                log.Items[count -1].SubItems.Add(e.HostIP == DeploymentHost ? DeploymentHostDisplay : e.HostIP);
                log.Items[count - 1].SubItems.Add( e.LogLineMessage );
                log.Items[count - 1].StateImageIndex = e.Icon;
            }
        }

        private void ProcessReportsForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            e.Cancel = true;
            Hide();
        }

        private void log_ColumnClick(object sender, ColumnClickEventArgs e)
        {
            if (!ProcessManager.processing)
            {
                // Determine if clicked column is already the column that is being sorted.
                if (e.Column == lvwColumnSorter.SortColumn)
                {
                    // Reverse the current sort direction for this column.
                    if (lvwColumnSorter.Order == SortOrder.Ascending)
                    {
                        lvwColumnSorter.Order = SortOrder.Descending;
                    }
                    else
                    {
                        lvwColumnSorter.Order = SortOrder.Ascending;
                    }
                }
                else
                {
                    // Set the column number that is to be sorted; default to ascending.
                    lvwColumnSorter.SortColumn = e.Column;
                    lvwColumnSorter.Order = SortOrder.Ascending;
                }

                // Perform the sort with these new sort options.
                log.Sort();
            }

            else
            {
                MessageBox.Show(Localization.T("ProcessReportsForm.SortBusy", "A deployment is currently in progress. Sorting will be available once the deployment is complete."),
                                 Localization.T("ProcessReportsForm.SortBusy.Title", "Deployment in Progress"), MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        public void PrepareForDeployment()
        {
            logEntries.Clear();
            log.Items.Clear();
            LogFileLocation.Text = "";
            MSIprogressBar.Value = 0;
            MSIDeploymentPercentage.Text = "0%";
            ActualMSILog.Text = "";
            MSILogList.Items.Clear();
            MSILogFiles.Clear();
            processList.Items.Clear();
            console.Clear();

            if (MSIDeployment)
            {
                for (int i = 0; i < ProcessManager.deployNameList.Count; i++)
                {
                    String Name = ProcessManager.deployNameList[i];
                    MSILogList.Items.Add(Name, Name, -1);
                    MSILogList.Items[i].SubItems.Add("Waiting for process to be started.");
                    MSILogList.Items[i].StateImageIndex = imageList.Images.IndexOfKey("CLIENT DISC");
                }

                MSIprogressBar.Maximum = ProcessManager.deployNameList.Count;
                tabControl1.SelectedIndex = 2;
            }            

            else
            {
                tabControl1.SelectedIndex = 0;
            }

            // Set the column number that is to be sorted; default to ascending.
            lvwColumnSorter.SortColumn = 0;
            lvwColumnSorter.Order = SortOrder.Ascending;

            // Perform the sort with these new sort options.
            log.Sort();            
        }

        private void msiLogPollingTimer_Tick(object sender, EventArgs e)
        {
            if (!ProcessManager.processing && MSILogFiles.Count == 0)
            {
                msiLogPollingTimer.Enabled = false;
                AddLogEntry(DeploymentHost, Localization.T("ProcessReportsForm.MsiLoggingStopped", "MSI logging has stopped."));
                return;
            }

            if (!pollingActive)
            {
                if (MSILogFiles.Count == 0)
                    return;

                Thread T = new Thread(new ThreadStart(PollMSILogs));
                T.Start();
            }
        }

        private delegate void MSIStatSetDel(String HostName, String stat, String statImgKey);
        private void MSIStatSet(String HostName, String stat, String statImgKey)
        {

            if (MSILogList.InvokeRequired)
            {
                MSIStatSetDel a = new MSIStatSetDel(MSIStatSet);
                object[] args = new object[] { HostName, stat, statImgKey };
                Invoke(a, args);
            }

            else
            {

                int i = 0;

                do
                {
                    i = MSILogList.Items.IndexOfKey(HostName);

                } while (i < 0);

                lock (StatSetLock)
                {
                    MSILogList.Items[i].SubItems[1].Text = stat;
                    MSILogList.Items[i].StateImageIndex = imageList.Images.IndexOfKey(statImgKey);

                    MSIOK = 0;
                    MSIWARN = 0;
                    MSIERR = 0;
                    MSIWAIT = 0;
                    MSIERRCODE = 0;

                    for (int j = 0; j < MSILogList.Items.Count; j++)
                    {
                        int Index = MSILogList.Items[j].StateImageIndex;

                        if (Index == 0)
                            MSIOK++;
                        else if (Index == 1)
                            MSIERR++;
                        else if (Index == 2)
                            MSIWARN++;
                        else if (Index == 8)
                            MSIWAIT++;
                        else if (Index == 9)
                            MSIERRCODE++;
                    }

                    MSICountTable.Items[0].SubItems[1].Text = MSIOK.ToString();
                    MSICountTable.Items[1].SubItems[1].Text = MSIWARN.ToString();
                    MSICountTable.Items[2].SubItems[1].Text = MSIERR.ToString();
                    MSICountTable.Items[3].SubItems[1].Text = MSIWAIT.ToString();
                    MSICountTable.Items[4].SubItems[1].Text = MSIERRCODE.ToString();

                    if (MSIprogressBar.Value < MSIprogressBar.Maximum)
                    {
                        MSIprogressBar.Value = MSIOK + MSIWARN + MSIERR + MSIERRCODE;
                        MSIDeploymentPercentage.Text = ((int)(Math.Round(((double)MSIprogressBar.Value / (double)MSIprogressBar.Maximum) * 100, 0))).ToString() + "%";
                    }
                }
            }
        }

        private delegate void PollDel();
        private void PollMSILogs()
        {
            lock (pollingLock)
            {
                pollingActive = true;

                for (int i = 0; i < MSILogFiles.Count; i++)
                {
                    if (File.Exists(MSILogFiles[i] + ".txt"))
                    {
                        try
                        {
                            String thisLogHostName = MSILogFiles[i].Substring(MSILogFiles[i].LastIndexOf("\\") + 1);
                            String thisLogFile = MSILogFiles[i] + ".txt";

                            bool indeterminate = true;

                            // using: an exception while parsing used to leave the log
                            // file open until the next garbage collection (every 5 s poll).
                            using (FileStream logFile = new FileStream(thisLogFile, FileMode.Open, FileAccess.Read))
                            using (StreamReader reader = new StreamReader(logFile))
                            {
                                while (!reader.EndOfStream)
                                {
                                    String line = reader.ReadLine();

                                    if (line.Contains("Product") && line.Contains(MSIProductName))
                                    {
                                        if (line.Contains("Success") || line.Contains("success") )
                                        {
                                            MSIStatSet(thisLogHostName, Localization.T("ProcessReportsForm.MsiInstalled", "Package was successfully installed."), "OK");
                                        }

                                        else if (line.Contains("fail") || line.Contains("Fail") || line.Contains("error") || line.Contains("Error") || line.Contains("Disk full: Out of disk space"))
                                        {
                                            bool errorCode = false;

                                            // Check for specifi error codes
                                            while (!reader.EndOfStream)
                                            {
                                                line = reader.ReadLine();

                                                if (line.Contains("Windows Installer reconfigured") && line.Contains("Reconfiguration success or error status: 1638"))
                                                {
                                                    MSIStatSet(thisLogHostName, Localization.T("ProcessReportsForm.MsiOtherVersion", "Another version of this product is already installed. Installation cannot continue."), "ERROR");
                                                    errorCode = true;
                                                    break;
                                                }
                                            }

                                            if ( !errorCode)
                                                MSIStatSet(thisLogHostName, line.Contains("Disk full: Out of disk space") ? Localization.T("ProcessReportsForm.MsiDiskFull", "Package installation failed. Disk out of space.") : Localization.T("ProcessReportsForm.MsiFailed", "Package installation failed."), "ERROR");
                                        }

                                        MSILogFiles.Remove(thisLogFile.Replace(".txt", ""));
                                        i--;
                                        indeterminate = false;
                                        break;
                                    }

                                    else if (line.Contains("Failed to connect to server"))
                                    {
                                        MSIStatSet(thisLogHostName, Localization.T("ProcessReportsForm.MsiFailed", "Package installation failed."), "ERROR");

                                        MSILogFiles.Remove(thisLogFile.Replace(".txt", ""));
                                        i--;
                                        indeterminate = false;
                                        break;
                                    }

                                    else if (line.Contains("Failed to grab mutex for logging"))
                                    {
                                        MSIStatSet(thisLogHostName, Localization.T("ProcessReportsForm.MsiFailed", "Package installation failed."), "ERROR");

                                        MSILogFiles.Remove(thisLogFile.Replace(".txt", ""));
                                        i--;
                                        indeterminate = false;
                                        break;
                                    }
                                }
                            }

                            if (indeterminate)
                            {
                                MSIStatSet(thisLogHostName, Localization.T("ProcessReportsForm.MsiIndeterminate", "Indeterminate log was found."), "FAIL");
                                MSILogFiles.Remove(thisLogFile.Replace(".txt", ""));
                                i--;
                            }

                        }
                        catch { }
                    }
                }

                pollingActive = false;
            }
        }

        private void ProcessReportsForm_Load(object sender, EventArgs e)
        {

        }

        private void MSILogList_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (MSILogList.SelectedIndices.Count > 0)
            {
                if (MSILogList.SelectedIndices[0] >= 0)
                {
                    try
                    {
                        int SelectedIndex = MSILogList.SelectedIndices[0];
                        String ThisHost = MSILogList.Items[SelectedIndex].Text;
                        String ThisLogFile = MSILoggingDir.Replace("%computername%", ThisHost) + ThisHost + ".txt";
                        LogFileLocation.Text = ThisLogFile;

                        if (File.Exists(ThisLogFile))
                        {
                            using (FileStream logFile = new FileStream(ThisLogFile, FileMode.Open, FileAccess.Read))
                            using (StreamReader reader = new StreamReader(logFile))
                            {
                                ActualMSILog.Text = reader.ReadToEnd();
                            }
                            OpenLog.Enabled = true;
                        }

                        else
                        {
                            ActualMSILog.Text = Localization.T("ProcessReportsForm.LogNotAvailable", "Log not available.");
                            OpenLog.Enabled = false;
                        }
                    }
                    catch
                    {
                        ActualMSILog.Text = Localization.T("ProcessReportsForm.LogInUse", "Log file is currently in use. Try again.");
                        OpenLog.Enabled = false;
                    }
                }

                else
                {
                    ActualMSILog.Text = Localization.T("ProcessReportsForm.LogError", "Error accessing log. Try again.");
                    OpenLog.Enabled = false;
                }

            }
            else
            {
                ActualMSILog.Text = Localization.T("ProcessReportsForm.NoHostSelected", "No host name or IP address selected.");
                OpenLog.Enabled = false;
            }
        }        

        private void log_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                if (log.Items.Count > 0)
                {
                    if (log.SelectedIndices.Count > 0)
                    {
                        if (log.SelectedIndices[0] > -1)
                        {
                            String theHIPname = log.Items[log.SelectedIndices[0]].SubItems[1].Text;

                            if (theHIPname != DeploymentHostDisplay)
                            {
                                hostName.Text = theHIPname;
                                contextMenu.Show(MousePosition);
                            }
                        }
                    }
                }
            }
        }

        private void processList_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                if (processList.Items.Count > 0)
                {
                    if (processList.SelectedIndex > -1)
                    {
                        hostName.Text = processList.Items[processList.SelectedIndex].ToString();
                        contextMenu.Show(MousePosition);
                    }
                }
            }
        }

        private void MSILogList_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                if (MSILogList.Items.Count > 0)
                {
                    if (MSILogList.SelectedIndices.Count > 0)
                    {
                        if (MSILogList.SelectedIndices[0] > -1)
                        {
                            String theHIPname = MSILogList.Items[MSILogList.SelectedIndices[0]].Text;

                            if (theHIPname != DeploymentHostDisplay)
                            {
                                hostName.Text = theHIPname;
                                contextMenu.Show(MousePosition);
                            }
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

        private void OpenLog_Click(object sender, EventArgs e)
        {
            if (File.Exists(LogFileLocation.Text))
            {
                using (Process.Start(LogFileLocation.Text)) { }
            }
        }        
    }
}
