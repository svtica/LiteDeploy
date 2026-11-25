// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    ScheduleRunner.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-12
// Purpose: Runs one scheduled check or update without a window (internal
//          engine, or "LiteDeploy.exe /schedule <name>" started by the Windows
//          task): skips paused computers and, during business hours, the
//          "outside business hours only" ones, and for an update the known
//          servers unless App > Options includes them (ServerPolicy); wakes the computers first when
//          asked; a few computers at a time; state file updated; a log per day
//          in %LOCALAPPDATA%\LiteDeploy\logs. Check or update only: a schedule
//          never runs the Install action.
// -----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace LiteDeploy
{
    static public class ScheduleRunner
    {
        /// <summary>Computers processed at the same time (App > Options > Software updates, like the main window).</summary>
        static int Parallelism { get { return UserSettings.ClampParallelism(UserSettings.Current.UpdatesParallelism); } }
        static readonly Object fileLock = new Object();

        static public String LogDir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LiteDeploy", "logs"); }
        }

        /// <summary>Runs the schedule of that name. Returns the number of computers that failed (-1: unknown schedule).</summary>
        static public int RunByName(String name)
        {
            UpdateSchedule s = UpdateSchedules.Load().Find(name);
            if (s == null)
            {
                Log("Schedule not found: " + name);
                return -1;
            }
            return Run(s);
        }

        static public int Run(UpdateSchedule schedule)
        {
            DateTime started = DateTime.Now;
            Log("Schedule \"" + schedule.Name + "\" (" + schedule.Command + ", " + schedule.Computers.Count + " computer(s)) started");
            // Schedules check or update only: the Install action is never run by a schedule,
            // even when the schedule file was edited by hand
            if (!IsScheduleCommand(schedule.Command))
            {
                Finish(schedule.Name, started, "Refused: unknown action \"" + schedule.Command + "\" (scan or upgrade only)");
                return -1;
            }

            UpdateStateStore store = UpdateStateStore.Load(UpdateStateStore.DefaultPath);
            // The software of the profile of the schedule (the whole catalog when it has none); a profile that
            // no longer exists: nothing is done (never the whole catalog by mistake)
            bool profileFound;
            List<UpdatePackage> packages = SoftwareProfiles.Load().Filter(UpdateRunner.LoadPackages(), schedule.Profile, out profileFound);
            if (!profileFound)
            {
                Finish(schedule.Name, started, "Refused: profile \"" + schedule.Profile + "\" not found (Software catalog...)");
                return -1;
            }
            if (!String.IsNullOrWhiteSpace(schedule.Profile)) Log("  Profile \"" + schedule.Profile + "\": " + packages.Count + " package(s)");
            bool businessHours = UserSettings.Current.IsBusinessHours(started);
            List<String> computers = new List<String>();
            foreach (String computer in schedule.Computers.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                ComputerUpdateState c = store.Find(computer);
                if (c != null && c.Hidden) { Log("  " + computer + ": removed from the list (hidden), skipped"); continue; }
                if (c != null && c.Paused) { Log("  " + computer + ": paused, skipped" + (c.PauseReason != null ? " (" + c.PauseReason + ")" : "")); continue; }
                if (c != null && c.NightOnly && businessHours && schedule.Command == "upgrade") { Log("  " + computer + ": outside business hours only, skipped"); continue; }
                if (SkipsServer(c, schedule.Command, UserSettings.Current)) { Log("  " + computer + ": server (" + c.OsType + "), skipped: servers not included (App > Options)"); continue; }
                computers.Add(computer);
            }

            if (packages.Count == 0 || computers.Count == 0)
            {
                Finish(schedule.Name, started, "Nothing to do");
                return 0;
            }

            if (schedule.WakeFirst)
            {
                int woken = 0;
                foreach (String computer in computers)
                {
                    ComputerUpdateState c = store.Find(computer);
                    if (c != null && !String.IsNullOrEmpty(c.Macs) && WakeOnLan.Wake(c.Macs, c.Ips) > 0) woken++;
                }
                Log("  Wake-on-LAN sent to " + woken + " computer(s), waiting 2 minutes");
                Thread.Sleep(TimeSpan.FromMinutes(2));
            }

            int failed = 0, done = 0;
            Queue<String> queue = new Queue<String>(computers);
            List<Thread> threads = new List<Thread>();
            for (int i = 0; i < Math.Min(Parallelism, computers.Count); i++)
            {
                Thread t = new Thread(() =>
                {
                    while (true)
                    {
                        String computer;
                        lock (queue) { if (queue.Count == 0) return; computer = queue.Dequeue(); }
                        UpdateRunResult r = UpdateRunner.Run(computer, schedule.Command, packages, null, null);
                        lock (store)
                        {
                            store.Apply(computer, r.Messages, r.Failure, DateTime.Now);
                            try { store.SaveIfChanged(UpdateStateStore.DefaultPath); } catch (Exception ex) { Log("  state file: " + ex.Message); }
                            ComputerUpdateState c = store.Find(computer);
                            bool error = r.Failure != null || (c != null && c.LastError != null);
                            if (error) failed++; else done++;
                            Log("  " + computer + ": " + (error ? "FAILED " + (r.Failure ?? c.LastError) : Summary(r)));
                        }
                    }
                }) { IsBackground = true, Name = "Schedule " + schedule.Name };
                threads.Add(t);
                t.Start();
            }
            foreach (Thread t in threads) t.Join();

            Finish(schedule.Name, started, done + " OK, " + failed + " failed");
            return failed;
        }

        /// <summary>The worker commands a schedule may run: "scan" and "upgrade" (never "install"). Unit-tested.</summary>
        static public bool IsScheduleCommand(String command)
        {
            return command == "scan" || command == "upgrade";
        }

        /// <summary>
        /// True when a scheduled update skips this computer: its last known type is a server or a
        /// domain controller that the settings do not include (the worker would refuse it anyway).
        /// A computer whose type is not known yet is run: the worker checks it. Unit-tested.
        /// </summary>
        static public bool SkipsServer(ComputerUpdateState c, String command, UserSettings settings)
        {
            if (c == null || !c.IsServer) return false;
            return ServerPolicy.Decide(command, ServerPolicy.ProductTypeOf(c.OsType), settings.IncludeServers, settings.AllowDomainControllers) != ServerDecision.Allowed;
        }

        static String Summary(UpdateRunResult r)
        {
            int upgraded = r.Messages.Count(m => m.Type == WorkerProtocol.TypeUpgrade && m["result"] == WorkerProtocol.ResultUpgraded);
            int outdated = r.Messages.Where(m => m.Type == WorkerProtocol.TypePackage).GroupBy(m => m["id"])
                .Count(g => g.Last()["status"] == WorkerProtocol.StatusOutdated);
            return "OK" + (upgraded > 0 ? ", " + upgraded + " updated" : "") + (outdated > 0 ? ", " + outdated + " update(s) available" : "");
        }

        static void Finish(String name, DateTime started, String result)
        {
            lock (fileLock)
            {
                try
                {
                    UpdateSchedules all = UpdateSchedules.Load();
                    UpdateSchedule s = all.Find(name);
                    if (s != null)
                    {
                        s.LastRun = started;
                        s.LastResult = result;
                        // A one-time schedule is done
                        if (!s.DayList.Any()) s.Enabled = false;
                        all.Save();
                    }
                }
                catch (Exception ex) { Log("  schedules file: " + ex.Message); }
            }
            Log("Schedule \"" + name + "\" finished: " + result);
        }

        static public void Log(String line)
        {
            lock (fileLock)
            {
                try
                {
                    Directory.CreateDirectory(LogDir);
                    File.AppendAllText(Path.Combine(LogDir, "schedules-" + DateTime.Now.ToString("yyyyMMdd") + ".log"),
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + line + Environment.NewLine, new UTF8Encoding(false));
                }
                catch (Exception) { }
            }
        }
    }
}
