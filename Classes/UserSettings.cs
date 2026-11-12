// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    UserSettings.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-11
// Purpose: Settings of the current user, saved in
//          %LOCALAPPDATA%\LiteDeploy\settings.xml (language, MSI logging
//          folder, Active Directory refresh at start-up).
// -----------------------------------------------------------------------------
using System;
using System.IO;
using System.Xml.Serialization;

namespace LiteDeploy
{
    [XmlRoot("LiteDeploySettings")]
    public class UserSettings
    {
        /// <summary>"auto" (Windows display language), "en" or "fr".</summary>
        public String Language = Localization.Automatic;
        public String LoggingDir = "\\\\%computername%\\C$\\LiteDeploy";
        public bool AutoUpdateAdOnStartup = true;
        /// <summary>"auto" (Windows light or dark mode), "light" or "dark".</summary>
        public String Theme = LiteDeploy.Theme.Automatic;
        /// <summary>Run update modules that are not signed (tests only: they run as SYSTEM on the computers).</summary>
        public bool AllowUnsignedModules = false;

        static public String FilePath
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LiteDeploy", "settings.xml");
            }
        }

        /// <summary>Current settings: the saved ones, or the defaults.</summary>
        static public UserSettings Current { get; private set; } = new UserSettings();

        static public void Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    using (FileStream file = new FileStream(FilePath, FileMode.Open, FileAccess.Read))
                    {
                        Current = (UserSettings)new XmlSerializer(typeof(UserSettings)).Deserialize(file);
                    }
                }
            }
            catch
            {
                // Unreadable file: defaults (it is rewritten at the next save)
                Current = new UserSettings();
            }

            Apply();
        }

        static public void Save(UserSettings settings)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));

            using (FileStream file = new FileStream(FilePath, FileMode.Create, FileAccess.Write))
            {
                new XmlSerializer(typeof(UserSettings)).Serialize(file, settings);
            }

            Current = settings;
            Apply();
        }

        static void Apply()
        {
            AppSettings.LOGGING_DIR = Current.LoggingDir;
            AppSettings.AUTO_UPDATE_AD_ON_STARTUP = Current.AutoUpdateAdOnStartup;
            LiteDeploy.Theme.Preference = Current.Theme ?? LiteDeploy.Theme.Automatic;
            LiteDeploy.Theme.Reset();
        }
    }
}
