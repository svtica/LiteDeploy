// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    RemoteRunForm.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-23
// Purpose: Progress window of an action run on several computers (quick
//          commands, computer cards, messages to the users), shown at once
//          and not modal: progress bar, computers done and under way, elapsed
//          time, the result of each computer as soon as it is known, Cancel
//          (no new computer is started; the ones under way finish). At the
//          end it shows every result in the order of the computers, with
//          Copy and Close. The main window stays usable meanwhile. Built in
//          code (no designer file).
// -----------------------------------------------------------------------------
using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace LiteDeploy
{
    public class RemoteRunForm : Form
    {
        readonly Label statusLabel = new Label();
        readonly ProgressBar progressBar = new ProgressBar();
        readonly TextBox textBox = new TextBox();
        readonly Button copyButton = new Button();
        readonly Button cancelButton = new Button();
        readonly Timer timer = new Timer { Interval = 1000 };
        readonly Stopwatch watch = Stopwatch.StartNew();
        readonly int total;
        int done, running;
        volatile bool cancelRequested;
        bool finished;

        public RemoteRunForm(String title, int total)
        {
            this.total = Math.Max(1, total);
            Text = title;
            ClientSize = new Size(600, 440);
            MinimumSize = new Size(400, 260);
            StartPosition = FormStartPosition.CenterParent;
            ShowIcon = false;
            ShowInTaskbar = false;

            statusLabel.SetBounds(12, 12, 576, 20);
            statusLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            statusLabel.AutoEllipsis = true;

            progressBar.SetBounds(12, 36, 576, 18);
            progressBar.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            progressBar.Maximum = this.total;
            // One computer: no steps to count, the bar only shows that it works
            progressBar.Style = this.total == 1 ? ProgressBarStyle.Marquee : ProgressBarStyle.Continuous;

            textBox.Multiline = true;
            textBox.ReadOnly = true;
            textBox.ScrollBars = ScrollBars.Both;
            textBox.WordWrap = false;
            textBox.Font = new Font(FontFamily.GenericMonospace, 9F);
            textBox.SetBounds(12, 62, 576, 334);
            textBox.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;

            copyButton.Text = Localization.T("TextViewForm.Copy", "Copy");
            copyButton.SetBounds(12, 405, 100, 25);
            copyButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            copyButton.Click += (s, e) =>
            {
                try { Clipboard.SetText(textBox.SelectionLength > 0 ? textBox.SelectedText : textBox.Text); }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            };

            cancelButton.Text = Localization.T("RemoteRunForm.Cancel", "Cancel");
            cancelButton.SetBounds(498, 405, 90, 25);
            cancelButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            cancelButton.Click += (s, e) =>
            {
                if (finished) Close();
                else RequestCancel();
            };
            CancelButton = cancelButton;

            Controls.AddRange(new Control[] { statusLabel, progressBar, textBox, copyButton, cancelButton });
            Theme.Apply(this);

            timer.Tick += (s, e) => ShowStatus();
            timer.Start();
            ShowStatus();
            // Closed while it runs: nothing new is started (the computers under way finish in the background)
            FormClosing += (s, e) => { if (!finished) cancelRequested = true; };
            FormClosed += (s, e) => { timer.Stop(); timer.Dispose(); };
        }

        /// <summary>Cancel was clicked, or the window closed: no new computer may start. Read from any thread.</summary>
        public bool CancelRequested { get { return cancelRequested; } }

        void RequestCancel()
        {
            cancelRequested = true;
            cancelButton.Enabled = false;
            ShowStatus();
        }

        void ShowStatus()
        {
            statusLabel.Text = RemoteRunProgress.StatusText(done, running, total, watch.Elapsed, cancelRequested, finished);
        }

        /// <summary>A computer starts (UI thread).</summary>
        public void Started()
        {
            running++;
            ShowStatus();
        }

        /// <summary>The result of a computer, appended as soon as it is known (UI thread).</summary>
        public void Completed(String text)
        {
            running = Math.Max(0, running - 1);
            done++;
            if (progressBar.Style == ProgressBarStyle.Continuous) progressBar.Value = Math.Min(total, done);
            AppendText(text);
            ShowStatus();
        }

        void AppendText(String text)
        {
            String block = (text ?? "").Replace("\r\n", "\n").Replace("\n", Environment.NewLine);
            if (textBox.TextLength > 0) block = Environment.NewLine + Environment.NewLine + block;
            textBox.AppendText(block);
        }

        /// <summary>The end (UI thread): every result in the order of the computers, Close instead of Cancel.</summary>
        public void Finish(String text)
        {
            finished = true;
            watch.Stop();
            timer.Stop();
            progressBar.Style = ProgressBarStyle.Continuous;
            progressBar.Value = cancelRequested ? Math.Min(total, done) : total;
            textBox.Text = (text ?? "").Replace("\r\n", "\n").Replace("\n", Environment.NewLine);
            textBox.SelectionStart = 0;
            textBox.SelectionLength = 0;
            cancelButton.Text = Localization.T("CredentialsForm.Close", "Close");
            cancelButton.Enabled = true;
            cancelButton.Focus();
            ShowStatus();
        }
    }
}
