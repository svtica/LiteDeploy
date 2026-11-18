// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    PackageListForm.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-12
// Purpose: Editor of the software update package list: search the winget
//          catalog (winget of this computer), add a result, set the processes
//          to close and the label, remove a package; add an update module
//          (docs/modules.md) and set its parameters (Parameters...); for a
//          module with instances, add one row per instance and import the
//          instances of a catalog (Import from catalog...); open Web versions...
//          (versions of the products of the shared cache on the web).
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

            String modulesWarning;
            foreach (UpdateModule m in UpdateModules.List(out modulesWarning))
                modulesCombo.Items.Add(new ModuleItem { Module = m });
            if (modulesCombo.Items.Count > 0) modulesCombo.SelectedIndex = 0;
            addModuleButton.Enabled = modulesCombo.Items.Count > 0;
            // Shared modules folder unreachable: the other modules are listed, the user is told why some are missing
            if (modulesWarning != null)
                Shown += (s, e) => MessageBox.Show(this, modulesWarning, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        sealed class ModuleItem
        {
            public UpdateModule Module;
            public override String ToString() { return Module.Label + "  (" + Module.Name + ")"; }
        }

        void addModuleButton_Click(object sender, EventArgs e)
        {
            ModuleItem item = modulesCombo.SelectedItem as ModuleItem;
            if (item == null) return;
            if (item.Module.Instances)
            {
                AddInstance(item.Module);
                return;
            }
            UpdatePackage p = item.Module.ToPackage();
            if (p == null) return;
            if (!IsListed(p.Id)) packagesGrid.Rows.Add(p.Id, String.Join(", ", p.Processes), p.Label ?? "");
        }

        bool IsListed(String id)
        {
            return packagesGrid.Rows.Cast<DataGridViewRow>()
                .Any(r => String.Equals(Convert.ToString(r.Cells[0].Value).Trim(), id, StringComparison.OrdinalIgnoreCase));
        }

        // A module with "# Instances: yes": one row per instance ("module:Name/Instance"), then its parameters
        void AddInstance(UpdateModule module)
        {
            String instance = "";
            while (true)
            {
                instance = Prompt.Show(this, Text,
                    Localization.F("PackageListForm.InstanceName", "Name of the instance of {0} (letters, digits and . - _), e.g. the product:", module.Name), instance);
                if (instance == null) return;
                if (!UpdatePackage.IsValidInstanceName(instance))
                {
                    MessageBox.Show(this, Localization.T("PackageListForm.InvalidInstance", "Invalid instance name: letters (A-Z), digits and . - _ only, 64 characters at most."),
                        Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    continue;
                }
                if (IsListed(UpdatePackage.ModuleId(module.Name, instance)))
                {
                    MessageBox.Show(this, Localization.F("PackageListForm.InstanceListed", "{0} is already in the list.", UpdatePackage.ModuleId(module.Name, instance)),
                        Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    continue;
                }
                break;
            }
            UpdatePackage p = module.ToPackage(instance, null);
            if (p == null) return;
            int index = packagesGrid.Rows.Add(p.Id, String.Join(", ", p.Processes), p.Label ?? "");
            packagesGrid.CurrentCell = packagesGrid.Rows[index].Cells[0];
            if (module.Parameters.Count > 0) EditParameters(module, p.InstanceName, packagesGrid.Rows[index]);
        }

        // ---- Module parameters -------------------------------------------------------

        // Package of the current row when it is a module or an instance; null for a winget package or an empty row
        UpdatePackage CurrentModule()
        {
            DataGridViewRow row = packagesGrid.CurrentRow;
            if (row == null || row.IsNewRow) return null;
            UpdatePackage p = UpdatePackage.Parse(Convert.ToString(row.Cells[0].Value));
            return p != null && p.IsModule ? p : null;
        }

        void packagesGrid_CurrentCellChanged(object sender, EventArgs e)
        {
            parametersButton.Enabled = CurrentModule() != null;
        }

        void parametersButton_Click(object sender, EventArgs e)
        {
            UpdatePackage p = CurrentModule();
            if (p == null) return;
            UpdateModule m = UpdateModules.Find(p.ModuleName);
            if (m == null)
            {
                MessageBox.Show(this, Localization.F("UpdateModules.Missing", "Module {0} not found in {1} or {2}.", p.ModuleName, UpdateModules.ShippedDir, UpdateModules.UserDir),
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (p.InstanceName != null && !m.Instances)
            {
                MessageBox.Show(this, Localization.F("UpdateModules.NoInstances", "The module {0} does not support instances ({1}).", m.Name, p.Id),
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (m.Parameters.Count == 0)
            {
                MessageBox.Show(this, Localization.F("PackageListForm.NoParameters", "The module {0} has no parameters.", m.Name),
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            EditParameters(m, p.InstanceName, packagesGrid.CurrentRow);
        }

        // Parameters of a module or of one of its instances; an instance row still labelled with its
        // name takes the Product value as its label
        void EditParameters(UpdateModule m, String instance, DataGridViewRow row)
        {
            String label = row == null ? null : Convert.ToString(row.Cells[2].Value);
            ModuleSettings settings = ModuleSettings.Load();
            using (ModuleParametersForm form = new ModuleParametersForm(m, instance, label, settings))
            {
                if (form.ShowDialog(this) != DialogResult.OK || instance == null || row == null) return;
            }
            ModuleParameterSetting product = settings.Get(ModuleSettings.Key(m.Name, instance), "Product");
            if (product != null && !String.IsNullOrWhiteSpace(product.Value)
                && (String.IsNullOrWhiteSpace(label) || String.Equals(label.Trim(), instance, StringComparison.OrdinalIgnoreCase)))
                row.Cells[2].Value = product.Value.Replace("|", " ").Trim();
        }

        // ---- Catalog of instances ----------------------------------------------------

        // The shared "catalogs" folder next to the shared modules folder when it exists (CatalogImport)
        void importCatalogButton_Click(object sender, EventArgs e)
        {
            AddRows(CatalogImport.Run(this, Text));
        }

        // Rows of imported instances that are not listed yet
        void AddRows(IEnumerable<UpdatePackage> imported)
        {
            foreach (UpdatePackage p in imported ?? Enumerable.Empty<UpdatePackage>())
            {
                if (!IsListed(p.Id)) packagesGrid.Rows.Add(p.Id, String.Join(", ", p.Processes), p.Label ?? "");
            }
        }

        // ---- Web versions -----------------------------------------------------------

        // Versions of the products of the cache on the web (instances with a WingetId); a download
        // changes the File parameter of an instance in module-settings.xml, not the rows of this list.
        // An import from that window adds its rows here (this list is saved with OK).
        void webVersionsButton_Click(object sender, EventArgs e)
        {
            using (WebVersionsForm form = new WebVersionsForm(AddRows))
                form.ShowDialog(this);
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
