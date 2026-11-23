// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    UserSettings.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-11
// Purpose: Settings of the current user, saved in
//          %LOCALAPPDATA%\LiteDeploy\settings.xml (language, MSI logging
//          folder, Active Directory refresh and update check at start-up...).
//          The file is rewritten at each save with a comment before each
//          setting that App > Options does not show (FileComments), so that
//          it can be edited by hand (LiteDeploy closed).
// -----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
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
        /// <summary>Check for a new version of LiteDeploy at start-up (Help > Check for updates).</summary>
        public bool CheckUpdatesOnStartup = true;
        /// <summary>"auto" (Windows light or dark mode), "light" or "dark".</summary>
        public String Theme = LiteDeploy.Theme.Automatic;
        /// <summary>Run update modules that are not signed (tests only: they run as SYSTEM on the computers).</summary>
        public bool AllowUnsignedModules = false;
        /// <summary>
        /// Include servers in the software updates and installs (worker option --allow-servers); the grid of
        /// the main window also shows them then. Default: workstations only.
        /// </summary>
        public bool IncludeServers = false;
        /// <summary>
        /// With IncludeServers, also domain controllers (worker option --allow-domain-controllers).
        /// No window: set by hand in settings.xml, on purpose.
        /// </summary>
        public bool AllowDomainControllers = false;
        /// <summary>Shared cache of the installers of the update modules (\\server\share), empty: none.</summary>
        public String UpdateCacheShare = "";
        /// <summary>Shared folder of the IT team's update modules (usually a network share), empty: none.</summary>
        public String SharedModulesFolder = "";
        /// <summary>Business hours, Monday to Friday (HH:mm): "outside business hours only" computers are not updated then.</summary>
        public String BusinessHoursStart = "07:00";
        public String BusinessHoursEnd = "17:00";
        /// <summary>Scheduler: "internal" (while LiteDeploy is open) or "windows" (Windows scheduled tasks).</summary>
        public String SchedulerEngine = UpdateSchedules.Internal;
        /// <summary>Stored credential (Tools > Credentials) used by the software updates; empty: the current user.</summary>
        public String UpdateCredential = "";
        /// <summary>How the worker is started: "WinRM" (scheduled task as SYSTEM, default) or "PsExec".</summary>
        public String RemoteMethod = RemoteRunner.WinRm;
        /// <summary>Main window: the standalone deployments (file or command run through PsExec). Hidden by default.</summary>
        public bool ShowStandaloneDeployments = false;
        /// <summary>Get the logs: extra folders, "label|folder under %windir%|newest files" separated by ';' (RemoteLogs.ParseExtraFolders). Empty: none.</summary>
        public String ExtraLogFolders = "";
        /// <summary>Tools > Web versions...: check the web versions every day at this time (HH:mm), while LiteDeploy is open. Empty: no daily check.</summary>
        public String WebCheckTime = "";
        /// <summary>The daily web check downloads the new versions to the cache (off: report only).</summary>
        public bool WebAutoDownload = false;
        /// <summary>Size of the Web versions window, "width,height" (empty: default).</summary>
        public String WebVersionsWindowSize = "";

        // ---- SIW report (SIW is never shipped: each administrator points to a folder that holds it) ----

        /// <summary>Folder that holds SIW (siw64.exe, siw-arm64.exe, siw32.exe, siw.lic); empty: SIW report not available.</summary>
        public String SiwFolder = "";
        /// <summary>Also the HTML report of SIW itself (SIW runs twice); off: the XML report only, shown by LiteDeploy.</summary>
        public bool SiwHtml = false;
        /// <summary>O= of the signer of the SIW executables; any other signer is refused.</summary>
        public String SiwPublisher = SiwPolicy.DefaultPublisher;
        /// <summary>SIW sections (/s:, /h:, /n: of its command line); the Licenses and account sections are always removed.</summary>
        public ulong SiwSoftware = SiwPolicy.DefaultSoftware;
        public ulong SiwHardware = SiwPolicy.DefaultHardware;
        public ulong SiwNetwork = SiwPolicy.DefaultNetwork;
        /// <summary>Time limit of one SIW run on the computer, in seconds (30 to 1800).</summary>
        public int SiwTimeoutSeconds = SiwPolicy.DefaultTimeoutSeconds;
        /// <summary>Folder of the portable report tools (PortableToolPolicy: UninstallView, WinUpdatesView...); empty: not available.</summary>
        public String ToolsFolder = "";
        /// <summary>Time limit of one run of a portable report tool on the computer, in seconds (10 to 1800).</summary>
        public int ToolTimeoutSeconds = PortableToolPolicy.DefaultTimeoutSeconds;
        /// <summary>Fields of the summary of several reports, "Page/Group/Property" separated by ';' (SiwReport); empty: the default fields.</summary>
        public String SiwSummaryFields = "";

        // ---- Main window and its grid (restored when it opens; safe defaults when absent or unreadable) ----

        /// <summary>Computers whose last logon (Active Directory) is older than this many days are "offline/stale"; 0: never.</summary>
        public int UpdatesStaleDays = 30;
        /// <summary>Update when available: a computer that has not answered after this many days leaves the queue; 0: never.</summary>
        public int QueueMaxDays = UpdateQueue.DefaultMaxDays;
        /// <summary>Normal bounds "x,y,width,height"; empty: the default size, centered.</summary>
        public String UpdatesWindowBounds = "";
        public bool UpdatesWindowMaximized = false;
        /// <summary>Height of the details under the list (splitter); 0: default.</summary>
        public int UpdatesSplitter = 0;
        /// <summary>Columns of the computer view, "name:width:position;..." (WindowLayout.FormatColumns); empty: default.</summary>
        public String UpdatesColumns = "";
        /// <summary>Columns of the package view, same format.</summary>
        public String UpdatesPackageColumns = "";
        /// <summary>Active status filters (ComputerStatusFilter names separated by ','); empty: none.</summary>
        public String UpdatesStatusFilter = "";
        /// <summary>Active profile of the software (main window, Profile button); empty: the whole catalog.</summary>
        public String UpdatesProfile = "";
        /// <summary>Online only: the computers that do not answer the ping are hidden (on by default).</summary>
        public bool UpdatesOnlineOnly = true;
        /// <summary>Columns of the computer view hidden with the Columns menu (column names separated by ','); empty: every column.</summary>
        public String UpdatesHiddenColumns = DefaultHiddenColumns;

        /// <summary>Columns hidden until the user shows them: the hardware inventory.</summary>
        public const String DefaultHiddenColumns = "manufacturerColumn,modelColumn,serialColumn,yearColumn,cpuColumn,coresColumn,ramColumn,diskColumn,diskFreeColumn";

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

        /// <summary>
        /// The settings that App > Options does not show, with what they do: written as a comment before each
        /// of them in settings.xml (no "--" in a text: not allowed in an XML comment). Keep in line with the
        /// table of the README (Settings outside App > Options).
        /// </summary>
        static public readonly IList<KeyValuePair<String, String>> FileComments = new List<KeyValuePair<String, String>>
        {
            new KeyValuePair<String, String>("AllowDomainControllers",
                "Edited by hand only. true: with IncludeServers, the software updates and installs also reach the domain controllers. Default: false."),
            new KeyValuePair<String, String>("SchedulerEngine",
                "Tools > Scheduled Updates. internal: the schedules run while LiteDeploy is open; windows: Windows scheduled tasks. Default: internal."),
            new KeyValuePair<String, String>("ExtraLogFolders",
                "Edited by hand only. Get the logs: extra folders, label|folder under %windir%|newest files (1 to 100, 10 when left out), separated by ;. Example: myscript|Temp\\MyScript|10. Default: empty."),
            new KeyValuePair<String, String>("WebCheckTime",
                "Tools > Web Versions. Daily check of the web versions at this time (HH:mm), while LiteDeploy is open. Empty: no daily check."),
            new KeyValuePair<String, String>("WebAutoDownload",
                "Tools > Web Versions. true: the daily check downloads the new versions to the cache; false: report only. Default: false."),
            new KeyValuePair<String, String>("WebVersionsWindowSize",
                "Kept by LiteDeploy: size of the Web Versions window, width,height. Empty: default."),
            new KeyValuePair<String, String>("SiwPublisher",
                "Edited by hand only. SIW report: O= of the signer of the SIW executables; any other signer is refused, on this computer and by the worker. Default: Topala Software Solutions."),
            new KeyValuePair<String, String>("SiwSoftware",
                "Edited by hand only. SIW report: software sections, the /s: number of the SIW command line (sum of the values of its documentation). The Licenses section (product keys) is always removed. Default: 5 (Operating System, Installed Programs)."),
            new KeyValuePair<String, String>("SiwHardware",
                "Edited by hand only. SIW report: hardware sections, the /h: number. Default: 136079 (System Summary, Motherboard, BIOS, CPU, Memory, Video, Storage Devices, Network Adapters, Logical Disks)."),
            new KeyValuePair<String, String>("SiwNetwork",
                "Edited by hand only. SIW report: network sections, the /n: number. The account and Active Directory sections are always removed. Default: 1 (Network Information)."),
            new KeyValuePair<String, String>("SiwTimeoutSeconds",
                "Edited by hand only. SIW report: time limit of one SIW run on the computer, in seconds (30 to 1800). Default: 300."),
            new KeyValuePair<String, String>("ToolTimeoutSeconds",
                "Edited by hand only. Portable report tools (UninstallView, WinUpdatesView): time limit of one run on the computer, in seconds (10 to 1800). Default: 120."),
            new KeyValuePair<String, String>("SiwSummaryFields",
                "Edited by hand only. SIW report on several computers: fields of the summary, Page/Group/Property separated by ; (Group * : any group; several matches are joined). Example: BIOS/BIOS Properties/BIOS Version;Video/Monitor/Model. Empty: the default fields."),
            new KeyValuePair<String, String>("UpdatesStaleDays",
                "Edited by hand only. Offline/stale filter: last logon (Active Directory) older than this many days. 0: never stale. Default: 30."),
            new KeyValuePair<String, String>("QueueMaxDays",
                "Edited by hand only. Update when available: a queued computer that has not answered after this many days leaves the queue. 0: never. Default: 7."),
            new KeyValuePair<String, String>("UpdatesWindowBounds",
                "Kept by LiteDeploy: main window, then its grid (bounds, maximized, splitter, columns, status filters). Empty or 0: default."),
            new KeyValuePair<String, String>("UpdatesHiddenColumns",
                "Columns menu of the main window: columns hidden (stateColumn, userColumn, osColumn, ipColumn, lastLogonColumn, statusColumn, updatesColumn, resultColumn, lastScanColumn, changedColumn, pausedColumn, nightOnlyColumn, manufacturerColumn, modelColumn, serialColumn, yearColumn, cpuColumn, coresColumn, ramColumn, diskColumn, diskFreeColumn), separated by commas. Empty: every column. Default: the hardware columns."),
        };

        /// <summary>The settings as written to settings.xml: serialized, with FileComments before their elements.</summary>
        static public XmlDocument ToXml(UserSettings settings)
        {
            XmlDocument doc = new XmlDocument();
            using (StringWriter text = new StringWriter())
            {
                new XmlSerializer(typeof(UserSettings)).Serialize(text, settings);
                doc.LoadXml(text.ToString());
            }
            // The declaration of the serializer says utf-16 (a string): the writer writes its own
            if (doc.FirstChild is XmlDeclaration) doc.RemoveChild(doc.FirstChild);
            XmlElement root = doc.DocumentElement;
            root.ParentNode.InsertBefore(doc.CreateComment(
                " LiteDeploy settings of this user. Most are set in App > Options; the comments describe the others. Close LiteDeploy before editing: it rewrites this file. "), root);
            foreach (KeyValuePair<String, String> c in FileComments)
            {
                XmlNode element = root[c.Key];
                if (element != null) root.InsertBefore(doc.CreateComment(" " + c.Key + ": " + c.Value.Replace("--", "- -") + " "), element);
            }
            return doc;
        }

        static public void Save(UserSettings settings)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));

            XmlDocument doc = ToXml(settings);
            using (XmlWriter writer = XmlWriter.Create(FilePath, new XmlWriterSettings { Indent = true, Encoding = new System.Text.UTF8Encoding(false) }))
            {
                doc.Save(writer);
            }

            Current = settings;
            Apply();

            // Windows that show a setting kept elsewhere too (Include servers) follow the change
            EventHandler handler = Saved;
            if (handler != null) handler(null, EventArgs.Empty);
        }

        /// <summary>Raised (on the thread that saved, the UI thread) once new settings are saved and current.</summary>
        static public event EventHandler Saved;

        static void Apply()
        {
            AppSettings.LOGGING_DIR = Current.LoggingDir;
            AppSettings.AUTO_UPDATE_AD_ON_STARTUP = Current.AutoUpdateAdOnStartup;
            LiteDeploy.Theme.Preference = Current.Theme ?? LiteDeploy.Theme.Automatic;
            LiteDeploy.Theme.Reset();
        }
    }
}
