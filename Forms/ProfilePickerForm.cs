// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    ProfilePickerForm.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-19
// Purpose: Choice of the active profile of the software (main window): a list
//          of the profiles with their number of software and their content,
//          "All" (the whole catalog) last; OK confirms. A dialog on purpose,
//          not a drop-down list of the main window that a turn of the mouse
//          wheel could change.
// -----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace LiteDeploy
{
    public class ProfilePickerForm : Form
    {
        readonly ListBox profilesList = new ListBox();
        readonly TextBox contentBox = new TextBox();
        readonly Label hintLabel = new Label();
        readonly Button okButton = new Button();
        readonly Button cancelButton = new Button();
        readonly List<Choice> choices = new List<Choice>();

        sealed class Choice
        {
            public String Name;      // "" : the whole catalog
            public String Text;
            public List<UpdatePackage> Packages;
            public override String ToString() { return Text; }
        }

        /// <summary>The profile chosen ("" for the whole catalog), or null when cancelled.</summary>
        public String Chosen { get; private set; }

        ProfilePickerForm(List<UpdatePackage> catalog, SoftwareProfiles profiles, String current)
        {
            Text = Localization.T("ProfilePickerForm.Title", "Lite Deploy - Profile");
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowIcon = ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(560, 330);

            hintLabel.Text = Localization.T("ProfilePickerForm.Hint", "Check, Update checked and Install work on the software of the profile (Software catalog... to edit the profiles).");
            hintLabel.SetBounds(12, 10, 536, 32);
            profilesList.SetBounds(12, 46, 230, 236);
            profilesList.IntegralHeight = false;
            contentBox.SetBounds(250, 46, 298, 236);
            contentBox.Multiline = true;
            contentBox.ReadOnly = true;
            contentBox.ScrollBars = ScrollBars.Vertical;
            okButton.Text = "OK";
            okButton.SetBounds(392, 292, 75, 25);
            cancelButton.Text = Localization.T("ProfilePickerForm.Cancel", "Cancel");
            cancelButton.SetBounds(473, 292, 75, 25);
            cancelButton.DialogResult = DialogResult.Cancel;
            AcceptButton = okButton;
            CancelButton = cancelButton;
            Controls.AddRange(new Control[] { hintLabel, profilesList, contentBox, okButton, cancelButton });

            bool found;
            foreach (SoftwareProfile p in profiles.Items.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                List<UpdatePackage> list = profiles.Filter(catalog, p.Name, out found);
                choices.Add(new Choice { Name = p.Name, Packages = list, Text = Localization.F("ProfilePickerForm.Item", "{0} ({1})", p.Name, list.Count) });
            }
            choices.Add(new Choice
            {
                Name = "",
                Packages = catalog,
                Text = Localization.F("ProfilePickerForm.All", "All the catalog ({0})", catalog.Count)
            });
            profilesList.Items.AddRange(choices.ToArray());
            Choice selected = choices.FirstOrDefault(c => String.Equals(c.Name, current ?? "", StringComparison.OrdinalIgnoreCase));
            profilesList.SelectedItem = selected ?? choices[0];

            profilesList.SelectedIndexChanged += (s, e) => ShowContent();
            profilesList.DoubleClick += (s, e) => Confirm();
            okButton.Click += (s, e) => Confirm();
            ShowContent();
            Theme.Apply(this);
        }

        void ShowContent()
        {
            Choice c = profilesList.SelectedItem as Choice;
            contentBox.Text = c == null ? "" : String.Join(Environment.NewLine,
                c.Packages.Select(p => String.IsNullOrWhiteSpace(p.Label) ? p.Id : p.Label + "  (" + p.Id + ")"));
        }

        void Confirm()
        {
            Choice c = profilesList.SelectedItem as Choice;
            if (c == null) return;
            Chosen = c.Name;
            DialogResult = DialogResult.OK;
            Close();
        }

        /// <summary>Shows the choice; the profile chosen ("" for the whole catalog), or null when cancelled.</summary>
        internal static String Pick(IWin32Window owner, List<UpdatePackage> catalog, SoftwareProfiles profiles, String current)
        {
            using (ProfilePickerForm form = new ProfilePickerForm(catalog, profiles, current))
            {
                return form.ShowDialog(owner) == DialogResult.OK ? form.Chosen : null;
            }
        }
    }
}
