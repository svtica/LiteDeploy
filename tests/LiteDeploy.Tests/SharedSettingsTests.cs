// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    SharedSettingsTests.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-18
// Purpose: Unit tests of the settings shared by the windows and by the
//          administrators: the server and disabled-account filter of the
//          computer list and of the main window's list (fictitious computers),
//          the check of a folder setting (temporary folders), the default
//          folder of the catalogs, the rows and empty states of Web versions.
// -----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LiteDeploy.Tests
{
    [TestClass]
    public class SharedSettingsTests
    {
        static List<AdComputer> Inventory()
        {
            return new List<AdComputer>
            {
                new AdComputer { Name = "PC01", OperatingSystem = "Windows 11 Enterprise", LastLogon = new DateTime(2026, 11, 2) },
                new AdComputer { Name = "PC02", OperatingSystem = "Windows 10 Pro", Disabled = true },
                new AdComputer { Name = "SRV01", OperatingSystem = "Windows Server 2022 Standard" },
                new AdComputer { Name = "SRV02", OperatingSystem = "Windows Server 2019 Datacenter", Disabled = true },
                new AdComputer { Name = "PC03" }   // cache of an earlier version: name only
            };
        }

        // ---- Computer list filter ----------------------------------------------------------

        [TestMethod]
        public void ComputerList_IsShown_SameRulesAsSoftwareUpdates()
        {
            Dictionary<String, AdComputer> ad = ComputerListFilter.ByName(Inventory());
            Assert.IsTrue(ComputerListFilter.IsShown(ad["PC01"], false));
            Assert.IsFalse(ComputerListFilter.IsShown(ad["PC02"], true), "disabled account: never");
            Assert.IsFalse(ComputerListFilter.IsShown(ad["SRV01"], false), "server: hidden by default");
            Assert.IsTrue(ComputerListFilter.IsShown(ad["SRV01"], true), "server: shown when included");
            Assert.IsFalse(ComputerListFilter.IsShown(ad["SRV02"], true), "disabled server: never");
            Assert.IsTrue(ComputerListFilter.IsShown(ad["PC03"], false), "unknown operating system: not a server");
            Assert.IsTrue(ComputerListFilter.IsShown(null, false), "not in Active Directory: kept");
        }

        [TestMethod]
        public void ComputerList_Shown_KeepsUnknownComputersAndOrder()
        {
            Dictionary<String, AdComputer> ad = ComputerListFilter.ByName(Inventory());
            List<String> hidden;
            List<String> shown = ComputerListFilter.Shown(new[] { "srv01", " PC01 ", "", "PC02", "PC04", "PC03" }, ad, false, out hidden);
            CollectionAssert.AreEqual(new[] { "PC01", "PC04", "PC03" }, shown, "case-insensitive lookup, trimmed, blanks dropped");
            CollectionAssert.AreEqual(new[] { "srv01", "PC02" }, hidden);

            shown = ComputerListFilter.Shown(new[] { "SRV01", "PC02" }, ad, true, out hidden);
            CollectionAssert.AreEqual(new[] { "SRV01" }, shown);
            CollectionAssert.AreEqual(new[] { "PC02" }, hidden);
        }

        [TestMethod]
        public void ComputerList_Shown_WithoutInventory_KeepsEveryName()
        {
            List<String> hidden;
            CollectionAssert.AreEqual(new[] { "PC01", "SRV01" }, ComputerListFilter.Shown(new[] { "PC01", "SRV01" }, null, false, out hidden));
            Assert.AreEqual(0, hidden.Count);
        }

        [TestMethod]
        public void ComputerList_ByName_SkipsEmptyNames_AndFormatsLastLogon()
        {
            Dictionary<String, AdComputer> ad = ComputerListFilter.ByName(new[] { null, new AdComputer { Name = " " }, new AdComputer { Name = "PC01" } });
            Assert.AreEqual(1, ad.Count);
            Assert.IsTrue(ad.ContainsKey("pc01"));
            Assert.AreEqual("2026-11-02", ComputerListFilter.FormatLastLogon(new DateTime(2026, 11, 2, 15, 30, 0)));
            Assert.AreEqual("", ComputerListFilter.FormatLastLogon(null));
        }

        // ---- Folder check -------------------------------------------------------------------

        [TestMethod]
        public void FolderCheck_Probe_ExistingMissingEmptyInvalid()
        {
            String dir = Path.Combine(Path.GetTempPath(), "LiteDeployTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                Assert.AreEqual(FolderState.Ok, FolderCheck.Probe(dir));
                Assert.AreEqual(FolderState.Ok, FolderCheck.Probe(" " + dir + " "), "trimmed");
                Assert.AreEqual(FolderState.NotFound, FolderCheck.Probe(Path.Combine(dir, "missing")));
                Assert.AreEqual(FolderState.Empty, FolderCheck.Probe("  "));
                Assert.AreEqual(FolderState.Empty, FolderCheck.Probe(null));
                Assert.AreEqual(FolderState.Invalid, FolderCheck.Probe(Path.Combine(dir, "a*b")));
                Assert.AreEqual(FolderState.Ok, FolderCheck.Probe(dir, TimeSpan.FromSeconds(30)));
                Assert.AreEqual(FolderState.Empty, FolderCheck.Probe("", TimeSpan.FromSeconds(1)));
            }
            finally { Directory.Delete(dir, true); }
        }

        [TestMethod]
        public void FolderCheck_Describe_OnlyProblems()
        {
            Assert.IsNull(FolderCheck.Describe(FolderState.Ok));
            Assert.IsNull(FolderCheck.Describe(FolderState.Empty));
            foreach (FolderState s in new[] { FolderState.NotFound, FolderState.NotReadable, FolderState.Invalid, FolderState.TimedOut })
                Assert.IsFalse(String.IsNullOrEmpty(FolderCheck.Describe(s)), s.ToString());
        }

        [TestMethod]
        public void FolderCheck_HintAndStartFolder()
        {
            String bs = new String((char)0x5C, 1);
            Assert.AreEqual(bs + bs + "server" + bs + "share", FolderCheck.ShareHint);
            Assert.IsNull(FolderCheck.StartFolder("   "));
            Assert.AreEqual(@"D:\Shared\Cache", FolderCheck.StartFolder(@" D:\Shared\Cache "));
        }

        // ---- Default folder of the catalogs ---------------------------------------------------

        [TestMethod]
        public void CatalogLocation_SharedFolder_NextToTheModulesFolder()
        {
            String bs = new String((char)0x5C, 1);
            String share = bs + bs + "SRV01" + bs + "IT";
            Assert.AreEqual(share + bs + "LiteDeploy" + bs + "catalogs", CatalogLocation.SharedFolder(share + bs + "LiteDeploy" + bs + "modules"));
            Assert.AreEqual(share + bs + "LiteDeploy" + bs + "catalogs", CatalogLocation.SharedFolder(share + bs + "LiteDeploy" + bs + "modules" + bs), "trailing separator");
            Assert.AreEqual(@"D:\LiteDeploy\catalogs", CatalogLocation.SharedFolder(@"D:\LiteDeploy\modules"));
            Assert.IsNull(CatalogLocation.SharedFolder(share), "share root: no parent");
            Assert.IsNull(CatalogLocation.SharedFolder(""));
            Assert.IsNull(CatalogLocation.SharedFolder(null));
        }

        [TestMethod]
        public void CatalogLocation_InitialFolder_SharedWhenItExists_ElseLocal()
        {
            String local = @"C:\Users\Utilisateur1\AppData\Local\LiteDeploy\catalog.local.json";
            Assert.AreEqual(@"D:\LiteDeploy\catalogs", CatalogLocation.InitialFolder(@"D:\LiteDeploy\modules", p => true, local));
            Assert.AreEqual(@"C:\Users\Utilisateur1\AppData\Local\LiteDeploy", CatalogLocation.InitialFolder(@"D:\LiteDeploy\modules", p => false, local), "no catalogs folder");
            Assert.AreEqual(@"C:\Users\Utilisateur1\AppData\Local\LiteDeploy", CatalogLocation.InitialFolder("", p => true, local), "no shared modules folder");
            List<String> asked = new List<String>();
            CatalogLocation.InitialFolder(@"D:\LiteDeploy\modules", p => { asked.Add(p); return false; }, local);
            CollectionAssert.AreEqual(new[] { @"D:\LiteDeploy\catalogs" }, asked);
        }

        // ---- Web versions: rows and empty states ----------------------------------------------

        [TestMethod]
        public void WebVersions_NoticeOf()
        {
            Assert.AreEqual(WebVersionNotice.NoCache | WebVersionNotice.NoInstances, WebVersionChecker.NoticeOf("", 0));
            Assert.AreEqual(WebVersionNotice.NoCache, WebVersionChecker.NoticeOf(null, 2));
            Assert.AreEqual(WebVersionNotice.NoInstances, WebVersionChecker.NoticeOf(@"D:\Cache", 0));
            Assert.AreEqual(WebVersionNotice.None, WebVersionChecker.NoticeOf(@"D:\Cache", 1));
        }

        [TestMethod]
        public void WebVersions_Rows_ListEveryInstance_BeforeTheFirstCheck()
        {
            List<WebVersionTarget> targets = new List<WebVersionTarget>
            {
                new WebVersionTarget { Module = "CachedInstaller", Instance = "ExampleApp", Product = "Contoso Example App", WingetId = "Contoso.ExampleApp", Type = "msi" },
                new WebVersionTarget { Module = "CachedInstaller", Instance = "OtherApp", Product = "Other App" },
                new WebVersionTarget { Module = "MsixProvision", Instance = "StoreApp", Product = "Store App", WingetId = "Contoso.StoreApp", Type = "msix" }
            };
            List<WebVersionEntry> saved = new List<WebVersionEntry>
            {
                new WebVersionEntry { Key = "msixprovision/storeapp", Module = "MsixProvision", Instance = "StoreApp", Status = WebVersionStatus.UpToDate, WebVersion = "1.2" }
            };

            List<WebVersionEntry> rows = WebVersionChecker.Rows(saved, targets);
            Assert.AreEqual(3, rows.Count);
            Assert.AreSame(saved[0], rows.Single(r => r.Instance == "StoreApp"), "a saved result is kept (keys case-insensitive)");
            Assert.AreEqual(WebVersionStatus.NotChecked, rows.Single(r => r.Instance == "ExampleApp").Status);
            WebVersionEntry noId = rows.Single(r => r.Instance == "OtherApp");
            Assert.AreEqual(WebVersionStatus.NoWingetId, noId.Status, "listed even without a WingetId");
            Assert.IsFalse(String.IsNullOrEmpty(noId.Message), "tells how to set it");
            Assert.IsFalse(noId.CanDownload);

            Assert.AreEqual(0, WebVersionChecker.Rows(null, null).Count);
        }

        [TestMethod]
        public void CatalogImport_AddMissing_AddsOnlyNewIds()
        {
            List<UpdatePackage> list = new List<UpdatePackage> { UpdatePackage.Parse("module:CachedInstaller/ExampleApp | | Contoso Example App") };
            int added = CatalogImport.AddMissing(list, new[]
            {
                UpdatePackage.Parse("module:cachedinstaller/exampleapp | | Contoso Example App"),
                UpdatePackage.Parse("module:CachedInstaller/OtherApp | | Other App"),
                null
            });
            Assert.AreEqual(1, added);
            CollectionAssert.AreEqual(new[] { "module:CachedInstaller/ExampleApp", "module:CachedInstaller/OtherApp" }, list.Select(p => p.Id).ToList());
        }
    }
}
