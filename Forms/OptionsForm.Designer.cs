// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    OptionsForm.Designer.cs
// Author:  LiteDeploy contributors
// Date:    2025-05-30
// Purpose: Windows Forms designer part of OptionsForm (layout and controls).
// -----------------------------------------------------------------------------
namespace LiteDeploy
{
    partial class OptionsForm
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(OptionsForm));
            this.groupBox2 = new System.Windows.Forms.GroupBox();
            this.updatesGroup = new System.Windows.Forms.GroupBox();
            this.themeLabel = new System.Windows.Forms.Label();
            this.themeCombo = new System.Windows.Forms.ComboBox();
            this.cacheLabel = new System.Windows.Forms.Label();
            this.cacheBox = new System.Windows.Forms.TextBox();
            this.hoursLabel = new System.Windows.Forms.Label();
            this.hoursStartBox = new System.Windows.Forms.TextBox();
            this.hoursEndBox = new System.Windows.Forms.TextBox();
            this.unsignedModulesCheckBox = new System.Windows.Forms.CheckBox();
            this.languageLabel = new System.Windows.Forms.Label();
            this.languageCombo = new System.Windows.Forms.ComboBox();
            this.button3 = new System.Windows.Forms.Button();
            this.label4 = new System.Windows.Forms.Label();
            this.LogDirDisplay = new System.Windows.Forms.TextBox();
            this.AutoUpADOnStartCheckBox = new System.Windows.Forms.CheckBox();
            this.DirectoryBrowser = new System.Windows.Forms.FolderBrowserDialog();
            this.ApplyButton = new System.Windows.Forms.Button();
            this.cancelButton = new System.Windows.Forms.Button();
            this.groupBox2.SuspendLayout();
            this.SuspendLayout();
            // 
            // groupBox2
            // 
            this.groupBox2.Controls.Add(this.languageLabel);
            this.groupBox2.Controls.Add(this.languageCombo);
            this.groupBox2.Controls.Add(this.themeLabel);
            this.groupBox2.Controls.Add(this.themeCombo);
            this.groupBox2.Controls.Add(this.button3);
            this.groupBox2.Controls.Add(this.label4);
            this.groupBox2.Controls.Add(this.LogDirDisplay);
            this.groupBox2.Controls.Add(this.AutoUpADOnStartCheckBox);
            this.groupBox2.Location = new System.Drawing.Point(12, 12);
            this.groupBox2.Name = "groupBox2";
            this.groupBox2.Size = new System.Drawing.Size(417, 190);
            this.groupBox2.TabIndex = 1;
            this.groupBox2.TabStop = false;
            this.groupBox2.Text = "User";
            // 
            // languageLabel
            // 
            this.languageLabel.AutoSize = true;
            this.languageLabel.Location = new System.Drawing.Point(17, 28);
            this.languageLabel.Name = "languageLabel";
            this.languageLabel.Size = new System.Drawing.Size(58, 13);
            this.languageLabel.TabIndex = 0;
            this.languageLabel.Text = "Language:";
            // 
            // languageCombo
            // 
            this.languageCombo.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.languageCombo.FormattingEnabled = true;
            this.languageCombo.Items.AddRange(new object[] {
            "Automatic (Windows display language)",
            "English",
            "Français"});
            this.languageCombo.Location = new System.Drawing.Point(20, 44);
            this.languageCombo.Name = "languageCombo";
            this.languageCombo.Size = new System.Drawing.Size(190, 21);
            this.languageCombo.TabIndex = 1;
            // 
            // label4
            // 
            this.label4.AutoSize = true;
            this.label4.Location = new System.Drawing.Point(17, 82);
            this.label4.Name = "label4";
            this.label4.Size = new System.Drawing.Size(121, 13);
            this.label4.TabIndex = 2;
            this.label4.Text = "MSI Logging Directory:";
            // 
            // LogDirDisplay
            // 
            this.LogDirDisplay.AutoCompleteMode = System.Windows.Forms.AutoCompleteMode.SuggestAppend;
            this.LogDirDisplay.AutoCompleteSource = System.Windows.Forms.AutoCompleteSource.AllSystemSources;
            this.LogDirDisplay.Location = new System.Drawing.Point(20, 98);
            this.LogDirDisplay.Name = "LogDirDisplay";
            this.LogDirDisplay.Size = new System.Drawing.Size(298, 20);
            this.LogDirDisplay.TabIndex = 3;
            // 
            // button3
            // 
            this.button3.Location = new System.Drawing.Point(324, 96);
            this.button3.Name = "button3";
            this.button3.Size = new System.Drawing.Size(75, 23);
            this.button3.TabIndex = 4;
            this.button3.Text = "Select";
            this.button3.UseVisualStyleBackColor = true;
            this.button3.Click += new System.EventHandler(this.button3_Click);
            // 
            // AutoUpADOnStartCheckBox
            // 
            this.AutoUpADOnStartCheckBox.Location = new System.Drawing.Point(20, 140);
            this.AutoUpADOnStartCheckBox.Name = "AutoUpADOnStartCheckBox";
            this.AutoUpADOnStartCheckBox.Size = new System.Drawing.Size(379, 30);
            this.AutoUpADOnStartCheckBox.TabIndex = 5;
            this.AutoUpADOnStartCheckBox.Text = "Update the Active Directory computer list when the application starts";
            this.AutoUpADOnStartCheckBox.UseVisualStyleBackColor = true;
            // 
            // DirectoryBrowser
            // 
            this.DirectoryBrowser.Description = "Select Directory";
            this.DirectoryBrowser.ShowNewFolderButton = false;
            // 
            // ApplyButton
            // 
            this.ApplyButton.Location = new System.Drawing.Point(237, 390);
            this.ApplyButton.Name = "ApplyButton";
            this.ApplyButton.Size = new System.Drawing.Size(93, 23);
            this.ApplyButton.TabIndex = 6;
            this.ApplyButton.Text = "Save";
            this.ApplyButton.UseVisualStyleBackColor = true;
            this.ApplyButton.Click += new System.EventHandler(this.ApplyButton_Click);
            // 
            // cancelButton
            // 
            this.cancelButton.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.cancelButton.Location = new System.Drawing.Point(336, 390);
            this.cancelButton.Name = "cancelButton";
            this.cancelButton.Size = new System.Drawing.Size(93, 23);
            this.cancelButton.TabIndex = 7;
            this.cancelButton.Text = "Cancel";
            this.cancelButton.UseVisualStyleBackColor = true;
            // 
            // themeLabel / themeCombo (in groupBox2)
            // 
            this.themeLabel.AutoSize = true;
            this.themeLabel.Location = new System.Drawing.Point(220, 28);
            this.themeLabel.Name = "themeLabel";
            this.themeLabel.Text = "Theme:";
            this.themeCombo.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.themeCombo.Items.AddRange(new object[] { "Automatic (Windows)", "Light", "Dark" });
            this.themeCombo.Location = new System.Drawing.Point(223, 44);
            this.themeCombo.Name = "themeCombo";
            this.themeCombo.Size = new System.Drawing.Size(176, 21);
            this.themeCombo.TabIndex = 10;
            // 
            // updatesGroup
            // 
            this.updatesGroup.Controls.Add(this.cacheLabel);
            this.updatesGroup.Controls.Add(this.cacheBox);
            this.updatesGroup.Controls.Add(this.hoursLabel);
            this.updatesGroup.Controls.Add(this.hoursStartBox);
            this.updatesGroup.Controls.Add(this.hoursEndBox);
            this.updatesGroup.Controls.Add(this.unsignedModulesCheckBox);
            this.updatesGroup.Location = new System.Drawing.Point(12, 208);
            this.updatesGroup.Name = "updatesGroup";
            this.updatesGroup.Size = new System.Drawing.Size(417, 170);
            this.updatesGroup.TabIndex = 3;
            this.updatesGroup.TabStop = false;
            this.updatesGroup.Text = "Software updates";
            this.cacheLabel.AutoSize = true;
            this.cacheLabel.Location = new System.Drawing.Point(17, 24);
            this.cacheLabel.Name = "cacheLabel";
            this.cacheLabel.Text = "Shared installer cache (\\\\server\\share, optional):";
            this.cacheBox.Location = new System.Drawing.Point(20, 40);
            this.cacheBox.Name = "cacheBox";
            this.cacheBox.Size = new System.Drawing.Size(379, 20);
            this.cacheBox.TabIndex = 11;
            this.hoursLabel.AutoSize = true;
            this.hoursLabel.Location = new System.Drawing.Point(17, 74);
            this.hoursLabel.Name = "hoursLabel";
            this.hoursLabel.Text = "Business hours (Monday to Friday, HH:mm):";
            this.hoursStartBox.Location = new System.Drawing.Point(20, 90);
            this.hoursStartBox.Name = "hoursStartBox";
            this.hoursStartBox.Size = new System.Drawing.Size(60, 20);
            this.hoursStartBox.TabIndex = 12;
            this.hoursEndBox.Location = new System.Drawing.Point(90, 90);
            this.hoursEndBox.Name = "hoursEndBox";
            this.hoursEndBox.Size = new System.Drawing.Size(60, 20);
            this.hoursEndBox.TabIndex = 13;
            this.unsignedModulesCheckBox.Location = new System.Drawing.Point(20, 120);
            this.unsignedModulesCheckBox.Name = "unsignedModulesCheckBox";
            this.unsignedModulesCheckBox.Size = new System.Drawing.Size(379, 40);
            this.unsignedModulesCheckBox.TabIndex = 14;
            this.unsignedModulesCheckBox.Text = "Run unsigned update modules (tests only: they run as SYSTEM on the computers)";
            this.unsignedModulesCheckBox.UseVisualStyleBackColor = true;
            // 
            // OptionsForm
            // 
            this.AcceptButton = this.ApplyButton;
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.CancelButton = this.cancelButton;
            this.ClientSize = new System.Drawing.Size(445, 425);
            this.Controls.Add(this.cancelButton);
            this.Controls.Add(this.ApplyButton);
            this.Controls.Add(this.groupBox2);
            this.Controls.Add(this.updatesGroup);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "OptionsForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Lite Deploy - Options";
            this.Load += new System.EventHandler(this.OptionsForm_Load);
            this.groupBox2.ResumeLayout(false);
            this.groupBox2.PerformLayout();
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.GroupBox groupBox2;
        private System.Windows.Forms.Label languageLabel;
        private System.Windows.Forms.ComboBox languageCombo;
        private System.Windows.Forms.CheckBox AutoUpADOnStartCheckBox;
        private System.Windows.Forms.Button button3;
        private System.Windows.Forms.Label label4;
        private System.Windows.Forms.TextBox LogDirDisplay;
        private System.Windows.Forms.FolderBrowserDialog DirectoryBrowser;
        private System.Windows.Forms.Button ApplyButton;
        private System.Windows.Forms.Button cancelButton;
        private System.Windows.Forms.Label themeLabel;
        private System.Windows.Forms.ComboBox themeCombo;
        private System.Windows.Forms.GroupBox updatesGroup;
        private System.Windows.Forms.Label cacheLabel;
        private System.Windows.Forms.TextBox cacheBox;
        private System.Windows.Forms.Label hoursLabel;
        private System.Windows.Forms.TextBox hoursStartBox;
        private System.Windows.Forms.TextBox hoursEndBox;
        private System.Windows.Forms.CheckBox unsignedModulesCheckBox;
    }
}
