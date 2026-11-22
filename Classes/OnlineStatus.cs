// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    OnlineStatus.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-19
// Purpose: Online state of the computers of the grid: the result of the last
//          ping of each computer (in memory, for the session), and a ping of
//          many computers at once in the background (asynchronous pings, short
//          time-out), the grid being told of each result. Not saved: a state
//          is only worth what the network says now.
// -----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;

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
        /// called (on a pool thread) after each one, <paramref name="done"/> once at the end. False when a ping of
        /// several computers is already running (nothing started). The pings are asynchronous: no thread is
        /// created for a run (a new thread per run kept its handle until the next full garbage collection, so the
        /// handles of the process grew with the periodic pings).
        /// </summary>
        public bool PingAll(IEnumerable<String> computers, Action<String, bool> result, Action done)
        {
            if (Interlocked.CompareExchange(ref running, 1, 0) != 0) return false;
            List<String> list = (computers ?? Enumerable.Empty<String>())
                .Where(c => !String.IsNullOrWhiteSpace(c)).Distinct(ComputerName.Comparer).ToList();
            Task.Run(() => PingAllAsync(list, result)).ContinueWith(t =>
            {
                Volatile.Write(ref running, 0);
                if (done != null) done();
            }, TaskContinuationOptions.ExecuteSynchronously);
            return true;
        }

        async Task PingAllAsync(List<String> computers, Action<String, bool> result)
        {
            using (SemaphoreSlim gate = new SemaphoreSlim(Parallelism))
            {
                await Task.WhenAll(computers.Select(async computer =>
                {
                    await gate.WaitAsync().ConfigureAwait(false);
                    try
                    {
                        bool online = await AnswersAsync(computer).ConfigureAwait(false);
                        Set(computer, online);
                        if (result != null) result(computer, online);
                    }
                    finally
                    {
                        gate.Release();
                    }
                })).ConfigureAwait(false);
            }
        }

        /// <summary>One ping; false when it does not answer in time or the name does not resolve.</summary>
        static async Task<bool> AnswersAsync(String computer)
        {
            try
            {
                using (Ping ping = new Ping())
                {
                    PingReply reply = await ping.SendPingAsync(computer, TimeoutMs).ConfigureAwait(false);
                    return reply.Status == IPStatus.Success;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
