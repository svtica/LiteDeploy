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
            this.cacheButton = new System.Windows.Forms.Button();
            this.sharedModulesLabel = new System.Windows.Forms.Label();
            this.sharedModulesBox = new System.Windows.Forms.TextBox();
            this.sharedModulesButton = new System.Windows.Forms.Button();
            this.hoursLabel = new System.Windows.Forms.Label();
            this.hoursStartBox = new System.Windows.Forms.TextBox();
            this.hoursEndBox = new System.Windows.Forms.TextBox();
            this.parallelLabel = new System.Windows.Forms.Label();
            this.parallelBox = new System.Windows.Forms.NumericUpDown();
            ((System.ComponentModel.ISupportInitialize)(this.parallelBox)).BeginInit();
            this.warnLabel = new System.Windows.Forms.Label();
            this.warnBox = new System.Windows.Forms.NumericUpDown();
            ((System.ComponentModel.ISupportInitialize)(this.warnBox)).BeginInit();
            this.postponeCheckBox = new System.Windows.Forms.CheckBox();
            this.unsignedModulesCheckBox = new System.Windows.Forms.CheckBox();
            this.accountLabel = new System.Windows.Forms.Label();
            this.accountCombo = new System.Windows.Forms.ComboBox();
            this.methodLabel = new System.Windows.Forms.Label();
            this.methodCombo = new System.Windows.Forms.ComboBox();
            this.includeServersCheckBox = new System.Windows.Forms.CheckBox();
            this.siwGroup = new System.Windows.Forms.GroupBox();
            this.siwFolderLabel = new System.Windows.Forms.Label();
            this.siwFolderBox = new System.Windows.Forms.TextBox();
            this.siwFolderButton = new System.Windows.Forms.Button();
            this.siwHtmlCheckBox = new System.Windows.Forms.CheckBox();
            this.toolsFolderLabel = new System.Windows.Forms.Label();
            this.toolsFolderBox = new System.Windows.Forms.TextBox();
            this.toolsFolderButton = new System.Windows.Forms.Button();
            this.languageLabel = new System.Windows.Forms.Label();
            this.languageCombo = new System.Windows.Forms.ComboBox();
            this.button3 = new System.Windows.Forms.Button();
            this.label4 = new System.Windows.Forms.Label();
            this.LogDirDisplay = new System.Windows.Forms.TextBox();
            this.AutoUpADOnStartCheckBox = new System.Windows.Forms.CheckBox();
            this.deploymentsCheckBox = new System.Windows.Forms.CheckBox();
            this.checkUpdatesCheckBox = new System.Windows.Forms.CheckBox();
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
            this.groupBox2.Controls.Add(this.deploymentsCheckBox);
            this.groupBox2.Controls.Add(this.checkUpdatesCheckBox);
            this.groupBox2.Location = new System.Drawing.Point(12, 12);
            this.groupBox2.Name = "groupBox2";
            this.groupBox2.Size = new System.Drawing.Size(417, 247);
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
            // deploymentsCheckBox
            // 
            this.deploymentsCheckBox.Location = new System.Drawing.Point(20, 172);
            this.deploymentsCheckBox.Name = "deploymentsCheckBox";
            this.deploymentsCheckBox.Size = new System.Drawing.Size(379, 30);
            this.deploymentsCheckBox.TabIndex = 6;
            this.deploymentsCheckBox.Text = "Show the standalone deployments (file or command through PsExec)";
            this.deploymentsCheckBox.UseVisualStyleBackColor = true;
            // 
            // checkUpdatesCheckBox
            // 
            this.checkUpdatesCheckBox.Location = new System.Drawing.Point(20, 204);
            this.checkUpdatesCheckBox.Name = "checkUpdatesCheckBox";
            this.checkUpdatesCheckBox.Size = new System.Drawing.Size(379, 30);
            this.checkUpdatesCheckBox.TabIndex = 7;
            this.checkUpdatesCheckBox.Text = "Check for updates when the application starts";
            this.checkUpdatesCheckBox.UseVisualStyleBackColor = true;
            // 
            // DirectoryBrowser
            // 
            this.DirectoryBrowser.Description = "Select Directory";
            this.DirectoryBrowser.ShowNewFolderButton = false;
            // 
            // ApplyButton
            // 
            this.ApplyButton.Location = new System.Drawing.Point(237, 860);
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
            this.cancelButton.Location = new System.Drawing.Point(336, 860);
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
            this.updatesGroup.Controls.Add(this.cacheButton);
            this.updatesGroup.Controls.Add(this.sharedModulesLabel);
            this.updatesGroup.Controls.Add(this.sharedModulesBox);
            this.updatesGroup.Controls.Add(this.sharedModulesButton);
            this.updatesGroup.Controls.Add(this.hoursLabel);
            this.updatesGroup.Controls.Add(this.hoursStartBox);
            this.updatesGroup.Controls.Add(this.hoursEndBox);
            this.updatesGroup.Controls.Add(this.parallelLabel);
            this.updatesGroup.Controls.Add(this.parallelBox);
            this.updatesGroup.Controls.Add(this.warnLabel);
            this.updatesGroup.Controls.Add(this.warnBox);
            this.updatesGroup.Controls.Add(this.postponeCheckBox);
            this.updatesGroup.Controls.Add(this.unsignedModulesCheckBox);
            this.updatesGroup.Controls.Add(this.accountLabel);
            this.updatesGroup.Controls.Add(this.accountCombo);
            this.updatesGroup.Controls.Add(this.methodLabel);
            this.updatesGroup.Controls.Add(this.methodCombo);
            this.updatesGroup.Controls.Add(this.includeServersCheckBox);
            this.updatesGroup.Location = new System.Drawing.Point(12, 265);
            this.updatesGroup.Name = "updatesGroup";
            this.updatesGroup.Size = new System.Drawing.Size(417, 421);
            this.updatesGroup.TabIndex = 3;
            this.updatesGroup.TabStop = false;
            this.updatesGroup.Text = "Software updates";
            this.cacheLabel.AutoSize = true;
            this.cacheLabel.Location = new System.Drawing.Point(17, 24);
            this.cacheLabel.Name = "cacheLabel";
            this.cacheLabel.Text = "Shared installer cache (\\\\server\\share, optional):";
            this.cacheBox.Location = new System.Drawing.Point(20, 40);
            this.cacheBox.Name = "cacheBox";
            this.cacheBox.Size = new System.Drawing.Size(298, 20);
            this.cacheBox.TabIndex = 11;
            this.cacheButton.Location = new System.Drawing.Point(324, 38);
            this.cacheButton.Name = "cacheButton";
            this.cacheButton.Size = new System.Drawing.Size(75, 23);
            this.cacheButton.TabIndex = 11;
            this.cacheButton.Text = "Select";
            this.cacheButton.UseVisualStyleBackColor = true;
            this.cacheButton.Click += new System.EventHandler(this.cacheButton_Click);
            this.sharedModulesLabel.AutoSize = true;
            this.sharedModulesLabel.Location = new System.Drawing.Point(17, 70);
            this.sharedModulesLabel.Name = "sharedModulesLabel";
            this.sharedModulesLabel.Text = "Shared update modules folder (optional):";
            this.sharedModulesBox.Location = new System.Drawing.Point(20, 86);
            this.sharedModulesBox.Name = "sharedModulesBox";
            this.sharedModulesBox.Size = new System.Drawing.Size(298, 20);
            this.sharedModulesBox.TabIndex = 12;
            this.sharedModulesButton.Location = new System.Drawing.Point(324, 84);
            this.sharedModulesButton.Name = "sharedModulesButton";
            this.sharedModulesButton.Size = new System.Drawing.Size(75, 23);
            this.sharedModulesButton.TabIndex = 13;
            this.sharedModulesButton.Text = "Select";
            this.sharedModulesButton.UseVisualStyleBackColor = true;
            this.sharedModulesButton.Click += new System.EventHandler(this.sharedModulesButton_Click);
            this.hoursLabel.AutoSize = true;
            this.hoursLabel.Location = new System.Drawing.Point(17, 120);
            this.hoursLabel.Name = "hoursLabel";
            this.hoursLabel.Text = "Business hours (Monday to Friday, HH:mm):";
            this.hoursStartBox.Location = new System.Drawing.Point(20, 136);
            this.hoursStartBox.Name = "hoursStartBox";
            this.hoursStartBox.Size = new System.Drawing.Size(60, 20);
            this.hoursStartBox.TabIndex = 14;
            this.hoursEndBox.Location = new System.Drawing.Point(90, 136);
            this.hoursEndBox.Name = "hoursEndBox";
            this.hoursEndBox.Size = new System.Drawing.Size(60, 20);
            this.hoursEndBox.TabIndex = 15;
            this.parallelLabel.AutoSize = true;
            this.parallelLabel.Location = new System.Drawing.Point(17, 339);
            this.parallelLabel.Name = "parallelLabel";
            this.parallelLabel.Text = "Computers processed at the same time (1 to 32):";
            this.parallelBox.Location = new System.Drawing.Point(339, 336);
            this.parallelBox.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            this.parallelBox.Maximum = new decimal(new int[] { 32, 0, 0, 0 });
            this.parallelBox.Value = new decimal(new int[] { 4, 0, 0, 0 });
            this.parallelBox.Name = "parallelBox";
            this.parallelBox.Size = new System.Drawing.Size(60, 20);
            this.parallelBox.TabIndex = 20;
            this.warnLabel.AutoSize = true;
            this.warnLabel.Location = new System.Drawing.Point(17, 369);
            this.warnLabel.Name = "warnLabel";
            this.warnLabel.Text = "Warn before closing the applications (seconds, 0: no):";
            this.warnBox.Location = new System.Drawing.Point(339, 366);
            this.warnBox.Minimum = new decimal(new int[] { 0, 0, 0, 0 });
            this.warnBox.Maximum = new decimal(new int[] { 900, 0, 0, 0 });
            this.warnBox.Increment = new decimal(new int[] { 30, 0, 0, 0 });
            this.warnBox.Value = new decimal(new int[] { 120, 0, 0, 0 });
            this.warnBox.Name = "warnBox";
            this.warnBox.Size = new System.Drawing.Size(60, 20);
            this.warnBox.TabIndex = 21;
            this.postponeCheckBox.Location = new System.Drawing.Point(20, 392);
            this.postponeCheckBox.Name = "postponeCheckBox";
            this.postponeCheckBox.Size = new System.Drawing.Size(379, 24);
            this.postponeCheckBox.TabIndex = 22;
            this.postponeCheckBox.Text = "The users can postpone the update (Cancel in the warning)";
            this.postponeCheckBox.UseVisualStyleBackColor = true;
            this.unsignedModulesCheckBox.Location = new System.Drawing.Point(20, 166);
            this.unsignedModulesCheckBox.Name = "unsignedModulesCheckBox";
            this.unsignedModulesCheckBox.Size = new System.Drawing.Size(379, 40);
            this.unsignedModulesCheckBox.TabIndex = 16;
            this.unsignedModulesCheckBox.Text = "Run unsigned update modules (tests only: they run as SYSTEM on the computers)";
            this.unsignedModulesCheckBox.UseVisualStyleBackColor = true;
            this.accountLabel.AutoSize = true;
            this.accountLabel.Location = new System.Drawing.Point(17, 212);
            this.accountLabel.Name = "accountLabel";
            this.accountLabel.Text = "Account (Tools > Credentials):";
            this.accountCombo.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.accountCombo.Location = new System.Drawing.Point(20, 228);
            this.accountCombo.Name = "accountCombo";
            this.accountCombo.Size = new System.Drawing.Size(379, 21);
            this.accountCombo.TabIndex = 17;
            this.methodLabel.AutoSize = true;
            this.methodLabel.Location = new System.Drawing.Point(17, 260);
            this.methodLabel.Name = "methodLabel";
            this.methodLabel.Text = "Remote method:";
            this.methodCombo.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.methodCombo.Location = new System.Drawing.Point(20, 276);
            this.methodCombo.Name = "methodCombo";
            this.methodCombo.Size = new System.Drawing.Size(379, 21);
            this.methodCombo.TabIndex = 18;
            this.includeServersCheckBox.Location = new System.Drawing.Point(20, 306);
            this.includeServersCheckBox.Name = "includeServersCheckBox";
            this.includeServersCheckBox.Size = new System.Drawing.Size(379, 24);
            this.includeServersCheckBox.TabIndex = 19;
            this.includeServersCheckBox.Text = "Include servers (updates and installs)";
            this.includeServersCheckBox.UseVisualStyleBackColor = true;
            //
            // siwGroup
            //
            this.siwGroup.Controls.Add(this.siwFolderLabel);
            this.siwGroup.Controls.Add(this.siwFolderBox);
            this.siwGroup.Controls.Add(this.siwFolderButton);
            this.siwGroup.Controls.Add(this.siwHtmlCheckBox);
            this.siwGroup.Controls.Add(this.toolsFolderLabel);
            this.siwGroup.Controls.Add(this.toolsFolderBox);
            this.siwGroup.Controls.Add(this.toolsFolderButton);
            this.siwGroup.Location = new System.Drawing.Point(12, 692);
            this.siwGroup.Name = "siwGroup";
            this.siwGroup.Size = new System.Drawing.Size(417, 156);
            this.siwGroup.TabIndex = 4;
            this.siwGroup.TabStop = false;
            this.siwGroup.Text = "Third-party report tools (not shipped)";
            this.siwFolderLabel.AutoSize = true;
            this.siwFolderLabel.Location = new System.Drawing.Point(17, 24);
            this.siwFolderLabel.Name = "siwFolderLabel";
            this.siwFolderLabel.Text = "Folder that holds SIW (System Information for Windows, optional):";
            this.siwFolderBox.Location = new System.Drawing.Point(20, 40);
            this.siwFolderBox.Name = "siwFolderBox";
            this.siwFolderBox.Size = new System.Drawing.Size(298, 20);
            this.siwFolderBox.TabIndex = 20;
            this.siwFolderButton.Location = new System.Drawing.Point(324, 38);
            this.siwFolderButton.Name = "siwFolderButton";
            this.siwFolderButton.Size = new System.Drawing.Size(75, 23);
            this.siwFolderButton.TabIndex = 21;
            this.siwFolderButton.Text = "Select";
            this.siwFolderButton.UseVisualStyleBackColor = true;
            this.siwFolderButton.Click += new System.EventHandler(this.siwFolderButton_Click);
            this.siwHtmlCheckBox.Location = new System.Drawing.Point(20, 68);
            this.siwHtmlCheckBox.Name = "siwHtmlCheckBox";
            this.siwHtmlCheckBox.Size = new System.Drawing.Size(379, 30);
            this.siwHtmlCheckBox.TabIndex = 22;
            this.siwHtmlCheckBox.Text = "Also the HTML report of SIW (SIW runs twice); off: XML report shown by LiteDeploy";
            this.siwHtmlCheckBox.UseVisualStyleBackColor = true;
            this.toolsFolderLabel.AutoSize = true;
            this.toolsFolderLabel.Location = new System.Drawing.Point(17, 104);
            this.toolsFolderLabel.Name = "toolsFolderLabel";
            this.toolsFolderLabel.Text = "Folder of the portable tools (UninstallView, WinUpdatesView, optional):";
            this.toolsFolderBox.Location = new System.Drawing.Point(20, 120);
            this.toolsFolderBox.Name = "toolsFolderBox";
            this.toolsFolderBox.Size = new System.Drawing.Size(298, 20);
            this.toolsFolderBox.TabIndex = 23;
            this.toolsFolderButton.Location = new System.Drawing.Point(324, 118);
            this.toolsFolderButton.Name = "toolsFolderButton";
            this.toolsFolderButton.Size = new System.Drawing.Size(75, 23);
            this.toolsFolderButton.TabIndex = 24;
            this.toolsFolderButton.Text = "Select";
            this.toolsFolderButton.UseVisualStyleBackColor = true;
            this.toolsFolderButton.Click += new System.EventHandler(this.toolsFolderButton_Click);
            //
            // OptionsForm
            // 
            this.AcceptButton = this.ApplyButton;
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.CancelButton = this.cancelButton;
            this.ClientSize = new System.Drawing.Size(445, 895);
            this.Controls.Add(this.cancelButton);
            this.Controls.Add(this.ApplyButton);
            this.Controls.Add(this.groupBox2);
            this.Controls.Add(this.updatesGroup);
            this.Controls.Add(this.siwGroup);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "OptionsForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Lite Deploy - Options";
            this.Load += new System.EventHandler(this.OptionsForm_Load);
            ((System.ComponentModel.ISupportInitialize)(this.parallelBox)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.warnBox)).EndInit();
            this.groupBox2.ResumeLayout(false);
            this.groupBox2.PerformLayout();
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.GroupBox groupBox2;
        private System.Windows.Forms.Label languageLabel;
        private System.Windows.Forms.ComboBox languageCombo;
        private System.Windows.Forms.CheckBox AutoUpADOnStartCheckBox;
        private System.Windows.Forms.CheckBox deploymentsCheckBox;
        private System.Windows.Forms.CheckBox checkUpdatesCheckBox;
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
        private System.Windows.Forms.Button cacheButton;
        private System.Windows.Forms.Label sharedModulesLabel;
        private System.Windows.Forms.TextBox sharedModulesBox;
        private System.Windows.Forms.Button sharedModulesButton;
        private System.Windows.Forms.Label hoursLabel;
        private System.Windows.Forms.TextBox hoursStartBox;
        private System.Windows.Forms.TextBox hoursEndBox;
        private System.Windows.Forms.Label parallelLabel;
        private System.Windows.Forms.NumericUpDown parallelBox;
        private System.Windows.Forms.Label warnLabel;
        private System.Windows.Forms.NumericUpDown warnBox;
        private System.Windows.Forms.CheckBox postponeCheckBox;
        private System.Windows.Forms.CheckBox unsignedModulesCheckBox;
        private System.Windows.Forms.Label accountLabel;
        private System.Windows.Forms.ComboBox accountCombo;
        private System.Windows.Forms.Label methodLabel;
        private System.Windows.Forms.ComboBox methodCombo;
        private System.Windows.Forms.CheckBox includeServersCheckBox;
        private System.Windows.Forms.GroupBox siwGroup;
        private System.Windows.Forms.Label siwFolderLabel;
        private System.Windows.Forms.TextBox siwFolderBox;
        private System.Windows.Forms.Button siwFolderButton;
        private System.Windows.Forms.CheckBox siwHtmlCheckBox;
        private System.Windows.Forms.Label toolsFolderLabel;
        private System.Windows.Forms.TextBox toolsFolderBox;
        private System.Windows.Forms.Button toolsFolderButton;
    }
}
