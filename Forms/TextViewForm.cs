// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    TextViewForm.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-18
// Purpose: Resizable, read-only text window with Copy and Close (computer
//          cards of Software Updates): the text can be selected and copied,
//          unlike a message box. Built in code (no designer file).
// -----------------------------------------------------------------------------
using System;
using System.Drawing;
using System.Windows.Forms;

namespace LiteDeploy
{
    public class TextViewForm : Form
    {
        readonly TextBox textBox = new TextBox();
        readonly Button copyButton = new Button();
        readonly Button closeButton = new Button();

        public TextViewForm(String title, String text)
        {
            Text = title;
            ClientSize = new Size(560, 420);
            MinimumSize = new Size(360, 240);
            StartPosition = FormStartPosition.CenterParent;
            ShowIcon = false;
            ShowInTaskbar = false;

            textBox.Multiline = true;
            textBox.ReadOnly = true;
            textBox.ScrollBars = ScrollBars.Both;
            textBox.WordWrap = false;
            textBox.Font = new Font(FontFamily.GenericMonospace, 9F);
            textBox.Text = (text ?? "").Replace("\r\n", "\n").Replace("\n", Environment.NewLine);
            textBox.SetBounds(12, 12, 536, 365);
            textBox.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;

            copyButton.Text = Localization.T("TextViewForm.Copy", "Copy");
            copyButton.SetBounds(12, 386, 100, 25);
            copyButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            copyButton.Click += (s, e) =>
            {
                try { Clipboard.SetText(textBox.SelectionLength > 0 ? textBox.SelectedText : textBox.Text); }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            };
            closeButton.Text = Localization.T("CredentialsForm.Close", "Close");
            closeButton.DialogResult = DialogResult.Cancel;
            closeButton.SetBounds(473, 386, 75, 25);
            closeButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            CancelButton = closeButton;
            Controls.AddRange(new Control[] { textBox, copyButton, closeButton });
            Theme.Apply(this);
            Shown += (s, e) => { textBox.SelectionStart = 0; textBox.SelectionLength = 0; closeButton.Focus(); };
        }

        /// <summary>Shows the text, modal over <paramref name="owner"/>.</summary>
        public static void ShowText(IWin32Window owner, String title, String text)
        {
            using (TextViewForm form = new TextViewForm(title, text)) form.ShowDialog(owner);
        }
    }
}
