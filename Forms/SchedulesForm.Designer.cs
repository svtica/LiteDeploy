// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    SchedulesForm.Designer.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-12
// Purpose: Controls of the Scheduled Updates window.
// -----------------------------------------------------------------------------
namespace LiteDeploy
{
    partial class SchedulesForm
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null)) components.Dispose();
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            this.schedulesList = new System.Windows.Forms.ListView();
            this.nameColumn = new System.Windows.Forms.ColumnHeader();
            this.actionColumn = new System.Windows.Forms.ColumnHeader();
            this.whenColumn = new System.Windows.Forms.ColumnHeader();
            this.computersColumn = new System.Windows.Forms.ColumnHeader();
            this.nextColumn = new System.Windows.Forms.ColumnHeader();
            this.lastColumn = new System.Windows.Forms.ColumnHeader();
            this.addButton = new System.Windows.Forms.Button();
            this.editButton = new System.Windows.Forms.Button();
            this.deleteButton = new System.Windows.Forms.Button();
            this.runButton = new System.Windows.Forms.Button();
            this.engineGroup = new System.Windows.Forms.GroupBox();
            this.internalRadio = new System.Windows.Forms.RadioButton();
            this.windowsRadio = new System.Windows.Forms.RadioButton();
            this.logButton = new System.Windows.Forms.Button();
            this.closeButton = new System.Windows.Forms.Button();
            this.statusLabel = new System.Windows.Forms.Label();
            this.engineGroup.SuspendLayout();
            this.SuspendLayout();
            this.schedulesList.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) | System.Windows.Forms.AnchorStyles.Left) | System.Windows.Forms.AnchorStyles.Right)));
            this.schedulesList.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] {
            this.nameColumn, this.actionColumn, this.whenColumn, this.computersColumn, this.nextColumn, this.lastColumn});
            this.schedulesList.FullRowSelect = true;
            this.schedulesList.HideSelection = false;
            this.schedulesList.Location = new System.Drawing.Point(12, 12);
            this.schedulesList.MultiSelect = false;
            this.schedulesList.Name = "schedulesList";
            this.schedulesList.Size = new System.Drawing.Size(760, 240);
            this.schedulesList.UseCompatibleStateImageBehavior = false;
            this.schedulesList.View = System.Windows.Forms.View.Details;
            this.schedulesList.DoubleClick += new System.EventHandler(this.editButton_Click);
            this.schedulesList.SelectedIndexChanged += new System.EventHandler(this.schedulesList_SelectedIndexChanged);
            this.nameColumn.Text = "Name";
            this.nameColumn.Width = 150;
            this.actionColumn.Text = "Action";
            this.actionColumn.Width = 90;
            this.whenColumn.Text = "When";
            this.whenColumn.Width = 160;
            this.computersColumn.Text = "Computers";
            this.computersColumn.Width = 70;
            this.nextColumn.Text = "Next run";
            this.nextColumn.Width = 110;
            this.lastColumn.Text = "Last run";
            this.lastColumn.Width = 170;
            System.Windows.Forms.Button[] buttons = { this.addButton, this.editButton, this.deleteButton, this.runButton, this.logButton };
            for (int i = 0; i < buttons.Length; i++)
            {
                buttons[i].Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
                buttons[i].Location = new System.Drawing.Point(12 + i * 100, 260);
                buttons[i].Size = new System.Drawing.Size(94, 25);
            }
            this.addButton.Name = "addButton";
            this.addButton.Text = "Add...";
            this.addButton.Click += new System.EventHandler(this.addButton_Click);
            this.editButton.Name = "editButton";
            this.editButton.Text = "Edit...";
            this.editButton.Click += new System.EventHandler(this.editButton_Click);
            this.deleteButton.Name = "deleteButton";
            this.deleteButton.Text = "Delete";
            this.deleteButton.Click += new System.EventHandler(this.deleteButton_Click);
            this.runButton.Name = "runButton";
            this.runButton.Text = "Run now";
            this.runButton.Click += new System.EventHandler(this.runButton_Click);
            this.logButton.Name = "logButton";
            this.logButton.Text = "Logs";
            this.logButton.Click += new System.EventHandler(this.logButton_Click);
            this.engineGroup.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left) | System.Windows.Forms.AnchorStyles.Right)));
            this.engineGroup.Controls.Add(this.internalRadio);
            this.engineGroup.Controls.Add(this.windowsRadio);
            this.engineGroup.Location = new System.Drawing.Point(12, 294);
            this.engineGroup.Name = "engineGroup";
            this.engineGroup.Size = new System.Drawing.Size(760, 70);
            this.engineGroup.Text = "Engine";
            this.internalRadio.Location = new System.Drawing.Point(12, 18);
            this.internalRadio.Name = "internalRadio";
            this.internalRadio.Size = new System.Drawing.Size(740, 22);
            this.internalRadio.Text = "Internal: the schedules run while LiteDeploy is open";
            this.internalRadio.CheckedChanged += new System.EventHandler(this.Engine_Changed);
            this.windowsRadio.Location = new System.Drawing.Point(12, 42);
            this.windowsRadio.Name = "windowsRadio";
            this.windowsRadio.Size = new System.Drawing.Size(740, 22);
            this.windowsRadio.Text = "Windows scheduled tasks: they run while you are logged on, even with LiteDeploy closed";
            this.windowsRadio.CheckedChanged += new System.EventHandler(this.Engine_Changed);
            this.statusLabel.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left) | System.Windows.Forms.AnchorStyles.Right)));
            this.statusLabel.Location = new System.Drawing.Point(12, 370);
            this.statusLabel.Name = "statusLabel";
            this.statusLabel.Size = new System.Drawing.Size(660, 32);
            this.closeButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.closeButton.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.closeButton.Location = new System.Drawing.Point(697, 374);
            this.closeButton.Name = "closeButton";
            this.closeButton.Size = new System.Drawing.Size(75, 25);
            this.closeButton.Text = "Close";
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.CancelButton = this.closeButton;
            this.ClientSize = new System.Drawing.Size(784, 411);
            this.Controls.AddRange(new System.Windows.Forms.Control[] {
                this.schedulesList, this.addButton, this.editButton, this.deleteButton, this.runButton, this.logButton,
                this.engineGroup, this.statusLabel, this.closeButton });
            this.MinimumSize = new System.Drawing.Size(640, 360);
            this.Name = "SchedulesForm";
            this.ShowIcon = false;
            this.ShowInTaskbar = false;
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Lite Deploy - Scheduled Updates";
            this.engineGroup.ResumeLayout(false);
            this.ResumeLayout(false);
        }

        private System.Windows.Forms.ListView schedulesList;
        private System.Windows.Forms.ColumnHeader nameColumn;
        private System.Windows.Forms.ColumnHeader actionColumn;
        private System.Windows.Forms.ColumnHeader whenColumn;
        private System.Windows.Forms.ColumnHeader computersColumn;
        private System.Windows.Forms.ColumnHeader nextColumn;
        private System.Windows.Forms.ColumnHeader lastColumn;
        private System.Windows.Forms.Button addButton;
        private System.Windows.Forms.Button editButton;
        private System.Windows.Forms.Button deleteButton;
        private System.Windows.Forms.Button runButton;
        private System.Windows.Forms.Button logButton;
        private System.Windows.Forms.GroupBox engineGroup;
        private System.Windows.Forms.RadioButton internalRadio;
        private System.Windows.Forms.RadioButton windowsRadio;
        private System.Windows.Forms.Label statusLabel;
        private System.Windows.Forms.Button closeButton;
    }
}
