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
        /// <summary>Shared cache of the installers of the update modules (\\server\share), empty: none.</summary>
        public String UpdateCacheShare = "";
        /// <summary>Business hours, Monday to Friday (HH:mm): "outside business hours only" computers are not updated then.</summary>
        public String BusinessHoursStart = "07:00";
        public String BusinessHoursEnd = "17:00";
        /// <summary>Scheduler: "internal" (while LiteDeploy is open) or "windows" (Windows scheduled tasks).</summary>
        public String SchedulerEngine = UpdateSchedules.Internal;
        /// <summary>Stored credential (Tools > Credentials) used by the software updates; empty: the current user.</summary>
        public String UpdateCredential = "";
        /// <summary>How the worker is started: "WinRM" (scheduled task as SYSTEM, default) or "PsExec".</summary>
        public String RemoteMethod = RemoteRunner.WinRm;

        /// <summary>A copy, so a window can change some settings and keep the others.</summary>
        public UserSettings Clone()
        {
            return (UserSettings)MemberwiseClone();
        }

        /// <summary>True during business hours (Monday to Friday, between start and end).</summary>
        public bool IsBusinessHours(DateTime now)
        {
            if (now.DayOfWeek == DayOfWeek.Saturday || now.DayOfWeek == DayOfWeek.Sunday) return false;
            TimeSpan start, end;
            if (!TimeSpan.TryParse(BusinessHoursStart, out start) || !TimeSpan.TryParse(BusinessHoursEnd, out end)) return false;
            return now.TimeOfDay >= start && now.TimeOfDay < end;
        }

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
