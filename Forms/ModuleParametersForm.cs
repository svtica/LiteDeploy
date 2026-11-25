// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    ModuleParametersForm.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-17
// Purpose: Package list > Parameters...: the values of the parameters of one
//          update module (docs/modules.md), or of one instance of it
//          ("module:Name/Instance"), saved in module-settings.xml. A
//          secret parameter takes a stored credential (Tools > Credentials),
//          chosen in a list: its password is never shown nor typed here.
//          A parameter with values=a|b is chosen in a list; one with
//          files=Folder/*.msi offers the matching files of the shared cache
//          (and can still be typed); an empty box shows its default in grey.
//          Built in code (no designer file): one row per parameter.
// -----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using LiteSuite;

namespace LiteDeploy
{
    public class ModuleParametersForm : Form
    {
        readonly UpdateModule module;
        readonly ModuleSettings settings;
        readonly TableLayoutPanel table = new TableLayoutPanel();
        readonly Label infoLabel = new Label();
        readonly Button okButton = new Button();
        readonly Button cancelButton = new Button();
        // Value control of each parameter: TextBox (plain) or ComboBox (credential)
        readonly Dictionary<String, Control> editors = new Dictionary<String, Control>(StringComparer.OrdinalIgnoreCase);

        // Key of the values: "Name", or "Name/Instance" for an instance of the module
        readonly String key;

        internal ModuleParametersForm(UpdateModule module, ModuleSettings settings) : this(module, null, null, settings) { }

        /// <summary>The values of one instance of the module (<paramref name="instance"/>: null for the module itself).</summary>
        internal ModuleParametersForm(UpdateModule module, String instance, String instanceLabel, ModuleSettings settings)
        {
            this.module = module;
            this.settings = settings;
            key = ModuleSettings.Key(module.Name, instance);
            Text = String.IsNullOrEmpty(instance)
                ? Localization.F("ModuleParametersForm.Text", "Lite Deploy - Parameters of {0}", module.Label ?? module.Name)
                : Localization.F("ModuleParametersForm.InstanceText", "Lite Deploy - Parameters of {0} ({1})",
                    String.IsNullOrWhiteSpace(instanceLabel) ? instance : instanceLabel, module.Name + UpdatePackage.InstanceSeparator + instance);
            ClientSize = new Size(620, 380);
            MinimumSize = new Size(520, 300);
            StartPosition = FormStartPosition.CenterParent;
            ShowIcon = false;
            ShowInTaskbar = false;
            MinimizeBox = false;

            infoLabel.Text = Localization.T("ModuleParametersForm.Info",
                "Values given to the module on each computer (LITEDEPLOY_PARAM_<Name>). A secret parameter uses the password of a stored credential (Tools > Credentials); it is never shown.");
            infoLabel.SetBounds(12, 10, 596, 40);
            infoLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

            table.SetBounds(12, 54, 596, 280);
            table.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            table.AutoScroll = true;
            table.ColumnCount = 3;
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            BuildRows();

            okButton.Text = "OK";
            okButton.SetBounds(452, 345, 75, 25);
            okButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            okButton.Click += (s, e) => Save();
            cancelButton.Text = Localization.T("Prompt.Cancel", "Cancel");
            cancelButton.DialogResult = DialogResult.Cancel;
            cancelButton.SetBounds(533, 345, 75, 25);
            cancelButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            AcceptButton = okButton;
            CancelButton = cancelButton;

            Controls.AddRange(new Control[] { infoLabel, table, okButton, cancelButton });
            Theme.Apply(this);
        }

        static List<String> CredentialNames()
        {
            try { return CredentialStore.List().Select(c => c.Name).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList(); }
            catch (Exception) { return new List<String>(); }
        }

