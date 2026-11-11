// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    MsiOptionsForm.cs
// Author:  LiteDeploy contributors
// Date:    2025-05-30
// Purpose: msiexec options of an MSI deployment (install, repair, uninstall,
//          restart, logging).
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
using System.Runtime.InteropServices;
using WindowsInstaller;

namespace LiteDeploy
{
    public partial class MsiOptionsForm : Form
    {
        public MsiOptionsForm(String msiP)
        {
            InitializeComponent();
            Localization.Apply(this);

            logDirectory.Text = AppSettings.LOGGING_DIR + "\\MSI\\";
            installer.Text = msiP;

            // The Windows Installer COM objects are released as soon as the two
            // properties are read: the open database used to keep the .msi file
            // open until the next garbage collection.
            Type t = Type.GetTypeFromProgID("WindowsInstaller.Installer");
            Installer inst = null;
            Database d = null;

            try
            {
                inst = (Installer)Activator.CreateInstance(t);
                d = inst.OpenDatabase( msiP, MsiOpenDatabaseMode.msiOpenDatabaseModeReadOnly);
                MSIProductName.Text = ReadProperty(d, "ProductName");
                MSIPVersion.Text = ReadProperty(d, "ProductVersion");
            }

            finally
            {
                ReleaseComObject(d);
                ReleaseComObject(inst);
            }
        }

        private static String ReadProperty(Database d, String name)
        {
            WindowsInstaller.View v = d.OpenView("SELECT * FROM Property WHERE Property = '" + name + "'");
            Record r = null;

            try
            {
                v.Execute(null);
                r = v.Fetch();
                return r.get_StringData(2);
            }

            finally
            {
                ReleaseComObject(r);

                try
                {
                    v.Close();
                }
                catch { }

                ReleaseComObject(v);
            }
        }

        private static void ReleaseComObject(Object o)
        {
            if (o != null && Marshal.IsComObject(o))
                Marshal.FinalReleaseComObject(o);
        }

        private void statusLogging_CheckedChanged(object sender, EventArgs e)
        {
            
        }

        private void useComputerName_CheckedChanged(object sender, EventArgs e)
        {
            logName.Text = "%computername%.txt";
            logName.Enabled = !useComputerName.Checked;
        }

        public String GetCommandString()
        {
            return (uninstall.Checked ? "/x \"" : reInstall.Checked ? "/faumsv \"" : "/i \"") + installer.Text + "\" " +

                   (silentInstall.Checked ? "/qn " : "") +

                   (allUsers.Checked ? "ALLUSERS=\"1\" " : "") +

                   (noRestart.Checked ? "REBOOT=\"R\" " : "") + 

                   (forceRestart.Checked ? "REBOOT=\"F\" " : "" ) +

                   (statusLogging.Checked ? "/Li \"" + logDirectory.Text + logName.Text + "\"" : "");
        }

        private void reInstall_CheckedChanged(object sender, EventArgs e)
        {
            uninstall.Enabled = !reInstall.Checked;

            if (reInstall.Checked)
                uninstall.Checked = !reInstall.Checked;
        }

        private void uninstall_CheckedChanged(object sender, EventArgs e)
        {
            reInstall.Enabled = !uninstall.Checked;

            if ( uninstall.Checked )
                reInstall.Checked = !uninstall.Checked;
        }

        private void noRestart_CheckedChanged(object sender, EventArgs e)
        {
            forceRestart.Enabled = !noRestart.Checked;

            if ( forceRestart.Checked )
                forceRestart.Checked = !noRestart.Checked;
        }

        private void done_Click(object sender, EventArgs e)
        {
            if (statusLogging.Checked)
            {
                MainFormAccess.mainForm.PConsoleAccess().MSILoggingDir = logDirectory.Text;
                MainFormAccess.mainForm.PConsoleAccess().MSIDeployment = true;
                MainFormAccess.mainForm.PConsoleAccess().MSIProductName = MSIProductName.Text;
            }            
            
            Close();
        }

        private void forceRestart_CheckedChanged(object sender, EventArgs e)
        {
            noRestart.Enabled = !forceRestart.Checked;

            if (noRestart.Checked)
                noRestart.Checked = !forceRestart.Checked;
        }

        private void logDirectory_TextChanged(object sender, EventArgs e)
        {
            
        }
    }
}
