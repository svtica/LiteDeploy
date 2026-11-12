// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    SubnetsForm.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-12
// Purpose: Computer list > Subnets > Edit: the user's list of subnets (one per
//          line, see SubnetList), with an import from a text or CSV file.
//          Invalid lines are shown and the list is not saved until they are
//          fixed. Built in code (no designer file).
// -----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace LiteDeploy
{
    public class SubnetsForm : Form
    {
        readonly TextBox editor = new TextBox();
        readonly Label infoLabel = new Label();
        readonly Button importButton = new Button();
        readonly Button okButton = new Button();
        readonly Button cancelButton = new Button();

        public SubnetsForm()
        {
            Text = Localization.T("SubnetsForm.Text", "Lite Deploy - Subnets");
            ClientSize = new Size(520, 400);
            MinimumSize = new Size(420, 320);
            StartPosition = FormStartPosition.CenterParent;
            ShowIcon = false;
            ShowInTaskbar = false;

            infoLabel.Text = Localization.F("SubnetsForm.Info",
                "One subnet per line, with an optional name: 10.1.2.0/24 Garage. /{0} to /22 are split into /24 subnets. # starts a comment. The team can also place a subnets.txt next to LiteDeploy.exe.",
                SubnetList.SmallestPrefix);
            infoLabel.SetBounds(12, 9, 496, 48);
            infoLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

            editor.Multiline = true;
            editor.AcceptsReturn = true;
            editor.ScrollBars = ScrollBars.Both;
            editor.WordWrap = false;
            editor.Font = new Font(FontFamily.GenericMonospace, 9.5f);
            editor.SetBounds(12, 60, 496, 290);
            editor.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            editor.Text = SubnetList.ReadUserText().Replace("\r\n", "\n").Replace("\n", "\r\n");

            importButton.Text = Localization.T("SubnetsForm.Import", "Import...");
            importButton.SetBounds(12, 362, 100, 25);
            importButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            importButton.Click += (s, e) => Import();
            okButton.Text = Localization.T("SubnetsForm.OK", "OK");
            okButton.SetBounds(352, 362, 75, 25);
            okButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            okButton.Click += (s, e) => SaveAndClose();
            cancelButton.Text = Localization.T("SubnetsForm.Cancel", "Cancel");
            cancelButton.DialogResult = DialogResult.Cancel;
            cancelButton.SetBounds(433, 362, 75, 25);
            cancelButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            CancelButton = cancelButton;

            Controls.AddRange(new Control[] { infoLabel, editor, importButton, okButton, cancelButton });
            Theme.Apply(this);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && editor.Font != null) editor.Font.Dispose();
            base.Dispose(disposing);
        }

        void Import()
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Filter = Localization.T("SubnetsForm.Filter", "Subnet lists (*.txt;*.csv)|*.txt;*.csv|All files (*.*)|*.*");
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                String[] lines;
                try { lines = File.ReadAllLines(dialog.FileName, Encoding.UTF8); }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }

                // Keep the lines as written (one line per subnet or range); skip invalid ones
                // and the subnets already in the editor.
                List<String> errors = new List<String>();
                HashSet<String> present = new HashSet<String>(SubnetList.Parse(EditorLines(), null).Select(x => x.Prefix));
                StringBuilder added = new StringBuilder();
                int count = 0, number = 0;
                foreach (String line in lines)
                {
                    number++;
                    List<SubnetEntry> entries;
                    String error;
                    if (!SubnetList.TryParseLine(line, out entries, out error))
                    {
                        errors.Add(Localization.F("SubnetList.Line", "Line {0}: {1}", number, error));
                        continue;
                    }
                    if (entries.Count == 0 || entries.All(x => present.Contains(x.Prefix))) continue;
                    foreach (SubnetEntry x in entries) present.Add(x.Prefix);
                    added.AppendLine(line.Trim());
                    count++;
                }
                if (added.Length > 0)
                {
                    if (editor.TextLength > 0 && !editor.Text.EndsWith("\n")) editor.AppendText("\r\n");
                    editor.AppendText(added.ToString());
                }
                String message = Localization.F("SubnetsForm.Imported", "{0} line(s) imported, {1} skipped.", count, errors.Count);
                if (errors.Count > 0) message += "\n\n" + String.Join("\n", errors.Take(15));
                MessageBox.Show(this, message, Text, MessageBoxButtons.OK, errors.Count > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
            }
        }

        IEnumerable<String> EditorLines()
        {
            return editor.Text.Replace("\r\n", "\n").Split('\n');
        }

        void SaveAndClose()
        {
            List<String> errors = new List<String>();
            SubnetList.Parse(EditorLines(), errors);
            if (errors.Count > 0)
            {
                MessageBox.Show(this, String.Join("\n", errors.Take(15)), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            try { SubnetList.SaveUserText(editor.Text); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error); return; }
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
