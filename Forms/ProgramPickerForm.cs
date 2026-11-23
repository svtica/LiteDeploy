// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    ProgramPickerForm.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-23
// Purpose: "Uninstall another program" (uninstall mode): the programs installed
//          on the target computers (ProgramInventory), with a search box, their
//          versions, publisher, how LiteDeploy can remove them and on how many
//          computers they are. One program is chosen; the ones that cannot be
//          removed are greyed and cannot be chosen.
// -----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace LiteDeploy
{
    public class ProgramPickerForm : Form
    {
        readonly TextBox searchBox = new TextBox();
        readonly ListView list = new ListView();
        readonly Label hintLabel = new Label();
        readonly Button okButton = new Button();
        readonly Button cancelButton = new Button();
        readonly List<ProgramRow> rows;

        internal ProgramRow Chosen { get; private set; }

        ProgramPickerForm(List<ProgramRow> rows, int computers)
        {
            this.rows = rows;
            Text = Localization.T("ProgramPickerForm.Title", "Lite Deploy - Uninstall another program");
            StartPosition = FormStartPosition.CenterParent;
            ShowIcon = ShowInTaskbar = false;
            MinimizeBox = false;
            ClientSize = new Size(860, 520);
            MinimumSize = new Size(600, 360);

            hintLabel.Text = Localization.F("ProgramPickerForm.Hint",
                "Programs installed for the whole computer on {0} computer(s). Choose one program: it is removed where it is installed (MSI: msiexec /x; EXE: quiet uninstall of the vendor only).", computers);
            hintLabel.SetBounds(12, 8, 836, 32);
            hintLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            searchBox.SetBounds(12, 44, 300, 22);
            searchBox.TextChanged += (s, e) => Fill();
            list.SetBounds(12, 72, 836, 404);
            list.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            list.View = View.Details;
            list.FullRowSelect = true;
            list.MultiSelect = false;
            list.HideSelection = false;
            list.Columns.Add(Localization.T("ProgramPickerForm.Program", "Program"), 300);
            list.Columns.Add(Localization.T("ProgramPickerForm.Version", "Version"), 110);
            list.Columns.Add(Localization.T("ProgramPickerForm.Publisher", "Publisher"), 170);
            list.Columns.Add(Localization.T("ProgramPickerForm.Removal", "Removal"), 160);
            list.Columns.Add(Localization.T("ProgramPickerForm.Computers", "Computers"), 70, HorizontalAlignment.Right);
            list.SelectedIndexChanged += (s, e) => okButton.Enabled = Selected() != null && Selected().Removable;
            list.DoubleClick += (s, e) => { if (okButton.Enabled) okButton.PerformClick(); };
            okButton.Text = Localization.T("ProgramPickerForm.Uninstall", "Uninstall...");
            okButton.SetBounds(680, 484, 80, 25);
            okButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            okButton.Enabled = false;
            okButton.Click += (s, e) => { Chosen = Selected(); DialogResult = DialogResult.OK; };
            cancelButton.Text = Localization.T("ProfilePickerForm.Cancel", "Cancel");
            cancelButton.SetBounds(768, 484, 80, 25);
            cancelButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            cancelButton.DialogResult = DialogResult.Cancel;
            CancelButton = cancelButton;
            Controls.AddRange(new Control[] { hintLabel, searchBox, list, okButton, cancelButton });
            Theme.Apply(this);
            Fill();
            Shown += (s, e) => searchBox.Focus();
        }

        ProgramRow Selected()
        {
            return list.SelectedItems.Count == 1 ? (ProgramRow)list.SelectedItems[0].Tag : null;
        }

        void Fill()
        {
            list.BeginUpdate();
            try
            {
                list.Items.Clear();
                foreach (ProgramRow r in ProgramInventory.Filter(rows, searchBox.Text))
                {
                    ListViewItem item = new ListViewItem(new[]
                    {
                        r.Name, String.Join(", ", r.Versions), r.Publisher, ProgramInventory.KindText(r), r.Computers.Count.ToString()
                    }) { Tag = r, ToolTipText = String.Join(", ", r.Computers) };
                    if (!r.Removable) item.ForeColor = SystemColors.GrayText;
                    list.Items.Add(item);
                }
            }
            finally { list.EndUpdate(); }
            okButton.Enabled = false;
        }

        /// <summary>The program chosen, or null when cancelled.</summary>
        internal static ProgramRow Choose(IWin32Window owner, List<ProgramRow> rows, int computers)
        {
            using (ProgramPickerForm form = new ProgramPickerForm(rows, computers))
                return form.ShowDialog(owner) == DialogResult.OK ? form.Chosen : null;
        }
    }
}
