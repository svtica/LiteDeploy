// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    ProcessManager.cs
// Author:  LiteDeploy contributors
// Date:    2025-05-30
// Purpose: Runs PsExec once per target computer, one after the other, and
//          collects its output.
// -----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
using System.Xml.Serialization;
using System.Xml;

namespace LiteDeploy
{
    static public class ProcessManager
    {
        static Thread mainThread;
        static public List <ProcessStartInfo> processList = new List<ProcessStartInfo>();
        static private List<Process> instances = new List<Process>();
        static public List<String> deployNameList = new List<String>();
        static public List<String> outputList = new List<String>();   
        static private String incrementString = "";
        static private String incrementString2 = "";
        static public bool processing = false;
        static private int currentHostIndex = 0;

        /// <summary>Command lines as shown in the console (password masked), one per process.</summary>
        static public List<String> displayList = new List<String>();

        /// <summary>Queues one PsExec run: arguments (PsExecCommandBuilder) and the same with the password masked.</summary>
        static public void AddProcess(String command, String displayCommand)
        {
            displayList.Add(displayCommand);

            ProcessStartInfo startInfo = new ProcessStartInfo(PsExecLocator.Path, command.TrimEnd(new char[]{' '}) );
            startInfo.CreateNoWindow = true;
            startInfo.UseShellExecute = false;
            startInfo.RedirectStandardOutput = true;
            startInfo.RedirectStandardError = true;
            startInfo.ErrorDialog = true;
            startInfo.WorkingDirectory = Directory.GetCurrentDirectory();
            startInfo.StandardOutputEncoding = Encoding.UTF8;
            startInfo.StandardErrorEncoding = Encoding.UTF8;
            processList.Add(startInfo);
        }

        static public void StartProcessing()
        {
            MainFormAccess.mainForm.DisableStart();            
            processing = true;

            if (MainFormAccess.mainForm.PConsoleAccess().MSIDeployment)
            {
                MainFormAccess.mainForm.PConsoleAccess().msiLogPollingTimer.Enabled = true;
                MainFormAccess.mainForm.PConsoleAccess().AddLogEntry(ProcessReportsForm.DeploymentHost, Localization.T("ProcessManager.MsiLoggingStarted", "MSI logging has been initiated."));
            }

            lock (instances)
            {
                instances.Clear();
            }

            AppConsole.WriteLine(Localization.T("ProcessManager.Started", "Deployment processing started."));
            StartProcessWindow();
            mainThread = new Thread(new ThreadStart(ProcessThreadFunction));
            mainThread.Start();
        }

        static private void StartProcessWindow()
        {
            MainForm.prepareWindow.initialize(processList.Count);
            MainForm.prepareWindow.Show();
        }

        static private void IncrementBar()
        {
            MainForm.prepareWindow.IncrementBar();
        }

        static private void SetDeployingToString()
        {
            MainForm.prepareWindow.SetIncrementString(incrementString, incrementString2);
        }

