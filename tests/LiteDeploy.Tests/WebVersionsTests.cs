// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    WebVersionsTests.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-18
// Purpose: Unit tests of Tools > Web versions...: reading of "winget show"
//          (English, French and unknown labels, fictitious package), version
//          comparison, targets and WingetId from the settings and a catalog,
//          winget arguments, daily schedule, MSIX manifest, file naming, the
//          copy into the cache and the OLD rotation (temporary folder),
//          signature rules and the settings and results files.
// -----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Serialization;
using LiteSuite;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LiteDeploy.Tests
{
    [TestClass]
    public class WebVersionsTests
    {
        const string Sha = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
        const string Url = "https://example.com/downloads/ExampleApp-2.5.0-x64.msi";

        const string English =
            "   - \r\n" +
            "Found Contoso Example App [Contoso.ExampleApp]\r\n" +
            "Version: 2.5.0\r\n" +
            "Publisher: Contoso Ltd\r\n" +
            "Publisher Url: https://example.com/\r\n" +
            "Publisher Support Url: https://example.com/support\r\n" +
            "Author: Contoso Ltd\r\n" +
            "Moniker: exampleapp\r\n" +
            "Description: An example application.\r\n" +
            "Homepage: https://example.com/exampleapp\r\n" +
            "License: Proprietary\r\n" +
            "License Url: https://example.com/license\r\n" +
            "Release Notes:\r\n" +
            "  - Fixes.\r\n" +
            "Release Notes Url: https://example.com/exampleapp/releases\r\n" +
            "Documentation:\r\n" +
            "  FAQ: https://example.com/faq\r\n" +
            "Tags:\r\n" +
            "  example\r\n" +
            "Installer:\r\n" +
            "  Installer Type: wix\r\n" +
            "  Installer Url: " + Url + "\r\n" +
            "  Installer SHA256: " + Sha + "\r\n" +
            "  Release Date: 2026-11-01\r\n" +
            "  Offline Distribution Supported: true\r\n";

        // Layout of a French winget: space before the colon, typographic apostrophes, a label without colon
        static readonly string French =
            "Trouvé Contoso Example App [Contoso.ExampleApp]\r\n" +
            "Version : 2.5.0\r\n" +
            "Publisher : Contoso Ltd\r\n" +
            "ID de l\u2019Editeur https://example.com/\r\n" +
            "URL de support de l\u2019éditeur : https://example.com/support\r\n" +
            "Moniker : exampleapp\r\n" +
            "Description : An example application.\r\n" +
            "Page d\u2019accueil : https://example.com/exampleapp\r\n" +
            "Licence : Proprietary\r\n" +
            "Notes de publication :\r\n" +
            "  - Fixes.\r\n" +
            "Documentation :\r\n" +
            "  FAQ: https://example.com/faq\r\n" +
            "Mots-clés :\r\n" +
            "  example\r\n" +
            "Programme d'installation :\r\n" +
            "  Type du programme d\u2019installation : wix\r\n" +
            "  URL du programme d\u2019installation : " + Url + "\r\n" +
            "  SHA256 du programme d\u2019installation : " + Sha.ToUpperInvariant() + "\r\n" +
            "  Date de version : 2026-11-01\r\n" +
            "  Distribution hors ligne prise en charge : true\r\n";

        // ---- winget show -------------------------------------------------------------

        static void AssertExample(WingetShowInfo info)
        {
            Assert.IsTrue(info.Found);
            Assert.AreEqual("Contoso.ExampleApp", info.Id);
            Assert.AreEqual("2.5.0", info.Version);
            Assert.AreEqual("wix", info.InstallerType);
            Assert.AreEqual(Url, info.InstallerUrl);
            Assert.AreEqual(Sha, info.InstallerSha256, "lower case");
            Assert.AreEqual("https://example.com/exampleapp", info.VendorUrl, "the home page");
        }

        [TestMethod]
        public void FilePattern_NewestMatchingInstaller()
        {
            Assert.IsTrue(WebVersionChecker.IsPattern("Firefox/Firefox Setup *.msi"));
            Assert.IsTrue(WebVersionChecker.IsPattern(@"Vendor\app-?.msi"));
            Assert.IsFalse(WebVersionChecker.IsPattern("Firefox/Firefox Setup 161.0.msi"));
            Assert.IsFalse(WebVersionChecker.IsPattern(null));
            Assert.IsTrue(WebVersionChecker.MatchesPattern("Firefox Setup *.msi", "firefox setup 162.0.msi"), "case-insensitive");
            Assert.IsFalse(WebVersionChecker.MatchesPattern("Firefox Setup *.msi", "Firefox Setup 162.0.msix"), "whole name: .msix is not .msi");
            Assert.IsFalse(WebVersionChecker.MatchesPattern("app-?.msi", "app-10.msi"));

            Dictionary<String, String> versions = new Dictionary<String, String>
            {
                { @"C:\cache\Vendor\app-1.9.msi", "1.9.0" },
                { @"C:\cache\Vendor\app-1.10.msi", "1.10.0" },
                { @"C:\cache\Vendor\app-unknown.msi", null },
                { @"C:\cache\Vendor\other-9.0.msi", "9.0" },
                { @"C:\cache\Vendor\app-1.10-copy.msi", "1.10.0" }
            };
            Dictionary<String, DateTime> times = versions.Keys.ToDictionary(k => k, k => new DateTime(2026, 1, 1));
            times[@"C:\cache\Vendor\app-1.10-copy.msi"] = new DateTime(2026, 2, 1);
            Assert.AreEqual(@"C:\cache\Vendor\app-1.10-copy.msi",
                WebVersionChecker.NewestMatch(versions.Keys, "app-*.msi", f => versions[f], f => times[f]), "1.10 over 1.9, the most recent for the same version");
            Assert.AreEqual(@"C:\cache\Vendor\app-1.10.msi",
                WebVersionChecker.NewestMatch(versions.Keys.Take(3), "app-*.msi", f => versions[f], f => times[f]), "an unknown version ranks lowest");
            Assert.IsNull(WebVersionChecker.NewestMatch(versions.Keys, "none-*.msi", f => versions[f], f => times[f]));
        }

        [TestMethod]
        public void WingetShow_English()
        {
            AssertExample(WingetShowParser.Parse(English));
        }

        [TestMethod]
        public void WingetShow_French()
        {
            AssertExample(WingetShowParser.Parse(French));
        }

        [TestMethod]
        public void WingetShow_UnknownLanguage_UsesTheLayout()
        {
            string other =
                "Gefunden Contoso Example App [Contoso.ExampleApp]\n" +
                "Ausgabe: 2.5.0\n" +
                "Herausgeber-URL: https://example.com/\n" +
                "Schlagworte:\n" +
                "  example\n" +
                "Installationsprogramm:\n" +
                "  Typ: msi\n" +
                "  Adresse: " + Url + "\n" +
                "  Hash: " + Sha + "\n";
            WingetShowInfo info = WingetShowParser.Parse(other);
            Assert.AreEqual("2.5.0", info.Version, "first field after the header");
            Assert.AreEqual("msi", info.InstallerType);
            Assert.AreEqual(Url, info.InstallerUrl);
            Assert.AreEqual(Sha, info.InstallerSha256);
            Assert.AreEqual("https://example.com/", info.VendorUrl, "first web address of the top-level lines");
        }

        [TestMethod]
        public void WingetShow_NoApplicableInstaller()
        {
            string output = French.Substring(0, French.IndexOf("Programme d'installation", StringComparison.Ordinal)) +
                "Programme d'installation :\r\n" +
                "  Aucun programme d\u2019installation applicable n\u2019a été trouvé. Pour plus d\u2019informations, consultez les journaux.\r\n";
            WingetShowInfo info = WingetShowParser.Parse(output);
            Assert.IsTrue(info.Found);
            Assert.AreEqual("2.5.0", info.Version);
            Assert.IsNull(info.InstallerUrl);
            Assert.IsNull(info.InstallerSha256);
        }

        [TestMethod]
        public void WingetShow_NotFound()
        {
            Assert.IsFalse(WingetShowParser.Parse("No package found matching input criteria.\r\n").Found);
            Assert.IsFalse(WingetShowParser.Parse("Aucun package ne correspond aux critères d\u2019entrée.\r\n").Found);
            Assert.IsFalse(WingetShowParser.Parse(null).Found);
        }

        [TestMethod]
        public void ShowArguments_AndInstallerTypes()
        {
            Assert.AreEqual("show --id Contoso.ExampleApp -e --source winget --architecture x64 --installer-type wix --accept-source-agreements --disable-interactivity",
                WebVersionChecker.ShowArguments("Contoso.ExampleApp", "wix"));
            foreach (string bad in new[] { null, "", "a b", "x\" --override y", "module:CachedInstaller" })
                Assert.ThrowsException<ArgumentException>(() => WebVersionChecker.ShowArguments(bad, "msi"), bad ?? "(null)");
            CollectionAssert.AreEqual(new[] { "msi", "wix" }, WebVersionChecker.InstallerTypesOf("MSI"));
            CollectionAssert.AreEqual(new[] { "msix" }, WebVersionChecker.InstallerTypesOf("msix"));
            Assert.AreEqual("exe", WebVersionChecker.InstallerTypesOf("exe")[0]);
            Assert.AreEqual(0, WebVersionChecker.InstallerTypesOf(null).Length);
        }

        // ---- Versions ------------------------------------------------------------------

        [TestMethod]
        public void CompareVersions_PartsAndTrailingZeros()
        {
            Assert.AreEqual(0, WebVersionChecker.CompareVersions("26.06", "26.6.0.0"));
            Assert.AreEqual(0, WebVersionChecker.CompareVersions("2.4", "2.4.0"));
            Assert.AreEqual(0, WebVersionChecker.CompareVersions("24.08.00000", "24.8"));
            Assert.IsTrue(WebVersionChecker.CompareVersions("2.4.1", "2.4") > 0);
            Assert.IsTrue(WebVersionChecker.CompareVersions("1.10", "1.9.9.9") > 0, "numbers, not text");
            Assert.IsTrue(WebVersionChecker.CompareVersions("3.2.0.15", "3.2.1") < 0);
            Assert.AreEqual(0, WebVersionChecker.CompareVersions("v2.4 build", "2.4.0.0"));
            Assert.IsNull(WebVersionChecker.CompareVersions("latest", "2.4"));
            Assert.IsNull(WebVersionChecker.CompareVersions(null, "2.4"));
        }

        [TestMethod]
        public void CompareVersions_BuildInParentheses()
        {
            Assert.AreEqual(0, WebVersionChecker.CompareVersions("6.1.5 (12345)", "6.1.5.12345"));
            Assert.IsTrue(WebVersionChecker.CompareVersions("6.1.5 (12346)", "6.1.5.12345") > 0);
            Assert.IsTrue(WebVersionChecker.CompareVersions("6.1.5.12345", "6.1.5 (12346)") < 0);
            Assert.AreEqual(0, WebVersionChecker.CompareVersions("6.1.x (12345)", "6.1.12345"));
            Assert.IsTrue(WebVersionChecker.CompareVersions("6.2.x (100)", "6.1.12345") > 0);
            Assert.AreEqual(0, WebVersionChecker.CompareVersions("6.1.5 (12345)", "6.1.5 (build 12345)"));
            long? build;
            CollectionAssert.AreEqual(new long[] { 6, 1 }, WebVersionChecker.VersionParts("6.1.x (12345)", out build));
            Assert.AreEqual(12345L, build);
        }

        [TestMethod]
        public void StatusOf_CacheAgainstWeb()
        {
            Assert.AreEqual(WebVersionStatus.UpToDate, WebVersionChecker.StatusOf("2.5.0.0", "2.5"));
            Assert.AreEqual(WebVersionStatus.UpToDate, WebVersionChecker.StatusOf("2.6", "2.5"), "the cache may be ahead");
            Assert.AreEqual(WebVersionStatus.NewerOnWeb, WebVersionChecker.StatusOf("2.4.1", "2.5.0"));
            Assert.AreEqual(WebVersionStatus.NotFound, WebVersionChecker.StatusOf("2.4.1", null));
            Assert.AreEqual(WebVersionStatus.Error, WebVersionChecker.StatusOf(null, "2.5"));
            Assert.AreEqual(WebVersionStatus.Error, WebVersionChecker.StatusOf("beta", "stable"));
            Assert.AreEqual(WebVersionStatus.UpToDate, WebVersionChecker.StatusOf("stable", "Stable"));
        }

        // ---- Targets, cache paths, schedule ------------------------------------------------

        [TestMethod]
        public void Targets_InstancesOfTheTwoModules()
        {
            ModuleSettings s = new ModuleSettings();
            string app = ModuleSettings.Key("CachedInstaller", "ContosoExampleApp");
            s.SetValue(app, "Product", "Contoso Example App");
            s.SetValue(app, "File", "CachedInstaller/ExampleVendor/ContosoExampleApp-2.4.1.msi");
            s.SetValue(app, "Publisher", "ExampleVendor Inc.");
            s.SetValue(app, "WingetId", "Contoso.ExampleApp");
            string tool = ModuleSettings.Key("CachedInstaller", "ExampleTool");
            s.SetValue(tool, "File", "CachedInstaller/ExampleVendor/ExampleTool-setup.bin");
            s.SetValue(tool, "Type", "EXE");
            s.SetValue(tool, "Version", "3.2");
            string msix = ModuleSettings.Key("MsixProvision", "ExampleApp");
            s.SetValue(msix, "PackageName", "Contoso.ExampleApp");
            s.SetValue(msix, "Folder", "ExampleApp");
            s.SetValue(msix, "Version", "ignored");
            s.SetValue("CachedInstaller", "File", "module-level.msi");                   // not an instance
            s.SetValue(ModuleSettings.Key("OtherModule", "X"), "WingetId", "Contoso.Other"); // another module

            List<WebVersionTarget> t = WebVersionChecker.Targets(s).OrderBy(x => x.Key).ToList();
            CollectionAssert.AreEqual(new[] { "CachedInstaller/ContosoExampleApp", "CachedInstaller/ExampleTool", "MsixProvision/ExampleApp" }, t.Select(x => x.Key).ToArray());
            Assert.AreEqual("Contoso.ExampleApp", t[0].WingetId);
            Assert.AreEqual("msi", t[0].Type, "from the extension");
            Assert.AreEqual("Contoso Example App", t[0].Product);
            Assert.IsNull(t[1].WingetId, "status No WingetId");
            Assert.AreEqual("exe", t[1].Type);
            Assert.AreEqual("3.2", t[1].Version);
            Assert.AreEqual("ExampleTool", t[1].Product, "the instance name");
            Assert.IsTrue(t[2].IsMsix);
            Assert.AreEqual("msix", t[2].Type);
            Assert.AreEqual("Contoso.ExampleApp", t[2].Product, "the package name");
            Assert.IsNull(t[2].Version, "no Version parameter for an MSIX");
        }

        [TestMethod]
        public void ResolveUnderCache_StaysInTheCache()
        {
            string root = @"C:\Cache";
            Assert.AreEqual(@"C:\Cache\CachedInstaller\Vendor\app.msi", WebVersionChecker.ResolveUnderCache(root, "CachedInstaller/Vendor/app.msi"));
            Assert.AreEqual(@"C:\Cache\ExampleApp", WebVersionChecker.ResolveUnderCache(root + @"\", @"\ExampleApp"));
            foreach (string bad in new[] { null, "", "../outside.msi", @"a\..\..\b.msi", @"C:\other\app.msi", "D:app.msi" })
                Assert.IsNull(WebVersionChecker.ResolveUnderCache(root, bad), bad ?? "null");
            Assert.IsNull(WebVersionChecker.ResolveUnderCache(null, "a.msi"));
        }

        [TestMethod]
        public void DailyCheck_OncePerDayAtTheTime()
        {
            DateTime started = new DateTime(2026, 11, 18, 5, 0, 0);
            Assert.IsFalse(WebVersionChecker.IsDailyCheckDue("06:30", null, new DateTime(2026, 11, 18, 6, 0, 0), started));
            Assert.IsTrue(WebVersionChecker.IsDailyCheckDue("06:30", null, new DateTime(2026, 11, 18, 6, 31, 0), started));
            Assert.IsFalse(WebVersionChecker.IsDailyCheckDue("06:30", new DateTime(2026, 11, 18, 6, 31, 0), new DateTime(2026, 11, 18, 20, 0, 0), started), "done today");
            Assert.IsTrue(WebVersionChecker.IsDailyCheckDue("06:30", new DateTime(2026, 11, 18, 6, 31, 0), new DateTime(2026, 11, 19, 6, 30, 0), started), "next day");
            Assert.IsFalse(WebVersionChecker.IsDailyCheckDue("06:30", null, new DateTime(2026, 11, 18, 9, 0, 0), new DateTime(2026, 11, 18, 8, 0, 0)), "missed before LiteDeploy started");
            Assert.IsFalse(WebVersionChecker.IsDailyCheckDue("", null, new DateTime(2026, 11, 18, 9, 0, 0), started), "off");
            Assert.IsFalse(WebVersionChecker.IsDailyCheckDue("25:99", null, new DateTime(2026, 11, 18, 9, 0, 0), started));
            Assert.IsTrue(WebVersionsForm.IsValidTime("06:30"));
            Assert.IsFalse(WebVersionsForm.IsValidTime("6:30"));
            Assert.IsFalse(WebVersionsForm.IsValidTime("24:00"));
        }

        // ---- MSIX manifest -----------------------------------------------------------------

        [TestMethod]
        public void MsixManifest_PackageAndBundle()
        {
            string package = "<?xml version=\"1.0\" encoding=\"utf-8\"?><Package xmlns=\"http://schemas.microsoft.com/appx/manifest/foundation/windows10\">" +
                "<Identity Name=\"Contoso.ExampleApp\" Publisher=\"CN=Contoso Ltd, O=Contoso Ltd, C=US\" Version=\"2.1.0.0\" ProcessorArchitecture=\"x64\" /></Package>";
            MsixManifest m = MsixManifest.Parse(package, false);
            Assert.AreEqual("Contoso.ExampleApp", m.Name);
            Assert.AreEqual("2.1.0.0", m.Version);
            Assert.AreEqual("CN=Contoso Ltd, O=Contoso Ltd, C=US", m.Publisher);

            string bundle = "<Bundle xmlns=\"http://schemas.microsoft.com/appx/2013/bundle\"><Identity Name=\"Contoso.ExampleApp\" Publisher=\"CN=Contoso Ltd\" Version=\"2026.1.0.0\" />" +
                "<Packages><Package Type=\"application\" Version=\"2.1.0.0\" /><Package Type=\"application\" Version=\"2.2.0.0\" /><Package Type=\"resource\" Version=\"9.0.0.0\" /></Packages></Bundle>";
            Assert.AreEqual("2.2.0.0", MsixManifest.Parse(bundle, true).Version, "newest application package");
            Assert.ThrowsException<InvalidDataException>(() => MsixManifest.Parse("<Package />", false));
        }

        // ---- Signature, URL ---------------------------------------------------------------

        static AuthenticodeResult Signature(AuthenticodeStatus status, string subject)
        {
            return new AuthenticodeResult { Status = status, SignerSubject = subject, SignerThumbprint = "AB" };
        }

        [TestMethod]
        public void IsSignedBy_ValidAndExactOrganisation()
        {
            Assert.IsTrue(CacheDownloader.IsSignedBy(Signature(AuthenticodeStatus.Valid, "CN=ExampleVendor Inc., O=ExampleVendor Inc., C=US"), "ExampleVendor Inc."));
            Assert.IsTrue(CacheDownloader.IsSignedBy(Signature(AuthenticodeStatus.Valid, "CN=ExampleVendor, O=\"ExampleVendor, Inc.\", C=US"), "ExampleVendor, Inc."));
            Assert.IsFalse(CacheDownloader.IsSignedBy(Signature(AuthenticodeStatus.Valid, "CN=ExampleVendor Inc., O=ExampleVendor Inc. Fake, C=US"), "ExampleVendor Inc."));
            Assert.IsFalse(CacheDownloader.IsSignedBy(Signature(AuthenticodeStatus.HashMismatch, "O=ExampleVendor Inc."), "ExampleVendor Inc."));
            Assert.IsFalse(CacheDownloader.IsSignedBy(Signature(AuthenticodeStatus.NotSigned, null), "ExampleVendor Inc."));
            Assert.IsFalse(CacheDownloader.IsSignedBy(Signature(AuthenticodeStatus.Valid, "O=ExampleVendor Inc."), ""), "a publisher is required");
            Assert.IsTrue(CacheDownloader.SameDistinguishedName("CN=Contoso Ltd, O=Contoso Ltd, C=US", "CN=Contoso Ltd,O=Contoso Ltd,C=US"));
            Assert.IsFalse(CacheDownloader.SameDistinguishedName("CN=Contoso Ltd, O=Contoso Ltd, C=US", "CN=Other, O=Contoso Ltd, C=US"));
        }

        [TestMethod]
        public void IsHttps_Only()
        {
            Assert.IsTrue(CacheDownloader.IsHttps(Url));
            Assert.IsFalse(CacheDownloader.IsHttps("http://example.com/a.msi"));
            Assert.IsFalse(CacheDownloader.IsHttps("ftp://example.com/a.msi"));
            Assert.IsFalse(CacheDownloader.IsHttps("file:///C:/a.msi"));
            Assert.IsFalse(CacheDownloader.IsHttps(null));
            WebVersionEntry e = new WebVersionEntry { Status = WebVersionStatus.NewerOnWeb, InstallerUrl = Url, InstallerSha256 = Sha, CacheFile = @"C:\Cache\a.msi" };
            Assert.IsTrue(e.CanDownload);
            e.InstallerUrl = "http://example.com/a.msi";
            Assert.IsFalse(e.CanDownload, "HTTPS only");
            e.InstallerUrl = Url;
            e.Status = WebVersionStatus.UpToDate;
            Assert.IsFalse(e.CanDownload);
        }

        // ---- Naming and rotation ------------------------------------------------------------

        [TestMethod]
        public void VersionedFileName_ReplacesTheVersion()
        {
            Assert.AreEqual("ContosoExampleApp-2.5.0.msi", CacheDownloader.VersionedFileName("ContosoExampleApp-2.4.1.msi", new[] { "2.4.1" }, "2.5.0", ".msi"));
            Assert.AreEqual("Contoso.ExampleApp_2.2.0.0_x64.msixbundle",
                CacheDownloader.VersionedFileName("Contoso.ExampleApp_2.1.0.0_x64.msixbundle", new[] { "2.1.0.0" }, "2.2.0.0", ".msixbundle"));
            Assert.AreEqual("ExampleTool-3.3-setup.exe", CacheDownloader.VersionedFileName("ExampleTool-3.2-setup.exe", new[] { "3.2.0.15", null }, "3.3", null), "last dotted number");
            Assert.AreEqual("ExampleTool-setup-3.3.exe", CacheDownloader.VersionedFileName("ExampleTool-setup.exe", new string[0], "3.3", ".exe"), "no version: added");
            Assert.AreEqual("app-6.1.5_12345.msi", CacheDownloader.VersionedFileName("app.msi", null, "6.1.5 (12345)", ".msi"));
            Assert.AreEqual("app-2.0.1-2.0.1.msi", CacheDownloader.VersionedFileName("app-2.0.1.msi", new[] { "2.0.1" }, "2.0.1", ".msi"), "never the same name");
            Assert.AreEqual("Example2App-2.5.msi", CacheDownloader.VersionedFileName("Example2App-2.msi", new[] { "2" }, "2.5", ".msi"), "whole numbers only");
            Assert.AreEqual("new", CacheDownloader.SafeVersion(" ? "));
        }

        [TestMethod]
        public void NewFileParameter_KeepsTheFolder()
        {
            Assert.AreEqual("CachedInstaller/ExampleVendor/app-2.msi", CacheDownloader.NewFileParameter("CachedInstaller/ExampleVendor/app-1.msi", "app-2.msi"));
            Assert.AreEqual(@"CachedInstaller\app-2.msi", CacheDownloader.NewFileParameter(@"CachedInstaller\app-1.msi", "app-2.msi"));
            Assert.AreEqual("app-2.msix", CacheDownloader.NewFileParameter("app-1.msix", "app-2.msix"));
            Assert.AreEqual("app-2.msix", CacheDownloader.NewFileParameter(null, "app-2.msix"));
        }

        [TestMethod]
        public void OldDestination_NeverOverwrites()
        {
            string folder = @"C:\Cache\Vendor";
            DateTime now = new DateTime(2026, 11, 18, 14, 5, 9);
            Assert.AreEqual(@"C:\Cache\Vendor\OLD\app-1.msi", CacheDownloader.OldDestination(folder, "app-1.msi", p => false, now));
            Assert.AreEqual(@"C:\Cache\Vendor\OLD\app-1-20261118140509.msi", CacheDownloader.OldDestination(folder, "app-1.msi", p => true, now));
        }

        static string TempFolder()
        {
            string dir = Path.Combine(Path.GetTempPath(), "LiteDeploy-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        [TestMethod]
        public void CopyIntoCache_ThenMoveToOld_OnATemporaryFolder()
        {
            string root = TempFolder();
            try
            {
                string cache = Path.Combine(root, "cache", "Vendor");
                Directory.CreateDirectory(cache);
                string previous = Path.Combine(cache, "app-1.0.msi");
                string other = Path.Combine(cache, "other-1.0.msi");
                File.WriteAllText(previous, "old version");
                File.WriteAllText(other, "another product");
                string download = Path.Combine(root, "download.msi");
                File.WriteAllText(download, "new version");
                string sha = CacheDownloader.Sha256Of(download);
                List<string> log = new List<string>();
                CacheDownloader d = new CacheDownloader(log.Add);

                d.AssertWritable(cache);
                string placed = d.CopyIntoCache(download, sha, cache, "app-2.0.msi");
                Assert.AreEqual(Path.Combine(cache, "app-2.0.msi"), placed);
                Assert.AreEqual("new version", File.ReadAllText(placed));
                Assert.IsFalse(File.Exists(placed + ".partial"));
                Assert.AreEqual("old version", File.ReadAllText(previous), "the previous file is still there until the settings are saved");

                string warning;
                string moved = d.MoveToOld(previous, placed, out warning);
                Assert.IsNull(warning);
                Assert.AreEqual(Path.Combine(cache, "OLD", "app-1.0.msi"), moved);
                Assert.IsFalse(File.Exists(previous));
                Assert.AreEqual("old version", File.ReadAllText(moved));
                Assert.AreEqual("another product", File.ReadAllText(other), "files not used by the instance are not touched");
                Assert.IsTrue(log.Count >= 3, "every step is logged");

                // Same name again in OLD: kept, the new one gets a time stamp
                File.WriteAllText(previous, "old version again");
                string movedAgain = d.MoveToOld(previous, placed, out warning);
                Assert.AreNotEqual(moved, movedAgain);
                Assert.AreEqual(2, Directory.GetFiles(Path.Combine(cache, "OLD")).Length);

                // The new file itself is never moved
                Assert.IsNull(d.MoveToOld(placed, placed, out warning));
                Assert.IsTrue(File.Exists(placed));

                // Same name, same content: kept; same name, other content: refused, nothing changed
                Assert.AreEqual(placed, d.CopyIntoCache(download, sha, cache, "app-2.0.msi"));
                File.WriteAllText(download, "tampered");
                Assert.ThrowsException<IOException>(() => d.CopyIntoCache(download, CacheDownloader.Sha256Of(download), cache, "app-2.0.msi"));
                Assert.AreEqual("new version", File.ReadAllText(placed));

                // A copy whose SHA256 is not the expected one is deleted
                Assert.ThrowsException<IOException>(() => d.CopyIntoCache(download, sha, cache, "app-3.0.msi"));
                Assert.IsFalse(File.Exists(Path.Combine(cache, "app-3.0.msi")));
                Assert.IsFalse(File.Exists(Path.Combine(cache, "app-3.0.msi.partial")));
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        // ---- Files ---------------------------------------------------------------------------

        [TestMethod]
        public void Results_RoundTrip()
        {
            string root = TempFolder();
            try
            {
                string path = Path.Combine(root, "web-versions.xml");
                WebVersionResults r = new WebVersionResults { LastCheck = new DateTime(2026, 11, 18, 6, 30, 0) };
                r.Set(new WebVersionEntry { Key = "CachedInstaller/ContosoExampleApp", Module = "CachedInstaller", Instance = "ContosoExampleApp", WingetId = "Contoso.ExampleApp",
                    CacheVersion = "2.4.1", WebVersion = "2.5.0", Status = WebVersionStatus.NewerOnWeb, InstallerUrl = Url, InstallerSha256 = Sha });
                r.Set(new WebVersionEntry { Key = "cachedinstaller/contosoexampleapp", Status = WebVersionStatus.UpToDate });
                Assert.AreEqual(1, r.Entries.Count, "same key: replaced");
                r.Entries[0] = new WebVersionEntry { Key = "CachedInstaller/ContosoExampleApp", WebVersion = "2.5.0", Status = WebVersionStatus.NewerOnWeb, InstallerUrl = Url };
                r.Save(path);
                r.Save(path);   // replaces the file
                WebVersionResults back = WebVersionResults.Load(path);
                Assert.AreEqual(r.LastCheck, back.LastCheck);
                Assert.AreEqual(WebVersionStatus.NewerOnWeb, back.Entries.Single().Status);
                Assert.AreEqual(Url, back.Entries[0].InstallerUrl);
                Assert.AreEqual(0, WebVersionResults.Load(Path.Combine(root, "missing.xml")).Entries.Count);
                File.WriteAllText(path, "not xml");
                Assert.AreEqual(0, WebVersionResults.Load(path).Entries.Count, "unreadable: empty");
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [TestMethod]
        public void UserSettings_WebCheck_RoundTrip()
        {
            UserSettings s = new UserSettings { WebCheckTime = "06:30", WebAutoDownload = true, WebVersionsWindowSize = "1000,600" };
            XmlSerializer xs = new XmlSerializer(typeof(UserSettings));
            StringBuilder sb = new StringBuilder();
            using (StringWriter w = new StringWriter(sb)) xs.Serialize(w, s);
            UserSettings c = (UserSettings)xs.Deserialize(new StringReader(sb.ToString()));
            Assert.AreEqual("06:30", c.WebCheckTime);
            Assert.IsTrue(c.WebAutoDownload);
            Assert.AreEqual("1000,600", c.WebVersionsWindowSize);
            UserSettings d = new UserSettings();
            Assert.AreEqual("", d.WebCheckTime, "no daily check by default");
            Assert.IsFalse(d.WebAutoDownload, "report only by default");
        }

        [TestMethod]
        public void ModuleSettings_WingetId_RoundTrip()
        {
            ModuleSettings s = new ModuleSettings();
            string key = ModuleSettings.Key("MsixProvision", "ExampleApp");
            s.SetValue(key, "WingetId", "Contoso.ExampleApp");
            s.SetValue(key, "File", "Contoso.ExampleApp_2.2.0.0_x64.msixbundle");
            ModuleSettings back = ModuleSettings.Parse(s.ToXml());
            Assert.AreEqual("Contoso.ExampleApp", back.Get(key, "WingetId").Value);
            Assert.AreEqual("Contoso.ExampleApp", WebVersionChecker.Targets(back).Single().WingetId);
        }

        [TestMethod]
        public void Catalog_WithWingetId()
        {
            string dir = AppDomain.CurrentDomain.BaseDirectory;
            while (dir != null && !File.Exists(Path.Combine(dir, "LiteDeploy.sln"))) dir = Path.GetDirectoryName(dir);
            Assert.IsNotNull(dir);
            UpdateModule module = UpdateModules.Read(Path.Combine(dir, "modules", "CachedInstaller.ps1"));
            ModuleParameter winget = module.Parameters.Single(p => p.Name == "WingetId");
            Assert.IsFalse(winget.Required, "optional");
            Assert.AreEqual("", winget.Default);

            ModuleCatalog catalog = ModuleCatalog.Parse(@"{ ""products"": [ {
                ""Instance"": ""ContosoExampleApp"", ""File"": ""CachedInstaller/ExampleVendor/ContosoExampleApp-2.4.1.msi"",
                ""DetectName"": ""Contoso Example App*"", ""Publisher"": ""ExampleVendor Inc."", ""WingetId"": ""Contoso.ExampleApp"" } ] }");
            Assert.AreEqual(0, catalog.Errors.Count, String.Join(" | ", catalog.Errors));
            ModuleSettings settings = new ModuleSettings();
            List<string> warnings = new List<string>();
            ModuleCatalog.Apply(catalog.Entries[0], module, settings, warnings);
            Assert.AreEqual(0, warnings.Count, String.Join(" | ", warnings));
            WebVersionTarget t = WebVersionChecker.Targets(settings).Single();
            Assert.AreEqual("Contoso.ExampleApp", t.WingetId);
            Assert.AreEqual("ExampleVendor Inc.", t.Publisher);
        }
    }
}
