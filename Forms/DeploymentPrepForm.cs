// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    DeploymentPrepForm.cs
// Author:  LiteDeploy contributors
// Date:    2025-05-30
// Purpose: Creates the MSI log folders on each target computer before an MSI
//          deployment.
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
using System.IO;

namespace LiteDeploy
{
    public partial class DeploymentPrepForm : Form
    {
        public DeploymentPrepForm()
        {
            InitializeComponent();
            Localization.Apply(this);
        }

        Thread PrepThread;
        bool cancelled;

        private void DeploymentPrepForm_Load(object sender, EventArgs e)
        {
            progressBar1.Maximum = ProcessManager.deployNameList.Count;
            progressBar1.Value = 0;
            StatusLabel.Text = Localization.F("DeploymentPrepForm.Verifying", "Verifying directories: {0}", ProcessManager.deployNameList[0]);
            cancelled = false;

            PrepThread = new Thread(new ThreadStart(PrepFunc));
            PrepThread.Start();
        }

        private void PrepFunc()
        {
            for (int i = 0; i < ProcessManager.deployNameList.Count; i++)
            {
                if (cancelled)
                    break;

                String thisHost = ProcessManager.deployNameList[i];

                if (!Directory.Exists(AppSettings.LOGGING_DIR.Replace("%computername%", thisHost)))
                {
                    try
                    {
                        Directory.CreateDirectory(AppSettings.LOGGING_DIR.Replace("%computername%", thisHost));
                    }

                    catch
                    {

                    }                    
                }

                if (!Directory.Exists(AppSettings.LOGGING_DIR.Replace("%computername%", thisHost) + "\\MSI"))
                {
                    try
                    {
                        Directory.CreateDirectory(AppSettings.LOGGING_DIR.Replace("%computername%", thisHost) + "\\MSI");
                    }

                    catch
                    {

                    }
                }

                else if (File.Exists(AppSettings.LOGGING_DIR.Replace("%computername%", thisHost) + "\\MSI" + "\\" + thisHost + ".txt"))
                {
                    File.Delete(AppSettings.LOGGING_DIR.Replace("%computername%", thisHost) + "\\MSI" + "\\" + thisHost + ".txt");
                }

                if (cancelled)
                    break;

                IncrementProgress();
            }
        }

        private delegate void IncrementProgressDel();
        private void IncrementProgress()
        {
            if (cancelled)
                return;

            if (progressBar1.InvokeRequired)
            {
                try
                {
                    IncrementProgressDel a = new IncrementProgressDel(IncrementProgress);
                    Invoke(a);
                }
                catch { }
            }

            else
            {
                try
                {
                    progressBar1.PerformStep();
                    prepPercent.Text = ((int)(((double)progressBar1.Value / (double)progressBar1.Maximum) * 100)).ToString() + "%";

                    if (progressBar1.Value < progressBar1.Maximum - 1)
                        StatusLabel.Text = Localization.F("DeploymentPrepForm.Verifying", "Verifying directories: {0}", ProcessManager.deployNameList[progressBar1.Value]);

                    if (progressBar1.Value == progressBar1.Maximum)
                    {
                        DialogResult = System.Windows.Forms.DialogResult.OK;
                    }
                }
                catch { }
            }
        }

        private void Cancel_Click(object sender, EventArgs e)
        {
            cancelled = true;
        }
    }
}
