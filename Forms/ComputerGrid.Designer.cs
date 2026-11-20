// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    ComputerGrid.Designer.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-12
// Purpose: Controls of the computer grid (main window).
// -----------------------------------------------------------------------------
namespace LiteDeploy
{
    partial class ComputerGrid
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
            this.filterStrip = new System.Windows.Forms.ToolStrip();
            this.rowMenu = new System.Windows.Forms.ContextMenuStrip(this.components);
            this.targetsItem = new System.Windows.Forms.ToolStripMenuItem();
            this.targetsSeparator = new System.Windows.Forms.ToolStripSeparator();
            this.pauseItem = new System.Windows.Forms.ToolStripMenuItem();
            this.resumeItem = new System.Windows.Forms.ToolStripMenuItem();
            this.nightOnlyItem = new System.Windows.Forms.ToolStripMenuItem();
            this.nightOnlyClearItem = new System.Windows.Forms.ToolStripMenuItem();
            this.menuSeparator = new System.Windows.Forms.ToolStripSeparator();
            this.cardItem = new System.Windows.Forms.ToolStripMenuItem();
            this.wakeItem = new System.Windows.Forms.ToolStripMenuItem();
            this.processesItem = new System.Windows.Forms.ToolStripMenuItem();
            this.litePMItem = new System.Windows.Forms.ToolStripMenuItem();
            this.logsItem = new System.Windows.Forms.ToolStripMenuItem();
            this.removeSeparator = new System.Windows.Forms.ToolStripSeparator();
            this.removeItem = new System.Windows.Forms.ToolStripMenuItem();
            this.unhideItem = new System.Windows.Forms.ToolStripMenuItem();
            this.hiddenFilter = new System.Windows.Forms.ToolStripButton();
            this.installItem = new System.Windows.Forms.ToolStripMenuItem();
            this.installSeparator = new System.Windows.Forms.ToolStripSeparator();
            this.teamViewerItem = new System.Windows.Forms.ToolStripMenuItem();
            this.remoteDesktopItem = new System.Windows.Forms.ToolStripMenuItem();
            this.commandPromptItem = new System.Windows.Forms.ToolStripMenuItem();
            this.quickCommandsItem = new System.Windows.Forms.ToolStripMenuItem();
            this.exportSeparator = new System.Windows.Forms.ToolStripSeparator();
            this.royalTsItem = new System.Windows.Forms.ToolStripMenuItem();
            this.pingItem = new System.Windows.Forms.ToolStripMenuItem();
            this.managementConsoleItem = new System.Windows.Forms.ToolStripMenuItem();
            this.adminShareItem = new System.Windows.Forms.ToolStripMenuItem();
            this.adminShareRootItem = new System.Windows.Forms.ToolStripMenuItem();
            this.publicDesktopItem = new System.Windows.Forms.ToolStripMenuItem();
            this.startMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.copyNameItem = new System.Windows.Forms.ToolStripMenuItem();
            this.deploySeparator = new System.Windows.Forms.ToolStripSeparator();
            this.deployItem = new System.Windows.Forms.ToolStripMenuItem();
            this.deployButton = new System.Windows.Forms.ToolStripButton();
            this.onlineOnlyButton = new System.Windows.Forms.ToolStripButton();
            this.profileButton = new System.Windows.Forms.ToolStripButton();
            this.inventoryItem = new System.Windows.Forms.ToolStripMenuItem();
            this.exportInventoryItem = new System.Windows.Forms.ToolStripMenuItem();
            this.refreshStateButton = new System.Windows.Forms.ToolStripButton();
            this.columnsButton = new System.Windows.Forms.ToolStripDropDownButton();
            this.stateColumn = new System.Windows.Forms.ColumnHeader();
            this.userColumn = new System.Windows.Forms.ColumnHeader();
            this.lastScanColumn = new System.Windows.Forms.ColumnHeader();
            this.manufacturerColumn = new System.Windows.Forms.ColumnHeader();
            this.modelColumn = new System.Windows.Forms.ColumnHeader();
            this.serialColumn = new System.Windows.Forms.ColumnHeader();
            this.yearColumn = new System.Windows.Forms.ColumnHeader();
            this.cpuColumn = new System.Windows.Forms.ColumnHeader();
            this.coresColumn = new System.Windows.Forms.ColumnHeader();
            this.ramColumn = new System.Windows.Forms.ColumnHeader();
            this.diskColumn = new System.Windows.Forms.ColumnHeader();
            this.diskFreeColumn = new System.Windows.Forms.ColumnHeader();
            this.separator4 = new System.Windows.Forms.ToolStripSeparator();
            this.checkButton = new System.Windows.Forms.ToolStripButton();
            this.selectOutdatedButton = new System.Windows.Forms.ToolStripButton();
            this.selectAllButton = new System.Windows.Forms.ToolStripButton();
            this.selectNoneButton = new System.Windows.Forms.ToolStripButton();
            this.byPackageButton = new System.Windows.Forms.ToolStripButton();
            this.separator2 = new System.Windows.Forms.ToolStripSeparator();
            this.updateButton = new System.Windows.Forms.ToolStripButton();
            this.stopButton = new System.Windows.Forms.ToolStripButton();
            this.separator3 = new System.Windows.Forms.ToolStripSeparator();
            this.actionsButton = new System.Windows.Forms.ToolStripDropDownButton();
            this.separator1 = new System.Windows.Forms.ToolStripSeparator();
            this.editPackagesButton = new System.Windows.Forms.ToolStripButton();
            this.scheduleButton = new System.Windows.Forms.ToolStripButton();
            this.searchLabel = new System.Windows.Forms.ToolStripLabel();
            this.searchBox = new System.Windows.Forms.ToolStripTextBox();
            this.filterSeparator1 = new System.Windows.Forms.ToolStripSeparator();
            this.toUpdateFilter = new System.Windows.Forms.ToolStripButton();
            this.errorsFilter = new System.Windows.Forms.ToolStripButton();
            this.upToDateFilter = new System.Windows.Forms.ToolStripButton();
            this.notCheckedFilter = new System.Windows.Forms.ToolStripButton();
            this.pausedFilter = new System.Windows.Forms.ToolStripButton();
            this.nightOnlyFilter = new System.Windows.Forms.ToolStripButton();
            this.staleFilter = new System.Windows.Forms.ToolStripButton();
            this.filterSeparator2 = new System.Windows.Forms.ToolStripSeparator();
            this.includeServersCheck = new System.Windows.Forms.CheckBox();
            this.addButton = new System.Windows.Forms.ToolStripButton();
            this.splitContainer = new System.Windows.Forms.SplitContainer();
            this.updatesList = new System.Windows.Forms.ListView();
            this.detailsBox = new System.Windows.Forms.TextBox();
            this.computerColumn = new System.Windows.Forms.ColumnHeader();
            this.osColumn = new System.Windows.Forms.ColumnHeader();
            this.lastLogonColumn = new System.Windows.Forms.ColumnHeader();
            this.packageColumn = new System.Windows.Forms.ColumnHeader();
            this.updatesColumn = new System.Windows.Forms.ColumnHeader();
            this.installedColumn = new System.Windows.Forms.ColumnHeader();
            this.availableColumn = new System.Windows.Forms.ColumnHeader();
            this.statusColumn = new System.Windows.Forms.ColumnHeader();
            this.resultColumn = new System.Windows.Forms.ColumnHeader();
            this.changedColumn = new System.Windows.Forms.ColumnHeader();
            this.pausedColumn = new System.Windows.Forms.ColumnHeader();
            this.nightOnlyColumn = new System.Windows.Forms.ColumnHeader();
            this.statusStrip = new System.Windows.Forms.StatusStrip();
            this.statusLabel = new System.Windows.Forms.ToolStripStatusLabel();
            this.countsLabel = new System.Windows.Forms.ToolStripStatusLabel();
            this.progressBar = new System.Windows.Forms.ToolStripProgressBar();
            this.toolStrip.SuspendLayout();
            this.filterStrip.SuspendLayout();
            this.rowMenu.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.splitContainer)).BeginInit();
            this.splitContainer.Panel1.SuspendLayout();
            this.splitContainer.Panel2.SuspendLayout();
            this.splitContainer.SuspendLayout();
            this.statusStrip.SuspendLayout();
            this.SuspendLayout();
            //
            // toolStrip
            //
            this.toolStrip.GripStyle = System.Windows.Forms.ToolStripGripStyle.Hidden;
            this.toolStrip.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.addButton,
            this.separator4,
            this.selectAllButton,
            this.selectNoneButton,
            this.selectOutdatedButton,
            this.separator2,
            this.profileButton,
            this.checkButton,
            this.updateButton,
            this.stopButton,
            this.separator3,
            this.actionsButton,
            this.deployButton,
            this.separator1,
            this.byPackageButton,
            this.editPackagesButton,
            this.scheduleButton});
            this.toolStrip.Location = new System.Drawing.Point(0, 0);
            this.toolStrip.Name = "toolStrip";
            this.toolStrip.Size = new System.Drawing.Size(1084, 25);
            this.toolStrip.TabIndex = 0;
            //
            // checkButton
            //
            this.checkButton.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.checkButton.Name = "checkButton";
            this.checkButton.Text = "Check";
            this.checkButton.ToolTipText = "Check the installed and available versions on the checked computers, or on every computer shown when none is checked (F5)";
            this.checkButton.Click += new System.EventHandler(this.checkButton_Click);
            //
            // selectOutdatedButton
            //
            this.selectOutdatedButton.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.selectOutdatedButton.Name = "selectOutdatedButton";
            this.selectOutdatedButton.Text = "Select outdated";
            this.selectOutdatedButton.ToolTipText = "Check exactly the rows shown that have an update available";
            this.selectOutdatedButton.Click += new System.EventHandler(this.selectOutdatedButton_Click);
            //
            // selectAllButton
            //
            this.selectAllButton.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.selectAllButton.Name = "selectAllButton";
            this.selectAllButton.Text = "Select all";
            this.selectAllButton.ToolTipText = "Check every row shown (Ctrl+A)";
            this.selectAllButton.Click += new System.EventHandler(this.selectAllButton_Click);
            //
            // selectNoneButton
            //
            this.selectNoneButton.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.selectNoneButton.Name = "selectNoneButton";
            this.selectNoneButton.Text = "Select none";
            this.selectNoneButton.ToolTipText = "Uncheck every row, shown or not";
            this.selectNoneButton.Click += new System.EventHandler(this.selectNoneButton_Click);
            //
            // separator2
            //
            this.separator2.Name = "separator2";
            //
            // byPackageButton
            //
            this.byPackageButton.CheckOnClick = true;
            this.byPackageButton.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.byPackageButton.Name = "byPackageButton";
            this.byPackageButton.Text = "Show packages";
            this.byPackageButton.ToolTipText = "One row per package instead of one row per computer";
            this.byPackageButton.Click += new System.EventHandler(this.byPackageButton_Click);
            //
            // updateButton
            //
            this.updateButton.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.updateButton.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Bold);
            this.updateButton.Name = "updateButton";
            this.updateButton.Text = "Update checked";
            this.updateButton.ToolTipText = "Update the checked computers (their packages with an update), or the checked packages (Ctrl+U)";
            this.updateButton.Click += new System.EventHandler(this.updateButton_Click);
            //
            // stopButton
            //
            this.stopButton.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.stopButton.Enabled = false;
            this.stopButton.Name = "stopButton";
            this.stopButton.Text = "Stop";
            this.stopButton.ToolTipText = "Stop the work in progress (Esc)";
            this.stopButton.Click += new System.EventHandler(this.stopButton_Click);
            //
            // separator3
            //
            this.separator3.Name = "separator3";
            //
            // actionsButton
            //
            this.actionsButton.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.actionsButton.Name = "actionsButton";
            this.actionsButton.Text = "Actions";
            this.actionsButton.ToolTipText = "Actions on the checked computers, or on the selected rows when none is checked (same as the right-click menu)";
            this.actionsButton.DropDownOpening += new System.EventHandler(this.actionsButton_DropDownOpening);
            //
            // separator1
            //
            this.separator1.Name = "separator1";
            //
            // editPackagesButton
            //
            this.editPackagesButton.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.editPackagesButton.Name = "editPackagesButton";
            this.editPackagesButton.Text = "Software catalog...";
            this.editPackagesButton.ToolTipText = "The software kept up to date (winget packages and modules) and the profiles";
            this.editPackagesButton.Click += new System.EventHandler(this.editPackagesButton_Click);
            //
            // scheduleButton
            //
            this.scheduleButton.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.scheduleButton.Name = "scheduleButton";
            this.scheduleButton.Text = "Schedule...";
            this.scheduleButton.ToolTipText = "Schedule a check or an update of the checked computers, or of the computers shown";
            this.scheduleButton.Click += new System.EventHandler(this.scheduleButton_Click);
            //
            // profileButton
            //
            this.profileButton.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.profileButton.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Bold);
            this.profileButton.Name = "profileButton";
            this.profileButton.Text = "Profile: All";
            this.profileButton.ToolTipText = "The software that Check, Update checked and Install work on: click to choose another profile";
            this.profileButton.Click += new System.EventHandler(this.profileButton_Click);
            //
            // onlineOnlyButton
            //
            this.onlineOnlyButton.CheckOnClick = true;
            this.onlineOnlyButton.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.onlineOnlyButton.Name = "onlineOnlyButton";
            this.onlineOnlyButton.Text = "Online only";
            this.onlineOnlyButton.ToolTipText = "Only the computers that answer the ping (the ones not pinged yet too); click to show every computer";
            this.onlineOnlyButton.CheckedChanged += new System.EventHandler(this.onlineOnlyButton_CheckedChanged);
            //
            // refreshStateButton
            //
            this.refreshStateButton.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.refreshStateButton.Name = "refreshStateButton";
            this.refreshStateButton.Text = "Refresh state";
            this.refreshStateButton.ToolTipText = "Ping the computers again (done by itself every 5 minutes; Shift+F5)";
            this.refreshStateButton.Click += new System.EventHandler(this.refreshStateButton_Click);
            //
            // columnsButton
            //
            this.columnsButton.Alignment = System.Windows.Forms.ToolStripItemAlignment.Right;
            this.columnsButton.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.columnsButton.Name = "columnsButton";
            this.columnsButton.Text = "Columns";
            this.columnsButton.ToolTipText = "Choose the columns shown (one row per computer)";
            this.columnsButton.DropDownOpening += new System.EventHandler(this.columnsButton_DropDownOpening);
            //
            // filterStrip
            //
            this.filterStrip.GripStyle = System.Windows.Forms.ToolStripGripStyle.Hidden;
            this.filterStrip.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.searchLabel,
            this.searchBox,
            this.filterSeparator1,
            this.toUpdateFilter,
            this.errorsFilter,
            this.upToDateFilter,
            this.notCheckedFilter,
            this.pausedFilter,
            this.nightOnlyFilter,
            this.staleFilter,
            this.hiddenFilter,
            this.filterSeparator2,
            this.onlineOnlyButton,
            this.refreshStateButton,
            new System.Windows.Forms.ToolStripControlHost(this.includeServersCheck),
            this.columnsButton});
            this.filterStrip.Location = new System.Drawing.Point(0, 25);
            this.filterStrip.Name = "filterStrip";
            this.filterStrip.Size = new System.Drawing.Size(1084, 25);
            this.filterStrip.TabIndex = 1;
            //
            // searchLabel
            //
            this.searchLabel.Name = "searchLabel";
            this.searchLabel.Text = "Search:";
            //
            // searchBox
            //
            this.searchBox.Name = "searchBox";
            this.searchBox.Size = new System.Drawing.Size(170, 25);
            this.searchBox.ToolTipText = "Name, operating system, OU, user or package (Ctrl+F)";
            this.searchBox.TextChanged += new System.EventHandler(this.searchBox_TextChanged);
            this.searchBox.KeyDown += new System.Windows.Forms.KeyEventHandler(this.searchBox_KeyDown);
            //
            // filterSeparator1
            //
            this.filterSeparator1.Name = "filterSeparator1";
            //
            // toUpdateFilter
            //
            this.toUpdateFilter.CheckOnClick = true;
            this.toUpdateFilter.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.toUpdateFilter.Name = "toUpdateFilter";
            this.toUpdateFilter.Text = "To update";
            this.toUpdateFilter.ToolTipText = "Computers with updates available (several filters: any of them)";
            this.toUpdateFilter.CheckedChanged += new System.EventHandler(this.statusFilter_CheckedChanged);
            //
            // errorsFilter
            //
            this.errorsFilter.CheckOnClick = true;
            this.errorsFilter.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.errorsFilter.Name = "errorsFilter";
            this.errorsFilter.Text = "Errors";
            this.errorsFilter.ToolTipText = "Package errors, or the last run failed";
            this.errorsFilter.CheckedChanged += new System.EventHandler(this.statusFilter_CheckedChanged);
            //
            // upToDateFilter
            //
            this.upToDateFilter.CheckOnClick = true;
            this.upToDateFilter.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.upToDateFilter.Name = "upToDateFilter";
            this.upToDateFilter.Text = "Up to date";
            this.upToDateFilter.CheckedChanged += new System.EventHandler(this.statusFilter_CheckedChanged);
            //
            // notCheckedFilter
            //
            this.notCheckedFilter.CheckOnClick = true;
            this.notCheckedFilter.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.notCheckedFilter.Name = "notCheckedFilter";
            this.notCheckedFilter.Text = "Not checked";
            this.notCheckedFilter.CheckedChanged += new System.EventHandler(this.statusFilter_CheckedChanged);
            //
            // pausedFilter
            //
            this.pausedFilter.CheckOnClick = true;
            this.pausedFilter.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.pausedFilter.Name = "pausedFilter";
            this.pausedFilter.Text = "Paused";
            this.pausedFilter.CheckedChanged += new System.EventHandler(this.statusFilter_CheckedChanged);
            //
            // nightOnlyFilter
            //
            this.nightOnlyFilter.CheckOnClick = true;
            this.nightOnlyFilter.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.nightOnlyFilter.Name = "nightOnlyFilter";
            this.nightOnlyFilter.Text = "Outside business hours";
            this.nightOnlyFilter.ToolTipText = "Computers updated outside business hours only";
            this.nightOnlyFilter.CheckedChanged += new System.EventHandler(this.statusFilter_CheckedChanged);
            //
            // staleFilter
            //
            this.staleFilter.CheckOnClick = true;
            this.staleFilter.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.staleFilter.Name = "staleFilter";
            this.staleFilter.Text = "Offline/stale";
            this.staleFilter.ToolTipText = "Last logon (Active Directory) older than the delay of the settings, or never";
            this.staleFilter.CheckedChanged += new System.EventHandler(this.statusFilter_CheckedChanged);
            //
            // hiddenFilter
            //
            this.hiddenFilter.CheckOnClick = true;
            this.hiddenFilter.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.hiddenFilter.Name = "hiddenFilter";
            this.hiddenFilter.Text = "Hidden";
            this.hiddenFilter.ToolTipText = "Computers removed from the list (Remove from the list...): shown only with this filter";
            this.hiddenFilter.CheckedChanged += new System.EventHandler(this.statusFilter_CheckedChanged);
            //
            // filterSeparator2
            //
            this.filterSeparator2.Name = "filterSeparator2";
            //
            // includeServersCheck
            //
            this.includeServersCheck.AutoSize = true;
            this.includeServersCheck.BackColor = System.Drawing.Color.Transparent;
            this.includeServersCheck.Name = "includeServersCheck";
            this.includeServersCheck.Padding = new System.Windows.Forms.Padding(4, 0, 0, 0);
            this.includeServersCheck.Text = "Include servers";
            this.includeServersCheck.UseVisualStyleBackColor = false;
            this.includeServersCheck.CheckedChanged += new System.EventHandler(this.includeServersCheck_CheckedChanged);
            //
            // addButton
            //
            this.addButton.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.addButton.Name = "addButton";
            this.addButton.Text = "Add computers...";
            this.addButton.ToolTipText = "Add computers to the list: Active Directory search, subnet scan, file or names typed (the ones not in Active Directory are kept in the list)";
            this.addButton.Click += new System.EventHandler(this.addButton_Click);
            //
            // separator4
            //
            this.separator4.Name = "separator4";
            //
            // deployButton
            //
            this.deployButton.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.deployButton.Name = "deployButton";
            this.deployButton.Text = "Deploy...";
            this.deployButton.ToolTipText = "Run a file or a command on the checked computers, or on the selected rows, through PsExec";
            this.deployButton.Visible = false;
            this.deployButton.Click += new System.EventHandler(this.deployItem_Click);
            //
            // splitContainer
            //
            this.splitContainer.Dock = System.Windows.Forms.DockStyle.Fill;
            this.splitContainer.FixedPanel = System.Windows.Forms.FixedPanel.Panel2;
            this.splitContainer.Location = new System.Drawing.Point(0, 50);
            this.splitContainer.Name = "splitContainer";
            this.splitContainer.Orientation = System.Windows.Forms.Orientation.Horizontal;
            this.splitContainer.Panel1.Controls.Add(this.updatesList);
            this.splitContainer.Panel2.Controls.Add(this.detailsBox);
            this.splitContainer.Panel2MinSize = 40;
            this.splitContainer.Size = new System.Drawing.Size(1084, 489);
            this.splitContainer.SplitterDistance = 380;
            this.splitContainer.TabIndex = 2;
            //
            // updatesList
            //
            this.updatesList.AllowColumnReorder = true;
            this.updatesList.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] {
            this.computerColumn,
            this.stateColumn,
            this.userColumn,
            this.osColumn,
            this.lastLogonColumn,
            this.statusColumn,
            this.updatesColumn,
            this.resultColumn,
            this.changedColumn,
            this.pausedColumn,
            this.nightOnlyColumn});
            this.updatesList.CheckBoxes = true;
            this.updatesList.ContextMenuStrip = this.rowMenu;
            this.updatesList.Dock = System.Windows.Forms.DockStyle.Fill;
            this.updatesList.FullRowSelect = true;
            this.updatesList.HideSelection = false;
            this.updatesList.ShowItemToolTips = true;
            this.updatesList.KeyDown += new System.Windows.Forms.KeyEventHandler(this.updatesList_KeyDown);
            this.updatesList.ColumnClick += new System.Windows.Forms.ColumnClickEventHandler(this.updatesList_ColumnClick);
            this.updatesList.DoubleClick += new System.EventHandler(this.updatesList_DoubleClick);
            this.updatesList.SelectedIndexChanged += new System.EventHandler(this.updatesList_SelectedIndexChanged);
            this.updatesList.Location = new System.Drawing.Point(0, 0);
            this.updatesList.Name = "updatesList";
            this.updatesList.Size = new System.Drawing.Size(1084, 380);
            this.updatesList.TabIndex = 0;
            this.updatesList.UseCompatibleStateImageBehavior = false;
            this.updatesList.View = System.Windows.Forms.View.Details;
            //
            // detailsBox
            //
            this.detailsBox.Dock = System.Windows.Forms.DockStyle.Fill;
            this.detailsBox.Multiline = true;
            this.detailsBox.Name = "detailsBox";
            this.detailsBox.ReadOnly = true;
            this.detailsBox.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            this.detailsBox.TabIndex = 1;
            this.detailsBox.TabStop = false;
            //
            // columns
            //
            this.computerColumn.Name = "computerColumn";
            this.computerColumn.Text = "Computer";
            this.computerColumn.Width = 130;
            this.stateColumn.Name = "stateColumn";
            this.stateColumn.Text = "State";
            this.stateColumn.Width = 70;
            this.userColumn.Name = "userColumn";
            this.userColumn.Text = "User";
            this.userColumn.Width = 130;
            this.lastScanColumn.Name = "lastScanColumn";
            this.lastScanColumn.Text = "Last scan";
            this.lastScanColumn.Width = 110;
            this.manufacturerColumn.Name = "manufacturerColumn";
            this.manufacturerColumn.Text = "Manufacturer";
            this.manufacturerColumn.Width = 110;
            this.modelColumn.Name = "modelColumn";
            this.modelColumn.Text = "Model";
            this.modelColumn.Width = 140;
            this.serialColumn.Name = "serialColumn";
            this.serialColumn.Text = "Serial number";
            this.serialColumn.Width = 110;
            this.yearColumn.Name = "yearColumn";
            this.yearColumn.Text = "Year (BIOS)";
            this.yearColumn.Width = 70;
            this.cpuColumn.Name = "cpuColumn";
            this.cpuColumn.Text = "Processor";
            this.cpuColumn.Width = 200;
            this.coresColumn.Name = "coresColumn";
            this.coresColumn.Text = "Cores/threads";
            this.coresColumn.Width = 80;
            this.ramColumn.Name = "ramColumn";
            this.ramColumn.Text = "RAM (GB)";
            this.ramColumn.Width = 65;
            this.diskColumn.Name = "diskColumn";
            this.diskColumn.Text = "Disk (GB)";
            this.diskColumn.Width = 65;
            this.diskFreeColumn.Name = "diskFreeColumn";
            this.diskFreeColumn.Text = "Free (GB)";
            this.diskFreeColumn.Width = 65;
            this.osColumn.Name = "osColumn";
            this.osColumn.Text = "OS";
            this.osColumn.Width = 170;
            this.lastLogonColumn.Name = "lastLogonColumn";
            this.lastLogonColumn.Text = "Last logon (AD)";
            this.lastLogonColumn.Width = 100;
            this.packageColumn.Name = "packageColumn";
            this.packageColumn.Text = "Package";
            this.packageColumn.Width = 150;
            //
            // updatesColumn
            //
            this.updatesColumn.Name = "updatesColumn";
            this.updatesColumn.Text = "Updates available";
            this.updatesColumn.Width = 260;
            this.installedColumn.Name = "installedColumn";
            this.installedColumn.Text = "Installed";
            this.installedColumn.Width = 90;
            this.availableColumn.Name = "availableColumn";
            this.availableColumn.Text = "Available";
            this.availableColumn.Width = 90;
            this.statusColumn.Name = "statusColumn";
            this.statusColumn.Text = "Status";
            this.statusColumn.Width = 110;
            this.resultColumn.Name = "resultColumn";
            this.resultColumn.Text = "Last result";
            this.resultColumn.Width = 180;
            this.changedColumn.Name = "changedColumn";
            this.changedColumn.Text = "Changed";
            this.changedColumn.Width = 110;
            this.pausedColumn.Name = "pausedColumn";
            this.pausedColumn.Text = "Pause";
            this.pausedColumn.Width = 60;
            this.nightOnlyColumn.Name = "nightOnlyColumn";
            this.nightOnlyColumn.Text = "Outside business hours";
            this.nightOnlyColumn.Width = 70;
            //
            // rowMenu
            //
            this.rowMenu.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.targetsItem,
            this.targetsSeparator,
            this.installItem,
            this.installSeparator,
            this.pauseItem,
            this.resumeItem,
            this.nightOnlyItem,
            this.nightOnlyClearItem,
            this.menuSeparator,
            this.cardItem,
            this.teamViewerItem,
            this.remoteDesktopItem,
            this.commandPromptItem,
            this.quickCommandsItem,
            this.processesItem,
            this.litePMItem,
            this.logsItem,
            this.wakeItem,
            this.pingItem,
            this.managementConsoleItem,
            this.adminShareItem,
            this.copyNameItem,
            this.deploySeparator,
            this.deployItem,
            this.exportSeparator,
            this.inventoryItem,
            this.exportInventoryItem,
            this.royalTsItem,
            this.removeSeparator,
            this.removeItem,
            this.unhideItem});
            this.rowMenu.Name = "rowMenu";
            this.rowMenu.Opening += new System.ComponentModel.CancelEventHandler(this.rowMenu_Opening);
            this.targetsItem.Enabled = false;
            this.targetsItem.Name = "targetsItem";
            this.targetsItem.Text = "Computers";
            this.targetsSeparator.Name = "targetsSeparator";
            this.installItem.Name = "installItem";
            this.installItem.Text = "Install or update";
            this.installSeparator.Name = "installSeparator";
            this.pauseItem.Name = "pauseItem";
            this.pauseItem.Text = "Pause these computers...";
            this.pauseItem.Click += new System.EventHandler(this.pauseItem_Click);
            this.resumeItem.Name = "resumeItem";
            this.resumeItem.Text = "Resume";
            this.resumeItem.Click += new System.EventHandler(this.resumeItem_Click);
            this.nightOnlyItem.Name = "nightOnlyItem";
            this.nightOnlyItem.Text = "Update outside business hours only";
            this.nightOnlyItem.Click += new System.EventHandler(this.nightOnlyItem_Click);
            this.nightOnlyClearItem.Name = "nightOnlyClearItem";
            this.nightOnlyClearItem.Text = "Allow updates during business hours";
            this.nightOnlyClearItem.Click += new System.EventHandler(this.nightOnlyClearItem_Click);
            this.menuSeparator.Name = "menuSeparator";
            this.cardItem.Name = "cardItem";
            this.cardItem.Text = "Computer card...";
            this.cardItem.Click += new System.EventHandler(this.cardItem_Click);
            this.teamViewerItem.Name = "teamViewerItem";
            this.teamViewerItem.Text = "TeamViewer ID...";
            this.teamViewerItem.Click += new System.EventHandler(this.teamViewerItem_Click);
            this.remoteDesktopItem.Name = "remoteDesktopItem";
            this.remoteDesktopItem.Text = "Remote desktop";
            this.remoteDesktopItem.Click += new System.EventHandler(this.remoteDesktopItem_Click);
            this.commandPromptItem.Name = "commandPromptItem";
            this.commandPromptItem.Text = "Command prompt on the computer";
            this.commandPromptItem.ToolTipText = "A console attached to the computer: PsExec (command prompt as SYSTEM) or WinRM (PowerShell session), as chosen in App > Options";
            this.commandPromptItem.Click += new System.EventHandler(this.commandPromptItem_Click);
            this.quickCommandsItem.Name = "quickCommandsItem";
            this.quickCommandsItem.Text = "Quick commands";
            this.exportSeparator.Name = "exportSeparator";
            this.royalTsItem.Name = "royalTsItem";
            this.royalTsItem.Text = "Export to Royal TS...";
            this.royalTsItem.ToolTipText = "Remote Desktop connections of the checked computers, else of every computer shown (CSV or RoyalJSON)";
            this.royalTsItem.Click += new System.EventHandler(this.royalTsItem_Click);
            this.inventoryItem.Name = "inventoryItem";
            this.inventoryItem.Text = "Read the inventory";
            this.inventoryItem.ToolTipText = "Read the hardware of these computers now (manufacturer, model, serial number, BIOS, processor, memory, disk); a check reads it too";
            this.inventoryItem.Click += new System.EventHandler(this.inventoryItem_Click);
            this.exportInventoryItem.Name = "exportInventoryItem";
            this.exportInventoryItem.Text = "Export the inventory...";
            this.exportInventoryItem.ToolTipText = "Inventory of the checked computers, else of every computer shown: Excel workbook (.xlsx) or CSV";
            this.exportInventoryItem.Click += new System.EventHandler(this.exportInventoryItem_Click);
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
            this.removeSeparator.Name = "removeSeparator";
            this.removeItem.Name = "removeItem";
            this.removeItem.Text = "Remove from the list...";
            this.removeItem.ToolTipText = "The computers are no longer shown nor updated: the ones added by name leave the list, the ones of Active Directory are hidden (Hidden filter, Show in the list again)";
            this.removeItem.Click += new System.EventHandler(this.removeItem_Click);
            this.unhideItem.Name = "unhideItem";
            this.unhideItem.Text = "Show in the list again";
            this.unhideItem.Click += new System.EventHandler(this.unhideItem_Click);
            this.pingItem.Name = "pingItem";
            this.pingItem.Text = "Ping...";
            this.pingItem.Click += new System.EventHandler(this.pingItem_Click);
            this.managementConsoleItem.Name = "managementConsoleItem";
            this.managementConsoleItem.Text = "Management console";
            this.managementConsoleItem.Click += new System.EventHandler(this.managementConsoleItem_Click);
            this.adminShareItem.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.adminShareRootItem,
            this.publicDesktopItem,
            this.startMenuItem});
            this.adminShareItem.Name = "adminShareItem";
            this.adminShareItem.Text = "Open admin share";
            this.adminShareRootItem.Name = "adminShareRootItem";
            this.adminShareRootItem.Text = "C$";
            this.adminShareRootItem.Click += new System.EventHandler(this.adminShareRootItem_Click);
            this.publicDesktopItem.Name = "publicDesktopItem";
            this.publicDesktopItem.Text = "Public desktop";
            this.publicDesktopItem.Click += new System.EventHandler(this.publicDesktopItem_Click);
            this.startMenuItem.Name = "startMenuItem";
            this.startMenuItem.Text = "Start menu (all users)";
            this.startMenuItem.Click += new System.EventHandler(this.startMenuItem_Click);
            this.copyNameItem.Name = "copyNameItem";
            this.copyNameItem.Text = "Copy the names";
            this.copyNameItem.Click += new System.EventHandler(this.copyNameItem_Click);
            this.deploySeparator.Name = "deploySeparator";
            this.deployItem.Name = "deployItem";
            this.deployItem.Text = "Deploy a file or command...";
            this.deployItem.ToolTipText = "Run a file or a command on these computers through PsExec";
            this.deployItem.Click += new System.EventHandler(this.deployItem_Click);
            //
            // statusStrip
            //
            this.statusStrip.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.statusLabel,
            this.countsLabel,
            this.progressBar});
            this.statusStrip.Location = new System.Drawing.Point(0, 539);
            this.statusStrip.Name = "statusStrip";
            this.statusStrip.Size = new System.Drawing.Size(1084, 22);
            this.statusStrip.TabIndex = 3;
            //
            // statusLabel
            //
            this.statusLabel.Name = "statusLabel";
            this.statusLabel.Spring = true;
            this.statusLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            //
            // countsLabel
            //
            this.countsLabel.Name = "countsLabel";
            this.countsLabel.ToolTipText = "Computers shown / total / checked";
            //
            // progressBar
            //
            this.progressBar.Name = "progressBar";
            this.progressBar.Size = new System.Drawing.Size(150, 16);
            //
            // ComputerGrid
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.splitContainer);
            this.Controls.Add(this.statusStrip);
            this.Controls.Add(this.filterStrip);
            this.Controls.Add(this.toolStrip);
            this.Name = "ComputerGrid";
            this.Size = new System.Drawing.Size(1084, 561);
            this.toolStrip.ResumeLayout(false);
            this.toolStrip.PerformLayout();
            this.filterStrip.ResumeLayout(false);
            this.filterStrip.PerformLayout();
            this.rowMenu.ResumeLayout(false);
            this.splitContainer.Panel1.ResumeLayout(false);
            this.splitContainer.Panel2.ResumeLayout(false);
            this.splitContainer.Panel2.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.splitContainer)).EndInit();
            this.splitContainer.ResumeLayout(false);
            this.statusStrip.ResumeLayout(false);
            this.statusStrip.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        private System.Windows.Forms.ToolStrip toolStrip;
        private System.Windows.Forms.ToolStrip filterStrip;
        private System.Windows.Forms.ContextMenuStrip rowMenu;
        private System.Windows.Forms.ToolStripMenuItem targetsItem;
        private System.Windows.Forms.ToolStripSeparator targetsSeparator;
        private System.Windows.Forms.ToolStripMenuItem pauseItem;
        private System.Windows.Forms.ToolStripMenuItem resumeItem;
        private System.Windows.Forms.ToolStripMenuItem nightOnlyItem;
        private System.Windows.Forms.ToolStripMenuItem nightOnlyClearItem;
        private System.Windows.Forms.ToolStripSeparator menuSeparator;
        private System.Windows.Forms.ToolStripMenuItem cardItem;
        private System.Windows.Forms.ToolStripMenuItem wakeItem;
        private System.Windows.Forms.ToolStripMenuItem processesItem;
        private System.Windows.Forms.ToolStripMenuItem litePMItem;
        private System.Windows.Forms.ToolStripMenuItem logsItem;
        private System.Windows.Forms.ToolStripSeparator removeSeparator;
        private System.Windows.Forms.ToolStripMenuItem removeItem;
        private System.Windows.Forms.ToolStripMenuItem unhideItem;
        private System.Windows.Forms.ToolStripButton hiddenFilter;
        private System.Windows.Forms.ToolStripMenuItem installItem;
        private System.Windows.Forms.ToolStripSeparator installSeparator;
        private System.Windows.Forms.ToolStripMenuItem teamViewerItem;
        private System.Windows.Forms.ToolStripMenuItem remoteDesktopItem;
        private System.Windows.Forms.ToolStripMenuItem commandPromptItem;
        private System.Windows.Forms.ToolStripMenuItem quickCommandsItem;
        private System.Windows.Forms.ToolStripSeparator exportSeparator;
        private System.Windows.Forms.ToolStripMenuItem royalTsItem;
        private System.Windows.Forms.ToolStripMenuItem inventoryItem;
        private System.Windows.Forms.ToolStripMenuItem exportInventoryItem;
        private System.Windows.Forms.ToolStripButton checkButton;
        private System.Windows.Forms.ToolStripButton selectOutdatedButton;
        private System.Windows.Forms.ToolStripButton selectAllButton;
        private System.Windows.Forms.ToolStripButton selectNoneButton;
        private System.Windows.Forms.ToolStripButton byPackageButton;
        private System.Windows.Forms.ToolStripSeparator separator2;
        private System.Windows.Forms.ToolStripButton updateButton;
        private System.Windows.Forms.ToolStripButton stopButton;
        private System.Windows.Forms.ToolStripSeparator separator3;
        private System.Windows.Forms.ToolStripDropDownButton actionsButton;
        private System.Windows.Forms.ToolStripSeparator separator1;
        private System.Windows.Forms.ToolStripButton editPackagesButton;
        private System.Windows.Forms.ToolStripButton scheduleButton;
        private System.Windows.Forms.ToolStripLabel searchLabel;
        private System.Windows.Forms.ToolStripTextBox searchBox;
        private System.Windows.Forms.ToolStripSeparator filterSeparator1;
        private System.Windows.Forms.ToolStripButton toUpdateFilter;
        private System.Windows.Forms.ToolStripButton errorsFilter;
        private System.Windows.Forms.ToolStripButton upToDateFilter;
        private System.Windows.Forms.ToolStripButton notCheckedFilter;
        private System.Windows.Forms.ToolStripButton pausedFilter;
        private System.Windows.Forms.ToolStripButton nightOnlyFilter;
        private System.Windows.Forms.ToolStripButton staleFilter;
        private System.Windows.Forms.ToolStripSeparator filterSeparator2;
        private System.Windows.Forms.CheckBox includeServersCheck;
        private System.Windows.Forms.ToolStripButton addButton;
        private System.Windows.Forms.ToolStripSeparator separator4;
        private System.Windows.Forms.ToolStripButton deployButton;
        private System.Windows.Forms.ToolStripButton onlineOnlyButton;
        private System.Windows.Forms.ToolStripButton profileButton;
        private System.Windows.Forms.ToolStripButton refreshStateButton;
        private System.Windows.Forms.ToolStripDropDownButton columnsButton;
        private System.Windows.Forms.ColumnHeader stateColumn;
        private System.Windows.Forms.ColumnHeader userColumn;
        private System.Windows.Forms.ColumnHeader lastScanColumn;
        private System.Windows.Forms.ColumnHeader manufacturerColumn;
        private System.Windows.Forms.ColumnHeader modelColumn;
        private System.Windows.Forms.ColumnHeader serialColumn;
        private System.Windows.Forms.ColumnHeader yearColumn;
        private System.Windows.Forms.ColumnHeader cpuColumn;
        private System.Windows.Forms.ColumnHeader coresColumn;
        private System.Windows.Forms.ColumnHeader ramColumn;
        private System.Windows.Forms.ColumnHeader diskColumn;
        private System.Windows.Forms.ColumnHeader diskFreeColumn;
        private System.Windows.Forms.ToolStripMenuItem pingItem;
        private System.Windows.Forms.ToolStripMenuItem managementConsoleItem;
        private System.Windows.Forms.ToolStripMenuItem adminShareItem;
        private System.Windows.Forms.ToolStripMenuItem adminShareRootItem;
        private System.Windows.Forms.ToolStripMenuItem publicDesktopItem;
        private System.Windows.Forms.ToolStripMenuItem startMenuItem;
        private System.Windows.Forms.ToolStripMenuItem copyNameItem;
        private System.Windows.Forms.ToolStripSeparator deploySeparator;
        private System.Windows.Forms.ToolStripMenuItem deployItem;
        private System.Windows.Forms.SplitContainer splitContainer;
        private System.Windows.Forms.ListView updatesList;
        private System.Windows.Forms.TextBox detailsBox;
        private System.Windows.Forms.ColumnHeader computerColumn;
        private System.Windows.Forms.ColumnHeader osColumn;
        private System.Windows.Forms.ColumnHeader lastLogonColumn;
        private System.Windows.Forms.ColumnHeader packageColumn;
        private System.Windows.Forms.ColumnHeader updatesColumn;
        private System.Windows.Forms.ColumnHeader installedColumn;
        private System.Windows.Forms.ColumnHeader availableColumn;
        private System.Windows.Forms.ColumnHeader statusColumn;
        private System.Windows.Forms.ColumnHeader resultColumn;
        private System.Windows.Forms.ColumnHeader changedColumn;
        private System.Windows.Forms.ColumnHeader pausedColumn;
        private System.Windows.Forms.ColumnHeader nightOnlyColumn;
        private System.Windows.Forms.StatusStrip statusStrip;
        private System.Windows.Forms.ToolStripStatusLabel statusLabel;
        private System.Windows.Forms.ToolStripStatusLabel countsLabel;
        private System.Windows.Forms.ToolStripProgressBar progressBar;
    }
}