        void BuildRows()
        {
            List<String> credentials = CredentialNames();
            int row = 0;
            foreach (ModuleParameter p in module.Parameters)
            {
                ModuleParameterSetting current = settings.Get(key, p.Name);

                Label name = new Label { AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 8, 9, 3) };
                name.Text = p.Name + (p.Secret ? " (" + Localization.T("ModuleParametersForm.Secret", "secret") + ")" : "")
                            + (p.Required ? " *" : "");
                Control editor;
                Button add = null;
                if (p.Secret)
                {
                    ComboBox combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
                    FillCredentials(combo, credentials, current == null ? null : current.Credential);
                    editor = combo;
                    add = new Button { Text = Localization.T("ModuleParametersForm.NewCredential", "New..."), AutoSize = true };
                    add.Click += (s, e) =>
                    {
                        String created = CredentialsForm.AddDialog(this);
                        if (created != null) FillCredentials(combo, CredentialNames(), created);
                    };
                }
                else if (p.Values.Length > 0)
                {
                    ComboBox combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
                    FillValues(combo, p, current == null ? null : current.Value);
                    editor = combo;
                }
                else if (p.Files != null)
                {
                    ComboBox combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown, Dock = DockStyle.Fill, MaxLength = ModuleParameters.MaxValueLength };
                    foreach (String f in CacheFiles(UserSettings.Current.UpdateCacheShare, p.Files, MaxCacheFiles)) combo.Items.Add(f);
                    combo.Text = current == null ? "" : current.Value ?? "";
                    editor = combo;
                }
                else
                {
                    TextBox box = new TextBox { Dock = DockStyle.Fill, MaxLength = ModuleParameters.MaxValueLength };
                    box.Text = current == null ? "" : current.Value ?? "";
                    if (!String.IsNullOrEmpty(p.Default))
                        box.HandleCreated += (s, e) => SetCue(box, Localization.F("ModuleParametersForm.DefaultCue", "default: {0}", p.Default));
                    editor = box;
                }
                editors[p.Name] = editor;

                String description = p.Description ?? "";
                if (p.Default != null)
                    description = (description + " " + Localization.F("ModuleParametersForm.Default", "(default: {0})", p.Default.Length == 0 ? "\"\"" : p.Default)).Trim();
                Label help = new Label { AutoSize = true, Text = description, ForeColor = SystemColors.GrayText, Margin = new Padding(3, 0, 3, 9) };

