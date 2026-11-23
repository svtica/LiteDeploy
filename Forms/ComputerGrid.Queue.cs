// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    ComputerGrid.Queue.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-23
// Purpose: "Update when available" in the computer grid (UpdateQueue). The
//          action queues the target computers with the active profile, after
//          one confirmation; each ping of the grid (every 5 minutes, and at
//          once after queuing) pings the queued computers too, then updates the
//          ones that answer (no other question: the queue was confirmed),
//          unless a check or update already runs (then at the next ping).
//          "Remove from the queue" takes them out. The Status column and the
//          tooltip show a queued computer; the status line counts them. Works
//          while LiteDeploy is open (internal engine).
// -----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace LiteDeploy
{
    public partial class ComputerGrid
    {
        ToolStripMenuItem queueItem;
        ToolStripMenuItem unqueueItem;

        void InitializeQueue()
        {
            queueItem = new ToolStripMenuItem
            {
                Name = "queueItem",
                Text = Localization.T("ComputerGrid.QueueItem", "Update when available..."),
                ToolTipText = Localization.T("ComputerGrid.QueueTip", "Update these computers with the active profile as soon as each one answers the ping (checked every 5 minutes while LiteDeploy is open)")
            };
            queueItem.Click += (s, e) => QueueTargets(ActionTargets());
            unqueueItem = new ToolStripMenuItem
            {
                Name = "unqueueItem",
                Text = Localization.T("ComputerGrid.UnqueueItem", "Remove from the queue")
            };
            unqueueItem.Click += (s, e) => UnqueueTargets(ActionTargets());
        }

        void PrepareQueueItems(List<String> targets, bool idle)
        {
            queueItem.Enabled = idle && targets.Count > 0 && packages.Count > 0;
            bool anyQueued;
            lock (storeLock) anyQueued = targets.Any(c => UpdateQueue.IsQueued(store.Find(c)));
            unqueueItem.Visible = anyQueued;
        }

        void QueueTargets(List<String> targets)
        {
            List<String> computers = targets.Where(c => !IsPaused(c)).ToList();
            if (computers.Count == 0 || packages.Count == 0) return;
            String names = String.Join(", ", computers.Take(10)) + (computers.Count > 10 ? ", ..." : "");
            if (MessageBox.Show(this, Localization.F("ComputerGrid.ConfirmQueue",
                    "Update {0} computer(s) with the profile {1} as soon as each one answers?\n\nThe ones online now start within a few seconds, the others when they come back (LiteDeploy must stay open; the queue is kept {2} day(s)). The applications being updated are closed without warning: {3}",
                    computers.Count, ProfileDisplayName, UserSettings.Current.QueueMaxDays, names),
                    Caption, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;
            if (!OfferCredential()) return;
            DateTime now = DateTime.Now;
            try
            {
                WithStore(store =>
                {
                    foreach (String c in computers) UpdateQueue.Enqueue(store.GetOrAdd(c), profileName, now);
                }, true);
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, Caption, MessageBoxButtons.OK, MessageBoxIcon.Error); return; }
            AppConsole.WriteLine("Update when available: " + computers.Count + " computer(s) queued (profile " + ProfileDisplayName + ")");
            RefreshList();
            StartPing(false);
        }

        void UnqueueTargets(List<String> targets)
        {
            try
            {
                WithStore(store =>
                {
                    foreach (String c in targets) UpdateQueue.Clear(store.Find(c));
                }, true);
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, Caption, MessageBoxButtons.OK, MessageBoxIcon.Error); }
            RefreshList();
        }

        /// <summary>The queued computers, pinged with the others even when the filters hide them.</summary>
        List<String> QueuedNames()
        {
            lock (storeLock) return UpdateQueue.Names(store.Computers);
        }

        /// <summary>After a ping (UI thread): drops the expired entries, then updates the queued computers that answer.</summary>
        void RunQueue()
        {
            if (IsDisposed || pending > 0) return;
            DateTime now = DateTime.Now;
            UserSettings s = UserSettings.Current;
            List<ComputerUpdateState> due;
            lock (storeLock)
            {
                List<ComputerUpdateState> dropped = UpdateQueue.Dropped(store.Computers, now, s.QueueMaxDays);
                foreach (ComputerUpdateState c in dropped)
                {
                    AppConsole.WriteLine("Update when available: " + c.Name + " leaves the queue (" + (c.Hidden ? "removed from the list" : "no answer for " + s.QueueMaxDays + " day(s)") + ")");
                    UpdateQueue.Clear(c);
                }
                if (dropped.Count > 0)
                {
                    try { store.SaveIfChanged(UpdateStateStore.DefaultPath); }
                    catch (Exception ex) { AppConsole.WriteLine("Update when available: could not save " + UpdateStateStore.DefaultPath + ": " + ex.Message); }
                }
                due = UpdateQueue.Due(store.Computers, online.Get, now, s.IsBusinessHours(now), s.IncludeServers, s.QueueMaxDays);
            }
            if (due.Count == 0) return;

            // Each computer with the software of the profile it was queued with
            SoftwareProfiles profiles = SoftwareProfiles.Load();
            List<Job> jobs = new List<Job>();
            foreach (ComputerUpdateState c in due)
            {
                bool found;
                List<UpdatePackage> list = profiles.Filter(catalog, c.QueuedProfile ?? "", out found);
                if (!found || list.Count == 0)
                {
                    AppConsole.WriteLine("Update when available: " + c.Name + " leaves the queue (profile \"" + c.QueuedProfile + "\" not found or empty)");
                    lock (storeLock) UpdateQueue.Clear(c);
                    continue;
                }
                jobs.Add(new Job { Computer = c.Name, Packages = list, FromQueue = true });
            }
            if (jobs.Count == 0) return;
            AppConsole.WriteLine("Update when available: " + String.Join(", ", jobs.Select(j => j.Computer)) + " answer: update started");
            Start("upgrade", jobs);
        }

        /// <summary>The Status text of a queued computer (null when it is not queued).</summary>
        static String QueueText(ComputerUpdateState c)
        {
            if (!UpdateQueue.IsQueued(c)) return null;
            return Localization.F("ComputerGrid.QueuedStatus", "Waiting: update when available (since {0})", c.QueuedSince.Value.ToString("yyyy-MM-dd HH:mm"));
        }
    }
}
