// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    DeployForm.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-18
// Purpose: Deploy a file or command (action of the grid): a file or a command
//          run through PsExec on the target computers of the grid (the checked
//          ones, else the selected rows, followed live), with the PsExec
//          options, the credentials (or SYSTEM), the MSI options and the
//          restart / shutdown commands. Non-modal and kept for the session: its
//          fields stay from one deployment to the next; the progress is in the
//          process reports window.
// -----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace LiteDeploy
{
    public partial class DeployForm : Form
    {
        /// <summary>The computers of the next deployment (the grid: checked, else selected).</summary>
        readonly Func<List<String>> targets;

        /// <summary>Relative working path (if set).</summary>
        public String workPath = "";

        /// <param name="targets">The target computers, read when shown and when the selection of the grid changes.</param>
        internal DeployForm(Func<List<String>> targets)
        {
            InitializeComponent();
            Localization.Apply(this);
            Theme.Apply(this);
            this.targets = targets;
            username.Text = Environment.UserDomainName + "\\" + Environment.UserName;
            username.Select(username.Text.Length, 0);
            priority.SelectedIndex = 2;
        }

        /// <summary>Shows the window over the main window (placed the first time), with its current targets.</summary>
        internal void ShowFor(Form owner)
        {
            RefreshTargets();
            if (!Visible)
            {
                if (Owner == null) Owner = owner;
                if (!placed && owner != null)
                {
                    placed = true;
                    Location = new Point(owner.Left + Math.Max(0, (owner.Width - Width) / 2), owner.Top + Math.Max(0, (owner.Height - Height) / 3));
                }
                Show();
            }
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Activate();
        }

        bool placed;

        /// <summary>The title line: how many computers, and the first names.</summary>
        internal void RefreshTargets()
        {
            if (IsDisposed) return;
            List<String> list = targets();
            String names = String.Join(", ", list.Take(8)) + (list.Count > 8 ? ", ..." : "");
            targetsLabel.Text = list.Count == 0
                ? Localization.T("DeployForm.NoTarget", "No computer checked or selected in the list")
                : Localization.F("DeployForm.Targets", "{0} computer(s): {1}", list.Count, names);
            targetsLabel.ForeColor = list.Count == 0 ? Theme.ErrorColor : (Theme.IsDark ? Theme.Text : SystemColors.ControlText);
        }

        /// <summary>The password typed here, to hide it in the console (null when the credentials are not used).</summary>
        internal String SecretToMask()
        {
            return credentials.Checked && password.Text.Length > 0 ? password.Text : null;
        }

        /// <summary>A deployment runs: no other one starts.</summary>
        internal void SetStartEnabled(bool enabled)
        {
            startDeploy.Enabled = enabled;
        }

        // Closed by the user: hidden, the fields are kept
        void DeployForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
            }
        }

        // ---- File, command, options -------------------------------------------------

        private void selectFileToExecute_Click(object sender, EventArgs e)
        {
            openExecutable.ShowDialog(this);
        }

        private void openExecutable_FileOk(object sender, CancelEventArgs e)
        {
            MainFormAccess.mainForm.PConsoleAccess().ClearMSI();
            executable.Text = openExecutable.FileName;
        }

        private void clearExec_Click(object sender, EventArgs e)
        {
            executable.Text = "";
            arguments.Text = "";
            MainFormAccess.mainForm.PConsoleAccess().ClearMSI();
        }

        private void executable_TextChanged(object sender, EventArgs e)
        {
            if (executable.Text == "" || !File.Exists(executable.Text)) return;
            FileInfo info = new FileInfo(executable.Text);
            if (info.Extension != ".msi" && info.Extension != ".mst") return;

            using (MsiOptionsForm MSIWindow = new MsiOptionsForm(openExecutable.FileName))
            {
                MSIWindow.ShowDialog(this);
                arguments.Text = MSIWindow.GetCommandString();
                executable.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "msiexec.exe");
                wait.Checked = true;
                copy.Checked = false;
                forceCopy.Checked = false;
                interactive.Checked = false;
                relWorkingPath.Checked = false;
                MainFormAccess.mainForm.PConsoleAccess().MSIStatProductName.Text = MSIWindow.MSIProductName.Text;
                MainFormAccess.mainForm.PConsoleAccess().MSIStatProductVersion.Text = MSIWindow.MSIPVersion.Text;
            }
        }

        private void systemAccount_CheckedChanged(object sender, EventArgs e)
        {
            credentials.Enabled = password.Enabled = username.Enabled = !systemAccount.Checked;
        }

        private void credentials_CheckedChanged(object sender, EventArgs e)
        {
            systemAccount.Enabled = !credentials.Checked;
        }

        private void relWorkingPath_CheckedChanged(object sender, EventArgs e)
        {
            if (relWorkingPath.Checked)
            {
                using (WorkingDirectoryForm dialog = new WorkingDirectoryForm(this, workPath))
                {
                    dialog.ShowDialog(this);
                }
            }
        }

        private void interactive_CheckedChanged(object sender, EventArgs e)
        {
            if (interactive.Checked)
                MessageBox.Show(this, Localization.T("DeployForm.InteractiveAccount", "Interactive process requires a local account on the remote machine. Make sure to use the proper credentials."),
                                Localization.T("DeployForm.InteractiveAccount.Title", "Interactive Process"), MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        // ---- Saved credentials ------------------------------------------------------
        // Saved... : fills the user name and password from a stored credential
        // (Tools > Credentials, shared with LiteTask and the software updates)

        private ContextMenuStrip savedCredentialsMenu;

        private void savedCredentialButton_Click(object sender, EventArgs e)
        {
            if (savedCredentialsMenu == null)
            {
                savedCredentialsMenu = new ContextMenuStrip();
                components.Add(savedCredentialsMenu);
            }
            while (savedCredentialsMenu.Items.Count > 0)
            {
                ToolStripItem old = savedCredentialsMenu.Items[0];
                savedCredentialsMenu.Items.RemoveAt(0);
                old.Dispose();
            }

            List<LiteSuite.StoredCredential> stored = new List<LiteSuite.StoredCredential>();
            try { stored = LiteSuite.CredentialStore.List().ToList(); }
            catch (Exception) { }
            foreach (LiteSuite.StoredCredential c in stored)
            {
                String name = c.Name;
                savedCredentialsMenu.Items.Add(name + " (" + c.UserName + ")", null, (s, a) => UseStoredCredential(name));
            }
            if (stored.Count == 0)
                savedCredentialsMenu.Items.Add(Localization.T("DeployForm.NoSavedCredential", "(none)")).Enabled = false;
            savedCredentialsMenu.Items.Add(new ToolStripSeparator());
            savedCredentialsMenu.Items.Add(Localization.T("DeployForm.ManageCredentials", "Manage credentials..."), null, (s, a) =>
            {
                using (CredentialsForm form = new CredentialsForm()) form.ShowDialog(this);
            });
            Theme.Apply(savedCredentialsMenu);
            savedCredentialsMenu.Show(savedCredentialButton, 0, savedCredentialButton.Height);
        }

        private void UseStoredCredential(String name)
        {
            LiteSuite.StoredCredential c = null;
            try { c = LiteSuite.CredentialStore.Read(name); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, name, MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            if (c == null) return;
            username.Text = c.UserName;
            password.Text = c.Password;
            credentials.Checked = true;
        }

        // ---- Restart, shutdown --------------------------------------------------------

        private void restartCommand_Click(object sender, EventArgs e)
        {
            SetOpsForRestartShutdownCmd();
            arguments.Text = "shutdown -r -t 0";
            startDeploy.Focus();
        }

        private void shutdownCommand_Click(object sender, EventArgs e)
        {
            SetOpsForRestartShutdownCmd();
            arguments.Text = "shutdown -s -t 0";
            startDeploy.Focus();
        }

        private void SetOpsForRestartShutdownCmd()
        {
            MainFormAccess.mainForm.PConsoleAccess().ClearMSI();
            credentials.Checked = false;
            systemAccount.Checked = true;
            timeOut.Value = 2;
            wait.Checked = true;
            copy.Checked = false;
            forceCopy.Checked = false;
            executable.Clear();
        }

        // ---- Start ----------------------------------------------------------------------

        // PsExec options for one target computer (PsExecCommandBuilder)
        private PsExecOptions BuildPsExecOptions(String pc)
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

        private void startDeploy_Click(object sender, EventArgs e)
        {
            // PsExec is optional (software updates use WinRM by default): only the deployments need it,
            // so it is asked for here
            if (PsExecLocator.Path == null && !Program.OfferPsExecDownload())
            {
                MessageBox.Show(this, Localization.T("DeployForm.PsExecNeeded", "The deployments need PsExec: download it in Tools > Manage Tools, or put PsExec.exe next to LiteDeploy.exe."),
                    Localization.T("Program.PsExecMissing.Title", "File Missing"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (executable.Text.Length == 0 && arguments.Text.Length == 0)
            {
                MessageBox.Show(this, Localization.T("DeployForm.NothingToExecute", "You have not selected a file to execute or provided a command."), Localization.T("DeployForm.NothingToExecute.Title", "Nothing to Execute"), MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                selectFileToExecute.Focus();
                return;
            }

            if (credentials.Checked)
            {
                if (username.Text.Length < 1)
                {
                    MessageBox.Show(this, Localization.T("DeployForm.UserNameMissing", "Please enter your user name."), Localization.T("DeployForm.CredentialsMissing.Title", "Credentials Missing"), MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                    username.Focus();
                    return;
                }

                else if (password.Text.Length < 1)
                {
                    MessageBox.Show(this, Localization.T("DeployForm.PasswordMissing", "Please enter your password."), Localization.T("DeployForm.CredentialsMissing.Title", "Credentials Missing"), MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                    password.Focus();
                    return;
                }
            }

            List<String> computers = targets();
            RefreshTargets();
            if (computers.Count == 0)
            {
                MessageBox.Show(this, Localization.T("DeployForm.NoComputerChecked", "Please check at least one computer in the list."), Localization.T("DeployForm.NoComputerChecked.Title", "Computer Not Selected"), MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                return;
            }

            MainFormAccess.mainForm.StartDeployment(computers, BuildPsExecOptions,
                executable.Text.Length > 0 ? executable.Text + " " + arguments.Text : arguments.Text);
        }
    }
}
