// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    SchedulerTests.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-12
// Purpose: Unit tests of the scheduler (next run, due, names, Windows task
//          definition) and of Wake-on-LAN (magic packet, targets, MAC addresses
//          kept from the worker).
// -----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LiteDeploy.Tests
{
    [TestClass]
    public class SchedulerTests
    {
        static readonly DateTime Thursday9 = new DateTime(2026, 11, 12, 9, 0, 0);

        [TestMethod]
        public void Weekly_NextRun_SameDayLaterOrNextChosenDay()
        {
            UpdateSchedule s = new UpdateSchedule { Days = "Monday,Thursday", Time = "22:00" };
            Assert.AreEqual(new DateTime(2026, 11, 12, 22, 0, 0), s.NextRun(Thursday9));
            Assert.AreEqual(new DateTime(2026, 11, 16, 22, 0, 0), s.NextRun(new DateTime(2026, 11, 12, 22, 0, 0)));
        }

        [TestMethod]
        public void Once_NextRunOnlyInTheFuture_DisabledNever()
        {
            UpdateSchedule s = new UpdateSchedule { Days = "", OnceAt = Thursday9.AddHours(3) };
            Assert.AreEqual(Thursday9.AddHours(3), s.NextRun(Thursday9));
            Assert.IsNull(s.NextRun(Thursday9.AddHours(4)));
            s.Enabled = false;
            Assert.IsNull(s.NextRun(Thursday9));
        }

        [TestMethod]
        public void IsDue_OnlySinceTheLastRunOrStart()
        {
            UpdateSchedule s = new UpdateSchedule { Days = "Thursday", Time = "08:00" };
            // Started at 08:30: the 08:00 run is not caught up
            Assert.IsFalse(s.IsDue(Thursday9, new DateTime(2026, 11, 12, 8, 30, 0)));
            // Started at 07:00, never run: due
            Assert.IsTrue(s.IsDue(Thursday9, new DateTime(2026, 11, 12, 7, 0, 0)));
            // Already run at 08:00: not due again
            s.LastRun = new DateTime(2026, 11, 12, 8, 0, 0);
            Assert.IsFalse(s.IsDue(Thursday9, new DateTime(2026, 11, 12, 7, 0, 0)));
        }

        [TestMethod]
        public void Names_AreSafeTaskNames()
        {
            Assert.IsTrue(UpdateSchedules.IsValidName("Nuit semaine-1"));
            Assert.IsFalse(UpdateSchedules.IsValidName("a\\b"));
            Assert.IsFalse(UpdateSchedules.IsValidName("x\" & calc"));
            Assert.IsFalse(UpdateSchedules.IsValidName(" lead"));
            Assert.IsFalse(UpdateSchedules.IsValidName(""));
        }

        [TestMethod]
        public void TaskXml_WeeklyInteractiveNoPassword()
        {
            UpdateSchedule s = new UpdateSchedule { Name = "Nuit", Days = "Monday,Friday", Time = "22:30" };
            string xml = UpdateSchedules.TaskXml(s, @"C:\Tools\LiteDeploy\LiteDeploy.exe", @"DOMAIN\admin");
            StringAssert.Contains(xml, "<Monday />");
            StringAssert.Contains(xml, "<Friday />");
            StringAssert.Contains(xml, "T22:30:00</StartBoundary>");
            StringAssert.Contains(xml, "<LogonType>InteractiveToken</LogonType>");
            StringAssert.Contains(xml, "<Arguments>/schedule \"Nuit\"</Arguments>");
            Assert.IsFalse(xml.Contains("Password"));
            new System.Xml.XmlDocument().LoadXml(xml.Substring(xml.IndexOf("<Task", StringComparison.Ordinal)));
        }

        [TestMethod]
        public void TaskXml_OnceUsesTimeTrigger()
        {
            UpdateSchedule s = new UpdateSchedule { Name = "Once", Days = "", OnceAt = new DateTime(2026, 12, 1, 21, 15, 0) };
            StringAssert.Contains(UpdateSchedules.TaskXml(s, "x.exe", "u"), "<TimeTrigger><StartBoundary>2026-12-01T21:15:00</StartBoundary>");
        }

        [TestMethod]
        public void WakeOnLan_MagicPacket()
        {
            byte[] p = WakeOnLan.MagicPacket("00-1A-2b-3C-4d-5E");
            Assert.AreEqual(102, p.Length);
            Assert.IsTrue(p.Take(6).All(b => b == 0xFF));
            CollectionAssert.AreEqual(new byte[] { 0x00, 0x1A, 0x2B, 0x3C, 0x4D, 0x5E }, p.Skip(96).ToArray());
            Assert.IsNull(WakeOnLan.MagicPacket("00-1A"));
            Assert.IsNull(WakeOnLan.MagicPacket(null));
        }

        [TestMethod]
        public void WakeOnLan_TargetsBroadcastAndSubnets()
        {
            List<IPAddress> t = WakeOnLan.Targets("10.1.2.3 10.1.2.9 172.16.5.4 bad");
            CollectionAssert.AreEqual(new[] { "255.255.255.255", "10.1.2.255", "172.16.5.255" }, t.Select(a => a.ToString()).ToArray());
        }

        [TestMethod]
        public void State_KeepsMacsFromTheWorker()
        {
            UpdateStateStore store = new UpdateStateStore();
            store.Apply("PC01", new List<WorkerMessage> { WorkerProtocol.Parse(WorkerProtocol.Format("type", "start", "macs", "00-1A-2B-3C-4D-5E", "ips", "10.1.2.3")) }, null, Thursday9);
            Assert.AreEqual("00-1A-2B-3C-4D-5E", store.Find("PC01").Macs);
            // A run without MAC addresses keeps the known ones
            store.Apply("PC01", new List<WorkerMessage>(), "unreachable", Thursday9);
            Assert.AreEqual("10.1.2.3", store.Find("PC01").Ips);
        }
    }
}
