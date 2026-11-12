// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    UpdateSchedules.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-12
// Purpose: Scheduled software updates (from the agent of the MAJ-Logiciels
//          panel): a check or an update of a list of computers, once or on some
//          days of the week at a time. Kept in %LOCALAPPDATA%\LiteDeploy\
//          schedules.xml. Two engines (UserSettings.SchedulerEngine):
//          - internal: the main window starts the schedules that are due while
//            LiteDeploy is open;
//          - Windows: one scheduled task per schedule (folder \LiteDeploy\ of the
//            Task Scheduler) starts "LiteDeploy.exe /schedule <name>" as the
//            logged-on user, even when LiteDeploy is closed; no password stored.
// -----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security;
using System.Security.Principal;
using System.Text;
using System.Xml.Serialization;

namespace LiteDeploy
{
    public class UpdateSchedule
    {
        [XmlAttribute] public String Name;
        /// <summary>"scan" or "upgrade".</summary>
        [XmlAttribute] public String Command = "scan";
        [XmlAttribute] public bool Enabled = true;
        /// <summary>Days of the week (Monday..Sunday); empty: once, at OnceAt.</summary>
        [XmlAttribute] public String Days = "";
        /// <summary>Time of day, HH:mm.</summary>
        [XmlAttribute] public String Time = "22:00";
        public DateTime? OnceAt;
        /// <summary>Wake-on-LAN first (then wait two minutes).</summary>
        [XmlAttribute] public bool WakeFirst;
        public DateTime? LastRun;
        public String LastResult;
        [XmlElement("Computer")] public List<String> Computers = new List<String>();

        public IEnumerable<DayOfWeek> DayList
        {
            get
            {
                foreach (String d in (Days ?? "").Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    DayOfWeek day;
                    if (Enum.TryParse(d.Trim(), true, out day)) yield return day;
                }
            }
        }

        /// <summary>Next start after <paramref name="after"/>; null when disabled or a one-time schedule already passed.</summary>
        public DateTime? NextRun(DateTime after)
        {
            if (!Enabled) return null;
            List<DayOfWeek> days = DayList.ToList();
            if (days.Count == 0)
                return OnceAt.HasValue && OnceAt.Value > after ? OnceAt : null;

            TimeSpan time;
            if (!TimeSpan.TryParse(Time, CultureInfo.InvariantCulture, out time)) return null;
            for (int i = 0; i <= 7; i++)
            {
                DateTime candidate = after.Date.AddDays(i) + time;
                if (candidate > after && days.Contains(candidate.DayOfWeek)) return candidate;
            }
            return null;
        }

        /// <summary>True when a start was due between the last run (or <paramref name="since"/>) and now.</summary>
        public bool IsDue(DateTime now, DateTime since)
        {
            DateTime from = LastRun.HasValue && LastRun.Value > since ? LastRun.Value : since;
            DateTime? next = NextRun(from);
            return next.HasValue && next.Value <= now;
        }

        public String Describe()
        {
            List<DayOfWeek> days = DayList.ToList();
            if (days.Count == 0)
                return OnceAt.HasValue ? OnceAt.Value.ToString("yyyy-MM-dd HH:mm") : "";
            CultureInfo ui = CultureInfo.CurrentUICulture;
            return String.Join(", ", days.Select(d => ui.DateTimeFormat.GetAbbreviatedDayName(d))) + " " + Time;
        }
    }

    [XmlRoot("LiteDeploySchedules")]
    public class UpdateSchedules
    {
        public const String Internal = "internal";
        public const String WindowsTask = "windows";
        public const String TaskFolder = "LiteDeploy";

        [XmlElement("Schedule")] public List<UpdateSchedule> Items = new List<UpdateSchedule>();

        static public String DefaultPath
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LiteDeploy", "schedules.xml"); }
        }

