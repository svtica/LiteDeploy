// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    ComputerGrid.Uninstall.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-23
// Purpose: Uninstall mode of the computer grid. Removing software is possible
//          only while the "Uninstall mode" check box of the filter bar is on:
//          never saved (off at each opening), an orange banner above the grid
//          with "Leave the uninstall mode", the window title and the status bar
//          marked, Update checked and Install or update disabled. The mode
//          turns itself off after each uninstall run, after 15 minutes without
//          one, and when the window closes. The Uninstall menu (actions of the
//          grid) exists only in this mode: one package of the active profile,
//          the computers where the last check says it is installed (never a
//          protected package, a module only with "# Uninstall: yes":
//          UninstallPolicy), a confirmation naming the package and the
//          computers, the number of computers typed above 5. The worker
//          "uninstall" command does the work (never run by a schedule).
// -----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace LiteDeploy
{
    public partial class ComputerGrid
    {
        CheckBox uninstallModeCheck;
        Panel uninstallBanner;
        ToolStripMenuItem uninstallItem;
        readonly Timer uninstallIdleTimer = new Timer { Interval = UninstallPolicy.ModeIdleMinutes * 60 * 1000 };
        String titleBeforeUninstallMode;
        Color statusBackBeforeUninstallMode;
        /// <summary>Command of the run in progress (Start), to leave the uninstall mode at its end.</summary>
        String runningCommand;

        /// <summary>The uninstall mode is on.</summary>
        internal bool UninstallMode { get { return uninstallModeCheck != null && uninstallModeCheck.Checked; } }

        /// <summary>Creates the check box, the banner and the Uninstall menu item (called by the constructor).</summary>
        void InitializeUninstallMode()
        {
            uninstallModeCheck = new CheckBox
            {
                Name = "uninstallModeCheck",
                AutoSize = true,
                BackColor = Color.Transparent,
                Padding = new Padding(8, 0, 0, 0),
                Text = Localization.T("ComputerGrid.UninstallModeCheck", "Uninstall mode"),
                ForeColor = Theme.IsDark ? Theme.Text : SystemColors.ControlText
            };
            uninstallModeCheck.CheckedChanged += (s, e) => ApplyUninstallMode();
            ToolStripControlHost host = new ToolStripControlHost(uninstallModeCheck)
            {
                ToolTipText = Localization.T("ComputerGrid.UninstallModeTip", "Shows the Uninstall action; turns itself off after each uninstall run and after 15 minutes without one")
            };
            filterStrip.Items.Insert(Math.Max(0, filterStrip.Items.IndexOf(columnsButton)), host);

            uninstallBanner = new Panel { Dock = DockStyle.Top, Height = 30, Visible = false, BackColor = Theme.WarningColor, Padding = new Padding(8, 3, 6, 3) };
            Label text = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Color.Black,
                Font = new Font(Font, FontStyle.Bold),
                Text = Localization.T("ComputerGrid.UninstallBanner", "Uninstall mode: the actions can remove software from the computers.")
            };
            Button leave = new Button
            {
                Dock = DockStyle.Right,
                AutoSize = true,
                ForeColor = Color.Black,
                BackColor = SystemColors.Control,
                Text = Localization.T("ComputerGrid.UninstallLeave", "Leave the uninstall mode")
            };
            leave.Click += (s, e) => uninstallModeCheck.Checked = false;
            uninstallBanner.Controls.Add(text);
            uninstallBanner.Controls.Add(leave);
            splitContainer.Panel1.Controls.Add(uninstallBanner);
            updatesList.BringToFront();   // the list fills what the banner leaves

            uninstallItem = new ToolStripMenuItem
            {
                Name = "uninstallItem",
                Text = Localization.T("ComputerGrid.UninstallItem", "Uninstall"),
                Visible = false
            };
            uninstallItem.Font = new Font(uninstallItem.Font, FontStyle.Bold);
            uninstallIdleTimer.Tick += (s, e) => { uninstallIdleTimer.Stop(); if (pending == 0) uninstallModeCheck.Checked = false; };
            Disposed += (s, e) => uninstallIdleTimer.Dispose();
        }

        void ApplyUninstallMode()
        {
            bool on = UninstallMode;
            uninstallBanner.Visible = on;
            uninstallItem.Visible = on;
            Form form = FindForm();
            if (on)
            {
                if (form != null)
                {
                    titleBeforeUninstallMode = form.Text;
                    form.Text = form.Text + " " + Localization.T("ComputerGrid.UninstallTitle", "[UNINSTALL]");
                }
                statusBackBeforeUninstallMode = statusStrip.BackColor;
                statusStrip.BackColor = Theme.WarningColor;
                uninstallIdleTimer.Stop();
                uninstallIdleTimer.Start();
            }
            else
            {
                if (form != null && titleBeforeUninstallMode != null) form.Text = titleBeforeUninstallMode;
                titleBeforeUninstallMode = null;
                statusStrip.BackColor = statusBackBeforeUninstallMode;
                uninstallIdleTimer.Stop();
            }
            UpdateButtons();
        }

        /// <summary>The Uninstall menu: every software of the active profile installed on at least one target computer.</summary>
        void BuildUninstallMenu(List<String> targets)
        {
            foreach (ToolStripItem old in uninstallItem.DropDownItems.Cast<ToolStripItem>().ToList()) old.Dispose();
            uninstallItem.DropDownItems.Clear();
            if (!UninstallMode) return;
            HashSet<String> uninstallable = new HashSet<String>(StringComparer.OrdinalIgnoreCase);
            if (packages.Any(p => p.IsModule))
                foreach (UpdateModule m in UpdateModules.List().Where(m => m.Uninstall)) uninstallable.Add(m.Name);
            List<KeyValuePair<UpdatePackage, List<String>>> candidates;
            lock (storeLock) candidates = UpdateRunner.UninstallCandidates(targets, packages, store.Find, uninstallable);
            foreach (KeyValuePair<UpdatePackage, List<String>> c in candidates)
            {
                UpdatePackage package = c.Key;
                List<String> computers = c.Value;
                ToolStripMenuItem item = new ToolStripMenuItem(
                    Localization.F("ComputerGrid.UninstallPackageItem", "{0}: uninstall ({1} computer(s))", package.DisplayName, computers.Count));
                item.Click += (s, e) => UninstallPackage(package, computers);
                uninstallItem.DropDownItems.Add(item);
            }
            if (candidates.Count == 0)
                uninstallItem.DropDownItems.Add(new ToolStripMenuItem(
                    Localization.T("ComputerGrid.NothingToUninstall", "Nothing to uninstall: no software of the profile is installed on these computers at their last check (or it is protected, or its module does not declare the Uninstall action).")) { Enabled = false });
            Theme.Apply(uninstallItem.DropDown);
            uninstallItem.Enabled = pending == 0;
        }

        void UninstallPackage(UpdatePackage package, List<String> targets)
        {
            if (pending > 0 || !UninstallMode) return;
            uninstallIdleTimer.Stop();
            uninstallIdleTimer.Start();
            List<Job> jobs = targets.Where(c => !IsPaused(c))
                .Select(c => new Job { Computer = c, Packages = new List<UpdatePackage> { package } }).ToList();
            String skipped;
            jobs = SkipNightOnly(jobs, out skipped);
            if (jobs.Count == 0)
            {
                if (skipped != null) statusLabel.Text = skipped;
                return;
            }
            String names = String.Join(", ", jobs.Select(j => j.Computer).Take(10)) + (jobs.Count > 10 ? ", ..." : "");
            String question = Localization.F("ComputerGrid.ConfirmUninstall",
                "UNINSTALL {0} from {1} computer(s)?\n\nThe application is closed without warning, then removed where the check still says it is installed: {2}",
                package.DisplayName, jobs.Count, names);
            if (skipped != null) question += "\n\n" + skipped;
            if (MessageBox.Show(this, question, Caption, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;
            if (jobs.Count > UninstallPolicy.TypedConfirmationAbove)
            {
                String typed = Prompt.Show(FindForm(), Caption,
                    Localization.F("ComputerGrid.ConfirmUninstallCount", "To confirm, type the number of computers {0} will be removed from:", package.DisplayName), "");
                if (!UninstallPolicy.IsTypedConfirmation(typed, jobs.Count))
                {
                    if (typed != null)
                        MessageBox.Show(this, Localization.T("ComputerGrid.UninstallCancelled", "Not the number of computers: nothing was uninstalled."), Caption, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
            }
            if (!OfferCredential()) return;
            AppConsole.WriteLine("Uninstall mode: " + package.Id + " on " + jobs.Count + " computer(s): " + String.Join(", ", jobs.Select(j => j.Computer)));
            Start(UninstallPolicy.Command, jobs);
        }

        /// <summary>End of a run (UI thread): an uninstall run leaves the uninstall mode.</summary>
        void RunFinished()
        {
            if (runningCommand == UninstallPolicy.Command && UninstallMode) uninstallModeCheck.Checked = false;
            runningCommand = null;
        }
    }
}
