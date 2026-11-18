// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    PingAllForm.cs
// Author:  LiteDeploy contributors
// Date:    2025-05-30
// Purpose: Pings computers of the grid in parallel (the checked ones, else
//          the ones shown) and updates the checks of the grid (IPingTarget).
// -----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Threading;
using System.Threading.Tasks;
using System.Net.NetworkInformation;

namespace LiteDeploy
{
    /// <summary>What the ping results change: the checks of the grid of the main window.</summary>
    internal interface IPingTarget
    {
        void SetChecked(IEnumerable<String> computers, bool check);
        /// <summary>Unchecked, and no longer shown when only added by name.</summary>
        void RemoveFromList(IEnumerable<String> computers);
    }

    public partial class PingAllForm : Form
    {
        private const int MAX_NUM_THREADS = 255;
        private const int PING_TIMEOUT = 1100;
        private List<String> hosts;
        private List<int> indexes;
        /// <summary>The computers to ping (SetComputers) and what the results change.</summary>
        private List<String> source = new List<String>();
        private IPingTarget target;
        private static bool stopSignal;
        private static bool pingRunning; 
        private static int currentNum;
        public bool firstInit;
        int totalNum;
        int okStatI;
        int failStatI;
        int errorStatI;
        private static int run;
        private static int runningThreadCount;
        private static Object CounterLock = new Object();

        public PingAllForm()
        {
            InitializeComponent();
            Localization.Apply(this);
            Theme.Apply(this);

            // The window is created at start-up and stays hidden most of the time:
            // only refresh the running thread count while it is shown (the timer
            // used to tick every millisecond for the whole session).
            timer1.Enabled = false;
            timer1.Interval = 250;
            VisibleChanged += PingAllForm_VisibleChanged;

            hosts = new List<String>();
            indexes = new List<int>();
            stopSignal = false;
            pingRunning = false;
            firstInit = true;
        }

        private void PingAllForm_Load(object sender, EventArgs e)
        {
        }

        private void PingAllForm_VisibleChanged(object sender, EventArgs e)
        {
            timer1.Enabled = Visible;
        }

        private delegate void ChangeStopButtonTextDel(String s);
        private void ChangeStopButtonText(String s)
        {
            if (stop.InvokeRequired)
            {
                ChangeStopButtonTextDel a = new ChangeStopButtonTextDel(ChangeStopButtonText);
                object[] args = new object[] { s };
                Invoke(a, s);
            }

            else
            {
                stop.Text = s;
            }
        }

        private void ResetStats()
        {
            totalStat.Text = Localization.F("PingAllForm.Total", "Total: {0}", 0);
            okStat.Text = Localization.F("PingAllForm.Ok", "Ok: {0}", 0);
            failStat.Text = Localization.F("PingAllForm.Fail", "Fail: {0}", 0);
            errorStat.Text = Localization.F("PingAllForm.Error", "Error: {0}", 0);
            okStatI = failStatI = errorStatI = 0;
            run = 0;
        }

        private void ResetFormsOps()
        {
            doneAction.SelectedIndex = -1;
            stopSignal = false;
            done.Enabled = false;
            progressBar1.Value = 0;
            ChangeStopButtonText(Localization.T("PingAllForm.Abort", "Abort"));
        }

        /// <summary>The computers of the next run (loadSelectedComputers with firstInit set).</summary>
        internal void SetComputers(IEnumerable<String> computers, IPingTarget pingTarget)
        {
            source = (computers ?? Enumerable.Empty<String>()).ToList();
            target = pingTarget;
        }

        public void loadSelectedComputers()
        {
            ResetStats();
            ResetFormsOps();

            if (firstInit)
            {
                firstInit = false;

                hosts.Clear();
                indexes.Clear();
                pingList.Items.Clear();

                for (int i = 0; i < source.Count; i++)
                {
                    pingList.Items.Add(source[i]);
                    pingList.Items[i].SubItems.Add("");
                    hosts.Add(pingList.Items[i].Text);
                    indexes.Add(i);
                }
            }

            else
            {
                foreach (int i in indexes)
                    SetPingResult("", i);
            }

            totalNum = hosts.Count;
            fraction.Text = "0/" + totalNum;
            progressBar1.Maximum = hosts.Count;
            totalStat.Text = Localization.F("PingAllForm.Total", "Total: {0}", totalNum);
            run = totalNum;
            runningThreadCount = 0;

            if (hosts.Count > 0)
            {
                pingRunning = true;
                currentNum = 0;
                ThreadStartTimer.Enabled = true;
            }

            else
            {
                IncreaseProgress("Done");
            }
        }

        private void AbortPing()
        {
            IncreaseProgress("Aborted");
        }

        private void PingThreadFunction()
        {
            if (stopSignal)
            {
                return;
            }

            int i = 0;

            lock (CounterLock)
            {
                i = currentNum;
                runningThreadCount++;
                currentNum++;
            }

            if (i >= pingList.Items.Count)
                return;

            SetPingResult("WORKING", i);
            String pingHostString = hosts[i];

            try
            {
                PingReply reply;

                using (Ping pingSender = new Ping())
                {
                    reply = pingSender.Send(pingHostString, PING_TIMEOUT);
                }

                if (reply.Status == IPStatus.Success)
                {
                    SetPingResult("OK", i);
                }

                else
                {
                    SetPingResult("FAIL", i);
                }
            }

            catch
            {
                SetPingResult("ERROR", i);
            }

            IncreaseProgress(pingHostString);

            lock (CounterLock)
            {
                run--;
                runningThreadCount--;
            }            
        }

