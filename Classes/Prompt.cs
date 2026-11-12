// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    Prompt.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-12
// Purpose: Small text input dialog (reason of a pause...), themed and translated.
// -----------------------------------------------------------------------------
using System;
using System.Drawing;
using System.Windows.Forms;

namespace LiteDeploy
{
    static public class Prompt
    {
        /// <summary>Asks for one line of text. Returns null when cancelled.</summary>
        static public String Show(IWin32Window owner, String title, String question, String initial)
        {
            using (Form dialog = new Form())
            using (Label label = new Label())
            using (TextBox box = new TextBox())
            using (Button ok = new Button())
            using (Button cancel = new Button())
            {
                dialog.Text = title;
                dialog.FormBorderStyle = FormBorderStyle.FixedDialog;
                dialog.StartPosition = FormStartPosition.CenterParent;
                dialog.MinimizeBox = false;
                dialog.MaximizeBox = false;
                dialog.ShowInTaskbar = false;
                dialog.ClientSize = new Size(400, 110);
                label.Text = question;
                label.SetBounds(12, 12, 376, 20);
                box.Text = initial ?? "";
                box.SetBounds(12, 36, 376, 20);
                ok.Text = Localization.T("Prompt.Ok", "OK");
                ok.DialogResult = DialogResult.OK;
                ok.SetBounds(232, 72, 75, 25);
                cancel.Text = Localization.T("Prompt.Cancel", "Cancel");
                cancel.DialogResult = DialogResult.Cancel;
                cancel.SetBounds(313, 72, 75, 25);
                dialog.Controls.AddRange(new Control[] { label, box, ok, cancel });
                dialog.AcceptButton = ok;
                dialog.CancelButton = cancel;
                Theme.Apply(dialog);
                return dialog.ShowDialog(owner) == DialogResult.OK ? box.Text.Trim() : null;
            }
        }
    }
}
