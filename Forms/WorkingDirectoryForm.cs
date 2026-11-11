// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    WorkingDirectoryForm.cs
// Author:  LiteDeploy contributors
// Date:    2025-05-30
// Purpose: Working directory of the remote process (PsExec -w).
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
    public partial class WorkingDirectoryForm : Form
    {
        MainForm parent;

        public WorkingDirectoryForm(MainForm p, String current)
        {
            InitializeComponent();
            Localization.Apply(this);
            enteredPath.Text = current;
            enteredPath.SelectAll();
            parent = p;
        }

        private void okBtn_Click(object sender, EventArgs e)
        {
            if (enteredPath.Text.Length >= 3)
            {
                if (enteredPath.Text[enteredPath.Text.Length - 1] == '\\')
                    enteredPath.Text = enteredPath.Text.Remove(enteredPath.Text.Length - 1 , 1);

                parent.workPath = enteredPath.Text;
                AppConsole.WriteLine(Localization.F("WorkingDirectoryForm.Set", "Working directory set: {0}", enteredPath.Text));
                Close();
            }

            else
            {
                MessageBox.Show(Localization.T("WorkingDirectoryForm.Empty", "Working path cannot be empty! Please enter a working directory path."), Localization.T("WorkingDirectoryForm.Empty.Title", "Enter Working Path"), MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                return;
            }
        }

        private void WorkingDirectoryForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (enteredPath.Text.Length < 3)
                parent.relWorkingPath.Checked = false;
        }

        private void enteredPath_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyData == Keys.Return)
            {
                okBtn_Click(sender, e);
            }

            else if (e.KeyData == Keys.Escape)
            {
                Close();
            }
        }

        private void WorkingDirectoryForm_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyData == Keys.Escape)
            {
                Close();
            }
        }

        private void okBtn_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyData == Keys.Escape)
            {
                Close();
            }
        }
    }
}
