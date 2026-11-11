// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    DeploymentProgressForm.cs
// Author:  LiteDeploy contributors
// Date:    2025-05-30
// Purpose: Progress of a running deployment: current computer, elapsed time,
//          skip, stop.
// -----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace LiteDeploy
{
    public partial class DeploymentProgressForm : Form
    {
        /// <summary>Text sent by ProcessManager when the deployment is over (hides the window; never shown).</summary>
        public const String DoneSignal = "Done!";

        static String FormatTime(String key, String english, int[] t)
        {
            return Localization.F(key, english, String.Format("{0:00}:{1:00}:{2:00}", t[0], t[1], t[2]));
        }
        const bool MORE_INFO = false;
        const bool LESS_INFO = true;
        bool ViewMode = LESS_INFO;

        const int HOURS = 0;
        const int MINUTES = 1;
        const int SECONDS = 2;

        static int[] TotalInts = new int[3] { 0, 0, 0 };
        static int[] CurrentInts = new int[3] { 0, 0, 0 }; 

        public DeploymentProgressForm()
        {
            InitializeComponent();
            Localization.Apply(this);
        }

        private void progressBar_Click(object sender, EventArgs e)
        {

        }

        private delegate void InitDel(int m);
        public void initialize(int Max)
        {
            if (progressBar.InvokeRequired)
            {
                InitDel a = new InitDel(initialize);
                object[] args = new object[] { Max };
                Invoke(a, args);
            }

            else
            {
                totalTimer.Enabled = false;
                currentTimer.Enabled = false;
                progressBar.Maximum = Max;
                progressBar.Value = 0;
                percentage.Text = "0%";
                currentStatus.Text = Localization.T("DeploymentProgressForm.Initializing", "Initializing...");
                CurrentTime.Text = FormatTime("DeploymentProgressForm.CurrentTime", "Current Time: {0}", new int[3]);
                TotalTime.Text = FormatTime("DeploymentProgressForm.TotalTime", "Total Time: {0}", new int[3]);
                Skip.Enabled = true;
                StopAll.Enabled = true;
                Skip.Text = Localization.T("DeploymentProgressForm.Skip.Text", "Skip");
                StopAll.Text = Localization.T("DeploymentProgressForm.StopAll.Text", "Stop All");
                TotalInts = new int[3] { 0, 0, 0 };
                CurrentInts = new int[3] { 0, 0, 0 };
                totalTimer.Enabled = true;
                currentTimer.Enabled = true;
            }
        }

        private delegate void IncrementBarDel();
        public void IncrementBar()
        {
            if (progressBar.InvokeRequired)
            {
                IncrementBarDel a = new IncrementBarDel(IncrementBar);
                Invoke(a);
            }

            else
            {
                currentTimer.Enabled = false;
                CurrentInts = new int[3] { 0, 0, 0 };
                CurrentTime.Text = FormatTime("DeploymentProgressForm.CurrentTime", "Current Time: {0}", new int[3]);
                progressBar.Value += 1;

                if (progressBar.Value == progressBar.Maximum)
                    progressBar.Value = progressBar.Maximum;

                percentage.Text = ((int)(((double)progressBar.Value / (double)progressBar.Maximum) * 100)).ToString() + "%";

                Skip.Enabled = true;
                StopAll.Enabled = true;
                currentTimer.Enabled = true;
                Skip.Text = Localization.T("DeploymentProgressForm.Skip.Text", "Skip");
                StopAll.Text = Localization.T("DeploymentProgressForm.StopAll.Text", "Stop All");
            }
        }

        private delegate void SetIncrementStringDel(String s, String s2);
        public void SetIncrementString(String s, String s2)
        {
            if (progressBar.InvokeRequired)
            {
                SetIncrementStringDel a = new SetIncrementStringDel(SetIncrementString);
                object[] args = new object[] { s, s2 };
                Invoke(a, args);
            }

            else
            {
                currentStatus.Text = s;
                Next.Text = s2;

                if (s == DoneSignal)
                {
                    totalTimer.Enabled = false;
                    currentTimer.Enabled = false;
                    Hide();
                }
            }
        }

        public void StopAll_Click(object sender, EventArgs e)
        {
            Skip.Enabled = false;
            StopAll.Enabled = false;
            StopAll.Text = Localization.T("DeploymentProgressForm.Wait", "Wait");
            ProcessManager.KillAll();
            MainFormAccess.mainForm.reEnableExitOps();
            Hide();
        }

        private void Skip_Click(object sender, EventArgs e)
        {
            Skip.Enabled = false;
            StopAll.Enabled = false;
            Skip.Text = Localization.T("DeploymentProgressForm.Wait", "Wait");
            ProcessManager.KillCurrentProcess();
        }

        private void moreInfo_Click(object sender, EventArgs e)
        {
            animTimer.Enabled = true;
            ViewMode = !ViewMode;

            if (ViewMode == MORE_INFO)
                moreInfo.Text = "/\\";
            else
            {
                moreInfo.Text = "\\/";
                dBox.Visible = false;
            }
        }

        private void animTimer_Tick(object sender, EventArgs e)
        {
            if (ViewMode == MORE_INFO)
            {
                if (Height < 271)
                {
                    Height += 7;
                }

                else
                {
                    Height = 271; 
                    animTimer.Enabled = false;
                    dBox.Visible = true;
                }
            }

            else if (ViewMode == LESS_INFO)
            {
                if (Height > 173)
                {
                    Height -= 7;
                }

                else
                {
                    Height = 173;
                    animTimer.Enabled = false;                    
                }
            }
        }

        private void DeploymentProgressForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            e.Cancel = true;

            DialogResult result = MessageBox.Show(Localization.T("DeploymentProgressForm.ConfirmCancel", "Are you sure you want to cancel this deployment?"), Localization.T("DeploymentProgressForm.ConfirmCancel.Title", "Confirm Cancel"), MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            
            if (result == System.Windows.Forms.DialogResult.Yes)
            {
                StopAll_Click(null, null);
            }
        }

        private void currentTimer_Tick(object sender, EventArgs e)
        {
            CurrentInts[SECONDS]++;

            if (CurrentInts[SECONDS] == 60)
            {
                CurrentInts[MINUTES]++;
                CurrentInts[SECONDS] = 0;
            }

            if (CurrentInts[MINUTES] == 60)
            {
                CurrentInts[HOURS]++;
                CurrentInts[MINUTES] = 0;
            }

            CurrentTime.Text = FormatTime("DeploymentProgressForm.CurrentTime", "Current Time: {0}", CurrentInts);
        }

        private void totalTimer_Tick(object sender, EventArgs e)
        {
            TotalInts[SECONDS]++;

            if (TotalInts[SECONDS] == 60)
            {
                TotalInts[MINUTES]++;
                TotalInts[SECONDS] = 0;
            }

            if ( TotalInts[MINUTES] == 60 )
            {
                TotalInts[HOURS]++;
                TotalInts[MINUTES] = 0;
            }

            TotalTime.Text = FormatTime("DeploymentProgressForm.TotalTime", "Total Time: {0}", TotalInts);
        }
    }
}
