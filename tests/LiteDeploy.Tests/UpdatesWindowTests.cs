// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    UpdatesWindowTests.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-18
// Purpose: Unit tests of the self-sufficient Software Updates window: parsing
//          of the Active Directory attributes and server classification
//          (AdComputer), the adcache.xml inventory (backward compatible), the
//          rows and their filters (ComputerFilter), the "offline/stale" rule,
//          the checking of rows (ComputerSelection), the window layout kept in
//          the settings (WindowLayout) and the new settings and state fields.
// -----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Xml.Serialization;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LiteDeploy.Tests
{
    [TestClass]
    public class UpdatesWindowTests
    {
        static readonly DateTime Now = new DateTime(2026, 11, 18, 10, 0, 0);

        static Func<String, Object> Props(Dictionary<String, Object> values)
        {
            return name => { Object v; return values.TryGetValue(name, out v) ? v : null; };
        }

        // ---- Active Directory attributes ----------------------------------------------

        [TestMethod]
        public void FromProperties_ReadsEveryAttribute()
        {
            DateTime logon = new DateTime(2026, 11, 1, 8, 30, 0, DateTimeKind.Utc);
            AdComputer c = AdComputer.FromProperties(Props(new Dictionary<String, Object>
            {
                { "name", "PC01" },
                { "operatingsystem", "Windows 11 Pro " },
                { "operatingsystemversion", "10.0 (22631)" },
                { "lastlogontimestamp", logon.ToFileTimeUtc() },
                { "useraccountcontrol", 4096 },
                { "distinguishedname", "CN=PC01,OU=Workstations,DC=example,DC=com" }
            }));
            Assert.AreEqual("PC01", c.Name);
            Assert.AreEqual("Windows 11 Pro", c.OperatingSystem);
            Assert.AreEqual("10.0 (22631)", c.OperatingSystemVersion);
            Assert.AreEqual(logon.ToLocalTime(), c.LastLogon);
            Assert.IsFalse(c.Disabled);
            Assert.AreEqual("OU=Workstations,DC=example,DC=com", c.Ou);
            Assert.IsFalse(c.IsServer);
        }

        [TestMethod]
        public void FromProperties_NoName_IsNull_AndMissingAttributesAreUnknown()
        {
            Assert.IsNull(AdComputer.FromProperties(Props(new Dictionary<String, Object>())));
            AdComputer c = AdComputer.FromProperties(Props(new Dictionary<String, Object> { { "name", "CN=PC02" } }));
            Assert.AreEqual("PC02", c.Name);
            Assert.IsNull(c.OperatingSystem);
            Assert.IsNull(c.LastLogon);
            Assert.IsNull(c.Ou);
            Assert.IsFalse(c.Disabled);
        }

        [TestMethod]
        public void ParseFileTime_NeverAndBadValues_AreNull()
        {
            Assert.IsNull(AdComputer.ParseFileTime(null));
            Assert.IsNull(AdComputer.ParseFileTime(0L));
            Assert.IsNull(AdComputer.ParseFileTime(Int64.MaxValue));
            Assert.IsNull(AdComputer.ParseFileTime("not a date"));
            DateTime utc = new DateTime(2026, 10, 3, 12, 0, 0, DateTimeKind.Utc);
            Assert.AreEqual(utc.ToLocalTime(), AdComputer.ParseFileTime(utc.ToFileTimeUtc().ToString()));
        }

        [TestMethod]
        public void IsDisabled_ReadsBitTwo()
        {
            Assert.IsTrue(AdComputer.IsDisabled(4098));      // WORKSTATION_TRUST_ACCOUNT | ACCOUNTDISABLE
            Assert.IsTrue(AdComputer.IsDisabled("514"));
            Assert.IsTrue(AdComputer.IsDisabled(2L));
            Assert.IsFalse(AdComputer.IsDisabled(4096));
            Assert.IsFalse(AdComputer.IsDisabled(null));
            Assert.IsFalse(AdComputer.IsDisabled("x"));
        }

        [TestMethod]
        public void IsServerOs_ContainsServer_UnknownIsNotServer()
        {
            Assert.IsTrue(AdComputer.IsServerOs("Windows Server 2022 Standard"));
            Assert.IsTrue(AdComputer.IsServerOs("Windows Server® 2008 R2 Enterprise"));
            Assert.IsFalse(AdComputer.IsServerOs("Windows 11 Enterprise"));
            Assert.IsFalse(AdComputer.IsServerOs(""));
            Assert.IsFalse(AdComputer.IsServerOs(null));
        }

        [TestMethod]
        public void ParentContainer_AndShortOu()
        {
            Assert.AreEqual("OU=Laptops,OU=Workstations,DC=example,DC=com",
                AdComputer.ParentContainer("CN=PC01,OU=Laptops,OU=Workstations,DC=example,DC=com"));
            Assert.AreEqual("OU=Workstations,DC=example,DC=com",
                AdComputer.ParentContainer(@"CN=PC\,01,OU=Workstations,DC=example,DC=com"), "escaped comma in the name");
            Assert.IsNull(AdComputer.ParentContainer("CN=PC01"));
            Assert.IsNull(AdComputer.ParentContainer(null));

            Assert.AreEqual("Workstations/Laptops", AdComputer.ShortOu("OU=Laptops,OU=Workstations,DC=example,DC=com"));
            Assert.AreEqual("Computers", AdComputer.ShortOu("CN=Computers,DC=example,DC=com"));
            Assert.AreEqual("", AdComputer.ShortOu(null));
        }

        // ---- adcache.xml ----------------------------------------------------------------

        [TestMethod]
        public void CacheFile_OldFormat_GivesNamesOnly()
        {
            const String old = "<?xml version=\"1.0\"?><ActiveDirCacheFile><DomainName>EXAMPLE</DomainName>" +
                               "<Computers><string>PC01</string><string>SRV01</string></Computers></ActiveDirCacheFile>";
            ActiveDirectoryCacheFile cache = (ActiveDirectoryCacheFile)new XmlSerializer(typeof(ActiveDirectoryCacheFile)).Deserialize(new StringReader(old));
            List<AdComputer> inventory = ActiveDirectoryCache.InventoryOf(cache);
            CollectionAssert.AreEqual(new[] { "PC01", "SRV01" }, inventory.Select(c => c.Name).ToArray());
            Assert.IsTrue(inventory.All(c => c.OperatingSystem == null && !c.Disabled && !c.IsServer));
        }

        [TestMethod]
        public void CacheFile_NewFormat_RoundTrips()
        {
            DateTime logon = new DateTime(2026, 11, 2, 7, 45, 0, DateTimeKind.Local);
            ActiveDirectoryCacheFile cache = new ActiveDirectoryCacheFile
            {
                DomainName = "EXAMPLE",
                Computers = new List<String> { "PC01", "SRV01" },
                Inventory = new List<AdComputer>
                {
                    new AdComputer { Name = "PC01", OperatingSystem = "Windows 11 Pro", LastLogon = logon, Ou = "OU=Workstations,DC=example,DC=com" },
                    new AdComputer { Name = "SRV01", OperatingSystem = "Windows Server 2022 Standard", Disabled = true }
                }
            };
            XmlSerializer serializer = new XmlSerializer(typeof(ActiveDirectoryCacheFile));
            StringWriter writer = new StringWriter();
            serializer.Serialize(writer, cache);
            ActiveDirectoryCacheFile read = (ActiveDirectoryCacheFile)serializer.Deserialize(new StringReader(writer.ToString()));
            List<AdComputer> inventory = ActiveDirectoryCache.InventoryOf(read);
            Assert.AreEqual(2, inventory.Count);
            Assert.AreEqual(logon, inventory[0].LastLogon);
            Assert.AreEqual("OU=Workstations,DC=example,DC=com", inventory[0].Ou);
            Assert.IsNull(inventory[1].LastLogon);
            Assert.IsTrue(inventory[1].Disabled);
            Assert.IsTrue(inventory[1].IsServer);
            CollectionAssert.AreEqual(new[] { "PC01", "SRV01" }, read.Computers, "the name list of the other windows is kept");
        }

        // ---- Rows -------------------------------------------------------------------------

        static readonly List<UpdatePackage> Packages = new List<UpdatePackage>
        {
            UpdatePackage.Parse("A.A||Browser A"),
            UpdatePackage.Parse("B.B||Archiver B"),
        };

        [TestMethod]
        public void Build_StateOsWinsOverDirectory()
        {
            AdComputer ad = new AdComputer { Name = "PC01", OperatingSystem = "Windows 10 Pro", OperatingSystemVersion = "10.0 (19045)" };
            ComputerUpdateState state = new ComputerUpdateState { Name = "PC01", OsCaption = "Microsoft Windows 11 Pro", OsType = "workstation" };
            ComputerRow row = ComputerRow.Build("PC01", ad, state, ComputerSummary.Of(state, Packages), Packages);
            Assert.AreEqual("Microsoft Windows 11 Pro", row.Os);
            Assert.IsNull(row.OsVersion);
            Assert.IsFalse(row.IsServer);
            Assert.IsTrue(row.InAd);

            // The kind read on the computer decides, even against the name of Active Directory
            AdComputer adServer = new AdComputer { Name = "SRV01", OperatingSystem = "Windows Server 2022 Standard" };
            Assert.IsFalse(ComputerRow.Build("SRV01", adServer, new ComputerUpdateState { Name = "SRV01", OsType = "workstation" }, null, Packages).IsServer);
            Assert.IsTrue(ComputerRow.Build("SRV01", adServer, null, null, Packages).IsServer);
            Assert.IsTrue(ComputerRow.Build("PC09", null, new ComputerUpdateState { Name = "PC09", OsType = "Server" }, null, Packages).IsServer);
            Assert.IsFalse(ComputerRow.Build("PC10", null, null, null, Packages).IsServer, "unknown: not a server");
        }

        [TestMethod]
        public void Build_PackagesAndSummary()
        {
            UpdateStateStore store = new UpdateStateStore();
            store.Apply("PC01", new List<WorkerMessage>
            {
                WorkerProtocol.Parse(WorkerProtocol.Format("type", "package", "id", "A.A", "status", "outdated", "installed", "1", "available", "2")),
                WorkerProtocol.Parse(WorkerProtocol.Format("type", "package", "id", "B.B", "status", "notinstalled"))
            }, null, Now);
            ComputerUpdateState state = store.Find("PC01");
            ComputerRow row = ComputerRow.Build("PC01", null, state, ComputerSummary.Of(state, Packages), Packages);
            Assert.AreEqual(ComputerSummaryKind.Outdated, row.Kind);
            Assert.AreEqual(1, row.UpdateCount);
            Assert.IsTrue(row.HasUpdates);
            CollectionAssert.AreEqual(new[] { "Browser A" }, row.PackageNames, "a package that is not installed is not searched");

            state.Paused = true;
            row = ComputerRow.Build("PC01", null, state, ComputerSummary.Of(state, Packages), Packages);
            Assert.IsFalse(row.HasUpdates, "paused: nothing to update");
        }

        [TestMethod]
        public void Universe_DirectoryAddedByNameAndListed()
        {
            List<AdComputer> ad = new List<AdComputer> { new AdComputer { Name = "PC02" }, new AdComputer { Name = "PC01" } };
            List<ComputerUpdateState> saved = new List<ComputerUpdateState>
            {
                new ComputerUpdateState { Name = "pc01" },
                new ComputerUpdateState { Name = "OLD01" },                       // checked once, no longer in AD: hidden
                new ComputerUpdateState { Name = "LAB01", AddedByName = true }
            };
            List<KeyValuePair<String, AdComputer>> u = ComputerFilter.Universe(ad, saved, new[] { "PC03", "pc02" });
            CollectionAssert.AreEqual(new[] { "LAB01", "PC01", "PC02", "PC03" }, u.Select(e => e.Key).ToArray());
            Assert.IsNull(u.First(e => e.Key == "LAB01").Value);
            Assert.IsNotNull(u.First(e => e.Key == "PC01").Value);

            // No directory: every saved computer
            CollectionAssert.AreEqual(new[] { "LAB01", "OLD01", "pc01" },
                ComputerFilter.Universe(new AdComputer[0], saved, null).Select(e => e.Key).ToArray());
        }

        [TestMethod]
        public void NormalizeName_RefusesWhatCannotBeAComputer()
        {
            Assert.AreEqual("PC01", ComputerFilter.NormalizeName("  PC01 "));
            Assert.AreEqual("pc01.example.com", ComputerFilter.NormalizeName("pc01.example.com"));
            Assert.IsNull(ComputerFilter.NormalizeName(""));
            Assert.IsNull(ComputerFilter.NormalizeName("PC 01"));
            Assert.IsNull(ComputerFilter.NormalizeName("PC01;rm"));
            Assert.IsNull(ComputerFilter.NormalizeName(null));
        }

        // ---- Filters ------------------------------------------------------------------------

        static List<ComputerRow> SampleRows()
        {
            return new List<ComputerRow>
            {
                new ComputerRow { Name = "PC01", Os = "Windows 11 Pro", Ou = "OU=Workstations,DC=example,DC=com", InAd = true, LastLogon = Now.AddDays(-2), Kind = ComputerSummaryKind.Outdated, UpdateCount = 2, PackageNames = { "Browser A" } },
                new ComputerRow { Name = "PC02", Os = "Windows 10 Pro", Ou = "OU=Laptops,OU=Workstations,DC=example,DC=com", InAd = true, LastLogon = Now.AddDays(-45), Kind = ComputerSummaryKind.UpToDate },
                new ComputerRow { Name = "PC03", Os = "Windows 11 Pro", InAd = true, LastLogon = Now.AddDays(-1), Kind = ComputerSummaryKind.Errors, NightOnly = true },
                new ComputerRow { Name = "PC04", InAd = true, Kind = ComputerSummaryKind.Paused, Paused = true, PauseReason = "lab" },
                new ComputerRow { Name = "SRV01", Os = "Windows Server 2022 Standard", IsServer = true, InAd = true, LastLogon = Now, Kind = ComputerSummaryKind.Outdated, UpdateCount = 1 },
                new ComputerRow { Name = "OFF01", Os = "Windows 11 Pro", InAd = true, Disabled = true, LastLogon = Now },
                new ComputerRow { Name = "LAB01", AddedByName = true, Kind = ComputerSummaryKind.NotChecked },
            };
        }

        static String[] Names(IEnumerable<ComputerRow> rows)
        {
            return rows.Select(r => r.Name).ToArray();
        }

        [TestMethod]
        public void Filter_Default_HidesServersAndDisabled()
        {
            ComputerFilterState f = new ComputerFilterState();
            CollectionAssert.AreEqual(new[] { "PC01", "PC02", "PC03", "PC04", "LAB01" }, Names(ComputerFilter.Apply(SampleRows(), f, Now)));
            f.IncludeServers = true;
            CollectionAssert.Contains(Names(ComputerFilter.Apply(SampleRows(), f, Now)), "SRV01");
            CollectionAssert.DoesNotContain(Names(ComputerFilter.Apply(SampleRows(), f, Now)), "OFF01", "a disabled account is never shown");
        }

        [TestMethod]
        public void Filter_Search_NameOsOuPackage()
        {
            ComputerFilterState f = new ComputerFilterState { Search = "laptops" };
            CollectionAssert.AreEqual(new[] { "PC02" }, Names(ComputerFilter.Apply(SampleRows(), f, Now)));
            f.Search = "browser";
            CollectionAssert.AreEqual(new[] { "PC01" }, Names(ComputerFilter.Apply(SampleRows(), f, Now)));
            f.Search = " windows 11 ";
            CollectionAssert.AreEqual(new[] { "PC01", "PC03" }, Names(ComputerFilter.Apply(SampleRows(), f, Now)));
            f.Search = "srv";
            Assert.AreEqual(0, ComputerFilter.Apply(SampleRows(), f, Now).Count, "servers stay hidden while searching");
        }

        [TestMethod]
        public void Filter_StatusChips_AnyOfThem_AndCombineWithSearchAndSelection()
        {
            ComputerFilterState f = new ComputerFilterState { Statuses = ComputerStatusFilter.ToUpdate };
            CollectionAssert.AreEqual(new[] { "PC01" }, Names(ComputerFilter.Apply(SampleRows(), f, Now)));
            f.Statuses = ComputerStatusFilter.Errors | ComputerStatusFilter.Paused;
            CollectionAssert.AreEqual(new[] { "PC03", "PC04" }, Names(ComputerFilter.Apply(SampleRows(), f, Now)));
            f.Statuses = ComputerStatusFilter.NightOnly;
            CollectionAssert.AreEqual(new[] { "PC03" }, Names(ComputerFilter.Apply(SampleRows(), f, Now)));
            f.Statuses = ComputerStatusFilter.NotChecked;
            CollectionAssert.AreEqual(new[] { "LAB01" }, Names(ComputerFilter.Apply(SampleRows(), f, Now)), "a paused computer is not 'not checked'");
            f.Statuses = ComputerStatusFilter.UpToDate | ComputerStatusFilter.ToUpdate;
            f.Search = "pc02";
            CollectionAssert.AreEqual(new[] { "PC02" }, Names(ComputerFilter.Apply(SampleRows(), f, Now)));

            f = new ComputerFilterState { Selection = new HashSet<String>(new[] { "pc01", "pc03", "SRV01" }, StringComparer.OrdinalIgnoreCase) };
            CollectionAssert.AreEqual(new[] { "PC01", "PC03" }, Names(ComputerFilter.Apply(SampleRows(), f, Now)));
        }

        [TestMethod]
        public void Stale_OlderThanDelayOrNever_OnlyForDirectoryComputers()
        {
            List<ComputerRow> rows = SampleRows();
            ComputerRow pc02 = rows.First(r => r.Name == "PC02");
            Assert.IsTrue(ComputerFilter.IsStale(pc02, Now, 30));
            Assert.IsFalse(ComputerFilter.IsStale(pc02, Now, 60));
            Assert.IsFalse(ComputerFilter.IsStale(pc02, Now, 0), "0: never stale");
            Assert.IsTrue(ComputerFilter.IsStale(rows.First(r => r.Name == "PC04"), Now, 30), "never logged on");
            Assert.IsFalse(ComputerFilter.IsStale(rows.First(r => r.Name == "LAB01"), Now, 30), "not in AD: unknown");
            Assert.IsFalse(ComputerFilter.IsStale(new ComputerRow { Name = "PC05", InAd = true, LastLogon = Now.AddDays(-30).AddMinutes(1) }, Now, 30));

            ComputerFilterState f = new ComputerFilterState { Statuses = ComputerStatusFilter.Stale };
            CollectionAssert.AreEqual(new[] { "PC02", "PC04" }, Names(ComputerFilter.Apply(rows, f, Now)));
        }

        [TestMethod]
        public void Statuses_RoundTripInSettings()
        {
            ComputerStatusFilter f = ComputerStatusFilter.ToUpdate | ComputerStatusFilter.Stale;
            Assert.AreEqual("ToUpdate,Stale", ComputerFilter.FormatStatuses(f));
            Assert.AreEqual(f, ComputerFilter.ParseStatuses("ToUpdate,Stale"));
            Assert.AreEqual(ComputerStatusFilter.Errors, ComputerFilter.ParseStatuses("errors, unknown, 1024"));
            Assert.AreEqual(ComputerStatusFilter.None, ComputerFilter.ParseStatuses(null));
            Assert.AreEqual("", ComputerFilter.FormatStatuses(ComputerStatusFilter.None));
        }

        // ---- Selection ------------------------------------------------------------------------

        [TestMethod]
        public void Selection_SelectAllChecksVisibleOnly_AndKeepsHiddenChecks()
        {
            HashSet<String> checkedNames = new HashSet<String>(StringComparer.OrdinalIgnoreCase) { "PC04" };
            List<ComputerRow> visible = ComputerFilter.Apply(SampleRows(), new ComputerFilterState { Statuses = ComputerStatusFilter.ToUpdate | ComputerStatusFilter.UpToDate }, Now);
            ComputerSelection.CheckVisible(checkedNames, visible);
            CollectionAssert.AreEquivalent(new[] { "PC04", "PC01", "PC02" }, checkedNames.ToArray());
        }

        [TestMethod]
        public void Selection_SelectOutdated_ExactlyVisibleRowsWithUpdates()
        {
            HashSet<String> checkedNames = new HashSet<String>(StringComparer.OrdinalIgnoreCase) { "PC02", "LAB01" };
            List<ComputerRow> visible = ComputerFilter.Apply(SampleRows(), new ComputerFilterState { IncludeServers = true }, Now);
            ComputerSelection.CheckOutdated(checkedNames, visible);
            CollectionAssert.AreEquivalent(new[] { "PC01", "SRV01" }, checkedNames.ToArray());
        }

        [TestMethod]
        public void Selection_Targets_CheckedElseSelected_NeverHiddenServersOrDisabled()
        {
            List<ComputerRow> rows = SampleRows();
            List<String> targets = ComputerSelection.Targets(rows, new List<String> { "pc03", "SRV01", "OFF01", "GONE01", "PC01" }, new[] { "PC02" }, false);
            CollectionAssert.AreEqual(new[] { "PC01", "PC03" }, targets);
            CollectionAssert.AreEqual(new[] { "PC01", "PC03", "SRV01" },
                ComputerSelection.Targets(rows, new List<String> { "pc03", "SRV01", "PC01" }, null, true));
            CollectionAssert.AreEqual(new[] { "PC02" }, ComputerSelection.Targets(rows, new List<String>(), new[] { "PC02", "pc02" }, false), "none checked: the selected rows");
            Assert.AreEqual(0, ComputerSelection.Targets(rows, new List<String>(), null, false).Count);
        }

        // ---- Window layout ------------------------------------------------------------------

        [TestMethod]
        public void Columns_RoundTrip_AndNewColumnsGoLast()
        {
            String text = WindowLayout.FormatColumns(new[]
            {
                new ColumnLayout { Name = "computerColumn", Width = 140, Position = 1 },
                new ColumnLayout { Name = "osColumn", Width = 5, Position = 0 },
                new ColumnLayout { Name = "bad:name", Width = 10, Position = 2 }
            });
            Assert.AreEqual("computerColumn:140:1;osColumn:5:0", text);
            Dictionary<String, ColumnLayout> read = WindowLayout.ParseColumns(text + ";broken;statusColumn:x:1;;");
            Assert.AreEqual(2, read.Count);
            Assert.AreEqual(WindowLayout.MinColumnWidth, read["osColumn"].Width, "a column is never lost");
            CollectionAssert.AreEqual(new[] { "osColumn", "computerColumn", "statusColumn" },
                WindowLayout.Order(new List<String> { "computerColumn", "osColumn", "statusColumn" }, read));
            Assert.AreEqual(0, WindowLayout.ParseColumns(null).Count);
        }

        [TestMethod]
        public void Bounds_SafeDefaults()
        {
            Rectangle r;
            Assert.IsTrue(WindowLayout.TryParseBounds(WindowLayout.FormatBounds(new Rectangle(10, 20, 900, 600)), new Size(700, 360), out r));
            Assert.AreEqual(new Rectangle(10, 20, 900, 600), r);
            Assert.IsFalse(WindowLayout.TryParseBounds("", new Size(700, 360), out r));
            Assert.IsFalse(WindowLayout.TryParseBounds("1,2,3", new Size(700, 360), out r));
            Assert.IsFalse(WindowLayout.TryParseBounds("0,0,100,100", new Size(700, 360), out r), "smaller than the minimum");
            Rectangle[] screens = { new Rectangle(0, 0, 1920, 1080) };
            Assert.IsTrue(WindowLayout.IsVisibleOn(new Rectangle(100, 100, 900, 600), screens));
            Assert.IsFalse(WindowLayout.IsVisibleOn(new Rectangle(3000, 100, 900, 600), screens), "screen no longer there");
        }

        // ---- Settings and state ---------------------------------------------------------------

        [TestMethod]
        public void Settings_NewFields_RoundTripAndDefaults()
        {
            UserSettings defaults = new UserSettings();
            Assert.IsFalse(defaults.IncludeServers);
            Assert.AreEqual(30, defaults.UpdatesStaleDays);
            Assert.AreEqual("", defaults.UpdatesStatusFilter);

            XmlSerializer serializer = new XmlSerializer(typeof(UserSettings));
            StringWriter writer = new StringWriter();
            serializer.Serialize(writer, new UserSettings
            {
                IncludeServers = true, UpdatesStaleDays = 45, UpdatesWindowBounds = "10,20,900,600", UpdatesWindowMaximized = true,
                UpdatesSplitter = 120, UpdatesColumns = "computerColumn:140:0", UpdatesPackageColumns = "packageColumn:200:1",
                UpdatesStatusFilter = "ToUpdate,Errors"
            });
            UserSettings read = (UserSettings)serializer.Deserialize(new StringReader(writer.ToString()));
            Assert.IsTrue(read.IncludeServers);
            Assert.AreEqual(45, read.UpdatesStaleDays);
            Assert.AreEqual("10,20,900,600", read.UpdatesWindowBounds);
            Assert.IsTrue(read.UpdatesWindowMaximized);
            Assert.AreEqual(120, read.UpdatesSplitter);
            Assert.AreEqual("computerColumn:140:0", read.UpdatesColumns);
            Assert.AreEqual("packageColumn:200:1", read.UpdatesPackageColumns);
            Assert.AreEqual("ToUpdate,Errors", read.UpdatesStatusFilter);

            // The view is no longer kept (the window opens on one row per computer): an older file still loads
            UserSettings withView = (UserSettings)serializer.Deserialize(new StringReader("<LiteDeploySettings><UpdatesView>packages</UpdatesView><UpdatesStaleDays>12</UpdatesStaleDays></LiteDeploySettings>"));
            Assert.AreEqual(12, withView.UpdatesStaleDays);

            UserSettings old = (UserSettings)serializer.Deserialize(new StringReader("<LiteDeploySettings><Language>en</Language></LiteDeploySettings>"));
            Assert.IsFalse(old.IncludeServers, "a file of an earlier version: servers hidden");
            Assert.AreEqual(30, old.UpdatesStaleDays);
        }

        [TestMethod]
        public void StateStore_NewAttributes_BackwardCompatible()
        {
            UpdateStateStore store = new UpdateStateStore();
            ComputerUpdateState c = store.GetOrAdd("LAB01");
            c.AddedByName = true;
            c.OsCaption = "Microsoft Windows 11 Pro";
            c.OsType = "workstation";
            String xml = store.Serialize();
            UpdateStateStore read = (UpdateStateStore)new XmlSerializer(typeof(UpdateStateStore)).Deserialize(new StringReader(xml));
            Assert.IsTrue(read.Find("lab01").AddedByName);
            Assert.AreEqual("Microsoft Windows 11 Pro", read.Find("LAB01").OsCaption);
            Assert.AreEqual("workstation", read.Find("LAB01").OsType);

            UpdateStateStore old = (UpdateStateStore)new XmlSerializer(typeof(UpdateStateStore))
                .Deserialize(new StringReader("<LiteDeployUpdates><Computer Name=\"PC01\" Paused=\"true\" /></LiteDeployUpdates>"));
            Assert.IsFalse(old.Find("PC01").AddedByName);
            Assert.IsNull(old.Find("PC01").OsCaption);
            Assert.IsTrue(old.Find("PC01").Paused);
        }
    }
}
