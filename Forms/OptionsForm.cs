// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    OptionsForm.cs
// Author:  LiteDeploy contributors
// Date:    2025-05-30
// Purpose: Options window: language, MSI logging folder, Active Directory
//          refresh and update check at start-up (saved per user by UserSettings).
//          The shared folders of the software updates are typed (a hint shows
//          the expected form), the folder dialog starts at the typed value, and
//          Save warns (Save anyway) when one cannot be read from this computer.
//          SIW report: the folder that holds SIW (checked the same way, then
//          its executables and their signature) and the HTML option; the
//          folder of the portable report tools (same check, then the
//          signature of each tool of the table it holds). The settings
//          scroll above a bar that keeps Save and Cancel visible; a window
//          taller than the screen is shortened to fit it.
// -----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace LiteDeploy
{
    public partial class OptionsForm : Form
    {
        // Order of the items of languageCombo
        static readonly String[] Languages = { Localization.Automatic, Localization.English, Localization.French };

        public OptionsForm()
        {
            InitializeComponent();
            MakeScrollable();
            Localization.Apply(this);
            Theme.Apply(this);
            Load += (s, e) => FitScreen();
        }

        // The settings scroll above a bar that keeps Save and Cancel in view (low screen resolutions)
        void MakeScrollable()
        {
            int barTop = siwGroup.Bottom + 4;
            Panel bar = new Panel { Dock = DockStyle.Bottom, Height = ClientSize.Height - barTop };
            Panel content = new Panel { Dock = DockStyle.Fill, AutoScroll = true, AutoScrollMargin = new System.Drawing.Size(0, 8) };
            SuspendLayout();
            foreach (Control c in new Control[] { groupBox2, updatesGroup, siwGroup })
            {
                Controls.Remove(c);
                content.Controls.Add(c);
            }
            foreach (Button b in new[] { ApplyButton, cancelButton })
            {
                Controls.Remove(b);
                b.Top -= barTop;
                bar.Controls.Add(b);
            }
            Controls.Add(bar);
            Controls.Add(content);
            content.BringToFront();
            ResumeLayout(true);
        }

        /// <summary>The height of the window on a screen whose working area is <paramref name="available"/> pixels high (a small margin kept). Unit-tested.</summary>
        internal static int FittingHeight(int wanted, int available)
        {
            const int Margin = 16;
            return Math.Max(200, Math.Min(wanted, available - Margin));
        }

        // Taller than the screen: the window fits it (the settings scroll), wider by the scroll bar, centered again
        void FitScreen()
        {
            System.Drawing.Rectangle area = Screen.FromControl(Owner ?? (Control)this).WorkingArea;
            int height = FittingHeight(Height, area.Height);
            if (height >= Height) return;
            Height = height;
            Width += SystemInformation.VerticalScrollBarWidth;
            CenterToParent();
            if (Top < area.Top) Top = area.Top;
            if (Bottom > area.Bottom) Top = Math.Max(area.Top, area.Bottom - Height);
        }

        private void OptionsForm_Load(object sender, EventArgs e)
        {
            UserSettings current = UserSettings.Current;
            languageCombo.SelectedIndex = Math.Max(0, Array.IndexOf(Languages, current.Language));
            LogDirDisplay.Text = current.LoggingDir;
            AutoUpADOnStartCheckBox.Checked = current.AutoUpdateAdOnStartup;
            checkUpdatesCheckBox.Checked = current.CheckUpdatesOnStartup;
            deploymentsCheckBox.Checked = current.ShowStandaloneDeployments;
            themeCombo.SelectedIndex = Math.Max(0, Array.IndexOf(Themes, current.Theme));
            cacheBox.Text = current.UpdateCacheShare ?? "";
            sharedModulesBox.Text = current.SharedModulesFolder ?? "";
            hoursStartBox.Text = current.BusinessHoursStart;
            hoursEndBox.Text = current.BusinessHoursEnd;
            unsignedModulesCheckBox.Checked = current.AllowUnsignedModules;
            includeServersCheckBox.Checked = current.IncludeServers;
            parallelBox.Value = UserSettings.ClampParallelism(current.UpdatesParallelism);
            warnBox.Value = CloseWarning.ClampSeconds(current.CloseWarningSeconds);
            postponeCheckBox.Checked = current.CloseWarningPostpone;
            siwFolderBox.Text = current.SiwFolder ?? "";
            siwHtmlCheckBox.Checked = current.SiwHtml;
            toolsFolderBox.Text = current.ToolsFolder ?? "";
            SetCue(toolsFolderBox, FolderCheck.ShareHint);
            SetCue(cacheBox, FolderCheck.ShareHint);
            SetCue(sharedModulesBox, FolderCheck.ShareHint);
            SetCue(siwFolderBox, FolderCheck.ShareHint);

            // Account of the software updates: the current user or a stored credential
            accountCombo.Items.Add(Localization.T("OptionsForm.CurrentUser", "Current user"));
            try
            {
                foreach (LiteSuite.StoredCredential c in LiteSuite.CredentialStore.List())
                    accountCombo.Items.Add(c.Name);
            }
            catch (Exception) { }
            int index = accountCombo.Items.IndexOf(current.UpdateCredential ?? "");
            accountCombo.SelectedIndex = index > 0 ? index : 0;

            // Same order as RemoteRunner.Methods
            methodCombo.Items.Add(Localization.T("OptionsForm.MethodWinRm", "WinRM + SYSTEM task (recommended)"));
            methodCombo.Items.Add(Localization.T("OptionsForm.MethodPsExec", "PsExec"));
            methodCombo.SelectedIndex = Math.Max(0, Array.IndexOf(RemoteRunner.Methods, current.RemoteMethod));
        }

        // Order of the items of themeCombo
        static readonly String[] Themes = { Theme.Automatic, Theme.Light, Theme.Dark };

        private void button3_Click(object sender, EventArgs e)
        {
            if (DirectoryBrowser.ShowDialog(this) == DialogResult.OK)
            {
                LogDirDisplay.Text = DirectoryBrowser.SelectedPath;
            }
        }

        private void sharedModulesButton_Click(object sender, EventArgs e)
        {
            BrowseShared(sharedModulesBox);
        }

        private void cacheButton_Click(object sender, EventArgs e)
        {
            BrowseShared(cacheBox);
        }

        private void siwFolderButton_Click(object sender, EventArgs e)
        {
            BrowseShared(siwFolderBox);
        }

        private void toolsFolderButton_Click(object sender, EventArgs e)
        {
            BrowseShared(toolsFolderBox);
        }

        // The box is the main input (a network share is usually typed or pasted); the dialog starts at its
        // value when one is typed, and the shares are under Network
        private void BrowseShared(TextBox box)
        {
            String description = DirectoryBrowser.Description;
            String selected = DirectoryBrowser.SelectedPath;
            try
            {
                DirectoryBrowser.Description = Localization.T("OptionsForm.BrowseShare", "Choose the shared folder (shares are under Network), or type its path in the box.");
                DirectoryBrowser.SelectedPath = FolderCheck.StartFolder(box.Text) ?? "";
                if (DirectoryBrowser.ShowDialog(this) == DialogResult.OK)
                    box.Text = DirectoryBrowser.SelectedPath;
            }
            finally
            {
                DirectoryBrowser.Description = description;
                DirectoryBrowser.SelectedPath = selected;
            }
        }

        /// <summary>Opens on the software updates settings (cache box focused).</summary>
        public void FocusSoftwareUpdates()
        {
            Shown += (s, e) => { cacheBox.Focus(); cacheBox.SelectAll(); };
        }

        // EM_SETCUEBANNER: grey hint shown while a box is empty (wParam 1: also while it has the focus)
        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, String lParam);
        const int EM_SETCUEBANNER = 0x1501;

        static void SetCue(TextBox box, String cue)
        {
            try { SendMessage(box.Handle, EM_SETCUEBANNER, (IntPtr)1, cue); }
            catch (Exception) { }   // no hint
        }

        /// <summary>
        /// The shared folders that cannot be used from this computer (not found, not readable, no answer), one
        /// line each; empty when they are fine or not set. Each check waits a few seconds at most.
        /// </summary>
        private List<String> FolderProblems(params String[] folders)
        {
            List<String> problems = new List<String>();
            Cursor = Cursors.WaitCursor;
            try
            {
                foreach (String folder in folders)
                {
                    String why = FolderCheck.Describe(FolderCheck.Probe(folder, TimeSpan.FromSeconds(8)));
                    if (why != null) problems.Add(Localization.F("OptionsForm.FolderProblem", "{0}: {1}", folder, why));
                }
            }
            finally { Cursor = Cursors.Default; }
            return problems;
        }

        private void ApplyButton_Click(object sender, EventArgs e)
        {
            // The settings that are not in this window are kept
            UserSettings settings = UserSettings.Current.Clone();
            String cache = cacheBox.Text.Trim();
            TimeSpan start, end;
            if (cache.Length > 0 && !(cache.StartsWith(@"\\") && cache.Length > 4 && cache.IndexOfAny(new[] { '"', '*', '?', '<', '>', '|' }) < 0))
            {
                MessageBox.Show(this, Localization.T("OptionsForm.BadCache", "The cache must be a shared folder: \\\\server\\share."), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            String sharedModules = sharedModulesBox.Text.Trim();
            if (sharedModules.Length > 0 && !IsFolderPath(sharedModules))
            {
                MessageBox.Show(this, Localization.T("OptionsForm.BadSharedModules", "The shared modules folder must be a full path (network share or local drive)."), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            String siwFolder = siwFolderBox.Text.Trim();
            if (siwFolder.Length > 0 && !IsFolderPath(siwFolder))
            {
                MessageBox.Show(this, Localization.T("OptionsForm.BadSiwFolder", "The SIW folder must be a full path (network share or local drive)."), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            String toolsFolder = toolsFolderBox.Text.Trim();
            if (toolsFolder.Length > 0 && !IsFolderPath(toolsFolder))
            {
                MessageBox.Show(this, Localization.T("OptionsForm.BadToolsFolder", "The folder of the portable tools must be a full path (network share or local drive)."), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (!TimeSpan.TryParse(hoursStartBox.Text.Trim(), out start) || !TimeSpan.TryParse(hoursEndBox.Text.Trim(), out end) || end <= start)
            {
                MessageBox.Show(this, Localization.T("OptionsForm.BadHours", "Business hours: start and end as HH:mm, the end after the start."), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            // Exists and readable from this computer: a warning only (a share can be down for a moment)
            List<String> problems = FolderProblems(cache, sharedModules);
            List<String> siwProblems = FolderProblems(siwFolder);
            problems.AddRange(siwProblems);
            // The SIW folder, once readable: its executables and their signature (the publisher of settings.xml)
            if (siwFolder.Length > 0 && siwProblems.Count == 0)
            {
                Cursor = Cursors.WaitCursor;
                String siwProblem;
                try { siwProblem = SiwReports.CheckFolder(siwFolder, UserSettings.Current.SiwPublisher); }
                finally { Cursor = Cursors.Default; }
                if (siwProblem != null) problems.Add(siwProblem);
            }
            // The folder of the portable tools, once readable: the signature of each tool it holds (a missing tool is fine)
            List<String> toolsProblems = FolderProblems(toolsFolder);
            problems.AddRange(toolsProblems);
            if (toolsFolder.Length > 0 && toolsProblems.Count == 0)
            {
                Cursor = Cursors.WaitCursor;
                try
                {
                    foreach (PortableTool tool in PortableToolPolicy.All)
                        if (PortableToolReports.Locate(toolsFolder, tool, System.IO.File.Exists) != null)
                        {
                            String toolProblem = PortableToolReports.Check(toolsFolder, tool);
                            if (toolProblem != null) problems.Add(toolProblem);
                        }
                }
                finally { Cursor = Cursors.Default; }
            }
            if (problems.Count > 0)
            {
                String question = Localization.F("OptionsForm.FoldersUnreachable",
                    "These folders cannot be used from this computer with your account:\n\n{0}\n\nCheck the path and the permissions of the share. Save anyway?",
                    String.Join(Environment.NewLine, problems));
                if (MessageBox.Show(this, question, Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                    return;
            }
            settings.UpdateCacheShare = cache;
            settings.SharedModulesFolder = sharedModules;
            settings.SiwFolder = siwFolder;
            settings.SiwHtml = siwHtmlCheckBox.Checked;
            settings.ToolsFolder = toolsFolder;
            settings.BusinessHoursStart = start.ToString(@"hh\:mm");
            settings.BusinessHoursEnd = end.ToString(@"hh\:mm");
            settings.AllowUnsignedModules = unsignedModulesCheckBox.Checked;
            settings.IncludeServers = includeServersCheckBox.Checked;
            settings.UpdatesParallelism = UserSettings.ClampParallelism((int)parallelBox.Value);
            settings.CloseWarningSeconds = CloseWarning.ClampSeconds((int)warnBox.Value);
            settings.CloseWarningPostpone = postponeCheckBox.Checked;
            settings.UpdateCredential = accountCombo.SelectedIndex > 0 ? (String)accountCombo.SelectedItem : "";
            settings.RemoteMethod = RemoteRunner.Methods[Math.Max(0, methodCombo.SelectedIndex)];
            settings.Theme = Themes[Math.Max(0, themeCombo.SelectedIndex)];
            bool themeChanged = settings.Theme != UserSettings.Current.Theme;
            settings.Language = Languages[Math.Max(0, languageCombo.SelectedIndex)];
            settings.LoggingDir = LogDirDisplay.Text.Trim().Length > 0 ? LogDirDisplay.Text.Trim() : new UserSettings().LoggingDir;
            settings.AutoUpdateAdOnStartup = AutoUpADOnStartCheckBox.Checked;
            settings.CheckUpdatesOnStartup = checkUpdatesCheckBox.Checked;
            settings.ShowStandaloneDeployments = deploymentsCheckBox.Checked;
            bool languageChanged = settings.Language != UserSettings.Current.Language;

            try
            {
                UserSettings.Save(settings);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, Localization.F("OptionsForm.SaveFailed", "The options could not be saved:\n{0}", ex.Message),
                                Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (languageChanged || themeChanged)
            {
                MessageBox.Show(this, Localization.T("OptionsForm.LanguageRestart", "The new language or theme is used the next time LiteDeploy starts."),
                                Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }

            DialogResult = DialogResult.OK;
            Close();
        }

        /// <summary>A full folder path (shared folder or local drive) without invalid characters.</summary>
        static bool IsFolderPath(String path)
        {
            if (path.IndexOfAny(System.IO.Path.GetInvalidPathChars()) >= 0 || path.IndexOfAny(new[] { '*', '?' }) >= 0) return false;
            return path.StartsWith(@"\\") ? path.Length > 4 : (path.Length >= 3 && path[1] == ':' && path[2] == '\\');
        }
    }
}
