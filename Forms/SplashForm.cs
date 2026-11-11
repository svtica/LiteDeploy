// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    SplashForm.cs
// Author:  LiteDeploy contributors
// Date:    2025-05-30
// Purpose: Splash screen shown while the Active Directory computer list loads.
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

namespace LiteDeploy
{
    public partial class SplashForm : Form
    {
        Thread adLoadThread;
        MainForm parent;

        public SplashForm(MainForm p)
        {
            InitializeComponent();
            Localization.Apply(this);
            label4.Text = Localization.F("SplashForm.Version", "version {0}", Application.ProductVersion);
            parent = p;
        }

        private void checkBox2_CheckedChanged(object sender, EventArgs e)
        {

        }

        private void SplashForm_Load(object sender, EventArgs e)
        {
            timer1.Enabled = true;

            adLoadThread = new Thread(new ThreadStart(ActiveDirectoryCache.GetComputers));
            adLoadThread.Start();
        }

        private void timer1_Tick(object sender, EventArgs e)
        {
            if (Opacity < 1)
            {
                Opacity += .05;
                return;
            }

            if (adLoadThread != null)
            {
                if (adLoadThread.ThreadState == ThreadState.Running)
                {
                    return;
                }
            }

            timer1.Enabled = false;
            Close();
            parent.Show();
        }

        private void ShowAbout()
        {

        }
    }
}
