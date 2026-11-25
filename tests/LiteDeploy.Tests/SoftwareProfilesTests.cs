// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    SoftwareProfilesTests.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-19
// Purpose: Unit tests of the profiles of the software catalog (filter, a
//          missing profile gives nothing, names, file round trip) and of the
//          export / import of the catalog (JSON round trip, an import only
//          adds). Fictitious package Ids only.
// -----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LiteDeploy.Tests
{
    [TestClass]
    public class SoftwareProfilesTests
    {
        static List<UpdatePackage> Catalog()
        {
            return new[] { "Example.Editor | editor | Example Editor", "Example.Reader | reader | Example Reader", "Example.Runtime | | Example Runtime" }
                .Select(UpdatePackage.Parse).ToList();
        }

        static SoftwareProfiles Profiles()
        {
            SoftwareProfiles p = new SoftwareProfiles();
            p.Items.Add(new SoftwareProfile { Name = "Base", Packages = { "example.reader", "Example.Editor", "Gone.Package" } });
            return p;
        }

        [TestMethod]
        public void Filter_ProfileAllAndMissing()
        {
            bool found;
            List<UpdatePackage> basePackages = Profiles().Filter(Catalog(), "base", out found);
            Assert.IsTrue(found);
            CollectionAssert.AreEqual(new[] { "Example.Editor", "Example.Reader" }, basePackages.Select(p => p.Id).ToList(), "order of the catalog, case-insensitive, gone ids left out");

            Assert.AreEqual(3, Profiles().Filter(Catalog(), "", out found).Count);
            Assert.IsTrue(found, "empty: the whole catalog");

            Assert.AreEqual(0, Profiles().Filter(Catalog(), "Removed", out found).Count, "a missing profile never gives the whole catalog");
            Assert.IsFalse(found);
        }

        [TestMethod]
        public void Names_AndKeepOnly()
        {
            Assert.IsTrue(SoftwareProfiles.IsValidName("Base (all computers)"));
            Assert.IsTrue(SoftwareProfiles.IsValidName("Java-8"));
            Assert.IsFalse(SoftwareProfiles.IsValidName(""));
            Assert.IsFalse(SoftwareProfiles.IsValidName("a/b"));
            Assert.IsFalse(SoftwareProfiles.IsValidName(new String('a', 41)));

            SoftwareProfiles p = Profiles();
            p.KeepOnly(Catalog());
            CollectionAssert.AreEqual(new[] { "example.reader", "Example.Editor" }, p.Items[0].Packages);
        }

        [TestMethod]
        public void File_RoundTrip_AndMissingFile()
        {
            String path = Path.Combine(Path.GetTempPath(), "LiteDeploy-profiles-" + Guid.NewGuid().ToString("N") + ".xml");
            try
            {
                Assert.AreEqual(0, SoftwareProfiles.Load(path).Items.Count);
                Profiles().Save(path);
                SoftwareProfiles back = SoftwareProfiles.Load(path);
                Assert.AreEqual("Base", back.Items.Single().Name);
                Assert.AreEqual(3, back.Items[0].Packages.Count);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [TestMethod]
        public void CatalogFile_JsonRoundTrip()
        {
            SoftwareProfiles profiles = Profiles();
            profiles.KeepOnly(Catalog());
            String json = SoftwareCatalogFile.From(Catalog(), profiles).ToJson();
            StringAssert.Contains(json, SoftwareCatalogFile.FormatName);

            List<UpdatePackage> target = new List<UpdatePackage>();
            SoftwareProfiles targetProfiles = new SoftwareProfiles();
            SoftwareCatalogFile.MergeResult r = SoftwareCatalogFile.Parse(json).MergeInto(target, targetProfiles);
            Assert.AreEqual(3, r.PackagesAdded);
            Assert.AreEqual(1, r.ProfilesAdded);
            CollectionAssert.AreEqual(new[] { "editor" }, target.Single(p => p.Id == "Example.Editor").Processes);
            Assert.AreEqual("Example Reader", target.Single(p => p.Id == "Example.Reader").Label);
            Assert.AreEqual(2, targetProfiles.Find("Base").Packages.Count);
        }

        [TestMethod]
        public void CatalogFile_ImportOnlyAdds()
        {
            List<UpdatePackage> catalog = new List<UpdatePackage> { UpdatePackage.Parse("Example.Editor | other | Kept Label") };
            SoftwareProfiles profiles = new SoftwareProfiles();
            profiles.Items.Add(new SoftwareProfile { Name = "Base", Packages = { "Example.Editor" } });
            SoftwareCatalogFile file = new SoftwareCatalogFile();
            file.Packages.Add(new SoftwareCatalogFile.CatalogPackage { Id = "Example.Editor", Label = "New Label" });
            file.Packages.Add(new SoftwareCatalogFile.CatalogPackage { Id = "Example.Reader" });
            file.Packages.Add(new SoftwareCatalogFile.CatalogPackage { Id = "bad id!" });
            file.Profiles.Add(new SoftwareCatalogFile.CatalogProfile { Name = "base", Packages = { "Example.Reader", "Unknown.Package" } });
            file.Profiles.Add(new SoftwareCatalogFile.CatalogProfile { Name = "bad/name" });

            SoftwareCatalogFile.MergeResult r = file.MergeInto(catalog, profiles);
            Assert.AreEqual(1, r.PackagesAdded);
            Assert.AreEqual("Kept Label", catalog[0].Label, "an existing package is not changed");
            Assert.AreEqual(0, r.ProfilesAdded, "same name, any case: merged");
            CollectionAssert.AreEqual(new[] { "Example.Editor", "Example.Reader" }, profiles.Find("Base").Packages, "unknown packages left out");
            CollectionAssert.AreEquivalent(new[] { "bad id!", "bad/name" }, r.Invalid);
        }

        [TestMethod]
        public void CatalogFile_NotACatalog()
        {
            Assert.ThrowsException<InvalidDataException>(() => SoftwareCatalogFile.Parse("{\"module\":\"CachedInstaller\"}"));
            Assert.ThrowsException<InvalidDataException>(() => SoftwareCatalogFile.Parse("not json"));
        }

        [TestMethod]
        public void Schedule_ProfileDefaultsToTheWholeCatalog()
        {
            Assert.AreEqual("", new UpdateSchedule().Profile);
            Assert.AreEqual("", new UserSettings().UpdatesProfile);
        }

        [TestMethod]
        public void ProfileTip_ListsTheSoftwareSorted()
        {
            List<UpdatePackage> software = new List<UpdatePackage>
            {
                UpdatePackage.Parse("Mozilla.Firefox|firefox|Firefox"),
                UpdatePackage.Parse("7zip.7zip||"),
                UpdatePackage.Parse("module:CachedInstaller/ExampleApp||Example App")
            };
            String tip = ComputerGrid.ProfileTip("Intro", "Office", true, software);
            StringAssert.StartsWith(tip, "Intro");
            StringAssert.Contains(tip, "Office: 3 software");
            int a = tip.IndexOf("  - 7zip.7zip"), b = tip.IndexOf("  - Example App"), c = tip.IndexOf("  - Firefox");
            Assert.IsTrue(a > 0 && a < b && b < c, "labels (else the Id), sorted: " + tip);

            StringAssert.Contains(ComputerGrid.ProfileTip("Intro", "Empty", true, new List<UpdatePackage>()), "Empty: no software.");
            StringAssert.Contains(ComputerGrid.ProfileTip("Intro", "Gone", false, null), "The profile Gone no longer exists");

            List<UpdatePackage> many = Enumerable.Range(1, ComputerGrid.ProfileTipMaxLines + 5)
                .Select(i => UpdatePackage.Parse("Vendor.App" + i.ToString("D3") + "||")).ToList();
            String longTip = ComputerGrid.ProfileTip("Intro", "All", true, many);
            Assert.AreEqual(ComputerGrid.ProfileTipMaxLines, longTip.Split('\n').Count(l => l.StartsWith("  - ")));
            StringAssert.Contains(longTip, "... and 5 more");
        }
    }
}