                table.RowCount = row + 2;
                table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                table.Controls.Add(name, 0, row);
                table.Controls.Add(editor, 1, row);
                if (add != null) table.Controls.Add(add, 2, row);
                table.Controls.Add(help, 1, row + 1);
                table.SetColumnSpan(help, 2);
                row += 2;
            }
        }

        // The list of credentials, with "(none)" first; a credential that no longer exists is shown and flagged
        static void FillCredentials(ComboBox combo, List<String> names, String selected)
        {
            combo.Items.Clear();
            combo.Items.Add(new CredentialItem(null, Localization.T("ModuleParametersForm.None", "(none)")));
            foreach (String n in names) combo.Items.Add(new CredentialItem(n, n));
            if (!String.IsNullOrEmpty(selected) && !names.Contains(selected, StringComparer.OrdinalIgnoreCase))
                combo.Items.Add(new CredentialItem(selected, Localization.F("ModuleParametersForm.MissingCredential", "{0} (no longer exists)", selected)));
            combo.SelectedIndex = 0;
            for (int i = 1; i < combo.Items.Count; i++)
                if (String.Equals(((CredentialItem)combo.Items[i]).Name, selected, StringComparison.OrdinalIgnoreCase)) { combo.SelectedIndex = i; break; }
        }

        // The values of the header in a list; an optional parameter starts with "(default: x)" (no value saved).
        // A saved value that is not in the list stays, flagged, so that opening and saving changes nothing.
        static void FillValues(ComboBox combo, ModuleParameter p, String selected)
        {
            combo.Items.Clear();
            if (!p.Required)
                combo.Items.Add(new ValueItem("", Localization.F("ModuleParametersForm.DefaultItem", "(default: {0})", p.Default.Length == 0 ? "\"\"" : p.Default)));
            foreach (String v in p.Values) combo.Items.Add(new ValueItem(v, v));
            if (!String.IsNullOrEmpty(selected) && !p.Values.Contains(selected, StringComparer.OrdinalIgnoreCase))
                combo.Items.Add(new ValueItem(selected, Localization.F("ModuleParametersForm.NotInList", "{0} (not in the list)", selected)));
            combo.SelectedIndex = 0;
            for (int i = 0; i < combo.Items.Count; i++)
                if (String.Equals(((ValueItem)combo.Items[i]).Value, selected ?? "", StringComparison.OrdinalIgnoreCase)) { combo.SelectedIndex = i; break; }
        }

        sealed class ValueItem
        {
            public readonly String Value;
            readonly String text;
            public ValueItem(String value, String text) { Value = value; this.text = text; }
            public override String ToString() { return text; }
        }

        /// <summary>Files listed for a files= parameter.</summary>
        internal const int MaxCacheFiles = 300;

        /// <summary>
        /// The files of the shared cache that match the patterns of a files= parameter ("Folder/*.msi|Folder/*.exe": the
        /// folder and its subfolders), as paths relative to the cache with "/", sorted; empty without a cache, for a
        /// pattern that leaves the cache, or when the folder cannot be read. Unit-tested.
        /// </summary>
        internal static List<String> CacheFiles(String cache, String patterns, int max)
        {
            List<String> found = new List<String>();
            if (String.IsNullOrWhiteSpace(cache) || String.IsNullOrWhiteSpace(patterns)) return found;
            String root;
            try { root = Path.GetFullPath(cache.Trim()).TrimEnd('\\') + "\\"; }
            catch (Exception) { return found; }
            foreach (String raw in patterns.Split('|'))
            {
                String pattern = raw.Trim().Replace('/', '\\');
                if (pattern.Length == 0 || pattern.Contains(":") || pattern.Split('\\').Contains("..") || Path.IsPathRooted(pattern)) continue;
                String folder = Path.GetDirectoryName(pattern) ?? "";
                String mask = Path.GetFileName(pattern);
                if (mask.Length == 0) mask = "*";
                try
                {
                    String dir = Path.GetFullPath(Path.Combine(root, folder));
                    if (!(dir + "\\").StartsWith(root, StringComparison.OrdinalIgnoreCase) || !Directory.Exists(dir)) continue;
                    foreach (String f in Directory.EnumerateFiles(dir, mask, SearchOption.AllDirectories))
                    {
                        if (found.Count >= max) break;
                        String rel = f.Substring(root.Length).Replace('\\', '/');
                        // Previous versions moved aside by Web versions are not offered
                        if (rel.Split('/').Any(part => part.Equals("OLD", StringComparison.OrdinalIgnoreCase))) continue;
                        if (!found.Contains(rel, StringComparer.OrdinalIgnoreCase)) found.Add(rel);
                    }
                }
                catch (Exception) { }   // unreadable folder: nothing offered, the box can still be typed in
            }
            found.Sort(StringComparer.OrdinalIgnoreCase);
            return found;
        }

        // EM_SETCUEBANNER: grey hint shown while a box is empty (wParam 1: also while it has the focus)
        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, String lParam);
        const int EM_SETCUEBANNER = 0x1501;

        static void SetCue(TextBox box, String cue)
        {
            try { SendMessage(box.Handle, EM_SETCUEBANNER, (IntPtr)1, cue); }
            catch (Exception) { }   // no hint
        }

        sealed class CredentialItem
        {
            public readonly String Name;
            readonly String text;
            public CredentialItem(String name, String text) { Name = name; this.text = text; }
            public override String ToString() { return text; }
        }

        void Save()
        {
            foreach (ModuleParameter p in module.Parameters)
            {
                Control editor;
                if (!editors.TryGetValue(p.Name, out editor)) continue;
                ComboBox combo = editor as ComboBox;
                if (p.Secret && combo != null)
                {
                    CredentialItem item = combo.SelectedItem as CredentialItem;
                    settings.SetCredential(key, p.Name, item == null ? null : item.Name);
                }
                else if (combo != null && combo.DropDownStyle == ComboBoxStyle.DropDownList)
                {
                    ValueItem item = combo.SelectedItem as ValueItem;
                    settings.SetValue(key, p.Name, item == null ? "" : item.Value);
                }
                else
                {
                    settings.SetValue(key, p.Name, editor.Text.Trim());
                }
            }
            try { settings.Save(); }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
