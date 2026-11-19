// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    GridStateTests.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-19
// Purpose: Unit tests of the state shown by the grid: online filter, logged-on
//          users and last scan kept from the worker lines, hidden columns of
//          the settings. Fictitious computers and users only.
// -----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LiteDeploy.Tests
{
    [TestClass]
    public class GridStateTests
    {
        static WorkerMessage Line(params string[] keyValues)
        {
            return WorkerProtocol.Parse(WorkerProtocol.Format(keyValues));
        }

        [TestMethod]
        public void Filter_OnlineOnly_HidesOfflineOnly()
        {
            List<ComputerRow> rows = new List<ComputerRow>
            {
                new ComputerRow { Name = "PC01", Online = true },
                new ComputerRow { Name = "PC02", Online = false },
                new ComputerRow { Name = "PC03" }
            };
            DateTime now = new DateTime(2026, 11, 19);
            CollectionAssert.AreEqual(new[] { "PC01", "PC03" },
                ComputerFilter.Apply(rows, new ComputerFilterState { OnlineOnly = true }, now).Select(r => r.Name).ToList(), "not pinged yet: shown");
            Assert.AreEqual(3, ComputerFilter.Apply(rows, new ComputerFilterState(), now).Count);
        }

        [TestMethod]
        public void Search_FindsTheUser()
        {
            ComputerRow row = new ComputerRow { Name = "PC01", Users = "EXAMPLE\\User1" };
            Assert.IsTrue(ComputerFilter.MatchesSearch(row, "user1"));
            Assert.IsFalse(ComputerFilter.MatchesSearch(row, "User2"));
        }

        [TestMethod]
        public void State_UsersAndLastScan_FromWorkerLines()
        {
            UpdateStateStore store = new UpdateStateStore();
            DateTime first = new DateTime(2026, 11, 19, 8, 0, 0);
            store.Apply("PC01", new List<WorkerMessage>
            {
                Line("type", "start", "users", "EXAMPLE\\User1"),
                Line("type", WorkerProtocol.TypeDone)
            }, null, first);
            ComputerUpdateState c = store.Find("PC01");
            Assert.AreEqual("EXAMPLE\\User1", c.Users);
            Assert.AreEqual(first, c.LastScan);
            DateTime changed = c.LastChange ?? DateTime.MinValue;

            // A check that finds nothing new: last scan moves, last change does not; nobody logged on now
            DateTime second = first.AddHours(1);
            store.Apply("PC01", new List<WorkerMessage>
            {
                Line("type", "start", "users", ""),
                Line("type", WorkerProtocol.TypeDone)
            }, null, second);
            Assert.AreEqual(second, c.LastScan);
            Assert.AreEqual(changed, c.LastChange ?? DateTime.MinValue);
            Assert.IsNull(c.Users);

            // An older worker sends no users: kept; a run that did not reach the computer: last scan kept
            store.Apply("PC01", new List<WorkerMessage> { Line("type", "start", "users", "EXAMPLE\\User2") }, null, second);
            store.Apply("PC01", new List<WorkerMessage> { Line("type", "start") }, null, second);
            Assert.AreEqual("EXAMPLE\\User2", c.Users);
            store.Apply("PC01", new List<WorkerMessage>(), "unreachable", second.AddHours(1));
            Assert.AreEqual(second, c.LastScan);
        }

        [TestMethod]
        public void MergeStates_KeepsUsersAndNewestScan()
        {
            ComputerUpdateState merged = UpdateStateStore.MergeStates(new List<ComputerUpdateState>
            {
                new ComputerUpdateState { Name = "PC01", LastChange = new DateTime(2026, 11, 2), LastScan = new DateTime(2026, 11, 2) },
                new ComputerUpdateState { Name = "pc01.example.com", LastChange = new DateTime(2026, 11, 1), LastScan = new DateTime(2026, 11, 18), Users = "EXAMPLE\\User1" }
            });
            Assert.AreEqual(new DateTime(2026, 11, 18), merged.LastScan);
            Assert.AreEqual("EXAMPLE\\User1", merged.Users);
        }

        [TestMethod]
        public void ParseNames_HiddenColumns()
        {
            CollectionAssert.AreEqual(new[] { "osColumn", "userColumn" }, WindowLayout.ParseNames(" osColumn, ;userColumn ,"));
            Assert.AreEqual(0, WindowLayout.ParseNames(null).Count);
            Assert.IsTrue(new UserSettings().UpdatesOnlineOnly, "online only by default");
        }
    }
}
