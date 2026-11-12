// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    CredentialsForm.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-12
// Purpose: Tools > Credentials: the accounts stored in the Windows Credential
//          Manager for the LiteSuite tools (LiteSuite.CredentialStore, shared
//          with LiteTask): add or replace, delete. The passwords are never
//          shown. Built in code (no designer file): a list and four buttons.
// -----------------------------------------------------------------------------
using System;
using System.Drawing;
using System.Windows.Forms;
using LiteSuite;

namespace LiteDeploy
{
    public class CredentialsForm : Form
    {
        readonly ListView list = new ListView();
        readonly ColumnHeader nameColumn = new ColumnHeader();
        readonly ColumnHeader userColumn = new ColumnHeader();
        readonly Button addButton = new Button();
        readonly Button deleteButton = new Button();
        readonly Button closeButton = new Button();
        readonly Label infoLabel = new Label();

        public CredentialsForm()
        {
            Text = Localization.T("CredentialsForm.Text", "Lite Deploy - Credentials");
            ClientSize = new Size(560, 330);
            MinimumSize = new Size(480, 300);
            StartPosition = FormStartPosition.CenterParent;
            ShowIcon = false;
            ShowInTaskbar = false;

            nameColumn.Text = Localization.T("CredentialsForm.Name", "Name");
            nameColumn.Width = 220;
            userColumn.Text = Localization.T("CredentialsForm.User", "User name");
            userColumn.Width = 300;
            list.Columns.AddRange(new[] { nameColumn, userColumn });
            list.View = View.Details;
            list.FullRowSelect = true;
            list.HideSelection = false;
            list.MultiSelect = false;
            list.SetBounds(12, 12, 536, 220);
            list.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            list.SelectedIndexChanged += (s, e) => deleteButton.Enabled = list.SelectedItems.Count > 0;

            infoLabel.Text = Localization.T("CredentialsForm.Info",
                "Stored in the Windows Credential Manager of your account (shared with LiteTask). The software updates use the account chosen in App > Options.");
            infoLabel.SetBounds(12, 238, 536, 40);
            infoLabel.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;

            addButton.Text = Localization.T("CredentialsForm.Add", "Add...");
            addButton.SetBounds(12, 292, 100, 25);
            addButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            addButton.Click += (s, e) => Add();
            deleteButton.Text = Localization.T("CredentialsForm.Delete", "Delete");
            deleteButton.SetBounds(118, 292, 100, 25);
            deleteButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            deleteButton.Enabled = false;
            deleteButton.Click += (s, e) => Delete();
            closeButton.Text = Localization.T("CredentialsForm.Close", "Close");
            closeButton.DialogResult = DialogResult.Cancel;
            closeButton.SetBounds(473, 292, 75, 25);
            closeButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            CancelButton = closeButton;

            Controls.AddRange(new Control[] { list, infoLabel, addButton, deleteButton, closeButton });
            Theme.Apply(this);
            Reload();
        }

        void Reload()
        {
            list.Items.Clear();
            try
            {
                foreach (StoredCredential c in CredentialStore.List())
                    list.Items.Add(new ListViewItem(new[] { c.Name, c.UserName }));
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            deleteButton.Enabled = false;
        }

        void Add()
        {
            using (Form dialog = new Form())
            using (Label nameLabel = new Label(), userLabel = new Label(), passwordLabel = new Label(), confirmLabel = new Label())
            using (TextBox name = new TextBox(), user = new TextBox(), password = new TextBox(), confirm = new TextBox())
            using (Button ok = new Button(), cancel = new Button())
            {
                dialog.Text = Localization.T("CredentialsForm.AddTitle", "Add a credential");
                dialog.FormBorderStyle = FormBorderStyle.FixedDialog;
                dialog.StartPosition = FormStartPosition.CenterParent;
                dialog.MinimizeBox = dialog.MaximizeBox = dialog.ShowInTaskbar = false;
                dialog.ClientSize = new Size(380, 170);
                Label[] labels = { nameLabel, userLabel, passwordLabel, confirmLabel };
                TextBox[] boxes = { name, user, password, confirm };
                String[] texts =
                {
                    Localization.T("CredentialsForm.Name", "Name") + ":",
                    Localization.T("CredentialsForm.User", "User name") + " (DOMAIN\\user):",
                    Localization.T("CredentialsForm.Password", "Password") + ":",
                    Localization.T("CredentialsForm.Confirm", "Confirm") + ":"
                };
                for (int i = 0; i < 4; i++)
                {
                    labels[i].Text = texts[i];
                    labels[i].SetBounds(12, 15 + i * 30, 150, 20);
                    boxes[i].SetBounds(170, 12 + i * 30, 198, 20);
                }
                password.UseSystemPasswordChar = confirm.UseSystemPasswordChar = true;
                ok.Text = "OK";
                ok.SetBounds(212, 135, 75, 25);
                cancel.Text = Localization.T("Prompt.Cancel", "Cancel");
                cancel.DialogResult = DialogResult.Cancel;
                cancel.SetBounds(293, 135, 75, 25);
                ok.Click += (s, e) =>
                {
                    String error = !CredentialStore.IsValidName(name.Text.Trim()) ? Localization.T("CredentialsForm.BadName", "Name: letters, digits, spaces and . - _ @ only (64 at most).")
                                 : user.Text.Trim().Length == 0 ? Localization.T("CredentialsForm.NoUser", "Type the user name.")
                                 : password.Text != confirm.Text ? Localization.T("CredentialsForm.Mismatch", "The two passwords differ.")
                                 : null;
                    if (error != null) { MessageBox.Show(dialog, error, dialog.Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
                    try
                    {
                        CredentialStore.Save(name.Text.Trim(), user.Text.Trim(), password.Text);
                        dialog.DialogResult = DialogResult.OK;
                    }
                    catch (Exception ex) { MessageBox.Show(dialog, ex.Message, dialog.Text, MessageBoxButtons.OK, MessageBoxIcon.Error); }
                };
                dialog.Controls.AddRange(labels);
                dialog.Controls.AddRange(boxes);
                dialog.Controls.AddRange(new Control[] { ok, cancel });
                dialog.AcceptButton = ok;
                dialog.CancelButton = cancel;
                Theme.Apply(dialog);
                dialog.ShowDialog(this);
                password.Text = confirm.Text = "";
            }
            Reload();
        }

        void Delete()
        {
            if (list.SelectedItems.Count == 0) return;
            String name = list.SelectedItems[0].Text;
            if (MessageBox.Show(this, Localization.F("CredentialsForm.ConfirmDelete", "Delete the credential \"{0}\"?", name), Text,
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            try { CredentialStore.Delete(name); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error); }
            Reload();
        }
    }
}
