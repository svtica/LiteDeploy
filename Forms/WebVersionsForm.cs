// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    WebVersionsForm.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-18
// Purpose: Tools > Web versions... (also Package list > Web versions...): the
//          version of the file each module instance uses in the shared cache
//          against the latest version known to winget (WingetId parameter),
//          Check now, Download to the cache (checked rows, or the current row),
//          Open vendor page; the daily check settings. Every instance is listed,
//          checked or not (No WingetId, Not checked); without a cache or an
//          instance for this Windows user, a notice offers Open Options... or
//          Import from catalog... (same import as the package list). The work is done by
//          WebVersionChecker and CacheDownloader, on the administrator's
//          computer, on a worker thread: a progress bar (done / total) and a
//          status line (item, step, elapsed time, time left) follow it, the row
//          being worked on is highlighted and each row shows its result as soon
//          as it is known; the button of the operation becomes Cancel (stops
//          after the current item, keeps and saves the results obtained).
//          Built in code (no designer file).
// -----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace LiteDeploy
{
    public class WebVersionsForm : Form
    {
        readonly Label infoLabel = new Label();
        readonly DataGridView grid = new DataGridView();
        readonly DataGridViewCheckBoxColumn checkColumn = new DataGridViewCheckBoxColumn();
        readonly DataGridViewTextBoxColumn productColumn = new DataGridViewTextBoxColumn();
        readonly DataGridViewTextBoxColumn moduleColumn = new DataGridViewTextBoxColumn();
        readonly DataGridViewTextBoxColumn cacheColumn = new DataGridViewTextBoxColumn();
        readonly DataGridViewTextBoxColumn webColumn = new DataGridViewTextBoxColumn();
        readonly DataGridViewTextBoxColumn statusColumn = new DataGridViewTextBoxColumn();
        readonly DataGridViewTextBoxColumn urlColumn = new DataGridViewTextBoxColumn();
        readonly FlowLayoutPanel buttons = new FlowLayoutPanel();
        readonly Button checkButton = new Button();
        readonly Button downloadButton = new Button();
        readonly Button vendorButton = new Button();
        readonly Label lastCheckLabel = new Label();
        readonly FlowLayoutPanel schedule = new FlowLayoutPanel();
        readonly CheckBox dailyCheck = new CheckBox();
        readonly TextBox dailyTime = new TextBox();
        readonly CheckBox autoDownload = new CheckBox();
        readonly Button closeButton = new Button();
        readonly ContextMenuStrip rowMenu = new ContextMenuStrip();
        // What is missing on this computer (no cache, no instance), with the button that fixes it
        readonly Panel notice = new Panel();
        readonly Label noticeLabel = new Label();
        readonly FlowLayoutPanel noticeButtons = new FlowLayoutPanel();
        readonly Button optionsButton = new Button();
        readonly Button importButton = new Button();
        // Progress of Check now and Download to the cache: done / total, the item and its step, the times
        readonly ProgressBar progressBar = new ProgressBar();
        readonly Label progressLabel = new Label();
        readonly System.Windows.Forms.Timer clock = new System.Windows.Forms.Timer { Interval = 1000 };
        readonly Stopwatch watch = new Stopwatch();
        bool busy;
        WebVersionOperation operation;
        CancellationTokenSource cancel;
        WebVersionProgress lastProgress;
        readonly Dictionary<String, DataGridViewRow> rowsByKey = new Dictionary<String, DataGridViewRow>(StringComparer.OrdinalIgnoreCase);

        WebVersionResults results;
        /// <summary>Instances of CachedInstaller and MsixProvision with parameters (module-settings.xml of this user).</summary>
        List<WebVersionTarget> targets = new List<WebVersionTarget>();
        /// <summary>Opened from the package list: the imported rows go to its grid (saved with its OK); null: to the saved list.</summary>
        readonly Action<List<UpdatePackage>> imported;

        public WebVersionsForm() : this(null)
        {
        }

        /// <param name="onImported">Receives the package rows of an Import from catalog...; null: they are added to the saved package list.</param>
        internal WebVersionsForm(Action<List<UpdatePackage>> onImported)
        {
            imported = onImported;
            Text = Localization.T("WebVersionsForm.Text", "Lite Deploy - Web Versions");
            ClientSize = new Size(900, 500);
            MinimumSize = new Size(640, 360);
            StartPosition = FormStartPosition.CenterParent;
            ShowIcon = false;
            ShowInTaskbar = false;
            MinimizeBox = false;
            RestoreSize();

            infoLabel.SetBounds(12, 8, ClientSize.Width - 24, 34);
            infoLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

            notice.SetBounds(12, 46, ClientSize.Width - 24, NoticeHeight - 6);
            notice.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            notice.BorderStyle = BorderStyle.FixedSingle;
            noticeLabel.SetBounds(6, 4, notice.Width - 14, 38);
            noticeLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            optionsButton.Text = Localization.T("WebVersionsForm.OpenOptions", "Open Options...");
            importButton.Text = Localization.T("WebVersionsForm.Import", "Import from catalog...");
            foreach (Button b in new[] { optionsButton, importButton })
            {
                b.AutoSize = true;
                b.MinimumSize = new Size(110, 25);
            }
            optionsButton.Click += (s, e) => OpenOptions();
            importButton.Click += (s, e) => ImportCatalog();
            noticeButtons.Controls.AddRange(new Control[] { optionsButton, importButton });
            noticeButtons.WrapContents = false;
            noticeButtons.SetBounds(3, 42, notice.Width - 8, 30);
            noticeButtons.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            notice.Controls.AddRange(new Control[] { noticeLabel, noticeButtons });

            checkColumn.HeaderText = "";
            checkColumn.Width = 28;
            checkColumn.SortMode = DataGridViewColumnSortMode.NotSortable;
            productColumn.HeaderText = Localization.T("WebVersionsForm.ProductColumn", "Product (instance)");
            moduleColumn.HeaderText = Localization.T("WebVersionsForm.ModuleColumn", "Module");
            cacheColumn.HeaderText = Localization.T("WebVersionsForm.CacheColumn", "Cache version");
            webColumn.HeaderText = Localization.T("WebVersionsForm.WebColumn", "Web version");
            statusColumn.HeaderText = Localization.T("WebVersionsForm.StatusColumn", "Status");
            urlColumn.HeaderText = Localization.T("WebVersionsForm.UrlColumn", "Source URL");
            foreach (DataGridViewTextBoxColumn c in new[] { productColumn, moduleColumn, cacheColumn, webColumn, statusColumn, urlColumn })
            {
                c.ReadOnly = true;
                c.SortMode = DataGridViewColumnSortMode.Automatic;
            }
            productColumn.FillWeight = 180;
            moduleColumn.FillWeight = 90;
            cacheColumn.FillWeight = 80;
            webColumn.FillWeight = 80;
            statusColumn.FillWeight = 100;
            urlColumn.FillWeight = 220;
            grid.Columns.AddRange(new DataGridViewColumn[] { checkColumn, productColumn, moduleColumn, cacheColumn, webColumn, statusColumn, urlColumn });
            checkColumn.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            grid.AllowUserToAddRows = false;
            grid.AllowUserToDeleteRows = false;
            grid.AllowUserToResizeRows = false;
            grid.RowHeadersVisible = false;
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.MultiSelect = false;
            grid.ShowCellToolTips = true;
            grid.SetBounds(12, 46, ClientSize.Width - 24, ClientSize.Height - 46 - 136);
            grid.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            grid.CurrentCellChanged += (s, e) => UpdateButtons();
            grid.CellValueChanged += (s, e) => UpdateButtons();
            grid.CurrentCellDirtyStateChanged += (s, e) => { if (grid.IsCurrentCellDirty) grid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
            grid.CellMouseDown += grid_CellMouseDown;
            // The grid stays usable while a check or a download runs (scroll, sort, tooltips), but not its check boxes
            grid.CellBeginEdit += (s, e) => { if (busy) e.Cancel = true; };
            grid.CellDoubleClick += (s, e) => { if (e.RowIndex >= 0 && e.ColumnIndex == urlColumn.Index) OpenVendorPage(); };

            ToolStripMenuItem downloadItem = new ToolStripMenuItem(Localization.T("WebVersionsForm.DownloadRow", "Download to the cache"));
            downloadItem.Click += (s, e) => Download(CurrentEntry() == null ? new List<WebVersionEntry>() : new List<WebVersionEntry> { CurrentEntry() });
            ToolStripMenuItem vendorItem = new ToolStripMenuItem(Localization.T("WebVersionsForm.Vendor", "Open vendor page"));
            vendorItem.Click += (s, e) => OpenVendorPage();
            rowMenu.Items.AddRange(new ToolStripItem[] { downloadItem, vendorItem });
            rowMenu.Opening += (s, e) =>
            {
                WebVersionEntry entry = CurrentEntry();
                downloadItem.Enabled = entry != null && entry.CanDownload && !busy;
                vendorItem.Enabled = entry != null && IsWebPage(entry.VendorUrl);
            };
            grid.ContextMenuStrip = rowMenu;

            checkButton.Text = Localization.T("WebVersionsForm.Check", "Check now");
            downloadButton.Text = Localization.T("WebVersionsForm.Download", "Download to the cache");
            vendorButton.Text = Localization.T("WebVersionsForm.Vendor", "Open vendor page");
            foreach (Button b in new[] { checkButton, downloadButton, vendorButton })
            {
                b.AutoSize = true;
                b.MinimumSize = new Size(110, 25);
            }
            // While a check (a download) runs, its button is Cancel: it stops after the current item
            checkButton.Click += (s, e) => { if (busy) RequestCancel(); else CheckNow(); };
            downloadButton.Click += (s, e) => { if (busy) RequestCancel(); else Download(EntriesToDownload()); };
            vendorButton.Click += (s, e) => OpenVendorPage();
            lastCheckLabel.AutoSize = true;
            lastCheckLabel.Margin = new Padding(8, 8, 3, 3);
            buttons.Controls.AddRange(new Control[] { checkButton, downloadButton, vendorButton, lastCheckLabel });
            buttons.WrapContents = false;
            buttons.SetBounds(12, ClientSize.Height - 100, ClientSize.Width - 24, 32);
            buttons.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;

            progressBar.SetBounds(12, ClientSize.Height - 126, 180, 18);
            progressBar.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            progressBar.Visible = false;
            progressLabel.SetBounds(12, ClientSize.Height - 128, ClientSize.Width - 24, 22);
            progressLabel.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            progressLabel.AutoEllipsis = true;
            progressLabel.TextAlign = ContentAlignment.MiddleLeft;
            clock.Tick += (s, e) => UpdateProgressText();

            dailyCheck.Text = Localization.T("WebVersionsForm.Daily", "Check web versions every day at");
            dailyCheck.AutoSize = true;
            dailyCheck.Margin = new Padding(3, 6, 3, 3);
            dailyTime.Width = 50;
            dailyTime.MaxLength = 5;
            autoDownload.Text = Localization.T("WebVersionsForm.AutoDownload", "Download new versions automatically");
            autoDownload.AutoSize = true;
            autoDownload.Margin = new Padding(16, 6, 3, 3);
            String time = UserSettings.Current.WebCheckTime ?? "";
            dailyCheck.Checked = time.Trim().Length > 0;
            dailyTime.Text = dailyCheck.Checked ? time.Trim() : "06:00";
            autoDownload.Checked = UserSettings.Current.WebAutoDownload;
            dailyCheck.CheckedChanged += (s, e) => { dailyTime.Enabled = autoDownload.Enabled = dailyCheck.Checked; };
            dailyTime.Enabled = autoDownload.Enabled = dailyCheck.Checked;
            schedule.Controls.AddRange(new Control[] { dailyCheck, dailyTime, autoDownload });
            schedule.WrapContents = false;
            schedule.SetBounds(12, ClientSize.Height - 64, ClientSize.Width - 120, 30);
            schedule.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;

            closeButton.Text = Localization.T("WebVersionsForm.Close", "Close");
            closeButton.DialogResult = DialogResult.Cancel;
            closeButton.SetBounds(ClientSize.Width - 87, ClientSize.Height - 35, 75, 25);
            closeButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            CancelButton = closeButton;

            Controls.AddRange(new Control[] { infoLabel, notice, grid, progressBar, progressLabel, buttons, schedule, closeButton });
            FormClosing += WebVersionsForm_FormClosing;
            // Closed while a check or a download runs: it stops after the current item and saves what it has
            FormClosed += (s, e) =>
            {
                clock.Stop();
                clock.Dispose();
                if (cancel != null) cancel.Cancel();
            };
            Theme.Apply(this);

            results = WebVersionResults.Load(WebVersionResults.DefaultPath);
            Fill();
        }

        // ---- Grid --------------------------------------------------------------------------

        static public String StatusText(WebVersionStatus status)
        {
            switch (status)
            {
                case WebVersionStatus.UpToDate: return Localization.T("WebVersionsForm.UpToDate", "Up to date");
                case WebVersionStatus.NewerOnWeb: return Localization.T("WebVersionsForm.Newer", "Newer on the web");
                case WebVersionStatus.NotFound: return Localization.T("WebVersionsForm.NotFound", "Not found");
                case WebVersionStatus.NoWingetId: return Localization.T("WebVersionsForm.NoWingetId", "No WingetId");
                case WebVersionStatus.NotChecked: return Localization.T("WebVersionsForm.NotChecked", "Not checked");
                default: return Localization.T("WebVersionsForm.Error", "Error");
            }
        }

        const int NoticeHeight = 86;

        // Info line, and the notice when the cache or the instances are missing for this Windows user
        void UpdateInfo()
        {
            String cache = UserSettings.Current.UpdateCacheShare;
            infoLabel.Text = Localization.F("WebVersionsForm.Info",
                "Instances of CachedInstaller and MsixProvision: version of their file in the shared cache ({0}) against the latest version known to winget (WingetId parameter). Checked from this computer only.",
                String.IsNullOrWhiteSpace(cache) ? Localization.T("WebVersionsForm.NoCache", "none set") : cache);

            WebVersionNotice missing = WebVersionChecker.NoticeOf(cache, targets.Count);
            List<String> lines = new List<String>();
            if ((missing & WebVersionNotice.NoCache) != 0)
                lines.Add(Localization.T("WebVersionsForm.NoticeNoCache",
                    "No shared installer cache is set for your Windows account on this computer (the settings are per user): set it in the options."));
            if ((missing & WebVersionNotice.NoInstances) != 0)
                lines.Add(Localization.T("WebVersionsForm.NoticeNoInstances",
                    "No instance of CachedInstaller or MsixProvision has parameters for your Windows account on this computer: import the team's catalog."));
            noticeLabel.Text = String.Join(Environment.NewLine, lines);
            optionsButton.Visible = (missing & WebVersionNotice.NoCache) != 0;
            importButton.Visible = (missing & WebVersionNotice.NoInstances) != 0;
            bool show = missing != WebVersionNotice.None;
            if (notice.Visible != show || grid.Top != (show ? 46 + NoticeHeight : 46))
            {
                notice.Visible = show;
                int bottom = grid.Bottom;
                grid.Top = show ? 46 + NoticeHeight : 46;
                grid.Height = Math.Max(60, bottom - grid.Top);
            }
        }

        // Instances with parameters for this user; none when module-settings.xml cannot be read
        void LoadTargets()
        {
            try { targets = WebVersionChecker.Targets(ModuleSettings.Load()); }
            catch (Exception ex)
            {
                targets = new List<WebVersionTarget>();
                WebVersionChecker.Log("Web versions window: module settings: " + ex.Message);
            }
        }

        void Fill()
        {
            LoadTargets();
            UpdateInfo();
            HashSet<String> checkedKeys = new HashSet<String>(CheckedEntries().Select(e => e.Key), StringComparer.OrdinalIgnoreCase);
            grid.Rows.Clear();
            rowsByKey.Clear();
            foreach (WebVersionEntry e in WebVersionChecker.Rows(results.Entries, targets).OrderBy(x => x.Product ?? x.Key, StringComparer.CurrentCultureIgnoreCase))
            {
                DataGridViewRow row = grid.Rows[grid.Rows.Add()];
                row.Cells[checkColumn.Index].Value = checkedKeys.Contains(e.Key) && e.CanDownload;
                ApplyRow(row, e);
                if (e.Key != null) rowsByKey[e.Key] = row;
            }
            ShowLastCheck();
            UpdateButtons();
        }

        // Values, tooltip and style of a row from its entry (at the fill, and live as each result arrives)
        void ApplyRow(DataGridViewRow row, WebVersionEntry e)
        {
            row.Tag = e;
            row.Cells[productColumn.Index].Value = (e.Product ?? e.Instance) + " (" + e.Instance + ")";
            row.Cells[moduleColumn.Index].Value = e.Module;
            row.Cells[cacheColumn.Index].Value = e.CacheVersion ?? "";
            row.Cells[webColumn.Index].Value = e.WebVersion ?? "";
            row.Cells[statusColumn.Index].Value = StatusText(e.Status);
            row.Cells[urlColumn.Index].Value = e.InstallerUrl ?? "";
            String tip = e.Message ?? "";
            if (!String.IsNullOrEmpty(e.WingetId)) tip = "winget: " + e.WingetId + (tip.Length > 0 ? "\n" + tip : "");
            if (!String.IsNullOrEmpty(e.CacheFile)) tip += (tip.Length > 0 ? "\n" : "") + e.CacheFile;
            foreach (DataGridViewCell c in row.Cells) c.ToolTipText = tip;
            row.Cells[statusColumn.Index].Style.Font = e.Status == WebVersionStatus.NewerOnWeb ? new Font(grid.Font, FontStyle.Bold) : null;
            DataGridViewCell check = row.Cells[checkColumn.Index];
            check.ReadOnly = !e.CanDownload;
            if (!e.CanDownload) check.Value = false;
        }

        void ShowLastCheck()
        {
            lastCheckLabel.Text = results.LastCheck.HasValue
                ? Localization.F("WebVersionsForm.LastCheck", "Last check: {0}", results.LastCheck.Value.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture))
                : Localization.T("WebVersionsForm.NeverChecked", "Not checked yet: Check now.");
        }

        IEnumerable<WebVersionEntry> CheckedEntries()
        {
            return grid.Rows.Cast<DataGridViewRow>()
                .Where(r => r.Tag is WebVersionEntry && Convert.ToBoolean(r.Cells[checkColumn.Index].Value))
                .Select(r => (WebVersionEntry)r.Tag);
        }

        WebVersionEntry CurrentEntry()
        {
            return grid.CurrentRow == null ? null : grid.CurrentRow.Tag as WebVersionEntry;
        }

        // The checked rows, else the current row
        List<WebVersionEntry> EntriesToDownload()
        {
            List<WebVersionEntry> list = CheckedEntries().ToList();
            if (list.Count == 0 && CurrentEntry() != null) list.Add(CurrentEntry());
            return list;
        }

        void UpdateButtons()
        {
            WebVersionEntry current = CurrentEntry();
            bool cancelling = cancel != null && cancel.IsCancellationRequested;
            checkButton.Enabled = !busy || (operation == WebVersionOperation.Check && !cancelling);
            downloadButton.Enabled = busy
                ? operation == WebVersionOperation.Download && !cancelling
                : CheckedEntries().Any() || (current != null && current.CanDownload);
            vendorButton.Enabled = current != null && IsWebPage(current.VendorUrl);
        }

        void grid_CellMouseDown(object sender, DataGridViewCellMouseEventArgs e)
        {
            // Right click: the row under the mouse becomes the current row of the menu
            if (e.Button == MouseButtons.Right && e.RowIndex >= 0 && e.ColumnIndex >= 0)
                grid.CurrentCell = grid.Rows[e.RowIndex].Cells[e.ColumnIndex];
        }

        // Start (or end) of a check or a download: the button of the operation becomes Cancel,
        // the progress bar and the clock of the status line run
        void SetBusy(bool on, WebVersionOperation op, String text)
        {
            busy = on;
            operation = op;
            if (on)
            {
                cancel = new CancellationTokenSource();
                lastProgress = null;
                progressBar.Value = 0;
                watch.Restart();
                clock.Start();
            }
            else
            {
                watch.Stop();
                clock.Stop();
                foreach (DataGridViewRow row in rowsByKey.Values) Highlight(row, false);
            }
            String cancelText = Localization.T("WebVersionsForm.Cancel", "Cancel");
            checkButton.Text = on && op == WebVersionOperation.Check ? cancelText : Localization.T("WebVersionsForm.Check", "Check now");
            downloadButton.Text = on && op == WebVersionOperation.Download ? cancelText : Localization.T("WebVersionsForm.Download", "Download to the cache");
            progressBar.Visible = on;
            progressLabel.SetBounds(on ? progressBar.Right + 8 : 12, progressLabel.Top, ClientSize.Width - 12 - (on ? progressBar.Right + 8 : 12), progressLabel.Height);
            if (text != null) progressLabel.Text = text;
            UpdateButtons();
        }

        // Cancel: the current item finishes, nothing new starts; the results obtained are kept and saved
        void RequestCancel()
        {
            if (!busy || cancel == null || cancel.IsCancellationRequested) return;
            cancel.Cancel();
            Button b = operation == WebVersionOperation.Check ? checkButton : downloadButton;
            b.Text = Localization.T("WebVersionsForm.Cancelling", "Cancelling...");
            UpdateButtons();
            UpdateProgressText();
        }

        // A report of the worker thread, on the UI thread (Progress<T> created on it); ignored once the window is closed
        void OnProgress(WebVersionProgress p)
        {
            if (IsDisposed || !busy || p == null) return;
            progressBar.Maximum = Math.Max(1, p.Total);
            progressBar.Value = Math.Max(0, Math.Min(p.Done, progressBar.Maximum));
            DataGridViewRow row = null;
            if (p.Key != null) rowsByKey.TryGetValue(p.Key, out row);
            if (p.Step == WebVersionStep.Done)
            {
                if (row != null)
                {
                    Highlight(row, false);
                    if (p.Entry != null)
                    {
                        lock (results) results.Set(p.Entry);
                        ApplyRow(row, p.Entry);
                    }
                    else if (p.Error != null)
                    {
                        row.Cells[statusColumn.Index].Value = StatusText(WebVersionStatus.Error);
                        foreach (DataGridViewCell c in row.Cells) c.ToolTipText = p.Error;
                    }
                }
                if (lastProgress != null && lastProgress.Done < p.Done) lastProgress.Done = p.Done;
                UpdateButtons();
            }
            else
            {
                lastProgress = p;
                if (row != null)
                {
                    Highlight(row, true);
                    row.Cells[statusColumn.Index].Value = WebVersionProgressText.RowText(p);
                    if (!row.Displayed)
                    {
                        try { grid.FirstDisplayedScrollingRowIndex = row.Index; }
                        catch (InvalidOperationException) { }
                    }
                }
            }
            UpdateProgressText();
        }

        // Status line: the item and its step, elapsed time and time left (refreshed every second by the clock)
        void UpdateProgressText()
        {
            if (!busy || IsDisposed) return;
            String text = lastProgress == null
                ? (operation == WebVersionOperation.Check ? Localization.T("WebVersionsForm.Checking", "Checking...") : Localization.T("WebVersionsForm.Downloading", "Downloading..."))
                    + " - " + WebVersionProgressText.TimeText(0, 0, watch.Elapsed)
                : WebVersionProgressText.StatusText(lastProgress, watch.Elapsed);
            if (cancel != null && cancel.IsCancellationRequested)
                text += " - " + Localization.T("WebVersionsForm.StoppingAfterCurrent", "stopping after the current item...");
            progressLabel.Text = text;
        }

        void Highlight(DataGridViewRow row, bool on)
        {
            row.DefaultCellStyle.BackColor = on ? (Theme.IsDark ? Color.FromArgb(74, 64, 28) : Color.FromArgb(255, 244, 196)) : Color.Empty;
        }

        // BeginInvoke that does nothing once the window is closed (the worker thread may outlive it)
        void PostToUi(Action action)
        {
            try
            {
                if (!IsDisposed && IsHandleCreated)
                    BeginInvoke(new MethodInvoker(() => { if (!IsDisposed) action(); }));
            }
            catch (InvalidOperationException) { }
        }

        // ---- Actions -------------------------------------------------------------------------

        void CheckNow()
        {
            if (!WebVersionChecker.TryBegin())
            {
                MessageBox.Show(this, Localization.T("WebVersionsForm.Busy", "A web check or a download is already running."), Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            Fill();   // a row for every instance, ready to show its progress
            SetBusy(true, WebVersionOperation.Check, Localization.T("WebVersionsForm.Checking", "Checking..."));
            IProgress<WebVersionProgress> progress = new Progress<WebVersionProgress>(OnProgress);
            CancellationToken token = cancel.Token;
            new Thread(() =>
            {
                WebVersionResults checkedResults = null;
                String error = null;
                try { checkedResults = WebVersionChecker.CheckAll(progress, token); }
                catch (Exception ex) { error = ex.Message; }
                finally { WebVersionChecker.End(); }
                PostToUi(() =>
                {
                    String elapsed = WebVersionProgressText.Duration(watch.Elapsed);
                    if (checkedResults != null) results = checkedResults;
                    SetBusy(false, WebVersionOperation.Check, "");
                    Fill();
                    if (checkedResults != null)
                        progressLabel.Text = checkedResults.Cancelled
                            ? Localization.F("WebVersionsForm.CheckCancelled", "Check cancelled after {0}: {1} of {2} checked; the results obtained are kept.",
                                elapsed, checkedResults.CheckedCount, targets.Count)
                            : Localization.F("WebVersionsForm.CheckDone", "Check finished in {0}: {1} checked, {2} newer on the web.",
                                elapsed, checkedResults.CheckedCount, checkedResults.Entries.Count(e => e.Status == WebVersionStatus.NewerOnWeb));
                    if (error != null) MessageBox.Show(this, error, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    else if (results.Entries.Count == 0)
                        MessageBox.Show(this, Localization.T("WebVersionsForm.NoInstances",
                            "Nothing to check: no instance of CachedInstaller or MsixProvision has parameters for your Windows account on this computer. Use Import from catalog... (or Package list > Parameters...)."),
                            Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                });
            }) { IsBackground = true, Name = "Web versions check" }.Start();
        }

        // Options on the software updates settings (cache, shared modules folder), then the window again
        void OpenOptions()
        {
            using (OptionsForm options = new OptionsForm())
            {
                options.FocusSoftwareUpdates();
                if (options.ShowDialog(this) != DialogResult.OK) return;
            }
            Fill();
        }

        // Same import as the package list editor; the rows of the new instances go to the package list
        void ImportCatalog()
        {
            List<UpdatePackage> packages = CatalogImport.Run(this, Text);
            if (packages == null) return;
            if (imported != null) imported(packages);
            else
            {
                try
                {
                    List<UpdatePackage> list = UpdateRunner.LoadPackages();
                    if (CatalogImport.AddMissing(list, packages) > 0) UpdateRunner.SavePackages(list);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, Localization.F("WebVersionsForm.PackageListNotSaved",
                        "The parameters were imported, but the package list could not be updated: {0}\nAdd the rows in Package list > Import from catalog...", ex.Message),
                        Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            Fill();
        }

        void Download(List<WebVersionEntry> entries)
        {
            List<WebVersionEntry> ready = entries.Where(e => e.CanDownload).ToList();
            if (ready.Count == 0)
            {
                MessageBox.Show(this, Localization.T("WebVersionsForm.NothingToDownload",
                    "Nothing to download: choose a row whose status is \"Newer on the web\" and that has a source URL (check again if needed)."),
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            String question = Localization.F("WebVersionsForm.ConfirmDownload",
                "Download {0} new version(s) to the cache: {1}?\n\nEach file is refused unless its SHA256 is the one of the winget manifest and it is validly signed by the Publisher of the instance. It is copied next to the current file under a new name, the File parameter of the instance is updated, and the previous file is moved to the OLD subfolder. You need write access to these cache folders.",
                ready.Count, String.Join(", ", ready.Select(e => e.Product + " " + e.WebVersion)));
            if (MessageBox.Show(this, question, Text, MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            if (!WebVersionChecker.TryBegin())
            {
                MessageBox.Show(this, Localization.T("WebVersionsForm.Busy", "A web check or a download is already running."), Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            SetBusy(true, WebVersionOperation.Download, Localization.T("WebVersionsForm.Downloading", "Downloading..."));
            String cache = UserSettings.Current.UpdateCacheShare;
            IProgress<WebVersionProgress> progress = new Progress<WebVersionProgress>(OnProgress);
            CancellationToken token = cancel.Token;
            WebVersionResults target = results;
            new Thread(() =>
            {
                List<CacheDownloadOutcome> outcomes = new List<CacheDownloadOutcome>();
                try
                {
                    outcomes = new CacheDownloader(WebVersionChecker.Log).DownloadAll(ready, cache, progress, token);
                    foreach (CacheDownloadOutcome o in outcomes)
                        if (o.Result != null) lock (target) target.Set(o.Result.Entry);
                    // Cancelled or not, what was downloaded is kept
                    try { lock (target) target.Save(WebVersionResults.DefaultPath); }
                    catch (Exception ex) { WebVersionChecker.Log("  results file: " + ex.Message); }
                }
                finally { WebVersionChecker.End(); }
                bool failed = outcomes.Any(o => o.Result == null);
                int downloaded = outcomes.Count(o => o.Result != null);
                PostToUi(() =>
                {
                    String elapsed = WebVersionProgressText.Duration(watch.Elapsed);
                    SetBusy(false, WebVersionOperation.Download, "");
                    Fill();
                    progressLabel.Text = outcomes.Count < ready.Count
                        ? Localization.F("WebVersionsForm.DownloadCancelled", "Download cancelled after {0}: {1} of {2} file(s) processed, {3} downloaded.", elapsed, outcomes.Count, ready.Count, downloaded)
                        : Localization.F("WebVersionsForm.DownloadDone", "Download finished in {0}: {1} downloaded, {2} failed.", elapsed, downloaded, outcomes.Count - downloaded);
                    ShowDownloadSummary(outcomes, ready.Count, failed);
                });
            }) { IsBackground = true, Name = "Web versions download" }.Start();
        }

        // One line per product; the full messages and the log file in a Details window (text that can be copied)
        void ShowDownloadSummary(List<CacheDownloadOutcome> outcomes, int requested, bool failed)
        {
            if (outcomes.Count == 0 && requested == 0) return;
            String summary = CacheDownloadSummary.Summary(outcomes, requested);
            MessageBoxIcon icon = failed ? MessageBoxIcon.Warning : MessageBoxIcon.Information;
            if (!CacheDownloadSummary.HasDetails(outcomes))
            {
                MessageBox.Show(this, summary, Text, MessageBoxButtons.OK, icon);
                return;
            }
            String question = summary + Environment.NewLine + Environment.NewLine
                + Localization.T("WebVersionsForm.ShowDetails", "Show the details (full messages and log file)?");
            if (MessageBox.Show(this, question, Text, MessageBoxButtons.YesNo, icon, MessageBoxDefaultButton.Button2) == DialogResult.Yes)
                TextViewForm.ShowText(this, Localization.T("WebVersionsForm.DetailsTitle", "Download to the cache: details"),
                    CacheDownloadSummary.Details(outcomes, WebVersionChecker.LogPath));
        }

        static bool IsWebPage(String url)
        {
            Uri uri;
            return !String.IsNullOrWhiteSpace(url) && Uri.TryCreate(url, UriKind.Absolute, out uri)
                && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);
        }

        void OpenVendorPage()
        {
            WebVersionEntry e = CurrentEntry();
            if (e == null || !IsWebPage(e.VendorUrl))
            {
                MessageBox.Show(this, Localization.T("WebVersionsForm.NoVendorPage", "The winget manifest gives no vendor page for this product."), Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            try { using (Process.Start(new ProcessStartInfo(e.VendorUrl) { UseShellExecute = true })) { } }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        // ---- Settings (daily check, window size) ---------------------------------------------

        void RestoreSize()
        {
            Match m = Regex.Match(UserSettings.Current.WebVersionsWindowSize ?? "", @"^(\d{3,4}),(\d{3,4})$");
            if (!m.Success) return;
            Size size = new Size(Int32.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), Int32.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture));
            Rectangle screen = Screen.PrimaryScreen.WorkingArea;
            Size = new Size(Math.Min(Math.Max(size.Width, MinimumSize.Width), screen.Width), Math.Min(Math.Max(size.Height, MinimumSize.Height), screen.Height));
        }

        static public bool IsValidTime(String text)
        {
            TimeSpan t;
            return text != null && Regex.IsMatch(text.Trim(), @"^\d{2}:\d{2}$")
                && TimeSpan.TryParseExact(text.Trim(), @"hh\:mm", CultureInfo.InvariantCulture, out t);
        }

        void WebVersionsForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (dailyCheck.Checked && !IsValidTime(dailyTime.Text))
            {
                MessageBox.Show(this, Localization.T("WebVersionsForm.InvalidTime", "Type the time of the daily check as HH:mm (for example 06:30)."), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                dailyTime.Focus();
                e.Cancel = true;
                return;
            }
            UserSettings s = UserSettings.Current.Clone();
            s.WebCheckTime = dailyCheck.Checked ? dailyTime.Text.Trim() : "";
            s.WebAutoDownload = dailyCheck.Checked && autoDownload.Checked;
            Size size = WindowState == FormWindowState.Normal ? Size : RestoreBounds.Size;
            s.WebVersionsWindowSize = size.Width.ToString(CultureInfo.InvariantCulture) + "," + size.Height.ToString(CultureInfo.InvariantCulture);
            if (s.WebCheckTime == UserSettings.Current.WebCheckTime && s.WebAutoDownload == UserSettings.Current.WebAutoDownload
                && s.WebVersionsWindowSize == UserSettings.Current.WebVersionsWindowSize)
                return;
            try { UserSettings.Save(s); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }
    }
}
