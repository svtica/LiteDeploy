// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    OptionsForm.cs
// Author:  LiteDeploy contributors
// Date:    2025-05-30
// Purpose: Options window (update folder, logging folder, Active Directory
//          refresh at start-up).
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
using System.Xml.Serialization;

namespace LiteDeploy
{
    public partial class OptionsForm : Form
    {
        private String UserSavePath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + "\\LiteDeploy\\";

        public OptionsForm()
        {
            InitializeComponent();
        }

        private void OptionsForm_Load(object sender, EventArgs e)
        {
            LocalUpdateDirDisplay.Text = AppSettings.LOCAL_UPDATE_DIR;
            AutoUpADOnStartCheckBox.Checked = AppSettings.AUTO_UPDATE_AD_ON_STARTUP;
            LogDirDisplay.Text = AppSettings.LOGGING_DIR;
            groupBox2.Select();

            if (!Directory.Exists(UserSavePath))
                Directory.CreateDirectory(UserSavePath);
        }

        private void button1_Click(object sender, EventArgs e)
        {
            DialogResult result = DirectoryBrowser.ShowDialog();

            if (result == System.Windows.Forms.DialogResult.OK)
            {
                LocalUpdateDirDisplay.Text = DirectoryBrowser.SelectedPath;
            }
        }

        private void button3_Click(object sender, EventArgs e)
        {
            DialogResult result = DirectoryBrowser.ShowDialog();

            if (result == System.Windows.Forms.DialogResult.OK)
            {
                LocalUpdateDirDisplay.Text = DirectoryBrowser.SelectedPath;
            }
        }

        private void ApplyButton_Click( object sender, EventArgs e )
        {
            Close();
        }
    }
}
