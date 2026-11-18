// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    DeployForm.Designer.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-18
// Purpose: Controls of the deployment window (file or command run through
//          PsExec on the computers of the grid).
// -----------------------------------------------------------------------------
namespace LiteDeploy
{
    partial class DeployForm
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
            this.groupBox1 = new System.Windows.Forms.GroupBox();
            this.savedCredentialButton = new System.Windows.Forms.Button();
            this.systemAccount = new System.Windows.Forms.CheckBox();
            this.credentials = new System.Windows.Forms.CheckBox();
            this.password = new System.Windows.Forms.TextBox();
            this.username = new System.Windows.Forms.TextBox();
            this.label2 = new System.Windows.Forms.Label();
            this.label1 = new System.Windows.Forms.Label();
            this.groupBox2 = new System.Windows.Forms.GroupBox();
            this.clearExec = new System.Windows.Forms.Button();
            this.arguments = new System.Windows.Forms.TextBox();
            this.label9 = new System.Windows.Forms.Label();
            this.selectFileToExecute = new System.Windows.Forms.Button();
            this.label4 = new System.Windows.Forms.Label();
            this.executable = new System.Windows.Forms.TextBox();
            this.startDeploy = new System.Windows.Forms.Button();
            this.openExecutable = new System.Windows.Forms.OpenFileDialog();
            this.label6 = new System.Windows.Forms.Label();
            this.timeOut = new System.Windows.Forms.NumericUpDown();
            this.copy = new System.Windows.Forms.CheckBox();
            this.wait = new System.Windows.Forms.CheckBox();
            this.label7 = new System.Windows.Forms.Label();
            this.priority = new System.Windows.Forms.ComboBox();
            this.forceCopy = new System.Windows.Forms.CheckBox();
            this.label8 = new System.Windows.Forms.Label();
            this.groupBox3 = new System.Windows.Forms.GroupBox();
            this.relWorkingPath = new System.Windows.Forms.CheckBox();
            this.isessionNum = new System.Windows.Forms.NumericUpDown();
            this.interactive = new System.Windows.Forms.CheckBox();
            this.toolTip = new System.Windows.Forms.ToolTip(this.components);
            this.targetsLabel = new System.Windows.Forms.Label();
            this.restartCommand = new System.Windows.Forms.Button();
            this.shutdownCommand = new System.Windows.Forms.Button();
            this.groupBox1.SuspendLayout();
            this.groupBox2.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.timeOut)).BeginInit();
            this.groupBox3.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.isessionNum)).BeginInit();
            this.SuspendLayout();
            // 
            // targetsLabel
            // 
            this.targetsLabel.AutoEllipsis = true;
            this.targetsLabel.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Bold);
            this.targetsLabel.Location = new System.Drawing.Point(10, 8);
            this.targetsLabel.Name = "targetsLabel";
            this.targetsLabel.Size = new System.Drawing.Size(752, 20);
            this.targetsLabel.TabIndex = 0;
            // 
            // restartCommand
            // 
            this.restartCommand.Location = new System.Drawing.Point(13, 155);
            this.restartCommand.Name = "restartCommand";
            this.restartCommand.Size = new System.Drawing.Size(107, 28);
            this.restartCommand.TabIndex = 3;
            this.restartCommand.Text = "Restart";
            this.restartCommand.UseVisualStyleBackColor = true;
            this.restartCommand.Click += new System.EventHandler(this.restartCommand_Click);
            // 
            // shutdownCommand
            // 
            this.shutdownCommand.Location = new System.Drawing.Point(127, 155);
            this.shutdownCommand.Name = "shutdownCommand";
            this.shutdownCommand.Size = new System.Drawing.Size(107, 28);
            this.shutdownCommand.TabIndex = 4;
            this.shutdownCommand.Text = "Shutdown";
            this.shutdownCommand.UseVisualStyleBackColor = true;
            this.shutdownCommand.Click += new System.EventHandler(this.shutdownCommand_Click);
            // 
            // groupBox1
            // 
            this.groupBox1.Controls.Add(this.systemAccount);
            this.groupBox1.Controls.Add(this.savedCredentialButton);
            this.groupBox1.Controls.Add(this.credentials);
            this.groupBox1.Controls.Add(this.password);
            this.groupBox1.Controls.Add(this.username);
            this.groupBox1.Controls.Add(this.label2);
            this.groupBox1.Controls.Add(this.label1);
            this.groupBox1.Location = new System.Drawing.Point(10, 32);
            this.groupBox1.Name = "groupBox1";
            this.groupBox1.Size = new System.Drawing.Size(255, 157);
            this.groupBox1.TabIndex = 1;
            this.groupBox1.TabStop = false;
            this.groupBox1.Text = "Credentials";
            // 
            // savedCredentialButton
            // 
            this.savedCredentialButton.Location = new System.Drawing.Point(150, 16);
            this.savedCredentialButton.Name = "savedCredentialButton";
            this.savedCredentialButton.Size = new System.Drawing.Size(94, 23);
            this.savedCredentialButton.TabIndex = 6;
            this.savedCredentialButton.Text = "Saved...";
            this.savedCredentialButton.UseVisualStyleBackColor = true;
            this.savedCredentialButton.Click += new System.EventHandler(this.savedCredentialButton_Click);
            // 
            // systemAccount
            // 
            this.systemAccount.AutoSize = true;
            this.systemAccount.Location = new System.Drawing.Point(15, 128);
            this.systemAccount.Name = "systemAccount";
            this.systemAccount.Size = new System.Drawing.Size(82, 17);
            this.systemAccount.TabIndex = 5;
            this.systemAccount.Text = "Use System";
            this.systemAccount.UseVisualStyleBackColor = true;
            this.systemAccount.CheckedChanged += new System.EventHandler(this.systemAccount_CheckedChanged);
            // 
            // credentials
            // 
            this.credentials.AutoSize = true;
            this.credentials.Location = new System.Drawing.Point(144, 128);
            this.credentials.Name = "credentials";
            this.credentials.Size = new System.Drawing.Size(100, 17);
            this.credentials.TabIndex = 4;
            this.credentials.Text = "Use Credentials";
            this.credentials.UseVisualStyleBackColor = true;
            this.credentials.CheckedChanged += new System.EventHandler(this.credentials_CheckedChanged);
            // 
            // password
            // 
            this.password.Location = new System.Drawing.Point(15, 95);
            this.password.Name = "password";
            this.password.PasswordChar = '*';
            this.password.Size = new System.Drawing.Size(229, 20);
            this.password.TabIndex = 3;
            // 
            // username
            // 
            this.username.Location = new System.Drawing.Point(15, 42);
            this.username.Name = "username";
            this.username.Size = new System.Drawing.Size(229, 20);
            this.username.TabIndex = 2;
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.Location = new System.Drawing.Point(12, 79);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(56, 13);
            this.label2.TabIndex = 1;
            this.label2.Text = "Password:";
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(12, 26);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(63, 13);
            this.label1.TabIndex = 0;
            this.label1.Text = "User Name:";
            // 
            // groupBox2
            // 
            this.groupBox2.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.groupBox2.Controls.Add(this.restartCommand);
            this.groupBox2.Controls.Add(this.shutdownCommand);
            this.groupBox2.Controls.Add(this.clearExec);
            this.groupBox2.Controls.Add(this.arguments);
            this.groupBox2.Controls.Add(this.label9);
            this.groupBox2.Controls.Add(this.selectFileToExecute);
            this.groupBox2.Controls.Add(this.label4);
            this.groupBox2.Controls.Add(this.executable);
            this.groupBox2.Location = new System.Drawing.Point(271, 32);
            this.groupBox2.Name = "groupBox2";
            this.groupBox2.Size = new System.Drawing.Size(242, 199);
            this.groupBox2.TabIndex = 2;
            this.groupBox2.TabStop = false;
            this.groupBox2.Text = "Deployment";
            // 
            // clearExec
            // 
            this.clearExec.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.clearExec.Font = new System.Drawing.Font("Microsoft Sans Serif", 6.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.clearExec.ForeColor = System.Drawing.Color.Red;
            this.clearExec.Location = new System.Drawing.Point(208, 13);
            this.clearExec.Name = "clearExec";
            this.clearExec.Size = new System.Drawing.Size(26, 23);
            this.clearExec.TabIndex = 0;
            this.clearExec.Text = "X";
            this.clearExec.UseVisualStyleBackColor = true;
            this.clearExec.Click += new System.EventHandler(this.clearExec_Click);
            // 
            // arguments
            // 
            this.arguments.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.arguments.Location = new System.Drawing.Point(13, 120);
            this.arguments.Name = "arguments";
            this.arguments.Size = new System.Drawing.Size(221, 20);
            this.arguments.TabIndex = 2;
            // 
            // label9
            // 
            this.label9.AutoSize = true;
            this.label9.Location = new System.Drawing.Point(10, 104);
            this.label9.Name = "label9";
            this.label9.Size = new System.Drawing.Size(122, 13);
            this.label9.TabIndex = 7;
            this.label9.Text = "Arguments or Command:";
            // 
            // selectFileToExecute
            // 
            this.selectFileToExecute.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.selectFileToExecute.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.selectFileToExecute.Location = new System.Drawing.Point(208, 76);
            this.selectFileToExecute.Name = "selectFileToExecute";
            this.selectFileToExecute.Size = new System.Drawing.Size(26, 23);
            this.selectFileToExecute.TabIndex = 1;
            this.selectFileToExecute.Text = ">";
            this.selectFileToExecute.UseVisualStyleBackColor = true;
            this.selectFileToExecute.Click += new System.EventHandler(this.selectFileToExecute_Click);
            // 
            // label4
            // 
            this.label4.AutoSize = true;
            this.label4.Location = new System.Drawing.Point(10, 37);
            this.label4.Name = "label4";
            this.label4.Size = new System.Drawing.Size(80, 13);
            this.label4.TabIndex = 1;
            this.label4.Text = "File to Execute:";
            // 
            // executable
            // 
            this.executable.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.executable.Location = new System.Drawing.Point(13, 53);
            this.executable.Name = "executable";
            this.executable.ReadOnly = true;
            this.executable.Size = new System.Drawing.Size(221, 20);
            this.executable.TabIndex = 0;
            this.executable.TextChanged += new System.EventHandler(this.executable_TextChanged);
            // 
            // startDeploy
            // 
            this.startDeploy.Location = new System.Drawing.Point(10, 194);
            this.startDeploy.Name = "startDeploy";
            this.startDeploy.Size = new System.Drawing.Size(255, 36);
            this.startDeploy.TabIndex = 5;
            this.startDeploy.Text = "Start Deploy";
            this.startDeploy.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Bold);
            this.startDeploy.UseVisualStyleBackColor = true;
            this.startDeploy.Click += new System.EventHandler(this.startDeploy_Click);
            // 
            // openExecutable
            // 
            this.openExecutable.Filter = "All Files|*.*";
            this.openExecutable.Title = "Select File to Execute";
            this.openExecutable.FileOk += new System.ComponentModel.CancelEventHandler(this.openExecutable_FileOk);
            // 
            // label6
            // 
            this.label6.AutoSize = true;
            this.label6.Location = new System.Drawing.Point(18, 23);
            this.label6.Name = "label6";
            this.label6.Size = new System.Drawing.Size(110, 13);
            this.label6.TabIndex = 0;
            this.label6.Text = "Connection Time Out:";
            // 
            // timeOut
            // 
            this.timeOut.Location = new System.Drawing.Point(135, 20);
            this.timeOut.Maximum = new decimal(new int[] {
            10000,
            0,
            0,
            0});
            this.timeOut.Name = "timeOut";
            this.timeOut.Size = new System.Drawing.Size(79, 20);
            this.timeOut.TabIndex = 1;
            this.timeOut.TextAlign = System.Windows.Forms.HorizontalAlignment.Center;
            // 
            // copy
            // 
            this.copy.AutoSize = true;
            this.copy.Location = new System.Drawing.Point(21, 47);
            this.copy.Name = "copy";
            this.copy.Size = new System.Drawing.Size(105, 17);
            this.copy.TabIndex = 2;
            this.copy.Text = "Copy executable";
            this.copy.UseVisualStyleBackColor = true;
            // 
            // wait
            // 
            this.wait.AutoSize = true;
            this.wait.Location = new System.Drawing.Point(21, 93);
            this.wait.Name = "wait";
            this.wait.Size = new System.Drawing.Size(155, 17);
            this.wait.TabIndex = 4;
            this.wait.Text = "Don\'t wait for each process";
            this.wait.UseVisualStyleBackColor = true;
            // 
            // label7
            // 
            this.label7.AutoSize = true;
            this.label7.Location = new System.Drawing.Point(18, 168);
            this.label7.Name = "label7";
            this.label7.Size = new System.Drawing.Size(64, 13);
            this.label7.TabIndex = 8;
            this.label7.Text = "Run Priority:";
            // 
            // priority
            // 
            this.priority.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.priority.FlatStyle = System.Windows.Forms.FlatStyle.System;
            this.priority.FormattingEnabled = true;
            this.priority.Items.AddRange(new object[] {
            "Low",
            "Below Normal",
            "Normal",
            "Above Normal",
            "High",
            "Realtime"});
            this.priority.Location = new System.Drawing.Point(88, 165);
            this.priority.Name = "priority";
            this.priority.Size = new System.Drawing.Size(127, 21);
            this.priority.TabIndex = 8;
            // 
            // forceCopy
            // 
            this.forceCopy.AutoSize = true;
            this.forceCopy.Location = new System.Drawing.Point(21, 70);
            this.forceCopy.Name = "forceCopy";
            this.forceCopy.Size = new System.Drawing.Size(109, 17);
            this.forceCopy.TabIndex = 3;
            this.forceCopy.Text = "Overwrite existing";
            this.forceCopy.UseVisualStyleBackColor = true;
            // 
            // label8
            // 
            this.label8.AutoSize = true;
            this.label8.Location = new System.Drawing.Point(246, 26);
            this.label8.Name = "label8";
            this.label8.Size = new System.Drawing.Size(56, 13);
            this.label8.TabIndex = 11;
            this.label8.Text = "Instances:";
            this.label8.Visible = false;
            // 
            // groupBox3
            // 
            this.groupBox3.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.groupBox3.Controls.Add(this.relWorkingPath);
            this.groupBox3.Controls.Add(this.isessionNum);
            this.groupBox3.Controls.Add(this.interactive);
            this.groupBox3.Controls.Add(this.label8);
            this.groupBox3.Controls.Add(this.forceCopy);
            this.groupBox3.Controls.Add(this.priority);
            this.groupBox3.Controls.Add(this.label7);
            this.groupBox3.Controls.Add(this.wait);
            this.groupBox3.Controls.Add(this.copy);
            this.groupBox3.Controls.Add(this.timeOut);
            this.groupBox3.Controls.Add(this.label6);
            this.groupBox3.Location = new System.Drawing.Point(519, 32);
            this.groupBox3.Name = "groupBox3";
            this.groupBox3.Size = new System.Drawing.Size(243, 199);
            this.groupBox3.TabIndex = 6;
            this.groupBox3.TabStop = false;
            this.groupBox3.Text = "Settings";
            // 
            // relWorkingPath
            // 
            this.relWorkingPath.AutoSize = true;
            this.relWorkingPath.Location = new System.Drawing.Point(21, 140);
            this.relWorkingPath.Name = "relWorkingPath";
            this.relWorkingPath.Size = new System.Drawing.Size(111, 17);
            this.relWorkingPath.TabIndex = 7;
            this.relWorkingPath.Text = "Working Directory";
            this.relWorkingPath.UseVisualStyleBackColor = true;
            this.relWorkingPath.CheckedChanged += new System.EventHandler(this.relWorkingPath_CheckedChanged);
            // 
            // isessionNum
            // 
            this.isessionNum.Location = new System.Drawing.Point(148, 115);
            this.isessionNum.Maximum = new decimal(new int[] {
            99999,
            0,
            0,
            0});
            this.isessionNum.Name = "isessionNum";
            this.isessionNum.Size = new System.Drawing.Size(79, 20);
            this.isessionNum.TabIndex = 6;
            this.isessionNum.TextAlign = System.Windows.Forms.HorizontalAlignment.Center;
            // 
            // interactive
            // 
            this.interactive.AutoSize = true;
            this.interactive.Location = new System.Drawing.Point(21, 116);
            this.interactive.Name = "interactive";
            this.interactive.Size = new System.Drawing.Size(125, 17);
            this.interactive.TabIndex = 5;
            this.interactive.Text = "Interactive - Session:";
            this.interactive.UseVisualStyleBackColor = true;
            this.interactive.CheckedChanged += new System.EventHandler(this.interactive_CheckedChanged);
            // 
            // DeployForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(772, 242);
            this.Controls.Add(this.targetsLabel);
            this.Controls.Add(this.groupBox1);
            this.Controls.Add(this.groupBox2);
            this.Controls.Add(this.groupBox3);
            this.Controls.Add(this.startDeploy);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.Name = "DeployForm";
            this.ShowIcon = false;
            this.ShowInTaskbar = false;
            this.StartPosition = System.Windows.Forms.FormStartPosition.Manual;
            this.Text = "Lite Deploy - Deploy";
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.DeployForm_FormClosing);
            this.groupBox1.ResumeLayout(false);
            this.groupBox1.PerformLayout();
            this.groupBox2.ResumeLayout(false);
            this.groupBox2.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.timeOut)).EndInit();
            this.groupBox3.ResumeLayout(false);
            this.groupBox3.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.isessionNum)).EndInit();
            this.ResumeLayout(false);

        }

        private System.Windows.Forms.GroupBox groupBox1;
        private System.Windows.Forms.Button savedCredentialButton;
        public System.Windows.Forms.CheckBox systemAccount;
        public System.Windows.Forms.CheckBox credentials;
        public System.Windows.Forms.TextBox password;
        private System.Windows.Forms.TextBox username;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.GroupBox groupBox2;
        private System.Windows.Forms.Button clearExec;
        private System.Windows.Forms.TextBox arguments;
        private System.Windows.Forms.Label label9;
        private System.Windows.Forms.Button selectFileToExecute;
        private System.Windows.Forms.Label label4;
        private System.Windows.Forms.TextBox executable;
        public System.Windows.Forms.Button startDeploy;
        private System.Windows.Forms.OpenFileDialog openExecutable;
        private System.Windows.Forms.Label label6;
        private System.Windows.Forms.NumericUpDown timeOut;
        private System.Windows.Forms.CheckBox copy;
        private System.Windows.Forms.CheckBox wait;
        private System.Windows.Forms.Label label7;
        private System.Windows.Forms.ComboBox priority;
        private System.Windows.Forms.CheckBox forceCopy;
        private System.Windows.Forms.Label label8;
        private System.Windows.Forms.GroupBox groupBox3;
        public System.Windows.Forms.CheckBox relWorkingPath;
        private System.Windows.Forms.NumericUpDown isessionNum;
        private System.Windows.Forms.CheckBox interactive;
        private System.Windows.Forms.ToolTip toolTip;
        private System.Windows.Forms.Label targetsLabel;
        private System.Windows.Forms.Button restartCommand;
        private System.Windows.Forms.Button shutdownCommand;
    }
}
