// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    PackageListForm.Designer.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-12
// Purpose: Controls of the package list editor.
// -----------------------------------------------------------------------------
namespace LiteDeploy
{
    partial class PackageListForm
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
            this.searchLabel = new System.Windows.Forms.Label();
            this.searchBox = new System.Windows.Forms.TextBox();
            this.searchButton = new System.Windows.Forms.Button();
            this.resultsList = new System.Windows.Forms.ListView();
            this.resultNameColumn = new System.Windows.Forms.ColumnHeader();
            this.resultIdColumn = new System.Windows.Forms.ColumnHeader();
            this.resultVersionColumn = new System.Windows.Forms.ColumnHeader();
            this.addButton = new System.Windows.Forms.Button();
            this.moduleLabel = new System.Windows.Forms.Label();
            this.modulesCombo = new System.Windows.Forms.ComboBox();
            this.addModuleButton = new System.Windows.Forms.Button();
            this.listLabel = new System.Windows.Forms.Label();
            this.packagesGrid = new System.Windows.Forms.DataGridView();
            this.idColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.processesColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.labelColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.okButton = new System.Windows.Forms.Button();
            this.cancelButton = new System.Windows.Forms.Button();
            ((System.ComponentModel.ISupportInitialize)(this.packagesGrid)).BeginInit();
            this.SuspendLayout();
            //
            // searchLabel
            //
            this.searchLabel.AutoSize = true;
            this.searchLabel.Location = new System.Drawing.Point(12, 15);
            this.searchLabel.Name = "searchLabel";
            this.searchLabel.Text = "Search winget:";
            //
            // searchBox
            //
            this.searchBox.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) | System.Windows.Forms.AnchorStyles.Right)));
            this.searchBox.Location = new System.Drawing.Point(110, 12);
            this.searchBox.Name = "searchBox";
            this.searchBox.Size = new System.Drawing.Size(460, 20);
            this.searchBox.TabIndex = 0;
            this.searchBox.KeyDown += new System.Windows.Forms.KeyEventHandler(this.searchBox_KeyDown);
            //
            // searchButton
            //
            this.searchButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.searchButton.Location = new System.Drawing.Point(576, 10);
            this.searchButton.Name = "searchButton";
            this.searchButton.Size = new System.Drawing.Size(96, 23);
            this.searchButton.TabIndex = 1;
            this.searchButton.Text = "Search";
            this.searchButton.Click += new System.EventHandler(this.searchButton_Click);
            //
            // resultsList
            //
            this.resultsList.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) | System.Windows.Forms.AnchorStyles.Right)));
            this.resultsList.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] {
            this.resultNameColumn,
            this.resultIdColumn,
            this.resultVersionColumn});
            this.resultsList.FullRowSelect = true;
            this.resultsList.HideSelection = false;
            this.resultsList.Location = new System.Drawing.Point(12, 40);
            this.resultsList.Name = "resultsList";
            this.resultsList.Size = new System.Drawing.Size(558, 150);
            this.resultsList.TabIndex = 2;
            this.resultsList.UseCompatibleStateImageBehavior = false;
            this.resultsList.View = System.Windows.Forms.View.Details;
            this.resultsList.DoubleClick += new System.EventHandler(this.addButton_Click);
            this.resultNameColumn.Text = "Name";
            this.resultNameColumn.Width = 220;
            this.resultIdColumn.Text = "Id";
            this.resultIdColumn.Width = 220;
            this.resultVersionColumn.Text = "Version";
            this.resultVersionColumn.Width = 90;
            //
            // addButton
            //
            this.addButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.addButton.Location = new System.Drawing.Point(576, 40);
            this.addButton.Name = "addButton";
            this.addButton.Size = new System.Drawing.Size(96, 23);
            this.addButton.TabIndex = 3;
            this.addButton.Text = "Add to list";
            this.addButton.Click += new System.EventHandler(this.addButton_Click);
            //
            // moduleLabel
            //
            this.moduleLabel.AutoSize = true;
            this.moduleLabel.Location = new System.Drawing.Point(12, 203);
            this.moduleLabel.Name = "moduleLabel";
            this.moduleLabel.Text = "Update module:";
            //
            // modulesCombo
            //
            this.modulesCombo.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) | System.Windows.Forms.AnchorStyles.Right)));
            this.modulesCombo.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.modulesCombo.Location = new System.Drawing.Point(110, 199);
            this.modulesCombo.Name = "modulesCombo";
            this.modulesCombo.Size = new System.Drawing.Size(460, 21);
            this.modulesCombo.TabIndex = 7;
            //
            // addModuleButton
            //
            this.addModuleButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.addModuleButton.Location = new System.Drawing.Point(576, 198);
            this.addModuleButton.Name = "addModuleButton";
            this.addModuleButton.Size = new System.Drawing.Size(96, 23);
            this.addModuleButton.TabIndex = 8;
            this.addModuleButton.Text = "Add module";
            this.addModuleButton.Click += new System.EventHandler(this.addModuleButton_Click);
            //
            // listLabel
            //
            this.listLabel.AutoSize = true;
            this.listLabel.Location = new System.Drawing.Point(12, 230);
            this.listLabel.Name = "listLabel";
            this.listLabel.Text = "Packages kept up to date (processes: closed before the update, without .exe, comma-separated):";
            //
            // packagesGrid
            //
            this.packagesGrid.AllowUserToResizeRows = false;
            this.packagesGrid.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) | System.Windows.Forms.AnchorStyles.Left) | System.Windows.Forms.AnchorStyles.Right)));
            this.packagesGrid.AutoSizeColumnsMode = System.Windows.Forms.DataGridViewAutoSizeColumnsMode.Fill;
            this.packagesGrid.Columns.AddRange(new System.Windows.Forms.DataGridViewColumn[] {
            this.idColumn,
            this.processesColumn,
            this.labelColumn});
            this.packagesGrid.Location = new System.Drawing.Point(12, 248);
            this.packagesGrid.Name = "packagesGrid";
            this.packagesGrid.RowHeadersWidth = 24;
            this.packagesGrid.Size = new System.Drawing.Size(660, 170);
            this.packagesGrid.TabIndex = 4;
            this.idColumn.HeaderText = "winget Id";
            this.idColumn.Name = "idColumn";
            this.processesColumn.HeaderText = "Processes to close";
            this.processesColumn.Name = "processesColumn";
            this.labelColumn.HeaderText = "Label";
            this.labelColumn.Name = "labelColumn";
            //
            // okButton
            //
            this.okButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.okButton.Location = new System.Drawing.Point(516, 428);
            this.okButton.Name = "okButton";
            this.okButton.Size = new System.Drawing.Size(75, 23);
            this.okButton.TabIndex = 5;
            this.okButton.Text = "Save";
            this.okButton.Click += new System.EventHandler(this.okButton_Click);
            //
            // cancelButton
            //
            this.cancelButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.cancelButton.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.cancelButton.Location = new System.Drawing.Point(597, 428);
            this.cancelButton.Name = "cancelButton";
            this.cancelButton.Size = new System.Drawing.Size(75, 23);
            this.cancelButton.TabIndex = 6;
            this.cancelButton.Text = "Cancel";
            //
            // PackageListForm
            //
            this.AcceptButton = this.searchButton;
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.CancelButton = this.cancelButton;
            this.ClientSize = new System.Drawing.Size(684, 463);
            this.Controls.Add(this.searchLabel);
            this.Controls.Add(this.searchBox);
            this.Controls.Add(this.searchButton);
            this.Controls.Add(this.resultsList);
            this.Controls.Add(this.addButton);
            this.Controls.Add(this.moduleLabel);
            this.Controls.Add(this.modulesCombo);
            this.Controls.Add(this.addModuleButton);
            this.Controls.Add(this.listLabel);
            this.Controls.Add(this.packagesGrid);
            this.Controls.Add(this.okButton);
            this.Controls.Add(this.cancelButton);
            this.MinimizeBox = false;
            this.MinimumSize = new System.Drawing.Size(600, 400);
            this.Name = "PackageListForm";
            this.ShowIcon = false;
            this.ShowInTaskbar = false;
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Lite Deploy - Package List";
            ((System.ComponentModel.ISupportInitialize)(this.packagesGrid)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        private System.Windows.Forms.Label searchLabel;
        private System.Windows.Forms.TextBox searchBox;
        private System.Windows.Forms.Button searchButton;
        private System.Windows.Forms.ListView resultsList;
        private System.Windows.Forms.ColumnHeader resultNameColumn;
        private System.Windows.Forms.ColumnHeader resultIdColumn;
        private System.Windows.Forms.ColumnHeader resultVersionColumn;
        private System.Windows.Forms.Button addButton;
        private System.Windows.Forms.Label moduleLabel;
        private System.Windows.Forms.ComboBox modulesCombo;
        private System.Windows.Forms.Button addModuleButton;
        private System.Windows.Forms.Label listLabel;
        private System.Windows.Forms.DataGridView packagesGrid;
        private System.Windows.Forms.DataGridViewTextBoxColumn idColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn processesColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn labelColumn;
        private System.Windows.Forms.Button okButton;
        private System.Windows.Forms.Button cancelButton;
    }
}
