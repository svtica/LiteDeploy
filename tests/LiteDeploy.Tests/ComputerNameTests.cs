// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    ComputerNameTests.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-18
// Purpose: One row per computer in Software Updates: the key of a computer
//          name (case, DNS name, account name, spaces, conflict objects, IP
//          addresses), the computers of the window (Active Directory, main
//          window's list, added by name, saved states) without duplicates, the
//          merge of the states saved under two spellings in updates.xml, the
//          targets of the actions and the cells kept on one line.
// -----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LiteDeploy.Tests
{
    [TestClass]
    public class ComputerNameTests
    {
        // ---- Key ----------------------------------------------------------------------------

        [TestMethod]
        public void Key_SameComputerWhateverTheSpelling()
        {
            foreach (String spelling in new[] { "PC01", "pc01", " Pc01 ", "PC01.example.com", "pc01.Example.COM.", "PC01$", "pc01$ ", "//PC01" })
                Assert.AreEqual("PC01", ComputerName.Key(spelling), spelling);
            Assert.IsTrue(ComputerName.Same("pc01.example.com", "PC01"));
            Assert.IsFalse(ComputerName.Same("PC01", "PC010"));
            Assert.IsFalse(ComputerName.Same("PC01.example.com", "PC02.example.com"));
        }

        [TestMethod]
        public void Key_EmptyAndIpAddresses()
        {
            Assert.AreEqual("", ComputerName.Key(null));
            Assert.AreEqual("", ComputerName.Key("   "));
            Assert.AreEqual("", ComputerName.Key("$"));
            // An IP address is not cut at its first dot
            Assert.AreEqual("192.0.2.10", ComputerName.Key(" 192.0.2.10 "));
            Assert.IsFalse(ComputerName.Same("192.0.2.10", "192.0.2.11"));
            Assert.AreEqual("2001:DB8::10", ComputerName.Key("2001:db8::10"));
        }

        [TestMethod]
        public void Key_ActiveDirectoryConflictObject_IsTheSameComputer()
        {
            Assert.AreEqual("PC01", ComputerName.Key("PC01\nCNF:00000000-0000-0000-0000-000000000000"));
        }

        [TestMethod]
        public void Comparer_InSetsAndDictionaries()
        {
            HashSet<String> set = new HashSet<String>(ComputerName.Comparer) { "PC01" };
            Assert.IsFalse(set.Add("pc01.example.com"));
            Assert.IsTrue(set.Contains("PC01$"));
            Assert.IsTrue(set.Add("PC02"));
            Dictionary<String, int> map = new Dictionary<String, int>(ComputerName.Comparer) { { "pc03.example.com", 3 } };
            Assert.AreEqual(3, map["PC03"]);
        }

        [TestMethod]
        public void OneLine_LineBreaksAndTabs()
        {
            Assert.AreEqual("a b c d", ComputerName.OneLine("a\r\nb\nc\td"));
            Assert.AreEqual("first second", ComputerName.OneLine("first \r\n\r\n  second"));
            Assert.AreEqual("", ComputerName.OneLine(null));
            Assert.AreEqual("same", ComputerName.OneLine("same"));
        }

        // ---- Computers of the window ---------------------------------------------------------

        [TestMethod]
        public void Universe_OneEntryPerComputer()
        {
            List<AdComputer> ad = new List<AdComputer> { new AdComputer { Name = "PC01" }, new AdComputer { Name = "PC04" } };
            List<ComputerUpdateState> saved = new List<ComputerUpdateState>
            {
                new ComputerUpdateState { Name = "PC03", AddedByName = true },
                new ComputerUpdateState { Name = "pc03.example.com", AddedByName = true },
                new ComputerUpdateState { Name = "pc04.example.com", AddedByName = true }
            };
            List<String> listed = new List<String> { "pc01.example.com", "PC01$", "PC02.example.com", "pc02", " PC03 " };

            List<KeyValuePair<String, AdComputer>> universe = ComputerFilter.Universe(ad, saved, listed);

            CollectionAssert.AreEqual(new[] { "PC01", "PC02.example.com", "PC03", "PC04" }, universe.Select(e => e.Key).ToArray());
            Assert.IsNotNull(universe[0].Value, "named and described by Active Directory");
            Assert.IsNull(universe[1].Value);
            Assert.IsNotNull(universe[3].Value, "added by name as a DNS name, but in Active Directory");
        }

        [TestMethod]
        public void Universe_TwoDirectoryEntries_TheEnabledMostRecentOne()
        {
            List<AdComputer> ad = new List<AdComputer>
            {
                new AdComputer { Name = "PC01", Disabled = true, LastLogon = new DateTime(2026, 11, 1) },
                new AdComputer { Name = "PC01\nCNF:00000000-0000-0000-0000-000000000000", LastLogon = new DateTime(2026, 6, 1) },
                new AdComputer { Name = "PC02", LastLogon = new DateTime(2026, 1, 1) },
                new AdComputer { Name = "pc02", LastLogon = new DateTime(2026, 11, 1) }
            };
            List<KeyValuePair<String, AdComputer>> universe = ComputerFilter.Universe(ad, null, null);
            Assert.AreEqual(2, universe.Count);
            Assert.AreEqual("PC01", universe[0].Key);
            Assert.IsFalse(universe[0].Value.Disabled);
            Assert.AreEqual("pc02", universe[1].Key);
            Assert.AreEqual(new DateTime(2026, 11, 1), universe[1].Value.LastLogon);
        }

        [TestMethod]
        public void ListFilter_ByName_FindsTheDnsName()
        {
            Dictionary<String, AdComputer> byName = ComputerListFilter.ByName(new[] { new AdComputer { Name = "SRV01", OperatingSystem = "Windows Server 2022 Standard" } });
            List<String> hidden;
            List<String> shown = ComputerListFilter.Shown(new[] { "srv01.example.com", "PC01" }, byName, false, out hidden);
            CollectionAssert.AreEqual(new[] { "PC01" }, shown);
            CollectionAssert.AreEqual(new[] { "srv01.example.com" }, hidden);
        }

        [TestMethod]
        public void Targets_SameComputerOnce()
        {
            CollectionAssert.AreEqual(new[] { "PC01", "PC02" }, ComputerActions.Targets(new[] { "PC02", "PC01", "pc01.example.com", "PC01$", " " }, null));

            List<ComputerRow> rows = new List<ComputerRow> { new ComputerRow { Name = "PC01" }, new ComputerRow { Name = "PC02" } };
            CollectionAssert.AreEqual(new[] { "PC01" },
                ComputerSelection.Targets(rows, new List<String> { "pc01.example.com", "PC01" }, null, false));
        }

        // ---- updates.xml --------------------------------------------------------------------

        [TestMethod]
        public void Store_Find_WhateverTheSpelling_AndNoNewDuplicate()
        {
            UpdateStateStore store = new UpdateStateStore();
            store.GetOrAdd("PC01").NightOnly = true;
            Assert.IsTrue(store.Find("pc01.example.com").NightOnly);
            store.GetOrAdd("PC01.example.com").Paused = true;
            Assert.AreEqual(1, store.Computers.Count);
            Assert.IsNull(store.Find(""));
        }

        [TestMethod]
        public void MergeStates_NewestData_UnionOfFlagsAndPackages()
        {
            ComputerUpdateState older = new ComputerUpdateState
            {
                Name = "PC01", LastChange = new DateTime(2026, 10, 1), Paused = true, PauseReason = "Lab",
                Macs = "00-00-5E-00-53-01", OsCaption = "Windows 10", TeamViewerId = "123456789", LastError = "old error"
            };
            older.Packages.Add(new PackageUpdateState { Id = "A.A", Status = "uptodate", Installed = "1.0" });
            older.Packages.Add(new PackageUpdateState { Id = "B.B", Status = "outdated", Installed = "2.0", Available = "2.1" });
            ComputerUpdateState newer = new ComputerUpdateState
            {
                Name = "pc01.example.com", LastChange = new DateTime(2026, 11, 1), NightOnly = true, AddedByName = true,
                OsCaption = "Windows 11", OsType = "workstation"
            };
            newer.Packages.Add(new PackageUpdateState { Id = "a.a", Status = "outdated", Installed = "1.0", Available = "1.1" });

            ComputerUpdateState m = UpdateStateStore.MergeStates(new List<ComputerUpdateState> { older, newer });

            Assert.AreEqual("PC01", m.Name, "the short name");
            Assert.AreEqual(new DateTime(2026, 11, 1), m.LastChange);
            Assert.IsNull(m.LastError, "the newest state has no error");
            Assert.IsTrue(m.Paused);
            Assert.AreEqual("Lab", m.PauseReason);
            Assert.IsTrue(m.NightOnly);
            Assert.IsTrue(m.AddedByName);
            Assert.AreEqual("Windows 11", m.OsCaption);
            Assert.AreEqual("workstation", m.OsType);
            Assert.AreEqual("00-00-5E-00-53-01", m.Macs, "kept from the older state when the newer has none");
            Assert.AreEqual("123456789", m.TeamViewerId);
            Assert.AreEqual(2, m.Packages.Count);
            Assert.AreEqual("1.1", m.Find("A.A").Available, "the newest state of a package");
            Assert.AreEqual("2.1", m.Find("B.B").Available);
        }

        [TestMethod]
        public void Load_MergesTheDuplicatesOfAnOlderFile()
        {
            UpdateStateStore store = new UpdateStateStore();
            store.Computers.Add(new ComputerUpdateState { Name = "PC01", LastChange = new DateTime(2026, 10, 1), Paused = true });
            store.Computers.Add(new ComputerUpdateState { Name = "PC02" });
            store.Computers.Add(new ComputerUpdateState { Name = "pc01.example.com", LastChange = new DateTime(2026, 11, 1), OsCaption = "Windows 11" });
            String path = Path.Combine(Path.GetTempPath(), "LiteDeploy-merge-" + Guid.NewGuid().ToString("N") + ".xml");
            try
            {
                File.WriteAllText(path, store.Serialize());
                UpdateStateStore read = UpdateStateStore.Load(path);
                CollectionAssert.AreEqual(new[] { "PC01", "PC02" }, read.Computers.Select(c => c.Name).ToArray());
                Assert.IsTrue(read.Find("PC01").Paused);
                Assert.AreEqual("Windows 11", read.Find("PC01").OsCaption);
                Assert.AreEqual(0, read.MergeDuplicates(), "nothing left to merge");
                Assert.IsTrue(read.SaveIfChanged(path), "written merged");
                Assert.AreEqual(2, UpdateStateStore.Load(path).Computers.Count);
            }
            finally
            {
                File.Delete(path);
            }
        }
    }
}
