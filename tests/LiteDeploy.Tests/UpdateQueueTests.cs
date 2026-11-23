// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    UpdateQueueTests.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-23
// Purpose: Unit tests of "Update when available" (UpdateQueue): queuing, the
//          computers due at a ping (online, not paused, business hours,
//          servers), expiry, hidden computers, merge of duplicate states and
//          the state file.
// -----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LiteDeploy.Tests
{
    [TestClass]
    public class UpdateQueueTests
    {
        static readonly DateTime Now = new DateTime(2026, 11, 23, 10, 0, 0);

        static ComputerUpdateState Queued(string name, int hoursAgo)
        {
            ComputerUpdateState c = new ComputerUpdateState { Name = name };
            UpdateQueue.Enqueue(c, "Office", Now.AddHours(-hoursAgo));
            return c;
        }

        [TestMethod]
        public void EnqueueAndClear()
        {
            ComputerUpdateState c = new ComputerUpdateState { Name = "PC01" };
            Assert.IsFalse(UpdateQueue.IsQueued(c));
            UpdateQueue.Enqueue(c, null, Now);
            Assert.IsTrue(UpdateQueue.IsQueued(c));
            Assert.AreEqual("", c.QueuedProfile, "the whole catalog");
            UpdateQueue.Clear(c);
            Assert.IsFalse(UpdateQueue.IsQueued(c));
            Assert.IsNull(c.QueuedProfile);
            UpdateQueue.Clear(null);
            Assert.IsFalse(UpdateQueue.IsQueued(null));
        }

        [TestMethod]
        public void Due_OnlineOnlyAndTheRules()
        {
            ComputerUpdateState online = Queued("PC01", 5);
            ComputerUpdateState older = Queued("PC02", 30);
            ComputerUpdateState offline = Queued("PC03", 1);
            ComputerUpdateState notPinged = Queued("PC04", 1);
            ComputerUpdateState paused = Queued("PC05", 1); paused.Paused = true;
            ComputerUpdateState night = Queued("PC06", 1); night.NightOnly = true;
            ComputerUpdateState server = Queued("SRV01", 1); server.OsType = ServerPolicy.TypeServer;
            ComputerUpdateState hidden = Queued("PC07", 1); hidden.Hidden = true;
            ComputerUpdateState notQueued = new ComputerUpdateState { Name = "PC08" };
            ComputerUpdateState expired = Queued("PC09", 24 * 8);
            List<ComputerUpdateState> all = new List<ComputerUpdateState> { online, older, offline, notPinged, paused, night, server, hidden, notQueued, expired };
            Func<string, bool?> ping = n => n == "PC03" ? false : n == "PC04" ? (bool?)null : true;

            List<ComputerUpdateState> due = UpdateQueue.Due(all, ping, Now, true, false, 7);
            CollectionAssert.AreEqual(new[] { "PC02", "PC01" }, due.Select(c => c.Name).ToArray(), "oldest first");

            List<string> outside = UpdateQueue.Due(all, ping, Now, false, true, 7).Select(c => c.Name).ToList();
            CollectionAssert.Contains(outside, "PC06", "outside business hours");
            CollectionAssert.Contains(outside, "SRV01", "servers included");
            CollectionAssert.DoesNotContain(outside, "PC09", "expired");
            CollectionAssert.Contains(UpdateQueue.Due(all, ping, Now, false, true, 0).Select(c => c.Name).ToList(), "PC09", "0: never expires");
        }

        [TestMethod]
        public void Dropped_HiddenOrExpired()
        {
            ComputerUpdateState fresh = Queued("PC01", 1);
            ComputerUpdateState expired = Queued("PC02", 24 * 8);
            ComputerUpdateState hidden = Queued("PC03", 1); hidden.Hidden = true;
            CollectionAssert.AreEqual(new[] { "PC02", "PC03" },
                UpdateQueue.Dropped(new[] { fresh, expired, hidden, null }, Now, 7).Select(c => c.Name).ToArray());
            Assert.AreEqual(1, UpdateQueue.Dropped(new[] { fresh, expired, hidden }, Now, 0).Count, "0: hidden only");
            CollectionAssert.AreEqual(new[] { "PC01", "PC02" }, UpdateQueue.Names(new[] { fresh, expired, hidden, new ComputerUpdateState { Name = "PC04" } }));
        }

        [TestMethod]
        public void MergeStates_KeepsTheNewestQueue()
        {
            ComputerUpdateState a = Queued("PC01", 10); a.QueuedProfile = "Old";
            ComputerUpdateState b = Queued("PC01.example.com", 2); b.QueuedProfile = "New";
            ComputerUpdateState m = UpdateStateStore.MergeStates(new[] { a, b });
            Assert.AreEqual(Now.AddHours(-2), m.QueuedSince);
            Assert.AreEqual("New", m.QueuedProfile);
            Assert.IsNull(UpdateStateStore.MergeStates(new[] { new ComputerUpdateState { Name = "PC02" } }).QueuedSince);
        }

        [TestMethod]
        public void StateFile_RoundTrip()
        {
            string path = Path.Combine(Path.GetTempPath(), "LiteDeploy-queue-" + Guid.NewGuid().ToString("N") + ".xml");
            try
            {
                UpdateStateStore store = new UpdateStateStore();
                UpdateQueue.Enqueue(store.GetOrAdd("PC01"), "Office", Now);
                store.SaveIfChanged(path);
                ComputerUpdateState back = UpdateStateStore.Load(path).Find("PC01");
                Assert.AreEqual(Now, back.QueuedSince);
                Assert.AreEqual("Office", back.QueuedProfile);
            }
            finally { File.Delete(path); }
        }

        [TestMethod]
        public void Settings_DefaultSevenDays()
        {
            Assert.AreEqual(7, new UserSettings().QueueMaxDays);
        }
    }
}
