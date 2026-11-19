// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    OnlineStatus.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-19
// Purpose: Online state of the computers of the grid: the result of the last
//          ping of each computer (in memory, for the session), and a ping of
//          many computers at once in the background (a few threads, short
//          time-out), the grid being told of each result. Not saved: a state
//          is only worth what the network says now.
// -----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Threading;

namespace LiteDeploy
{
    internal sealed class OnlineStatus
    {
        /// <summary>Time-out of one ping, in milliseconds.</summary>
        public const int TimeoutMs = 1000;
        /// <summary>Pings at the same time.</summary>
        public const int Parallelism = 32;

        readonly Dictionary<String, bool> states = new Dictionary<String, bool>(ComputerName.Comparer);
        readonly Object sync = new Object();
        int running;

        /// <summary>The last result for this computer: true (answered), false (did not), null (not pinged).</summary>
        public bool? Get(String computer)
        {
            if (String.IsNullOrEmpty(computer)) return null;
            lock (sync)
            {
                bool online;
                return states.TryGetValue(computer, out online) ? online : (bool?)null;
            }
        }

        public void Set(String computer, bool online)
        {
            if (String.IsNullOrEmpty(computer)) return;
            lock (sync) states[computer] = online;
        }

        /// <summary>Computers that answered the last ping.</summary>
        public int OnlineCount
        {
            get { lock (sync) return states.Count(s => s.Value); }
        }

        /// <summary>A ping of several computers is running.</summary>
        public bool Busy { get { return Volatile.Read(ref running) > 0; } }

        /// <summary>
        /// Pings the computers in the background, <see cref="Parallelism"/> at a time; <paramref name="result"/> is
        /// called (on a worker thread) after each one, <paramref name="done"/> once at the end. False when a ping of
        /// several computers is already running (nothing started).
        /// </summary>
        public bool PingAll(IEnumerable<String> computers, Action<String, bool> result, Action done)
        {
            if (Interlocked.CompareExchange(ref running, 1, 0) != 0) return false;
            Queue<String> queue = new Queue<String>((computers ?? Enumerable.Empty<String>())
                .Where(c => !String.IsNullOrWhiteSpace(c)).Distinct(ComputerName.Comparer));
            int threads = Math.Max(1, Math.Min(Parallelism, queue.Count));
            int left = threads;
            for (int i = 0; i < threads; i++)
            {
                new Thread(() =>
                {
                    while (true)
                    {
                        String computer;
                        lock (queue)
                        {
                            if (queue.Count == 0) break;
                            computer = queue.Dequeue();
                        }
                        bool online = Answers(computer);
                        Set(computer, online);
                        if (result != null) result(computer, online);
                    }
                    if (Interlocked.Decrement(ref left) == 0)
                    {
                        Volatile.Write(ref running, 0);
                        if (done != null) done();
                    }
                }) { IsBackground = true, Name = "Online state" }.Start();
            }
            return true;
        }

        /// <summary>One ping; false when it does not answer in time or the name does not resolve.</summary>
        static bool Answers(String computer)
        {
            try
            {
                using (Ping ping = new Ping())
                {
                    return ping.Send(computer, TimeoutMs).Status == IPStatus.Success;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
