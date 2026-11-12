// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    SoftwareUpdatesForm.Designer.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-12
// Purpose: Controls of the Software Updates window.
// -----------------------------------------------------------------------------
namespace LiteDeploy
{
    partial class SoftwareUpdatesForm
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            this.toolStrip = new System.Windows.Forms.ToolStrip();
            this.rowMenu = new System.Windows.Forms.ContextMenuStrip(this.components);
            this.pauseItem = new System.Windows.Forms.ToolStripMenuItem();
            this.resumeItem = new System.Windows.Forms.ToolStripMenuItem();
            this.nightOnlyItem = new System.Windows.Forms.ToolStripMenuItem();
            this.menuSeparator = new System.Windows.Forms.ToolStripSeparator();
            this.cardItem = new System.Windows.Forms.ToolStripMenuItem();
            this.wakeItem = new System.Windows.Forms.ToolStripMenuItem();
            this.processesItem = new System.Windows.Forms.ToolStripMenuItem();
            this.litePMItem = new System.Windows.Forms.ToolStripMenuItem();
            this.logsItem = new System.Windows.Forms.ToolStripMenuItem();
            this.checkButton = new System.Windows.Forms.ToolStripButton();
            this.selectOutdatedButton = new System.Windows.Forms.ToolStripButton();
            this.updateButton = new System.Windows.Forms.ToolStripButton();
            this.stopButton = new System.Windows.Forms.ToolStripButton();
            this.separator1 = new System.Windows.Forms.ToolStripSeparator();
            this.editPackagesButton = new System.Windows.Forms.ToolStripButton();
            this.scheduleButton = new System.Windows.Forms.ToolStripButton();
            this.updatesList = new System.Windows.Forms.ListView();
            this.computerColumn = new System.Windows.Forms.ColumnHeader();
            this.packageColumn = new System.Windows.Forms.ColumnHeader();
            this.installedColumn = new System.Windows.Forms.ColumnHeader();
            this.availableColumn = new System.Windows.Forms.ColumnHeader();
            this.statusColumn = new System.Windows.Forms.ColumnHeader();
            this.resultColumn = new System.Windows.Forms.ColumnHeader();
            this.changedColumn = new System.Windows.Forms.ColumnHeader();
            this.statusStrip = new System.Windows.Forms.StatusStrip();
            this.statusLabel = new System.Windows.Forms.ToolStripStatusLabel();
            this.progressBar = new System.Windows.Forms.ToolStripProgressBar();
            this.toolStrip.SuspendLayout();
            this.statusStrip.SuspendLayout();
            this.SuspendLayout();
            //
            // toolStrip
            //
            this.toolStrip.GripStyle = System.Windows.Forms.ToolStripGripStyle.Hidden;
            this.toolStrip.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.checkButton,
            this.selectOutdatedButton,
            this.updateButton,
            this.stopButton,
            this.separator1,
            this.editPackagesButton,
            this.scheduleButton});
            this.toolStrip.Location = new System.Drawing.Point(0, 0);
            this.toolStrip.Name = "toolStrip";
            this.toolStrip.Size = new System.Drawing.Size(884, 25);
            this.toolStrip.TabIndex = 0;
            //
            // checkButton
            //
            this.checkButton.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.checkButton.Name = "checkButton";
            this.checkButton.Text = "Check";
            this.checkButton.ToolTipText = "Check the installed and available versions on the checked computers";
            this.checkButton.Click += new System.EventHandler(this.checkButton_Click);
            //
            // selectOutdatedButton
            //
            this.selectOutdatedButton.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.selectOutdatedButton.Name = "selectOutdatedButton";
            this.selectOutdatedButton.Text = "Select outdated";
            this.selectOutdatedButton.ToolTipText = "Check every row that has an update available";
            this.selectOutdatedButton.Click += new System.EventHandler(this.selectOutdatedButton_Click);
            //
            // updateButton
            //
            this.updateButton.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.updateButton.Name = "updateButton";
            this.updateButton.Text = "Update checked";
            this.updateButton.ToolTipText = "Update the checked packages on their computers";
            this.updateButton.Click += new System.EventHandler(this.updateButton_Click);
            //
            // stopButton
            //
            this.stopButton.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.stopButton.Enabled = false;
            this.stopButton.Name = "stopButton";
            this.stopButton.Text = "Stop";
            this.stopButton.Click += new System.EventHandler(this.stopButton_Click);
            //
            // separator1
            //
            this.separator1.Name = "separator1";
            //
            // editPackagesButton
            //
            this.editPackagesButton.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.editPackagesButton.Name = "editPackagesButton";
            this.editPackagesButton.Text = "Package list...";
            this.editPackagesButton.ToolTipText = "Choose the winget packages to keep up to date";
            this.editPackagesButton.Click += new System.EventHandler(this.editPackagesButton_Click);
            //
            // scheduleButton
            //
            this.scheduleButton.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.scheduleButton.Name = "scheduleButton";
            this.scheduleButton.Text = "Schedule...";
            this.scheduleButton.ToolTipText = "Schedule a check or an update of these computers";
            this.scheduleButton.Click += new System.EventHandler(this.scheduleButton_Click);
            //
            // updatesList
            //
            this.updatesList.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] {
            this.computerColumn,
            this.packageColumn,
            this.installedColumn,
            this.availableColumn,
            this.statusColumn,
            this.resultColumn,
            this.changedColumn});
            this.updatesList.CheckBoxes = true;
            this.updatesList.ContextMenuStrip = this.rowMenu;
            this.updatesList.Dock = System.Windows.Forms.DockStyle.Fill;
            this.updatesList.FullRowSelect = true;
            this.updatesList.HideSelection = false;
            this.updatesList.Location = new System.Drawing.Point(0, 25);
            this.updatesList.Name = "updatesList";
            this.updatesList.Size = new System.Drawing.Size(884, 414);
            this.updatesList.TabIndex = 1;
            this.updatesList.UseCompatibleStateImageBehavior = false;
            this.updatesList.View = System.Windows.Forms.View.Details;
            //
            // columns
            //
            this.computerColumn.Text = "Computer";
            this.computerColumn.Width = 130;
            this.packageColumn.Text = "Package";
            this.packageColumn.Width = 150;
            this.installedColumn.Text = "Installed";
            this.installedColumn.Width = 90;
            this.availableColumn.Text = "Available";
            this.availableColumn.Width = 90;
            this.statusColumn.Text = "Status";
            this.statusColumn.Width = 110;
            this.resultColumn.Text = "Last result";
            this.resultColumn.Width = 180;
            this.changedColumn.Text = "Changed";
            this.changedColumn.Width = 120;
            //
            // rowMenu
            //
            this.rowMenu.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.pauseItem,
            this.resumeItem,
            this.nightOnlyItem,
            this.menuSeparator,
            this.cardItem,
            this.processesItem,
            this.litePMItem,
            this.logsItem,
            this.wakeItem});
            this.rowMenu.Name = "rowMenu";
            this.rowMenu.Opening += new System.ComponentModel.CancelEventHandler(this.rowMenu_Opening);
            this.pauseItem.Name = "pauseItem";
            this.pauseItem.Text = "Pause these computers...";
            this.pauseItem.Click += new System.EventHandler(this.pauseItem_Click);
            this.resumeItem.Name = "resumeItem";
            this.resumeItem.Text = "Resume";
            this.resumeItem.Click += new System.EventHandler(this.resumeItem_Click);
            this.nightOnlyItem.Name = "nightOnlyItem";
            this.nightOnlyItem.Text = "Outside business hours only";
            this.nightOnlyItem.Click += new System.EventHandler(this.nightOnlyItem_Click);
            this.menuSeparator.Name = "menuSeparator";
            this.cardItem.Name = "cardItem";
            this.cardItem.Text = "Computer card...";
            this.cardItem.Click += new System.EventHandler(this.cardItem_Click);
            this.wakeItem.Name = "wakeItem";
            this.wakeItem.Text = "Wake up (Wake-on-LAN)";
            this.wakeItem.Click += new System.EventHandler(this.wakeItem_Click);
            this.processesItem.Name = "processesItem";
            this.processesItem.Text = "Processes and handles...";
            this.processesItem.Click += new System.EventHandler(this.processesItem_Click);
            this.litePMItem.Name = "litePMItem";
            this.litePMItem.Text = "Open in LitePM";
            this.litePMItem.Click += new System.EventHandler(this.litePMItem_Click);
            this.logsItem.Name = "logsItem";
            this.logsItem.Text = "Get the logs";
            this.logsItem.Click += new System.EventHandler(this.logsItem_Click);
            //
            // statusStrip
            //
            this.statusStrip.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.statusLabel,
            this.progressBar});
            this.statusStrip.Location = new System.Drawing.Point(0, 439);
            this.statusStrip.Name = "statusStrip";
            this.statusStrip.Size = new System.Drawing.Size(884, 22);
            this.statusStrip.TabIndex = 2;
            //
            // statusLabel
            //
            this.statusLabel.Name = "statusLabel";
            this.statusLabel.Spring = true;
            this.statusLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            //
            // progressBar
            //
            this.progressBar.Name = "progressBar";
            this.progressBar.Size = new System.Drawing.Size(150, 16);
            //
            // SoftwareUpdatesForm
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(884, 461);
            this.Controls.Add(this.updatesList);
            this.Controls.Add(this.statusStrip);
            this.Controls.Add(this.toolStrip);
            this.MinimumSize = new System.Drawing.Size(600, 300);
            this.Name = "SoftwareUpdatesForm";
            this.ShowIcon = false;
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Lite Deploy - Software Updates";
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.SoftwareUpdatesForm_FormClosing);
            this.toolStrip.ResumeLayout(false);
            this.toolStrip.PerformLayout();
            this.statusStrip.ResumeLayout(false);
            this.statusStrip.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        private System.Windows.Forms.ToolStrip toolStrip;
        private System.Windows.Forms.ContextMenuStrip rowMenu;
        private System.Windows.Forms.ToolStripMenuItem pauseItem;
        private System.Windows.Forms.ToolStripMenuItem resumeItem;
        private System.Windows.Forms.ToolStripMenuItem nightOnlyItem;
        private System.Windows.Forms.ToolStripSeparator menuSeparator;
        private System.Windows.Forms.ToolStripMenuItem cardItem;
        private System.Windows.Forms.ToolStripMenuItem wakeItem;
        private System.Windows.Forms.ToolStripMenuItem processesItem;
        private System.Windows.Forms.ToolStripMenuItem litePMItem;
        private System.Windows.Forms.ToolStripMenuItem logsItem;
        private System.Windows.Forms.ToolStripButton checkButton;
        private System.Windows.Forms.ToolStripButton selectOutdatedButton;
        private System.Windows.Forms.ToolStripButton updateButton;
        private System.Windows.Forms.ToolStripButton stopButton;
        private System.Windows.Forms.ToolStripSeparator separator1;
        private System.Windows.Forms.ToolStripButton editPackagesButton;
        private System.Windows.Forms.ToolStripButton scheduleButton;
        private System.Windows.Forms.ListView updatesList;
        private System.Windows.Forms.ColumnHeader computerColumn;
        private System.Windows.Forms.ColumnHeader packageColumn;
        private System.Windows.Forms.ColumnHeader installedColumn;
        private System.Windows.Forms.ColumnHeader availableColumn;
        private System.Windows.Forms.ColumnHeader statusColumn;
        private System.Windows.Forms.ColumnHeader resultColumn;
        private System.Windows.Forms.ColumnHeader changedColumn;
        private System.Windows.Forms.StatusStrip statusStrip;
        private System.Windows.Forms.ToolStripStatusLabel statusLabel;
        private System.Windows.Forms.ToolStripProgressBar progressBar;
    }
}