        private void WaitForThreadsToFinish()
        {
            // Wait (without spinning a CPU core) until every ping thread is done.
            while (true)
            {
                lock (CounterLock)
                {
                    if (runningThreadCount == 0)
                        run = 0;

                    if (run <= 0)
                    {
                        runningThreadCount = 0;
                        break;
                    }
                }

                Thread.Sleep(50);
            }

            IncreaseProgress("Done");
        }

        private delegate void IncreaseProgressDel(String nC);
        private void IncreaseProgress(String nextComp)
        {
            if (progressBar1.InvokeRequired )
            {
                IncreaseProgressDel a = new IncreaseProgressDel(IncreaseProgress);
                object[] args = new object[] { nextComp };
                Invoke(a, args);
            }

            else
            {
                if (nextComp == "Done" || nextComp == "Aborted")
                {                                                        
                    okStat.Text = Localization.F("PingAllForm.Ok", "Ok: {0}", okStatI);
                    errorStat.Text = Localization.F("PingAllForm.Error", "Error: {0}", errorStatI);
                    failStat.Text = Localization.F("PingAllForm.Fail", "Fail: {0}", failStatI);
                    pingRunning = false;
                    ChangeStopButtonText(Localization.T("PingAllForm.Restart", "Restart"));
                    stop.Enabled = true;
                    done.Enabled = true;                    
                }

                else
                {
                    fraction.Text = currentNum + "/" + totalNum;
                    progressBar1.PerformStep();                    
                }                
            }
        }        

        private delegate void SetPingResultDel(String res, int i);
        private void SetPingResult(String result, int i)
        {
            if (pingList.InvokeRequired)
            {
                SetPingResultDel a = new SetPingResultDel( SetPingResult );
                object[] args = new object[] { result, i };
                Invoke( a, args );
            }

            else
            {
                pingList.Items[i].SubItems[1].Text = result;

                if (result == "OK")
                {
                    pingList.Items[i].StateImageIndex = 0;
                    okStatI++;
                    okStat.Text = Localization.F("PingAllForm.Ok", "Ok: {0}", okStatI);
                }

                else if (result == "ERROR")
                {
                    pingList.Items[i].StateImageIndex = 1;
                    errorStatI++;
                    errorStat.Text = Localization.F("PingAllForm.Error", "Error: {0}", errorStatI);
                }

                else if (result == "FAIL")
                {
                    pingList.Items[i].StateImageIndex = 2;
                    failStatI++;
                    failStat.Text = Localization.F("PingAllForm.Fail", "Fail: {0}", failStatI);
                }

                else if (result == "")
                {
                    pingList.Items[i].StateImageIndex = -1;
                }
            }
        }

        private void done_Click(object sender, EventArgs e)
        {
            // 0: check the OK ones; 1: uncheck the FAIL ones; 2, 3, 4: remove the FAIL, ERROR, or both
            int action = doneAction.SelectedIndex;
            bool removeFail = action == 2 || action == 4;
            bool removeError = action == 3 || action == 4;
            List<String> ok = new List<String>(), fail = new List<String>(), removed = new List<String>();
            for (int i = 0; i < pingList.Items.Count; i++)
            {
                String result = pingList.Items[i].SubItems[1].Text;
                String name = pingList.Items[i].Text;
                if (result == "OK") ok.Add(name);
                if (result == "FAIL") fail.Add(name);
                if ((removeFail && result == "FAIL") || (removeError && result == "ERROR"))
                {
                    removed.Add(name);
                    pingList.Items.RemoveAt(i);
                    hosts.RemoveAt(i);
                    --i;
                }
            }

            if (target != null)
            {
                if (action == 0) target.SetChecked(ok, true);
                else if (action == 1) target.SetChecked(fail, false);
                else if (removed.Count > 0) target.RemoveFromList(removed);
            }

            if (!KeepAlive.Checked)
            {
                Refresh();
                Close();
            }

            else
            {
                indexes.Clear();

                for (int i = 0; i < hosts.Count; i++)
                {
                    indexes.Add( i );
                }
            }
        }

        private void Cancel_Click(object sender, EventArgs e)
        {
            if (pingRunning)
            {
                stopSignal = true;
                stop.Enabled = false;
                run = runningThreadCount;
            }

            else
            {
                loadSelectedComputers();
            }            
        }

        private void PingAllForm_Resize(object sender, EventArgs e)
        {

        }

        private void PingAllForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (pingRunning)
            {
                stopSignal = true;
                stop.Enabled = false;
                e.Cancel = true;
            }
        }

        private void timer1_Tick( object sender, EventArgs e )
        {
            runningThreads.Text = Localization.F("PingAllForm.Threads", "Threads Running: {0}", runningThreadCount);
        }

        private void ThreadStartTimer_Tick(object sender, EventArgs e)
        {
            if (stopSignal)
            {
                pingRunning = false;
                ThreadStartTimer.Enabled = false;
                Thread WaitThread = new Thread(new ThreadStart(WaitForThreadsToFinish));
                WaitThread.Start();
                return;
            }

            if (currentNum == totalNum)
            {
                ThreadStartTimer.Enabled = false;
                Thread WaitThread = new Thread(new ThreadStart(WaitForThreadsToFinish));
                WaitThread.Start();
                return;
            }

            // Start this thread
            Thread T = new Thread(new ThreadStart(PingThreadFunction));
            T.Start();
        }
    }
}
