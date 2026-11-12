// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    PackageListForm.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-12
// Purpose: Editor of the software update package list: search the winget
//          catalog (winget of this computer), add a result, set the processes
//          to close and the label, remove a package; add an update module
//          (docs/modules.md).
// -----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using LiteDeploy.Worker;

namespace LiteDeploy
{
    public partial class PackageListForm : Form
    {
        /// <summary>The list as saved by the user (valid rows only).</summary>
        internal List<UpdatePackage> Packages { get; private set; }

        internal PackageListForm(IEnumerable<UpdatePackage> packages)
        {
            InitializeComponent();
            Localization.Apply(this);
            idColumn.HeaderText = Localization.T("PackageListForm.IdColumn", "winget Id");
            processesColumn.HeaderText = Localization.T("PackageListForm.ProcessesColumn", "Processes to close");
            labelColumn.HeaderText = Localization.T("PackageListForm.LabelColumn", "Label");
            Theme.Apply(this);
            foreach (UpdatePackage p in packages)
                packagesGrid.Rows.Add(p.Id, String.Join(", ", p.Processes), p.Label ?? "");

            foreach (UpdateModule m in UpdateModules.List())
                modulesCombo.Items.Add(new ModuleItem { Module = m });
            if (modulesCombo.Items.Count > 0) modulesCombo.SelectedIndex = 0;
            addModuleButton.Enabled = modulesCombo.Items.Count > 0;
        }

        sealed class ModuleItem
        {
            public UpdateModule Module;
            public override String ToString() { return Module.Label + "  (" + Module.Name + ")"; }
        }

        void addModuleButton_Click(object sender, EventArgs e)
        {
            ModuleItem item = modulesCombo.SelectedItem as ModuleItem;
            UpdatePackage p = item == null ? null : item.Module.ToPackage();
            if (p == null) return;
            bool present = packagesGrid.Rows.Cast<DataGridViewRow>()
                .Any(r => String.Equals(Convert.ToString(r.Cells[0].Value), p.Id, StringComparison.OrdinalIgnoreCase));
            if (!present) packagesGrid.Rows.Add(p.Id, String.Join(", ", p.Processes), p.Label ?? "");
        }

        // ---- Search -----------------------------------------------------------------

        void searchBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                searchButton_Click(sender, e);
            }
        }

        void searchButton_Click(object sender, EventArgs e)
        {
            String query = searchBox.Text.Trim();
            if (query.Length < 2) return;
            String winget = WingetClient.Locate();
            if (winget == null)
            {
                MessageBox.Show(this, Localization.T("PackageListForm.NoWinget", "winget is not available on this computer: type the package Id in the list."),
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            searchButton.Enabled = false;
            resultsList.Items.Clear();
            Cursor = Cursors.WaitCursor;
            new Thread(() =>
            {
                List<WingetListRow> rows;
                try { rows = WingetListParser.Parse(new WingetClient(winget, TimeSpan.FromMinutes(2)).Search(query).Output); }
                catch (Exception) { rows = new List<WingetListRow>(); }
                if (IsDisposed) return;
                BeginInvoke(new MethodInvoker(() =>
                {
                    foreach (WingetListRow r in rows.Where(r => UpdatePackage.IsValidId(r.Id)))
                        resultsList.Items.Add(new ListViewItem(new[] { r.Name, r.Id, r.Version }));
                    if (resultsList.Items.Count == 0)
                        resultsList.Items.Add(new ListViewItem(new[] { Localization.T("PackageListForm.NoResult", "No package found."), "", "" }));
                    searchButton.Enabled = true;
                    Cursor = Cursors.Default;
                }));
            }) { IsBackground = true }.Start();
        }

        void addButton_Click(object sender, EventArgs e)
        {
            foreach (ListViewItem item in resultsList.SelectedItems)
            {
                String id = item.SubItems[1].Text;
                if (!UpdatePackage.IsValidId(id)) continue;
                bool present = packagesGrid.Rows.Cast<DataGridViewRow>()
                    .Any(r => String.Equals(Convert.ToString(r.Cells[0].Value), id, StringComparison.OrdinalIgnoreCase));
                if (!present)
                    packagesGrid.Rows.Add(id, "", item.SubItems[0].Text);
            }
        }

        // ---- Save -------------------------------------------------------------------

        void okButton_Click(object sender, EventArgs e)
        {
            List<UpdatePackage> list = new List<UpdatePackage>();
            List<String> invalid = new List<String>();
            foreach (DataGridViewRow row in packagesGrid.Rows)
            {
                if (row.IsNewRow) continue;
                String id = Convert.ToString(row.Cells[0].Value).Trim();
                if (id.Length == 0) continue;
                // Same rules as the package list file (Id and process names are checked)
                UpdatePackage p = UpdatePackage.Parse(id + " | " + Convert.ToString(row.Cells[1].Value) + " | " + Convert.ToString(row.Cells[2].Value).Replace("|", " "));
                if (p == null) { invalid.Add(id); continue; }
                if (!list.Any(x => String.Equals(x.Id, p.Id, StringComparison.OrdinalIgnoreCase))) list.Add(p);
            }
            if (invalid.Count > 0)
            {
                MessageBox.Show(this, Localization.F("PackageListForm.InvalidIds", "Invalid package Id: {0}. An Id has letters, digits and . - _ + only.", String.Join(", ", invalid)),
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            Packages = list;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
