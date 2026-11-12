// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    RemoteProcessesForm.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-12
// Purpose: Processes of a computer, the most handles first, with their private
//          memory and threads (LiteDeploy.Worker.exe processes): spots a process
//          that leaks handles or memory on a workstation, as LitePM does
//          locally. Built in code (no designer file).
// -----------------------------------------------------------------------------
using System;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace LiteDeploy
{
    public class RemoteProcessesForm : Form
    {
        readonly String computer;
        readonly ListView list = new ListView();
        readonly Label totals = new Label();
        readonly Button refreshButton = new Button();
        readonly Button closeButton = new Button();

        public RemoteProcessesForm(String computer)
        {
            this.computer = computer;
            Text = Localization.F("RemoteProcessesForm.Text", "Lite Deploy - Processes of {0}", computer);
            ClientSize = new Size(620, 460);
            MinimumSize = new Size(480, 320);
            StartPosition = FormStartPosition.CenterParent;
            ShowIcon = false;
            ShowInTaskbar = false;

            String[] headers =
            {
                Localization.T("RemoteProcessesForm.Process", "Process"), "PID",
                Localization.T("RemoteProcessesForm.Handles", "Handles"),
                Localization.T("RemoteProcessesForm.Private", "Private memory (MB)"),
                Localization.T("RemoteProcessesForm.Threads", "Threads")
            };
            int[] widths = { 200, 70, 90, 140, 80 };
            for (int i = 0; i < headers.Length; i++)
                list.Columns.Add(new ColumnHeader { Text = headers[i], Width = widths[i], TextAlign = i == 0 ? HorizontalAlignment.Left : HorizontalAlignment.Right });
            list.View = View.Details;
            list.FullRowSelect = true;
            list.SetBounds(12, 12, 596, 380);
            list.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;

            totals.SetBounds(12, 398, 596, 20);
            totals.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            refreshButton.Text = Localization.T("RemoteProcessesForm.Refresh", "Refresh");
            refreshButton.SetBounds(12, 424, 100, 25);
            refreshButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            refreshButton.Click += (s, e) => Load();
            closeButton.Text = Localization.T("CredentialsForm.Close", "Close");
            closeButton.DialogResult = DialogResult.Cancel;
            closeButton.SetBounds(533, 424, 75, 25);
            closeButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            CancelButton = closeButton;
            Controls.AddRange(new Control[] { list, totals, refreshButton, closeButton });
            Theme.Apply(this);
            Shown += (s, e) => Load();
        }

        new void Load()
        {
            refreshButton.Enabled = false;
            totals.Text = Localization.T("RemoteProcessesForm.Reading", "Reading the processes...");
            Cursor = Cursors.WaitCursor;
            new Thread(() =>
            {
                UpdateRunResult r = UpdateRunner.Processes(computer);
                if (IsDisposed) return;
                BeginInvoke(new MethodInvoker(() =>
                {
                    Cursor = Cursors.Default;
                    refreshButton.Enabled = true;
                    list.BeginUpdate();
                    list.Items.Clear();
                    foreach (WorkerMessage m in r.Messages.Where(m => m.Type == "process"))
                        list.Items.Add(new ListViewItem(new[] { m["name"], m["pid"], m["handles"], m["privatemb"], m["threads"] }));
                    list.EndUpdate();
                    WorkerMessage sum = r.Messages.FirstOrDefault(m => m.Type == "processes");
                    totals.Text = sum != null
                        ? Localization.F("RemoteProcessesForm.Totals", "{0} processes, {1} handles in all ({2:HH:mm:ss}). The 60 processes with the most handles are listed.", sum["count"], sum["handles"], DateTime.Now)
                        : r.Failure ?? "?";
                }));
            }) { IsBackground = true }.Start();
        }
    }
}