        static void EndProcessWindow()
        {
            MainForm.prepareWindow.SetIncrementString(DeploymentProgressForm.DoneSignal, DeploymentProgressForm.DoneSignal);
            AppConsole.WriteLine(Localization.T("ProcessManager.Completed", "Processing completed."));
            MainFormAccess.mainForm.PConsoleAccess().AddLogEntry(ProcessReportsForm.DeploymentHost, Localization.T("ProcessManager.Finished", "Deployment finished."));

            processing = false;
            MainFormAccess.mainForm.EnableStart();
            MainFormAccess.mainForm.reEnableExitOps();

            if (!MainFormAccess.mainForm.IsProcessConsoleVisible())
                ShowConsole();

            if (MainFormAccess.mainForm.PConsoleAccess().MSIDeployment)
            {
                MessageBox.Show(Localization.T("ProcessManager.MsiAllStarted", "LiteDeploy has finished starting all processes for this MSI deployment. MSI logging will stop once all packages have finished running."),
                                Localization.T("ProcessManager.MsiAllStarted.Title", "LiteDeploy - MSI Deployment"), MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
            }
        }
        
        static void ShowConsole()
        {
            MainFormAccess.mainForm.ShowProcessConsole();
        }

        static public void WriteRawData(String file)
        {
            try
            {
                using (FileStream fstream = new FileStream(file, FileMode.Create, FileAccess.Write))
                using (XmlTextWriter xmlWriter = new XmlTextWriter(fstream, Encoding.UTF8))
                {
                    XmlSerializer serializer = new XmlSerializer(typeof(List<String>));
                    xmlWriter.Formatting = Formatting.Indented;
                    serializer.Serialize(xmlWriter, outputList);
                }
            }
            catch (Exception e)
            {
                MessageBox.Show(Localization.F("ProcessManager.OutputWriteError", "Output data could not be written to the data collection directory.\n\nDetails\n\n{0}\n\n{1}", e.Message, file), Localization.T("ProcessManager.OutputWriteError.Title", "Data Collection Error"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        static private void ProcessThreadFunction()
        {
            for (int i = 0; i < processList.Count; i++)
            {
                outputList.Add("");
                currentHostIndex = i;

                using (Process p = Process.Start(processList[i]))
                {
                    lock (instances)
                    {
                        instances.Add(p);
                    }

                    AppConsole.WriteLine(Localization.F("ProcessManager.Executing", "Executing: {0} {1}", Path.GetFileName(PsExecLocator.Path), displayList[i]));
                    incrementString = "Deploying to: " + deployNameList[i];
                    incrementString2 = "Next: " + ((i + 1) < deployNameList.Count ? deployNameList[i + 1] : "");
                    SetDeployingToString();

                    // Read standard output on another thread while standard error is
                    // read here: reading them one after the other blocks PsExec (and
                    // this thread) as soon as the other pipe buffer is full.
                    Task<String> stdout = p.StandardOutput.ReadToEndAsync();
                    String stderr = p.StandardError.ReadToEnd();
                    outputList[i] = stderr.Replace("\r\n", "\n") + stdout.Result.Replace("\r\n", "\n");
                    p.WaitForExit();

                    lock (instances)
                    {
                        instances.Remove(p);
                    }

                    MainFormAccess.mainForm.PConsoleAccess().AddProcessToList(deployNameList[i]);
                    IncrementBar();

                    // CODE TO ADD LOG ITEMS
                    MainFormAccess.mainForm.PConsoleAccess().AddLogEntry(deployNameList[i], outputList[i]);
                }
            }

            EndProcessWindow();
        }

        static public void KillAll()
        {
            if (processing)
            {
                AppConsole.WriteLine(Localization.T("ProcessManager.Stopping", "Stopping all processing."));
                processing = false;
                Process p = null;

                lock (instances)
                {
                    if (instances.Count > 0)
                        p = instances[instances.Count - 1];
                }

                // Kill the running PsExec first: the processing thread is blocked
                // reading its output and only stops once the pipes are closed.
                if (p != null)
                {
                    try
                    {
                        p.Kill();
                    }
                    catch (Exception e)
                    {
                        AppConsole.WriteLine(Localization.F("ProcessManager.AbortFailedDetail", "Process could not be aborted. {0}", e.Message));
                    }
                }

                mainThread.Abort();

                AppConsole.WriteLine(Localization.T("ProcessManager.Aborted", "Deployment was manually aborted by user."));
                MainFormAccess.mainForm.PConsoleAccess().AddLogEntry(ProcessReportsForm.DeploymentHost, Localization.T("ProcessManager.Aborted", "Deployment was manually aborted by user."));
            }

            else
            {
                AppConsole.WriteLine(Localization.T("ProcessManager.NothingToStop", "No deployment to stop."));
            }
        }

        static public void KillCurrentProcess()
        {
            string currentHost = "";

            if ( deployNameList.Count > 0 )
                currentHost = deployNameList[currentHostIndex];

            Process[] PsExec = Process.GetProcessesByName(PsExecLocator.ProcessName);

            try
            {
                if (PsExec.Count() > 0)
                {
                    try
                    {
                        PsExec[0].Kill();
                        AppConsole.WriteLine(Localization.T("ProcessManager.ProcessAborted", "Process was manually aborted by user."));

                        if ( currentHost != "" )
                            MainFormAccess.mainForm.PConsoleAccess().AddLogEntry(currentHost, ProcessReportsForm.ManualAbortSignal);
                    }
                    catch
                    {
                        AppConsole.WriteLine(Localization.T("ProcessManager.AbortFailed", "Process could not be aborted."));
                    }
                }

                else
                {
                    AppConsole.WriteLine(Localization.T("ProcessManager.NoProcess", "No process running."));
                }
            }

            finally
            {
                // Process objects hold a process handle once used: release them all.
                foreach (Process proc in PsExec)
                    proc.Dispose();
            }
        }
    }
}
