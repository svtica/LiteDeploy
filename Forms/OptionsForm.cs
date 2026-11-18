// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    OptionsForm.cs
// Author:  LiteDeploy contributors
// Date:    2025-05-30
// Purpose: Options window: language, MSI logging folder, Active Directory
//          refresh and update check at start-up (saved per user by UserSettings).
// -----------------------------------------------------------------------------
using System;
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
            Localization.Apply(this);
            Theme.Apply(this);
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
            if (DirectoryBrowser.ShowDialog(this) == DialogResult.OK)
            {
                sharedModulesBox.Text = DirectoryBrowser.SelectedPath;
            }
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
            if (!TimeSpan.TryParse(hoursStartBox.Text.Trim(), out start) || !TimeSpan.TryParse(hoursEndBox.Text.Trim(), out end) || end <= start)
            {
                MessageBox.Show(this, Localization.T("OptionsForm.BadHours", "Business hours: start and end as HH:mm, the end after the start."), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            settings.UpdateCacheShare = cache;
            settings.SharedModulesFolder = sharedModules;
            settings.BusinessHoursStart = start.ToString(@"hh\:mm");
            settings.BusinessHoursEnd = end.ToString(@"hh\:mm");
            settings.AllowUnsignedModules = unsignedModulesCheckBox.Checked;
            settings.IncludeServers = includeServersCheckBox.Checked;
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
