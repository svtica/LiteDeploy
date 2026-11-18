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
//          Built in code (no designer file): one row per parameter.
// -----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Drawing;
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
                else
                {
                    TextBox box = new TextBox { Dock = DockStyle.Fill, MaxLength = ModuleParameters.MaxValueLength };
                    box.Text = current == null ? "" : current.Value ?? "";
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
                if (combo != null)
                {
                    CredentialItem item = combo.SelectedItem as CredentialItem;
                    settings.SetCredential(key, p.Name, item == null ? null : item.Name);
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
