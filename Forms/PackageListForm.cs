// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    PackageListForm.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-12
// Purpose: Software catalog (packages.txt) and its profiles (profiles.xml):
//          search the winget catalog (winget of this computer), add a result,
//          set the processes to close and the label, remove packages (Remove or
//          the Delete key: the selected rows, else the rows of the selected
//          cells, after a confirmation that names them); add an
//          update module (docs/modules.md) and set its parameters
//          (Parameters...); for a module with instances, add one row per
//          instance and import the instances of a module catalog (Import
//          instances...); open Web versions... (versions of the products of
//          the shared cache on the web). One check box column per profile (a
//          named selection of the catalog: New, Rename, Delete profile).
//          Export / Import of the catalog and its profiles (SoftwareCatalogFile;
//          an import only adds).
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

        /// <summary>The profiles as saved by the user (packages of the catalog only).</summary>
        internal SoftwareProfiles Profiles { get; private set; }

        /// <summary>Name prefix of the check box column of a profile.</summary>
        const String ProfileColumnPrefix = "profile:";

        internal PackageListForm(IEnumerable<UpdatePackage> packages, SoftwareProfiles profiles)
        {
            InitializeComponent();
            Localization.Apply(this);
            idColumn.HeaderText = Localization.T("PackageListForm.IdColumn", "winget Id");
            processesColumn.HeaderText = Localization.T("PackageListForm.ProcessesColumn", "Processes to close");
            labelColumn.HeaderText = Localization.T("PackageListForm.LabelColumn", "Label");
            Theme.Apply(this);
            // The two imports do different things: say which is which
            if (components == null) components = new System.ComponentModel.Container();
            ToolTip tips = new ToolTip(components) { AutoPopDelay = 15000 };
            tips.SetToolTip(exportButton, Localization.T("PackageListForm.ExportTip",
                "Saves the software list and its profiles to a file, for another administrator (Import a list...)."));
            tips.SetToolTip(importButton, Localization.T("PackageListForm.ImportTip",
                "Adds the software and the profiles of a file made with Export the list... Nothing is removed."));
            tips.SetToolTip(importCatalogButton, Localization.T("PackageListForm.ImportInstancesTip",
                "Creates or updates the instances of modules (CachedInstaller, MsixProvision, TeamViewer...) and their parameters from a JSON catalog of the team's catalogs folder."));
            ClientSize = new System.Drawing.Size(860, 560);
            Populate(packages.ToList(), profiles ?? new SoftwareProfiles());

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

        // ---- Profiles -----------------------------------------------------------------

        // The rows and the profile columns of a catalog
        void Populate(List<UpdatePackage> packages, SoftwareProfiles profiles)
        {
            packagesGrid.Rows.Clear();
            foreach (DataGridViewColumn c in packagesGrid.Columns.Cast<DataGridViewColumn>().Where(IsProfileColumn).ToList())
            {
                packagesGrid.Columns.Remove(c);
                c.Dispose();
            }
            foreach (SoftwareProfile profile in profiles.Items) AddProfileColumn(profile.Name);
            foreach (UpdatePackage p in packages)
            {
                int index = packagesGrid.Rows.Add(p.Id, String.Join(", ", p.Processes), p.Label ?? "");
                foreach (SoftwareProfile profile in profiles.Items)
                    packagesGrid.Rows[index].Cells[ProfileColumnPrefix + profile.Name].Value = profile.Contains(p.Id);
            }
        }

        static bool IsProfileColumn(DataGridViewColumn c)
        {
            return c.Name.StartsWith(ProfileColumnPrefix, StringComparison.Ordinal);
        }

        DataGridViewCheckBoxColumn AddProfileColumn(String name)
        {
            DataGridViewCheckBoxColumn column = new DataGridViewCheckBoxColumn
            {
                Name = ProfileColumnPrefix + name,
                HeaderText = name,
                ToolTipText = Localization.F("PackageListForm.ProfileTip", "Checked: in the profile {0}", name),
                AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells,
                MinimumWidth = 50,
                SortMode = DataGridViewColumnSortMode.NotSortable
            };
            packagesGrid.Columns.Add(column);
            return column;
        }

        IEnumerable<String> ProfileNames()
        {
            return packagesGrid.Columns.Cast<DataGridViewColumn>().Where(IsProfileColumn).Select(c => c.Name.Substring(ProfileColumnPrefix.Length));
        }

        // The profile of the current cell, else the only one; null (with a message) otherwise
        DataGridViewColumn CurrentProfileColumn()
        {
            DataGridViewColumn column = packagesGrid.CurrentCell == null ? null : packagesGrid.Columns[packagesGrid.CurrentCell.ColumnIndex];
            if (column != null && IsProfileColumn(column)) return column;
            List<DataGridViewColumn> all = packagesGrid.Columns.Cast<DataGridViewColumn>().Where(IsProfileColumn).ToList();
            if (all.Count == 1) return all[0];
            MessageBox.Show(this, Localization.T("PackageListForm.SelectProfile", "Click a cell of the profile column first."), Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return null;
        }

        // A new name, valid and not used; null when cancelled
        String AskProfileName(String current)
        {
            String name = current ?? "";
            while (true)
            {
                name = Prompt.Show(this, Text, Localization.T("PackageListForm.ProfileName", "Name of the profile (letters, digits, spaces and . - _ + ( ), 40 at most):"), name);
                if (name == null) return null;
                name = name.Trim();
                if (!SoftwareProfiles.IsValidName(name))
                {
                    MessageBox.Show(this, Localization.T("PackageListForm.InvalidProfileName", "Invalid profile name."), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    continue;
                }
                if (!String.Equals(name, current, StringComparison.OrdinalIgnoreCase)
                    && ProfileNames().Any(n => String.Equals(n, name, StringComparison.OrdinalIgnoreCase)))
                {
                    MessageBox.Show(this, Localization.F("PackageListForm.ProfileExists", "The profile {0} already exists.", name), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    continue;
                }
                return name;
            }
        }

        void newProfileButton_Click(object sender, EventArgs e)
        {
            String name = AskProfileName(null);
            if (name == null) return;
            DataGridViewCheckBoxColumn column = AddProfileColumn(name);
            foreach (DataGridViewRow row in packagesGrid.Rows)
            {
                if (!row.IsNewRow) row.Cells[column.Index].Value = false;
            }
            if (packagesGrid.Rows.Count > 0 && !packagesGrid.Rows[0].IsNewRow)
                packagesGrid.CurrentCell = packagesGrid.Rows[0].Cells[column.Index];
        }

        void renameProfileButton_Click(object sender, EventArgs e)
        {
            DataGridViewColumn column = CurrentProfileColumn();
            if (column == null) return;
            String current = column.Name.Substring(ProfileColumnPrefix.Length);
            String name = AskProfileName(current);
            if (name == null || name == current) return;
            column.Name = ProfileColumnPrefix + name;
            column.HeaderText = name;
            column.ToolTipText = Localization.F("PackageListForm.ProfileTip", "Checked: in the profile {0}", name);
            renamed[current] = name;
        }

        /// <summary>Profiles renamed in this window, old name to new name (the schedules and the main window follow).</summary>
        readonly Dictionary<String, String> renamed = new Dictionary<String, String>(StringComparer.OrdinalIgnoreCase);

        internal IDictionary<String, String> Renamed { get { return renamed; } }

        void deleteProfileButton_Click(object sender, EventArgs e)
        {
            DataGridViewColumn column = CurrentProfileColumn();
            if (column == null) return;
            String name = column.Name.Substring(ProfileColumnPrefix.Length);
            if (MessageBox.Show(this, Localization.F("PackageListForm.ConfirmDeleteProfile", "Delete the profile {0}? The software stays in the catalog.", name),
                    Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;
            packagesGrid.Columns.Remove(column);
            column.Dispose();
        }

        // ---- Export / import of the catalog --------------------------------------------

        /// <summary>The catalog and the profiles of the window; false (with a message) when a row is invalid.</summary>
        bool Collect(out List<UpdatePackage> list, out SoftwareProfiles profiles)
        {
            list = new List<UpdatePackage>();
            profiles = new SoftwareProfiles();
            List<String> invalid = new List<String>();
            List<String> names = ProfileNames().ToList();
            foreach (String n in names) profiles.Items.Add(new SoftwareProfile { Name = n });
            foreach (DataGridViewRow row in packagesGrid.Rows)
            {
                if (row.IsNewRow) continue;
                String id = Convert.ToString(row.Cells[0].Value).Trim();
                if (id.Length == 0) continue;
                // Same rules as the package list file (Id and process names are checked)
                UpdatePackage p = UpdatePackage.Parse(id + " | " + Convert.ToString(row.Cells[1].Value) + " | " + Convert.ToString(row.Cells[2].Value).Replace("|", " "));
                if (p == null) { invalid.Add(id); continue; }
                if (list.Any(x => String.Equals(x.Id, p.Id, StringComparison.OrdinalIgnoreCase))) continue;
                list.Add(p);
                foreach (SoftwareProfile profile in profiles.Items)
                {
                    if (Convert.ToBoolean(row.Cells[ProfileColumnPrefix + profile.Name].Value ?? false)) profile.Packages.Add(p.Id);
                }
            }
            if (invalid.Count > 0)
            {
                MessageBox.Show(this, Localization.F("PackageListForm.InvalidIds", "Invalid package Id: {0}. An Id has letters, digits and . - _ + only.", String.Join(", ", invalid)),
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            return true;
        }

        // The shared catalogs folder when it exists (as Import instances...), else the local folder of LiteDeploy
        static String CatalogFolder()
        {
            try
            {
                return CatalogLocation.InitialFolder(UserSettings.Current.SharedModulesFolder,
                    p => FolderCheck.Probe(p, TimeSpan.FromSeconds(5)) == FolderState.Ok, ModuleCatalog.LocalPath);
            }
            catch (Exception) { return null; }
        }

        void exportButton_Click(object sender, EventArgs e)
        {
            List<UpdatePackage> list;
            SoftwareProfiles profiles;
            if (!Collect(out list, out profiles)) return;
            using (SaveFileDialog dialog = new SaveFileDialog())
            {
                dialog.Title = Localization.T("PackageListForm.ExportTitle", "Export the software catalog");
                dialog.Filter = Localization.T("PackageListForm.SoftwareCatalogFilter", "Software catalog (*.json)|*.json");
                dialog.FileName = "LiteDeploy-software-catalog.json";
                String folder = CatalogFolder();
                if (folder != null) dialog.InitialDirectory = folder;
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    System.IO.File.WriteAllText(dialog.FileName, SoftwareCatalogFile.From(list, profiles).ToJson(), new System.Text.UTF8Encoding(false));
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        void importButton_Click(object sender, EventArgs e)
        {
            List<UpdatePackage> list;
            SoftwareProfiles profiles;
            if (!Collect(out list, out profiles)) return;
            SoftwareCatalogFile file;
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = Localization.T("PackageListForm.ImportTitle", "Import a software catalog");
                dialog.Filter = Localization.T("PackageListForm.SoftwareCatalogFilter", "Software catalog (*.json)|*.json");
                String folder = CatalogFolder();
                if (folder != null) dialog.InitialDirectory = folder;
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try { file = SoftwareCatalogFile.Parse(System.IO.File.ReadAllText(dialog.FileName)); }
                catch (Exception ex)
                {
                    MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
            }
            SoftwareCatalogFile.MergeResult result = file.MergeInto(list, profiles);
            Populate(list, profiles);
            String message = Localization.F("PackageListForm.Imported", "{0} software added, {1} profile(s) added, {2} software added to existing profiles. Nothing was removed; Save keeps the result.",
                result.PackagesAdded, result.ProfilesAdded, result.ProfilePackagesAdded);
            if (result.Invalid.Count > 0)
                message += "\n\n" + Localization.F("PackageListForm.ImportInvalid", "Left out (invalid): {0}", String.Join(", ", result.Invalid));
            MessageBox.Show(this, message, Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        // ---- Remove --------------------------------------------------------------------

        /// <summary>The rows to remove: the selected rows, else the rows of the selected cells (never the new row), in their order.</summary>
        List<DataGridViewRow> RowsToRemove()
        {
            IEnumerable<DataGridViewRow> rows = packagesGrid.SelectedRows.Count > 0
                ? packagesGrid.SelectedRows.Cast<DataGridViewRow>()
                : packagesGrid.SelectedCells.Cast<DataGridViewCell>().Select(c => c.OwningRow);
            return rows.Where(r => r != null && !r.IsNewRow).Distinct().OrderBy(r => r.Index).ToList();
        }

        void removeButton_Click(object sender, EventArgs e)
        {
            RemoveSelected();
        }

        // Delete (not while a cell is being edited): same as Remove
        void packagesGrid_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Delete || packagesGrid.IsCurrentCellInEditMode) return;
            e.Handled = true;
            RemoveSelected();
        }

        void RemoveSelected()
        {
            List<DataGridViewRow> rows = RowsToRemove();
            if (rows.Count == 0)
            {
                MessageBox.Show(this, Localization.T("PackageListForm.SelectToRemove", "Select the software to remove (a cell or a row of each, Ctrl or Shift for several)."),
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            List<String> names = rows.Select(r =>
            {
                String id = Convert.ToString(r.Cells[0].Value);
                String label = Convert.ToString(r.Cells[2].Value);
                return String.IsNullOrWhiteSpace(label) || label == id ? id : label + " (" + id + ")";
            }).ToList();
            String list = String.Join("\n", names.Take(15)) + (names.Count > 15 ? "\n..." : "");
            if (MessageBox.Show(this, Localization.F("PackageListForm.ConfirmRemove",
                    "Remove {0} software from the catalog (and from its profiles)?\n\n{1}\n\nThe parameters of a module are kept. Nothing is uninstalled; OK saves the catalog.", rows.Count, list),
                    Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;
            foreach (DataGridViewRow r in rows) packagesGrid.Rows.Remove(r);
            parametersButton.Enabled = CurrentModule() != null;
        }

        // ---- Save -------------------------------------------------------------------

        void okButton_Click(object sender, EventArgs e)
        {
            List<UpdatePackage> list;
            SoftwareProfiles profiles;
            if (!Collect(out list, out profiles)) return;
            Packages = list;
            Profiles = profiles;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
