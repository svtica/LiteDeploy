// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    TeamViewerIdForm.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-18
// Purpose: TeamViewer ID of one or more computers: the IDs saved in
//          updates.xml at once, read from the computers (worker "info") when
//          none is known or on request. Copy (the selected IDs, or every
//          known ID) and, when TeamViewer is installed on this computer,
//          Connect (TeamViewer.exe -i <ID>).
// -----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace LiteDeploy
{
    public class TeamViewerIdForm : Form
    {
        readonly ListView list = new ListView();
        readonly Button readButton = new Button();
        readonly Button copyButton = new Button();
        readonly Button connectButton = new Button();
        readonly Button closeButton = new Button();
        readonly Label statusLabel = new Label();
        readonly Func<String, String> read;
        readonly String localTeamViewer;
        int reading;

        /// <param name="known">Saved ID by computer (null or empty: unknown).</param>
        /// <param name="read">Reads the ID on a computer (background thread): "" when TeamViewer is not installed; throws when the computer cannot be reached.</param>
        public TeamViewerIdForm(IList<String> computers, IDictionary<String, String> known, Func<String, String> read)
        {
            this.read = read;
            localTeamViewer = RemoteActions.LocalTeamViewer();
            Text = Localization.T("TeamViewerIdForm.Text", "TeamViewer ID");
            ClientSize = new Size(480, 300);
            MinimumSize = new Size(380, 220);
            StartPosition = FormStartPosition.CenterParent;
            ShowIcon = false;
            ShowInTaskbar = false;

            list.View = View.Details;
            list.FullRowSelect = true;
            list.HideSelection = false;
            list.Columns.Add(Localization.T("TeamViewerIdForm.Computer", "Computer"), 150);
            list.Columns.Add(Localization.T("TeamViewerIdForm.Id", "TeamViewer ID"), 290);
            list.SetBounds(12, 12, 456, 216);
            list.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            foreach (String computer in computers)
            {
                String id;
                known.TryGetValue(computer, out id);
                list.Items.Add(new ListViewItem(new[] { computer, String.IsNullOrEmpty(id) ? "" : id }) { Name = computer, Tag = String.IsNullOrEmpty(id) ? null : id });
            }
            if (list.Items.Count == 1) list.Items[0].Selected = true;
            list.SelectedIndexChanged += (s, e) => UpdateButtons();
            list.DoubleClick += (s, e) => Connect();

            statusLabel.SetBounds(12, 232, 456, 18);
            statusLabel.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            statusLabel.AutoEllipsis = true;

            readButton.Text = Localization.T("TeamViewerIdForm.Read", "Read from the computers");
            readButton.SetBounds(12, 262, 160, 25);
            readButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            readButton.Click += (s, e) => ReadAll(list.Items.Cast<ListViewItem>().ToList());

            copyButton.Text = Localization.T("TextViewForm.Copy", "Copy");
            copyButton.SetBounds(180, 262, 85, 25);
            copyButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            copyButton.Click += (s, e) => Copy();

            connectButton.Text = Localization.T("TeamViewerIdForm.Connect", "Connect");
            connectButton.SetBounds(273, 262, 85, 25);
            connectButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            connectButton.Visible = localTeamViewer != null;
            connectButton.Click += (s, e) => Connect();

            closeButton.Text = Localization.T("CredentialsForm.Close", "Close");
            closeButton.DialogResult = DialogResult.Cancel;
            closeButton.SetBounds(393, 262, 75, 25);
            closeButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            CancelButton = closeButton;

            Controls.AddRange(new Control[] { list, statusLabel, readButton, copyButton, connectButton, closeButton });
            Theme.Apply(this);
            UpdateButtons();
            // Nothing known yet: read at once
            Shown += (s, e) =>
            {
                List<ListViewItem> unknown = list.Items.Cast<ListViewItem>().Where(i => i.Tag == null).ToList();
                if (unknown.Count > 0) ReadAll(unknown);
            };
        }

        // The selected rows with an ID, else every row with an ID
        List<ListViewItem> WithId()
        {
            List<ListViewItem> selected = list.SelectedItems.Cast<ListViewItem>().Where(i => i.Tag != null).ToList();
            return selected.Count > 0 ? selected : list.Items.Cast<ListViewItem>().Where(i => i.Tag != null).ToList();
        }

        void UpdateButtons()
        {
            readButton.Enabled = reading == 0;
            copyButton.Enabled = WithId().Count > 0;
            connectButton.Enabled = list.SelectedItems.Count == 1 && list.SelectedItems[0].Tag != null;
            if (localTeamViewer == null)
                statusLabel.Text = Localization.T("TeamViewerIdForm.NoLocalTeamViewer", "TeamViewer is not installed on this computer: copy the ID to connect from elsewhere.");
        }

        void Copy()
        {
            List<ListViewItem> items = WithId();
            if (items.Count == 0) return;
            String text = items.Count == 1 ? (String)items[0].Tag : String.Join(Environment.NewLine, items.Select(i => i.Name + "\t" + i.Tag));
            try { Clipboard.SetText(text); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }

        void Connect()
        {
            if (localTeamViewer == null || list.SelectedItems.Count != 1 || list.SelectedItems[0].Tag == null) return;
            try { RemoteActions.ConnectTeamViewer((String)list.SelectedItems[0].Tag); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        // Reads the IDs on the computers, a few at a time; each row shows its result as it comes
        void ReadAll(List<ListViewItem> items)
        {
            if (reading > 0 || items.Count == 0) return;
            String reads = Localization.T("TeamViewerIdForm.Reading", "Reading...");
            List<String> names = items.Select(i => i.Name).ToList();
            foreach (ListViewItem item in items) item.SubItems[1].Text = reads;
            reading = names.Count;
            statusLabel.Text = Localization.F("TeamViewerIdForm.ReadingMany", "Reading the TeamViewer ID of {0} computer(s)...", names.Count);
            UpdateButtons();
            int next = -1;
            for (int t = 0; t < Math.Min(4, names.Count); t++)
            {
                new Thread(() =>
                {
                    int i;
                    while ((i = Interlocked.Increment(ref next)) < names.Count)
                    {
                        String computer = names[i], id = null, error = null;
                        try { id = read(computer); }
                        catch (Exception ex) { error = ex.Message; }
                        if (IsDisposed) return;
                        try { BeginInvoke(new MethodInvoker(() => ShowResult(computer, id, error))); }
                        catch (InvalidOperationException) { return; }
                    }
                }) { IsBackground = true }.Start();
            }
        }

        void ShowResult(String computer, String id, String error)
        {
            ListViewItem item = list.Items[computer];
            if (item != null)
            {
                if (error != null)
                {
                    // The saved ID stays usable when the computer cannot be reached now
                    item.SubItems[1].Text = (item.Tag != null ? item.Tag + " - " : "") + Localization.F("TeamViewerIdForm.NotRead", "Not read: {0}", error);
                }
                else
                {
                    item.Tag = String.IsNullOrEmpty(id) ? null : id;
                    item.SubItems[1].Text = String.IsNullOrEmpty(id) ? Localization.T("ComputerActions.TeamViewerNotInstalled", "(TeamViewer not installed)") : id;
                }
            }
            reading--;
            if (reading == 0) statusLabel.Text = "";
            UpdateButtons();
        }
    }
}
