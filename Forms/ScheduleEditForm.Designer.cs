// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    ScheduleEditForm.Designer.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-12
// Purpose: Controls of the schedule editor.
// -----------------------------------------------------------------------------
namespace LiteDeploy
{
    partial class ScheduleEditForm
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null)) components.Dispose();
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            this.nameLabel = new System.Windows.Forms.Label();
            this.nameBox = new System.Windows.Forms.TextBox();
            this.actionLabel = new System.Windows.Forms.Label();
            this.actionCombo = new System.Windows.Forms.ComboBox();
            this.onceRadio = new System.Windows.Forms.RadioButton();
            this.oncePicker = new System.Windows.Forms.DateTimePicker();
            this.weeklyRadio = new System.Windows.Forms.RadioButton();
            this.dayMonday = new System.Windows.Forms.CheckBox();
            this.dayTuesday = new System.Windows.Forms.CheckBox();
            this.dayWednesday = new System.Windows.Forms.CheckBox();
            this.dayThursday = new System.Windows.Forms.CheckBox();
            this.dayFriday = new System.Windows.Forms.CheckBox();
            this.daySaturday = new System.Windows.Forms.CheckBox();
            this.daySunday = new System.Windows.Forms.CheckBox();
            this.timeLabel = new System.Windows.Forms.Label();
            this.timePicker = new System.Windows.Forms.DateTimePicker();
            this.wakeCheck = new System.Windows.Forms.CheckBox();
            this.enabledCheck = new System.Windows.Forms.CheckBox();
            this.computersLabel = new System.Windows.Forms.Label();
            this.computersBox = new System.Windows.Forms.TextBox();
            this.okButton = new System.Windows.Forms.Button();
            this.cancelButton = new System.Windows.Forms.Button();
            this.SuspendLayout();
            this.nameLabel.AutoSize = true;
            this.nameLabel.Location = new System.Drawing.Point(12, 15);
            this.nameLabel.Name = "nameLabel";
            this.nameLabel.Text = "Name:";
            this.nameBox.Location = new System.Drawing.Point(120, 12);
            this.nameBox.Name = "nameBox";
            this.nameBox.Size = new System.Drawing.Size(300, 20);
            this.actionLabel.AutoSize = true;
            this.actionLabel.Location = new System.Drawing.Point(12, 45);
            this.actionLabel.Name = "actionLabel";
            this.actionLabel.Text = "Action:";
            this.actionCombo.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.actionCombo.Items.AddRange(new object[] { "Check the versions", "Update the outdated packages" });
            this.actionCombo.Location = new System.Drawing.Point(120, 42);
            this.actionCombo.Name = "actionCombo";
            this.actionCombo.Size = new System.Drawing.Size(300, 21);
            this.onceRadio.AutoSize = true;
            this.onceRadio.Location = new System.Drawing.Point(12, 78);
            this.onceRadio.Name = "onceRadio";
            this.onceRadio.Text = "Once:";
            this.onceRadio.CheckedChanged += new System.EventHandler(this.Recurrence_Changed);
            this.oncePicker.CustomFormat = "yyyy-MM-dd HH:mm";
            this.oncePicker.Format = System.Windows.Forms.DateTimePickerFormat.Custom;
            this.oncePicker.Location = new System.Drawing.Point(120, 76);
            this.oncePicker.Name = "oncePicker";
            this.oncePicker.Size = new System.Drawing.Size(150, 20);
            this.weeklyRadio.AutoSize = true;
            this.weeklyRadio.Location = new System.Drawing.Point(12, 108);
            this.weeklyRadio.Name = "weeklyRadio";
            this.weeklyRadio.Text = "Every week:";
            this.weeklyRadio.CheckedChanged += new System.EventHandler(this.Recurrence_Changed);
            System.Windows.Forms.CheckBox[] days = { this.dayMonday, this.dayTuesday, this.dayWednesday, this.dayThursday, this.dayFriday, this.daySaturday, this.daySunday };
            string[] names = { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" };
            for (int i = 0; i < days.Length; i++)
            {
                days[i].AutoSize = true;
                days[i].Location = new System.Drawing.Point(120 + i * 44, 108);
                days[i].Text = names[i];
            }
            this.dayMonday.Name = "dayMonday";
            this.dayTuesday.Name = "dayTuesday";
            this.dayWednesday.Name = "dayWednesday";
            this.dayThursday.Name = "dayThursday";
            this.dayFriday.Name = "dayFriday";
            this.daySaturday.Name = "daySaturday";
            this.daySunday.Name = "daySunday";
            this.timeLabel.AutoSize = true;
            this.timeLabel.Location = new System.Drawing.Point(30, 137);
            this.timeLabel.Name = "timeLabel";
            this.timeLabel.Text = "At:";
            this.timePicker.CustomFormat = "HH:mm";
            this.timePicker.Format = System.Windows.Forms.DateTimePickerFormat.Custom;
            this.timePicker.ShowUpDown = true;
            this.timePicker.Location = new System.Drawing.Point(120, 134);
            this.timePicker.Name = "timePicker";
            this.timePicker.Size = new System.Drawing.Size(70, 20);
            this.wakeCheck.AutoSize = true;
            this.wakeCheck.Location = new System.Drawing.Point(12, 168);
            this.wakeCheck.Name = "wakeCheck";
            this.wakeCheck.Text = "Wake the computers first (Wake-on-LAN)";
            this.enabledCheck.AutoSize = true;
            this.enabledCheck.Location = new System.Drawing.Point(12, 192);
            this.enabledCheck.Name = "enabledCheck";
            this.enabledCheck.Text = "Enabled";
            this.computersLabel.AutoSize = true;
            this.computersLabel.Location = new System.Drawing.Point(12, 222);
            this.computersLabel.Name = "computersLabel";
            this.computersLabel.Text = "Computers (one per line):";
            this.computersBox.Location = new System.Drawing.Point(12, 240);
            this.computersBox.Multiline = true;
            this.computersBox.Name = "computersBox";
            this.computersBox.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            this.computersBox.Size = new System.Drawing.Size(408, 140);
            this.okButton.Location = new System.Drawing.Point(264, 392);
            this.okButton.Name = "okButton";
            this.okButton.Size = new System.Drawing.Size(75, 25);
            this.okButton.Text = "Save";
            this.okButton.Click += new System.EventHandler(this.okButton_Click);
            this.cancelButton.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.cancelButton.Location = new System.Drawing.Point(345, 392);
            this.cancelButton.Name = "cancelButton";
            this.cancelButton.Size = new System.Drawing.Size(75, 25);
            this.cancelButton.Text = "Cancel";
            this.AcceptButton = this.okButton;
            this.CancelButton = this.cancelButton;
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(434, 429);
            this.Controls.AddRange(new System.Windows.Forms.Control[] {
                this.nameLabel, this.nameBox, this.actionLabel, this.actionCombo, this.onceRadio, this.oncePicker,
                this.weeklyRadio, this.dayMonday, this.dayTuesday, this.dayWednesday, this.dayThursday, this.dayFriday,
                this.daySaturday, this.daySunday, this.timeLabel, this.timePicker, this.wakeCheck, this.enabledCheck,
                this.computersLabel, this.computersBox, this.okButton, this.cancelButton });
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "ScheduleEditForm";
            this.ShowIcon = false;
            this.ShowInTaskbar = false;
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Lite Deploy - Schedule";
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        private System.Windows.Forms.Label nameLabel;
        private System.Windows.Forms.TextBox nameBox;
        private System.Windows.Forms.Label actionLabel;
        private System.Windows.Forms.ComboBox actionCombo;
        private System.Windows.Forms.RadioButton onceRadio;
        private System.Windows.Forms.DateTimePicker oncePicker;
        private System.Windows.Forms.RadioButton weeklyRadio;
        private System.Windows.Forms.CheckBox dayMonday;
        private System.Windows.Forms.CheckBox dayTuesday;
        private System.Windows.Forms.CheckBox dayWednesday;
        private System.Windows.Forms.CheckBox dayThursday;
        private System.Windows.Forms.CheckBox dayFriday;
        private System.Windows.Forms.CheckBox daySaturday;
        private System.Windows.Forms.CheckBox daySunday;
        private System.Windows.Forms.Label timeLabel;
        private System.Windows.Forms.DateTimePicker timePicker;
        private System.Windows.Forms.CheckBox wakeCheck;
        private System.Windows.Forms.CheckBox enabledCheck;
        private System.Windows.Forms.Label computersLabel;
        private System.Windows.Forms.TextBox computersBox;
        private System.Windows.Forms.Button okButton;
        private System.Windows.Forms.Button cancelButton;
    }
}