        public UpdateSchedule Find(String name)
        {
            return Items.FirstOrDefault(s => String.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        static public UpdateSchedules Load()
        {
            try
            {
                if (File.Exists(DefaultPath))
                    using (FileStream f = new FileStream(DefaultPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                        return (UpdateSchedules)new XmlSerializer(typeof(UpdateSchedules)).Deserialize(f) ?? new UpdateSchedules();
            }
            catch (Exception) { }
            return new UpdateSchedules();
        }

        public void Save()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(DefaultPath));
            String temp = DefaultPath + ".tmp";
            using (FileStream f = new FileStream(temp, FileMode.Create, FileAccess.Write))
                new XmlSerializer(typeof(UpdateSchedules)).Serialize(f, this);
            if (File.Exists(DefaultPath)) File.Replace(temp, DefaultPath, null);
            else File.Move(temp, DefaultPath);
        }

        /// <summary>A schedule name: letters, digits, spaces and . - _ (it becomes a task name).</summary>
        static public bool IsValidName(String name)
        {
            return !String.IsNullOrWhiteSpace(name) && name.Length <= 64 && name.Trim() == name
                && name.All(c => Char.IsLetterOrDigit(c) || c == ' ' || c == '.' || c == '-' || c == '_');
        }

        // ---- Windows engine (schtasks.exe, XML definition: no date format of the locale) ----

        /// <summary>Task Scheduler XML of a schedule: runs as the logged-on user (no stored password).</summary>
        static public String TaskXml(UpdateSchedule s, String exePath, String user)
        {
            StringBuilder trigger = new StringBuilder();
            List<DayOfWeek> days = s.DayList.ToList();
            TimeSpan time;
            TimeSpan.TryParse(s.Time, CultureInfo.InvariantCulture, out time);
            if (days.Count == 0)
            {
                DateTime at = s.OnceAt ?? DateTime.Now.AddMinutes(5);
                trigger.Append("<TimeTrigger><StartBoundary>").Append(at.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture))
                       .Append("</StartBoundary><Enabled>true</Enabled></TimeTrigger>");
            }
            else
            {
                DateTime start = DateTime.Today + time;
                trigger.Append("<CalendarTrigger><StartBoundary>").Append(start.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture))
                       .Append("</StartBoundary><Enabled>true</Enabled><ScheduleByWeek><DaysOfWeek>");
                foreach (DayOfWeek d in days) trigger.Append('<').Append(d).Append(" />");
                trigger.Append("</DaysOfWeek><WeeksInterval>1</WeeksInterval></ScheduleByWeek></CalendarTrigger>");
            }

            return "<?xml version=\"1.0\" encoding=\"UTF-16\"?>\r\n" +
                "<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">" +
                "<RegistrationInfo><Description>LiteDeploy - " + SecurityElement.Escape(s.Name) + "</Description></RegistrationInfo>" +
                "<Triggers>" + trigger + "</Triggers>" +
                "<Principals><Principal id=\"Author\"><UserId>" + SecurityElement.Escape(user) + "</UserId>" +
                "<LogonType>InteractiveToken</LogonType><RunLevel>LeastPrivilege</RunLevel></Principal></Principals>" +
                "<Settings><MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy><StartWhenAvailable>true</StartWhenAvailable>" +
                "<ExecutionTimeLimit>PT12H</ExecutionTimeLimit><Enabled>" + (s.Enabled ? "true" : "false") + "</Enabled></Settings>" +
                "<Actions Context=\"Author\"><Exec><Command>" + SecurityElement.Escape(exePath) + "</Command>" +
                "<Arguments>/schedule \"" + SecurityElement.Escape(s.Name) + "\"</Arguments></Exec></Actions></Task>";
        }

        /// <summary>Creates or replaces the Windows task of each schedule, removes the others. Returns the errors.</summary>
        public List<String> SyncWindowsTasks(String exePath)
        {
            List<String> errors = new List<String>();
            String user = WindowsIdentity.GetCurrent().Name;
            foreach (UpdateSchedule s in Items)
            {
                String xml = Path.Combine(Path.GetTempPath(), "LiteDeploy-task-" + Guid.NewGuid().ToString("N") + ".xml");
                try
                {
                    File.WriteAllText(xml, TaskXml(s, exePath, user), Encoding.Unicode);
                    String error = Schtasks("/Create /F /TN \"" + TaskFolder + "\\" + s.Name + "\" /XML \"" + xml + "\"");
                    if (error != null) errors.Add(s.Name + ": " + error);
                }
                finally
                {
                    try { File.Delete(xml); } catch (Exception) { }
                }
            }
            foreach (String task in ListWindowsTasks().Where(t => Find(t) == null))
                Schtasks("/Delete /F /TN \"" + TaskFolder + "\\" + task + "\"");
            return errors;
        }

        /// <summary>Removes every Windows task of LiteDeploy (switch to the internal engine).</summary>
        static public void RemoveWindowsTasks()
        {
            foreach (String task in ListWindowsTasks())
                Schtasks("/Delete /F /TN \"" + TaskFolder + "\\" + task + "\"");
        }

        static List<String> ListWindowsTasks()
        {
            List<String> names = new List<String>();
            String output;
            if (Schtasks("/Query /FO CSV /NH", out output) != null) return names;
            String prefix = "\"\\" + TaskFolder + "\\";
            foreach (String line in output.Split('\n'))
            {
                if (!line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                int end = line.IndexOf('"', prefix.Length);
                if (end > prefix.Length) names.Add(line.Substring(prefix.Length, end - prefix.Length));
            }
            return names.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        static String Schtasks(String arguments)
        {
            String output;
            return Schtasks(arguments, out output);
        }

        // Null on success, else the message of schtasks.exe
        static String Schtasks(String arguments, out String output)
        {
            ProcessStartInfo psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "schtasks.exe"), arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using (Process p = Process.Start(psi))
            {
                output = p.StandardOutput.ReadToEnd();
                String error = p.StandardError.ReadToEnd();
                p.WaitForExit();
                return p.ExitCode == 0 ? null : (error + output).Trim();
            }
        }
    }
}
