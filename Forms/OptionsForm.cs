// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    OptionsForm.cs
// Author:  LiteDeploy contributors
// Date:    2025-05-30
// Purpose: Options window: language, MSI logging folder, Active Directory
//          refresh at start-up (saved per user by UserSettings).
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
        }

        private void button3_Click(object sender, EventArgs e)
        {
            if (DirectoryBrowser.ShowDialog(this) == DialogResult.OK)
            {
                LogDirDisplay.Text = DirectoryBrowser.SelectedPath;
            }
        }

        private void ApplyButton_Click(object sender, EventArgs e)
        {
            UserSettings settings = new UserSettings();
            settings.Language = Languages[Math.Max(0, languageCombo.SelectedIndex)];
            settings.LoggingDir = LogDirDisplay.Text.Trim().Length > 0 ? LogDirDisplay.Text.Trim() : new UserSettings().LoggingDir;
            settings.AutoUpdateAdOnStartup = AutoUpADOnStartCheckBox.Checked;
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

            if (languageChanged)
            {
                MessageBox.Show(this, Localization.T("OptionsForm.LanguageRestart", "The new language is used the next time LiteDeploy starts."),
                                Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }

            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
