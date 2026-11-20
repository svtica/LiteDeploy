// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    MainForm.Designer.cs
// Author:  LiteDeploy contributors
// Date:    2025-05-30
// Purpose: Windows Forms designer part of MainForm (layout and controls).
// -----------------------------------------------------------------------------
namespace LiteDeploy
{
    partial class MainForm
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
            this.components = new System.ComponentModel.Container();
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(MainForm));
            this.menuStrip1 = new System.Windows.Forms.MenuStrip();
            this.fileToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.toolsToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.helpToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.optionsToolStripMenuItem1 = new System.Windows.Forms.ToolStripMenuItem();
            this.exitToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.addComputersToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.deployToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.pingAllToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.openProcessConsoleToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.killCurrentProcessToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.killAllProcessesToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.schedulesToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.softwareCatalogToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.exportInventoryToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.webVersionsToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.credentialsToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.manageToolsToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.litePMToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.clearConsoleToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.changeLogToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.checkForUpdatesToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.aboutToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.toolStripSeparator1 = new System.Windows.Forms.ToolStripSeparator();
            this.toolStripSeparator3 = new System.Windows.Forms.ToolStripSeparator();
            this.toolStripSeparator4 = new System.Windows.Forms.ToolStripSeparator();
            this.toolStripSeparator5 = new System.Windows.Forms.ToolStripSeparator();
            this.mainSplit = new System.Windows.Forms.SplitContainer();
            this.grid = new LiteDeploy.ComputerGrid();
            this.consoleDisplay = new System.Windows.Forms.RichTextBox();
            this.menuStrip1.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.mainSplit)).BeginInit();
            this.mainSplit.Panel1.SuspendLayout();
            this.mainSplit.Panel2.SuspendLayout();
            this.mainSplit.SuspendLayout();
            this.SuspendLayout();
            // 
            // menuStrip1
            // 
            this.menuStrip1.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.fileToolStripMenuItem,
            this.toolsToolStripMenuItem,
            this.helpToolStripMenuItem});
            this.menuStrip1.Location = new System.Drawing.Point(0, 0);
            this.menuStrip1.Name = "menuStrip1";
            this.menuStrip1.TabIndex = 0;
            this.menuStrip1.Text = "menuStrip1";
            // 
            // fileToolStripMenuItem
            // 
            this.fileToolStripMenuItem.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.optionsToolStripMenuItem1,
            this.toolStripSeparator1,
            this.exitToolStripMenuItem});
            this.fileToolStripMenuItem.Name = "fileToolStripMenuItem";
            this.fileToolStripMenuItem.Text = "App";
            // 
            // optionsToolStripMenuItem1
            // 
            this.optionsToolStripMenuItem1.Name = "optionsToolStripMenuItem1";
            this.optionsToolStripMenuItem1.Text = "Options...";
            this.optionsToolStripMenuItem1.Click += new System.EventHandler(this.optionsToolStripMenuItem1_Click);
            // 
            // toolStripSeparator1
            // 
            this.toolStripSeparator1.Name = "toolStripSeparator1";
            // 
            // exitToolStripMenuItem
            // 
            this.exitToolStripMenuItem.Name = "exitToolStripMenuItem";
            this.exitToolStripMenuItem.Text = "Exit";
            this.exitToolStripMenuItem.Click += new System.EventHandler(this.exitToolStripMenuItem_Click);
            // 
            // toolsToolStripMenuItem
            // 
            this.toolsToolStripMenuItem.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.addComputersToolStripMenuItem,
            this.deployToolStripMenuItem,
            this.pingAllToolStripMenuItem,
            this.toolStripSeparator3,
            this.openProcessConsoleToolStripMenuItem,
            this.killCurrentProcessToolStripMenuItem,
            this.killAllProcessesToolStripMenuItem,
            this.toolStripSeparator4,
            this.softwareCatalogToolStripMenuItem,
            this.schedulesToolStripMenuItem,
            this.exportInventoryToolStripMenuItem,
            this.webVersionsToolStripMenuItem,
            this.credentialsToolStripMenuItem,
            this.manageToolsToolStripMenuItem,
            this.litePMToolStripMenuItem,
            this.toolStripSeparator5,
            this.clearConsoleToolStripMenuItem});
            this.toolsToolStripMenuItem.Name = "toolsToolStripMenuItem";
            this.toolsToolStripMenuItem.Text = "Tools";
            // 
            // addComputersToolStripMenuItem
            // 
            this.addComputersToolStripMenuItem.Name = "addComputersToolStripMenuItem";
            this.addComputersToolStripMenuItem.Text = "Add computers...";
            this.addComputersToolStripMenuItem.Click += new System.EventHandler(this.addComputersToolStripMenuItem_Click);
            // 
            // deployToolStripMenuItem
            // 
            this.deployToolStripMenuItem.Name = "deployToolStripMenuItem";
            this.deployToolStripMenuItem.Text = "Deploy a file or command...";
            this.deployToolStripMenuItem.ToolTipText = "Run a file or a command on the checked computers, or on the selected rows, through PsExec";
            this.deployToolStripMenuItem.Click += new System.EventHandler(this.deployToolStripMenuItem_Click);
            // 
            // pingAllToolStripMenuItem
            // 
            this.pingAllToolStripMenuItem.Name = "pingAllToolStripMenuItem";
            this.pingAllToolStripMenuItem.Text = "Ping";
            this.pingAllToolStripMenuItem.ToolTipText = "Ping the checked computers, or every computer shown when none is checked";
            this.pingAllToolStripMenuItem.Click += new System.EventHandler(this.pingAllToolStripMenuItem_Click);
            // 
            // toolStripSeparator3
            // 
            this.toolStripSeparator3.Name = "toolStripSeparator3";
            // 
            // openProcessConsoleToolStripMenuItem
            // 
            this.openProcessConsoleToolStripMenuItem.Name = "openProcessConsoleToolStripMenuItem";
            this.openProcessConsoleToolStripMenuItem.Text = "Process Reports";
            this.openProcessConsoleToolStripMenuItem.Click += new System.EventHandler(this.openProcessConsoleToolStripMenuItem_Click);
            // 
            // killCurrentProcessToolStripMenuItem
            // 
            this.killCurrentProcessToolStripMenuItem.Name = "killCurrentProcessToolStripMenuItem";
            this.killCurrentProcessToolStripMenuItem.Text = "Stop Current Process";
            this.killCurrentProcessToolStripMenuItem.Click += new System.EventHandler(this.killCurrentProcessToolStripMenuItem_Click);
            // 
            // killAllProcessesToolStripMenuItem
            // 
            this.killAllProcessesToolStripMenuItem.Name = "killAllProcessesToolStripMenuItem";
            this.killAllProcessesToolStripMenuItem.Text = "Stop All Processing";
            this.killAllProcessesToolStripMenuItem.Click += new System.EventHandler(this.killAllProcessesToolStripMenuItem_Click);
            // 
            // toolStripSeparator4
            // 
            this.toolStripSeparator4.Name = "toolStripSeparator4";
            // 
            // softwareCatalogToolStripMenuItem
            // 
            this.softwareCatalogToolStripMenuItem.Name = "softwareCatalogToolStripMenuItem";
            this.softwareCatalogToolStripMenuItem.Text = "Software Catalog...";
            this.softwareCatalogToolStripMenuItem.Click += new System.EventHandler(this.softwareCatalogToolStripMenuItem_Click);
            this.exportInventoryToolStripMenuItem.Name = "exportInventoryToolStripMenuItem";
            this.exportInventoryToolStripMenuItem.Text = "Export the Inventory...";
            this.exportInventoryToolStripMenuItem.ToolTipText = "Inventory of the checked computers, else of every computer shown: Excel workbook (.xlsx) or CSV";
            this.exportInventoryToolStripMenuItem.Click += new System.EventHandler(this.exportInventoryToolStripMenuItem_Click);
            // 
            // schedulesToolStripMenuItem
            // 
            this.schedulesToolStripMenuItem.Name = "schedulesToolStripMenuItem";
            this.schedulesToolStripMenuItem.Text = "Scheduled Updates...";
            this.schedulesToolStripMenuItem.Click += new System.EventHandler(this.schedulesToolStripMenuItem_Click);
            // 
            // webVersionsToolStripMenuItem
            // 
            this.webVersionsToolStripMenuItem.Name = "webVersionsToolStripMenuItem";
            this.webVersionsToolStripMenuItem.Text = "Web Versions...";
            this.webVersionsToolStripMenuItem.Click += new System.EventHandler(this.webVersionsToolStripMenuItem_Click);
            // 
            // credentialsToolStripMenuItem
            // 
            this.credentialsToolStripMenuItem.Name = "credentialsToolStripMenuItem";
            this.credentialsToolStripMenuItem.Text = "Credentials...";
            this.credentialsToolStripMenuItem.Click += new System.EventHandler(this.credentialsToolStripMenuItem_Click);
            // 
            // manageToolsToolStripMenuItem
            // 
            this.manageToolsToolStripMenuItem.Name = "manageToolsToolStripMenuItem";
            this.manageToolsToolStripMenuItem.Text = "Manage Tools...";
            this.manageToolsToolStripMenuItem.Click += new System.EventHandler(this.manageToolsToolStripMenuItem_Click);
            // 
            // litePMToolStripMenuItem
            // 
            this.litePMToolStripMenuItem.Name = "litePMToolStripMenuItem";
            this.litePMToolStripMenuItem.Text = "LitePM (Processes)";
            this.litePMToolStripMenuItem.Click += new System.EventHandler(this.litePMToolStripMenuItem_Click);
            // 
            // toolStripSeparator5
            // 
            this.toolStripSeparator5.Name = "toolStripSeparator5";
            // 
            // clearConsoleToolStripMenuItem
            // 
            this.clearConsoleToolStripMenuItem.Name = "clearConsoleToolStripMenuItem";
            this.clearConsoleToolStripMenuItem.Text = "Clear Console";
            this.clearConsoleToolStripMenuItem.Click += new System.EventHandler(this.clearConsoleToolStripMenuItem_Click);
            // 
            // helpToolStripMenuItem
            // 
            this.helpToolStripMenuItem.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.changeLogToolStripMenuItem,
            this.checkForUpdatesToolStripMenuItem,
            this.aboutToolStripMenuItem});
            this.helpToolStripMenuItem.Name = "helpToolStripMenuItem";
            this.helpToolStripMenuItem.Text = "Help";
            // 
            // changeLogToolStripMenuItem
            // 
            this.changeLogToolStripMenuItem.Name = "changeLogToolStripMenuItem";
            this.changeLogToolStripMenuItem.Text = "Change Log";
            this.changeLogToolStripMenuItem.Click += new System.EventHandler(this.changeLogToolStripMenuItem_Click);
            // 
            // checkForUpdatesToolStripMenuItem
            // 
            this.checkForUpdatesToolStripMenuItem.Name = "checkForUpdatesToolStripMenuItem";
            this.checkForUpdatesToolStripMenuItem.Text = "Check for updates...";
            this.checkForUpdatesToolStripMenuItem.Click += new System.EventHandler(this.checkForUpdatesToolStripMenuItem_Click);
            // 
            // aboutToolStripMenuItem
            // 
            this.aboutToolStripMenuItem.Name = "aboutToolStripMenuItem";
            this.aboutToolStripMenuItem.Text = "About";
            this.aboutToolStripMenuItem.Click += new System.EventHandler(this.aboutToolStripMenuItem_Click);
            // 
            // mainSplit
            // 
            this.mainSplit.Dock = System.Windows.Forms.DockStyle.Fill;
            this.mainSplit.FixedPanel = System.Windows.Forms.FixedPanel.Panel2;
            this.mainSplit.Location = new System.Drawing.Point(0, 24);
            this.mainSplit.Name = "mainSplit";
            this.mainSplit.Orientation = System.Windows.Forms.Orientation.Horizontal;
            this.mainSplit.Panel1.Controls.Add(this.grid);
            this.mainSplit.Panel2.Controls.Add(this.consoleDisplay);
            this.mainSplit.Panel2MinSize = 40;
            this.mainSplit.Size = new System.Drawing.Size(1084, 656);
            this.mainSplit.SplitterDistance = 530;
            this.mainSplit.TabIndex = 1;
            // 
            // grid
            // 
            this.grid.Dock = System.Windows.Forms.DockStyle.Fill;
            this.grid.Location = new System.Drawing.Point(0, 0);
            this.grid.Name = "grid";
            this.grid.Size = new System.Drawing.Size(1084, 530);
            this.grid.TabIndex = 0;
            // 
            // consoleDisplay
            // 
            this.consoleDisplay.BackColor = System.Drawing.Color.Black;
            this.consoleDisplay.Cursor = System.Windows.Forms.Cursors.IBeam;
            this.consoleDisplay.DetectUrls = false;
            this.consoleDisplay.Dock = System.Windows.Forms.DockStyle.Fill;
            this.consoleDisplay.Font = new System.Drawing.Font("Lucida Console", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.consoleDisplay.ForeColor = System.Drawing.Color.White;
            this.consoleDisplay.Name = "consoleDisplay";
            this.consoleDisplay.ReadOnly = true;
            this.consoleDisplay.TabIndex = 0;
            this.consoleDisplay.TabStop = false;
            this.consoleDisplay.Text = "";
            // 
            // MainForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(1084, 680);
            this.Controls.Add(this.mainSplit);
            this.Controls.Add(this.menuStrip1);
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.MainMenuStrip = this.menuStrip1;
            this.MinimumSize = new System.Drawing.Size(800, 500);
            this.Name = "MainForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "Lite Deploy";
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.MainForm_FormClosing);
            this.Load += new System.EventHandler(this.MainForm_Load);
            this.Shown += new System.EventHandler(this.MainForm_Shown);
            this.menuStrip1.ResumeLayout(false);
            this.menuStrip1.PerformLayout();
            this.mainSplit.Panel1.ResumeLayout(false);
            this.mainSplit.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.mainSplit)).EndInit();
            this.mainSplit.ResumeLayout(false);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.MenuStrip menuStrip1;
        private System.Windows.Forms.ToolStripMenuItem fileToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem toolsToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem helpToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem optionsToolStripMenuItem1;
        private System.Windows.Forms.ToolStripMenuItem exitToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem addComputersToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem deployToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem pingAllToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem openProcessConsoleToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem killCurrentProcessToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem killAllProcessesToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem schedulesToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem softwareCatalogToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem exportInventoryToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem webVersionsToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem credentialsToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem manageToolsToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem litePMToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem clearConsoleToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem changeLogToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem checkForUpdatesToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem aboutToolStripMenuItem;
        private System.Windows.Forms.ToolStripSeparator toolStripSeparator1;
        private System.Windows.Forms.ToolStripSeparator toolStripSeparator3;
        private System.Windows.Forms.ToolStripSeparator toolStripSeparator4;
        private System.Windows.Forms.ToolStripSeparator toolStripSeparator5;
        private System.Windows.Forms.SplitContainer mainSplit;
        private LiteDeploy.ComputerGrid grid;
        public System.Windows.Forms.RichTextBox consoleDisplay;
    }
}
