// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    CatalogImport.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-18
// Purpose: Import from catalog... (Package list and Web versions): choose a
//          catalog (ModuleCatalog), confirm, write the parameter values of its
//          instances to module-settings.xml. The dialog opens the shared
//          "catalogs" folder next to the shared modules folder when it exists
//          (CatalogLocation), else %LOCALAPPDATA%\LiteDeploy.
// -----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using LiteDeploy.Worker;

namespace LiteDeploy
{
    internal static class CatalogImport
    {
        /// <summary>
        /// Asks for a catalog and imports it. Returns the package rows of the imported instances (the caller adds
        /// the missing ones to the package list), or null when nothing was imported (cancelled or refused).
        /// </summary>
        public static List<UpdatePackage> Run(IWin32Window owner, String title)
        {
            String path;
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                String localFolder = Path.GetDirectoryName(ModuleCatalog.LocalPath);
                String folder = CatalogLocation.InitialFolder(UserSettings.Current.SharedModulesFolder,
                    p => FolderCheck.Probe(p, TimeSpan.FromSeconds(5)) == FolderState.Ok, ModuleCatalog.LocalPath);
                dialog.Title = Localization.T("PackageListForm.CatalogTitle", "Catalog of products");
                dialog.Filter = Localization.T("PackageListForm.CatalogFilter", "Catalog (*.json)|*.json|All files (*.*)|*.*");
                dialog.InitialDirectory = folder;
                // The local catalog is proposed only when the dialog opens its folder
                dialog.FileName = String.Equals(folder, localFolder, StringComparison.OrdinalIgnoreCase) ? Path.GetFileName(ModuleCatalog.LocalPath) : "";
                dialog.CheckFileExists = true;
                if (dialog.ShowDialog(owner) != DialogResult.OK) return null;
                path = dialog.FileName;
            }

            ModuleCatalog catalog;
            try { catalog = ModuleCatalog.Load(path); }
            catch (Exception ex)
            {
                MessageBox.Show(owner, ex.Message, title, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return null;
            }
            // The products of one file may belong to several modules: each module must exist and support instances;
            // the products of a module that does not are skipped and reported
            List<String> problems = new List<String>(catalog.Errors);
            List<KeyValuePair<UpdateModule, List<ModuleCatalogEntry>>> groups = new List<KeyValuePair<UpdateModule, List<ModuleCatalogEntry>>>();
            foreach (IGrouping<String, ModuleCatalogEntry> g in catalog.Entries.GroupBy(e => e.Module, StringComparer.OrdinalIgnoreCase))
            {
                UpdateModule m = UpdateModules.Find(g.Key);
                if (m == null)
                    problems.Add(Localization.F("UpdateModules.Missing", "Module {0} not found in {1}.", g.Key, String.Join(", ", UpdateModules.Dirs)));
                else if (!m.Instances)
                    problems.Add(Localization.F("UpdateModules.NoInstances", "The module {0} does not support instances ({1}).", m.Name, path));
                else
                    groups.Add(new KeyValuePair<UpdateModule, List<ModuleCatalogEntry>>(m, g.ToList()));
            }
            if (groups.Count == 0)
            {
                if (catalog.Entries.Count == 0) problems.Insert(0, Localization.T("PackageListForm.CatalogEmpty", "The catalog has no valid product."));
                MessageBox.Show(owner, String.Join(Environment.NewLine, problems), title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return null;
            }

            String question = Localization.T("PackageListForm.CatalogImportQuestion", "Import these products?") + "\n"
                + String.Join("\n", groups.Select(g => "- " + Localization.F("PackageListForm.CatalogModuleLine", "{0} product(s) of the module {1}: {2}",
                    g.Value.Count, g.Key.Name, String.Join(", ", g.Value.Select(x => x.Instance)))))
                + "\n" + Localization.T("PackageListForm.CatalogReplaces", "The parameter values of these instances are replaced by the ones of the catalog (secret parameters are not changed).");
            if (problems.Count > 0) question += "\n\n" + String.Join("\n", problems);
            if (MessageBox.Show(owner, question, title, MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return null;

            ModuleSettings settings = ModuleSettings.Load();
            List<String> warnings = new List<String>();
            List<UpdatePackage> imported = new List<UpdatePackage>();
            foreach (KeyValuePair<UpdateModule, List<ModuleCatalogEntry>> g in groups)
            {
                foreach (ModuleCatalogEntry entry in g.Value)
                {
                    ModuleCatalog.Apply(entry, g.Key, settings, warnings);
                    UpdatePackage p = g.Key.ToPackage(entry.Instance, entry.Label);
                    if (p != null) imported.Add(p);
                }
            }
            try { settings.Save(); }
            catch (Exception ex)
            {
                MessageBox.Show(owner, ex.Message, title, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return null;
            }
            if (warnings.Count > 0)
                MessageBox.Show(owner, String.Join(Environment.NewLine, warnings), title, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return imported;
        }

        /// <summary>Adds the packages that are not in the list yet (same Id, case-insensitive); returns how many were added.</summary>
        public static int AddMissing(List<UpdatePackage> list, IEnumerable<UpdatePackage> packages)
        {
            int added = 0;
            foreach (UpdatePackage p in packages ?? Enumerable.Empty<UpdatePackage>())
            {
                if (p == null || list.Any(x => String.Equals(x.Id, p.Id, StringComparison.OrdinalIgnoreCase))) continue;
                list.Add(p);
                added++;
            }
            return added;
        }
    }
}
