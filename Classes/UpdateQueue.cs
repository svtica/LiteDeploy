// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    UpdateQueue.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-23
// Purpose: "Update when available": computers queued by the administrator (with
//          the profile chosen then) are updated as soon as they answer the
//          ping of the grid (every 5 minutes, while LiteDeploy is open). Kept
//          in updates.xml (QueuedSince, QueuedProfile). A queued computer is
//          left waiting while it is paused, during business hours when it is
//          "outside business hours only", and when it is a known server that
//          App > Options does not include; hidden computers and entries older
//          than QueueMaxDays leave the queue. The queue is cleared for a
//          computer once a queued run has reached it. Pure logic, unit-tested.
// -----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Linq;

namespace LiteDeploy
{
    internal static class UpdateQueue
    {
        public const int DefaultMaxDays = 7;

        public static void Enqueue(ComputerUpdateState c, String profile, DateTime now)
        {
            c.QueuedSince = now;
            c.QueuedProfile = profile ?? "";
        }

        public static void Clear(ComputerUpdateState c)
        {
            if (c == null) return;
            c.QueuedSince = null;
            c.QueuedProfile = null;
        }

        public static bool IsQueued(ComputerUpdateState c)
        {
            return c != null && c.QueuedSince.HasValue;
        }

        /// <summary>Queued for more than <paramref name="maxDays"/> days (0 or less: never expires).</summary>
        public static bool IsExpired(ComputerUpdateState c, DateTime now, int maxDays)
        {
            return IsQueued(c) && maxDays > 0 && now - c.QueuedSince.Value > TimeSpan.FromDays(maxDays);
        }

        /// <summary>The queued computers that leave the queue without an update: hidden, or expired.</summary>
        public static List<ComputerUpdateState> Dropped(IEnumerable<ComputerUpdateState> states, DateTime now, int maxDays)
        {
            return (states ?? Enumerable.Empty<ComputerUpdateState>())
                .Where(c => IsQueued(c) && (c.Hidden || IsExpired(c, now, maxDays))).ToList();
        }

        /// <summary>
        /// The queued computers to update now: answering the ping (<paramref name="online"/> true), not expired,
        /// not hidden, not paused, not "outside business hours only" during business hours, not a known server
        /// that is not included. In the order of the queue (oldest first).
        /// </summary>
        public static List<ComputerUpdateState> Due(IEnumerable<ComputerUpdateState> states, Func<String, bool?> online, DateTime now,
            bool businessHours, bool includeServers, int maxDays)
        {
            return (states ?? Enumerable.Empty<ComputerUpdateState>())
                .Where(c => IsQueued(c) && !c.Hidden && !IsExpired(c, now, maxDays) && !c.Paused
                            && !(c.NightOnly && businessHours) && !(c.IsServer && !includeServers)
                            && online(c.Name) == true)
                .OrderBy(c => c.QueuedSince.Value).ToList();
        }

        /// <summary>The names of the queued computers (to ping them even when the filters of the grid hide them).</summary>
        public static List<String> Names(IEnumerable<ComputerUpdateState> states)
        {
            return (states ?? Enumerable.Empty<ComputerUpdateState>()).Where(c => IsQueued(c) && !c.Hidden).Select(c => c.Name).ToList();
        }
    }
}
