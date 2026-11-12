// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    ToolsForm.Designer.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-12
// Purpose: Controls of the Manage Tools window.
// -----------------------------------------------------------------------------
namespace LiteDeploy
{
    partial class ToolsForm
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
            this.toolsList = new System.Windows.Forms.ListView();
            this.toolColumn = new System.Windows.Forms.ColumnHeader();
            this.versionColumn = new System.Windows.Forms.ColumnHeader();
            this.locationColumn = new System.Windows.Forms.ColumnHeader();
            this.descriptionColumn = new System.Windows.Forms.ColumnHeader();
            this.installButton = new System.Windows.Forms.Button();
            this.openButton = new System.Windows.Forms.Button();
            this.closeButton = new System.Windows.Forms.Button();
            this.statusLabel = new System.Windows.Forms.Label();
            this.SuspendLayout();
            //
            // toolsList
            //
            this.toolsList.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) | System.Windows.Forms.AnchorStyles.Left) | System.Windows.Forms.AnchorStyles.Right)));
            this.toolsList.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] {
            this.toolColumn,
            this.versionColumn,
            this.locationColumn,
            this.descriptionColumn});
            this.toolsList.FullRowSelect = true;
            this.toolsList.HideSelection = false;
            this.toolsList.Location = new System.Drawing.Point(12, 12);
            this.toolsList.MultiSelect = false;
            this.toolsList.Name = "toolsList";
            this.toolsList.Size = new System.Drawing.Size(660, 150);
            this.toolsList.TabIndex = 0;
            this.toolsList.UseCompatibleStateImageBehavior = false;
            this.toolsList.View = System.Windows.Forms.View.Details;
            this.toolsList.SelectedIndexChanged += new System.EventHandler(this.toolsList_SelectedIndexChanged);
            this.toolColumn.Text = "Tool";
            this.toolColumn.Width = 80;
            this.versionColumn.Text = "Version";
            this.versionColumn.Width = 90;
            this.locationColumn.Text = "Location";
            this.locationColumn.Width = 230;
            this.descriptionColumn.Text = "Description";
            this.descriptionColumn.Width = 250;
            //
            // statusLabel
            //
            this.statusLabel.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left) | System.Windows.Forms.AnchorStyles.Right)));
            this.statusLabel.Location = new System.Drawing.Point(12, 170);
            this.statusLabel.Name = "statusLabel";
            this.statusLabel.Size = new System.Drawing.Size(660, 30);
            //
            // installButton
            //
            this.installButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.installButton.Location = new System.Drawing.Point(12, 205);
            this.installButton.Name = "installButton";
            this.installButton.Size = new System.Drawing.Size(140, 25);
            this.installButton.TabIndex = 1;
            this.installButton.Text = "Download / Update";
            this.installButton.Click += new System.EventHandler(this.installButton_Click);
            //
            // openButton
            //
            this.openButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.openButton.Location = new System.Drawing.Point(158, 205);
            this.openButton.Name = "openButton";
            this.openButton.Size = new System.Drawing.Size(100, 25);
            this.openButton.TabIndex = 2;
            this.openButton.Text = "Open";
            this.openButton.Click += new System.EventHandler(this.openButton_Click);
            //
            // closeButton
            //
            this.closeButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.closeButton.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.closeButton.Location = new System.Drawing.Point(597, 205);
            this.closeButton.Name = "closeButton";
            this.closeButton.Size = new System.Drawing.Size(75, 25);
            this.closeButton.TabIndex = 3;
            this.closeButton.Text = "Close";
            //
            // ToolsForm
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.CancelButton = this.closeButton;
            this.ClientSize = new System.Drawing.Size(684, 242);
            this.Controls.Add(this.toolsList);
            this.Controls.Add(this.statusLabel);
            this.Controls.Add(this.installButton);
            this.Controls.Add(this.openButton);
            this.Controls.Add(this.closeButton);
            this.MinimizeBox = false;
            this.MinimumSize = new System.Drawing.Size(500, 240);
            this.Name = "ToolsForm";
            this.ShowIcon = false;
            this.ShowInTaskbar = false;
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Lite Deploy - Tools";
            this.ResumeLayout(false);
        }

        private System.Windows.Forms.ListView toolsList;
        private System.Windows.Forms.ColumnHeader toolColumn;
        private System.Windows.Forms.ColumnHeader versionColumn;
        private System.Windows.Forms.ColumnHeader locationColumn;
        private System.Windows.Forms.ColumnHeader descriptionColumn;
        private System.Windows.Forms.Button installButton;
        private System.Windows.Forms.Button openButton;
        private System.Windows.Forms.Button closeButton;
        private System.Windows.Forms.Label statusLabel;
    }
}
