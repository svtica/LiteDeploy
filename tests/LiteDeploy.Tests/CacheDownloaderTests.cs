// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    CacheDownloaderTests.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-18
// Purpose: Unit tests of Download to the cache (Tools > Web versions...):
//          several organisations in the Publisher parameter and the message
//          of a refused signature, zip installers ("winget show" in English
//          and French, choice of the installer, the installer a zip holds,
//          extraction from a temporary zip with path and size checks) and the
//          summary shown after the downloads. Fictitious package only.
// -----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using LiteSuite;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LiteDeploy.Tests
{
    [TestClass]
    public class CacheDownloaderTests
    {
        const string Sha = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
        const string ZipUrl = "https://example.com/downloads/ExampleApp-2.5.0-x64.zip";

        static AuthenticodeResult Signature(AuthenticodeStatus status, string subject)
        {
            return new AuthenticodeResult { Status = status, SignerSubject = subject, SignerThumbprint = "AB" };
        }

        // ---- Publisher: several organisations ---------------------------------------------

        [TestMethod]
        public void PublisherNames_SplitOnSemicolon()
        {
            CollectionAssert.AreEqual(new[] { "Contoso Ltd" }, CacheDownloader.PublisherNames("Contoso Ltd"));
            CollectionAssert.AreEqual(new[] { "Contoso Ltd", "Fabrikam Inc." }, CacheDownloader.PublisherNames(" Contoso Ltd ; Fabrikam Inc. ;; "));
            CollectionAssert.AreEqual(new[] { "Contoso, Ltd", "Fabrikam Inc." }, CacheDownloader.PublisherNames("Contoso, Ltd;Fabrikam Inc."), "a comma stays in the name");
            CollectionAssert.AreEqual(new[] { "Contoso; Labs", "Fabrikam" }, CacheDownloader.PublisherNames("\"Contoso; Labs\"; Fabrikam"), "quoted: the ; is kept");
            CollectionAssert.AreEqual(new[] { "Contoso, Ltd" }, CacheDownloader.PublisherNames("\"Contoso, Ltd\""), "quotes removed");
            CollectionAssert.AreEqual(new[] { "Contoso Ltd" }, CacheDownloader.PublisherNames("Contoso Ltd;Contoso Ltd"), "no duplicate");
            Assert.AreEqual(0, CacheDownloader.PublisherNames(null).Count);
            Assert.AreEqual(0, CacheDownloader.PublisherNames(" ; ").Count);
        }

        [TestMethod]
        public void IsSignedBy_AnyOfSeveralOrganisations()
        {
            string renamed = "CN=Fabrikam Inc., O=Fabrikam Inc., L=Example City, C=US";
            Assert.IsFalse(CacheDownloader.IsSignedBy(Signature(AuthenticodeStatus.Valid, renamed), "Contoso Ltd"), "old name only");
            Assert.IsTrue(CacheDownloader.IsSignedBy(Signature(AuthenticodeStatus.Valid, renamed), "Contoso Ltd; Fabrikam Inc."), "new name added");
            Assert.IsTrue(CacheDownloader.IsSignedBy(Signature(AuthenticodeStatus.Valid, "CN=Contoso Ltd, O=Contoso Ltd, C=US"), "Contoso Ltd; Fabrikam Inc."), "old name still accepted");
            Assert.IsTrue(CacheDownloader.IsSignedBy(Signature(AuthenticodeStatus.Valid, "CN=Fabrikam, O=\"Fabrikam, Inc.\", C=US"), "Contoso Ltd;\"Fabrikam, Inc.\""), "quoted with a comma");
            Assert.IsTrue(CacheDownloader.IsSignedBy(Signature(AuthenticodeStatus.Valid, "CN=Fabrikam, O=\"Fabrikam, Inc.\", C=US"), "Contoso Ltd;Fabrikam, Inc."), "comma, not quoted");
            Assert.IsFalse(CacheDownloader.IsSignedBy(Signature(AuthenticodeStatus.Valid, "CN=Fabrikam Inc. Fake, O=Fabrikam Inc. Fake, C=US"), "Contoso Ltd; Fabrikam Inc."), "exact name only");
            Assert.IsFalse(CacheDownloader.IsSignedBy(Signature(AuthenticodeStatus.Valid, "CN=Fabrikam Inc., C=US"), "Fabrikam Inc."), "the CN is not the organisation");
            Assert.IsFalse(CacheDownloader.IsSignedBy(Signature(AuthenticodeStatus.Invalid, renamed), "Contoso Ltd; Fabrikam Inc."), "a valid signature is required");
            Assert.IsFalse(CacheDownloader.IsSignedBy(Signature(AuthenticodeStatus.Valid, renamed), " ; "), "a publisher is required");
        }

        [TestMethod]
        public void OrganisationOf_Subject()
        {
            Assert.AreEqual("Fabrikam Inc.", CacheDownloader.OrganisationOf("CN=Fabrikam Inc., O=Fabrikam Inc., C=US"));
            Assert.AreEqual("Fabrikam, Inc.", CacheDownloader.OrganisationOf("CN=Fabrikam, O=\"Fabrikam, Inc.\", C=US"));
            Assert.AreEqual("Contoso Ltd", CacheDownloader.OrganisationOf("O=Contoso Ltd"));
            Assert.IsNull(CacheDownloader.OrganisationOf("CN=Contoso Ltd, OU=IT, C=US"), "OU is not O");
            Assert.IsNull(CacheDownloader.OrganisationOf(null));
        }

        [TestMethod]
        public void SignatureRefusal_NamesTheActualSigner()
        {
            CacheDownloadException other = CacheDownloader.SignatureRefusal(Signature(AuthenticodeStatus.Valid, "CN=Fabrikam Inc., O=Fabrikam Inc., C=US"), "Contoso Ltd");
            Assert.AreEqual("signed by Fabrikam Inc. instead of Contoso Ltd", other.Reason);
            StringAssert.Contains(other.Message, "by Fabrikam Inc. instead of Contoso Ltd");
            StringAssert.Contains(other.Message, "add Fabrikam Inc. to the Publisher parameter");
            StringAssert.Contains(other.Message, "separated by ;");
            StringAssert.Contains(other.Message, "Signer: CN=Fabrikam Inc., O=Fabrikam Inc., C=US");

            CacheDownloadException none = CacheDownloader.SignatureRefusal(Signature(AuthenticodeStatus.Invalid, null), "Contoso Ltd");
            Assert.AreEqual("no valid signature (Invalid)", none.Reason);
            StringAssert.Contains(none.Message, "signer -");
            Assert.AreEqual("no valid signature (?)", CacheDownloader.SignatureRefusal(null, "Contoso Ltd").Reason);
        }

        [TestMethod]
        public void ShortReason_RefusalOrFirstLine()
        {
            Assert.AreEqual("short", CacheDownloader.ShortReason(new CacheDownloadException("short", "long message")));
            Assert.AreEqual("First line", CacheDownloader.ShortReason(new IOException("First line\r\nSecond line")));
            string longMessage = new string('x', 300);
            string reason = CacheDownloader.ShortReason(new IOException(longMessage));
            Assert.AreEqual(120, reason.Length);
            Assert.IsTrue(reason.EndsWith("..."));
        }

        // ---- Zip installers: winget show -----------------------------------------------------

        const string EnglishZip =
            "Found Contoso Example App [Contoso.ExampleApp]\r\n" +
            "Version: 2.5.0\r\n" +
            "Publisher: Contoso Ltd\r\n" +
            "Homepage: https://example.com/exampleapp\r\n" +
            "Tags:\r\n" +
            "  example\r\n" +
            "Installer:\r\n" +
            "  Installer Type: wix (zip)\r\n" +
            "  Installer Url: " + ZipUrl + "\r\n" +
            "  Installer SHA256: " + Sha + "\r\n" +
            "  Release Date: 2026-11-01\r\n" +
            "  Offline Distribution Supported: true\r\n";

        // As a French winget shows it: space before the colon, typographic apostrophes
        const string FrenchZip =
            "Trouvé Contoso Example App [Contoso.ExampleApp]\r\n" +
            "Version : 2.5.0\r\n" +
            "Éditeur : Contoso Ltd\r\n" +
            "Page d\u2019accueil : https://example.com/exampleapp\r\n" +
            "Mots-clés :\r\n" +
            "  example\r\n" +
            "Programme d'installation :\r\n" +
            "  Type du programme d\u2019installation : exe (zip)\r\n" +
            "  URL du programme d\u2019installation : " + ZipUrl + "\r\n" +
            "  SHA256 du programme d\u2019installation : " + Sha + "\r\n" +
            "  Date de version : 2026-11-01\r\n" +
            "  Distribution hors ligne prise en charge : true\r\n";

        [TestMethod]
        public void WingetShow_ZipInstaller_EnglishAndFrench()
        {
            WingetShowInfo en = WingetShowParser.Parse(EnglishZip);
            Assert.AreEqual("zip", en.InstallerType);
            Assert.AreEqual("wix", en.NestedInstallerType);
            Assert.AreEqual(ZipUrl, en.InstallerUrl);
            Assert.AreEqual(Sha, en.InstallerSha256);
            Assert.AreEqual("2.5.0", en.Version);

            WingetShowInfo fr = WingetShowParser.Parse(FrenchZip);
            Assert.AreEqual("zip", fr.InstallerType);
            Assert.AreEqual("exe", fr.NestedInstallerType);
            Assert.AreEqual(ZipUrl, fr.InstallerUrl);

            WingetShowInfo plain = WingetShowParser.Parse(EnglishZip.Replace("wix (zip)", "msi"));
            Assert.AreEqual("msi", plain.InstallerType);
            Assert.IsNull(plain.NestedInstallerType);
            Assert.IsNull(WingetShowParser.Parse(EnglishZip.Replace("wix (zip)", "foo (zip)")).InstallerType, "unknown nested type");
        }

        [TestMethod]
        public void NestedTypeMatches_TheTypeOfTheInstance()
        {
            Assert.IsTrue(WebVersionChecker.NestedTypeMatches("msi", "wix"));
            Assert.IsTrue(WebVersionChecker.NestedTypeMatches("MSI", "msi"));
            Assert.IsTrue(WebVersionChecker.NestedTypeMatches("exe", "nullsoft"));
            Assert.IsFalse(WebVersionChecker.NestedTypeMatches("msi", "exe"));
            Assert.IsFalse(WebVersionChecker.NestedTypeMatches("exe", "portable"), "a portable program is not an installer");
            Assert.IsFalse(WebVersionChecker.NestedTypeMatches("msix", "msix"));
            Assert.IsFalse(WebVersionChecker.NestedTypeMatches("msi", null));
            Assert.IsTrue(WebVersionChecker.IsArchive("ZIP"));
            Assert.IsFalse(WebVersionChecker.IsArchive("msi"));
        }

        [TestMethod]
        public void PreferredInstaller_PlainBeforeZip()
        {
            WingetShowInfo none = new WingetShowInfo { Found = true, Version = "2.5.0" };
            WingetShowInfo zip = new WingetShowInfo { Found = true, Version = "2.5.0", InstallerType = "zip", NestedInstallerType = "wix", InstallerUrl = ZipUrl };
            WingetShowInfo msi = new WingetShowInfo { Found = true, Version = "2.5.0", InstallerType = "wix", InstallerUrl = "https://example.com/a.msi" };
            Assert.AreSame(msi, WebVersionChecker.PreferredInstaller(new[] { none, zip, msi }), "a plain installer, even asked later");
            Assert.AreSame(zip, WebVersionChecker.PreferredInstaller(new[] { none, zip }), "a zip when there is nothing else");
            Assert.AreSame(none, WebVersionChecker.PreferredInstaller(new[] { none }), "the version without an installer");
            Assert.IsNull(WebVersionChecker.PreferredInstaller(new WingetShowInfo[0]));
        }

        [TestMethod]
        public void CanDownload_ZipOnlyWithAnInstallerOfTheType()
        {
            WebVersionEntry e = new WebVersionEntry
            {
                Status = WebVersionStatus.NewerOnWeb, Type = "msi", InstallerType = "zip", NestedInstallerType = "wix",
                InstallerUrl = ZipUrl, InstallerSha256 = Sha, CacheFile = Path.Combine(Path.GetTempPath(), "a.msi")
            };
            Assert.IsTrue(e.CanDownload);
            e.NestedInstallerType = "portable";
            Assert.IsFalse(e.CanDownload);
            StringAssert.Contains(WebVersionChecker.ZipManualMessage("portable", "msi"), "download it manually");
        }

        // ---- Zip installers: the installer a zip holds ---------------------------------------

        [TestMethod]
        public void NestedInstallerEntry_SeveralFiles_SameNameThenX64()
        {
            string[] vendor = { "Software/Example_Win10.msi", "Software/Example_arm64.msi" };
            // The one of the other architecture is left out
            Assert.AreEqual("Software/Example_Win10.msi", CacheDownloader.NestedInstallerEntry(vendor, "msi"));
            Assert.AreEqual("x64/ExampleApp.msi", CacheDownloader.NestedInstallerEntry(new[] { "x64/ExampleApp.msi", "x86/ExampleApp.msi" }, "msi"));
            Assert.AreEqual("ExampleApp-x86_64.msi", CacheDownloader.NestedInstallerEntry(new[] { "ExampleApp-x86_64.msi", "ExampleApp-win32.msi" }, "msi"));
            // The same file name as the current file of the instance wins
            Assert.AreEqual("b/ExampleTools.msi",
                CacheDownloader.NestedInstallerEntry(new[] { "a/ExampleApp.msi", "b/ExampleTools.msi" }, "msi", "Vendor/ExampleTools.msi"));
            Assert.AreEqual("b/ExampleTools.msi",
                CacheDownloader.NestedInstallerEntry(new[] { "a/ExampleApp.msi", "b/ExampleTools.msi" }, "msi", "EXAMPLETOOLS.MSI"));
            // Still several: download it manually
            Assert.ThrowsException<CacheDownloadException>(() =>
                CacheDownloader.NestedInstallerEntry(new[] { "a/ExampleApp.msi", "b/ExampleTools.msi" }, "msi", "Other.msi"));
        }

        [TestMethod]
        public void IsOtherArchitecture_Words()
        {
            foreach (string other in new[] { "Example_arm64.msi", "arm64/Example.msi", "Example-ARM.msi", "x86/Example.msi", "Example_x86.msi", "Example-Win32.msi", "Example-32bit.msi", "Example 32-bit.msi", "aarch64/a.msi" })
                Assert.IsTrue(CacheDownloader.IsOtherArchitecture(other), other);
            foreach (string x64 in new[] { "Example_x64.msi", "Example-x86_64.msi", "Example-x86-64.msi", "Example_Win10RS.msi", "Farmer.msi", "Example64.msi", "", null })
                Assert.IsFalse(CacheDownloader.IsOtherArchitecture(x64), x64 ?? "(null)");
        }

        [TestMethod]
        public void NestedInstallerEntry_OneFileOfTheType()
        {
            Assert.AreEqual("Host/ExampleApp.msi", CacheDownloader.NestedInstallerEntry(new[] { "Host/", "Host/ExampleApp.msi", "readme.txt", "setup.exe" }, "msi"));
            Assert.AreEqual("setup.EXE", CacheDownloader.NestedInstallerEntry(new[] { "setup.EXE", "data.cab" }, "exe"));

            CacheDownloadException several = Assert.ThrowsException<CacheDownloadException>(() =>
                CacheDownloader.NestedInstallerEntry(new[] { "ExampleApp.msi", "ExampleTools.msi" }, "msi"));
            Assert.AreEqual("zip installer: download it manually", several.Reason);
            StringAssert.Contains(several.Message, "ExampleApp.msi, ExampleTools.msi");

            Assert.AreEqual("zip installer: download it manually",
                Assert.ThrowsException<CacheDownloadException>(() => CacheDownloader.NestedInstallerEntry(new[] { "readme.txt" }, "msi")).Reason);
            Assert.AreEqual("zip installer: download it manually",
                Assert.ThrowsException<CacheDownloadException>(() => CacheDownloader.NestedInstallerEntry(new[] { "a.msix" }, "msix")).Reason);
            foreach (string bad in new[] { "../ExampleApp.msi", "Host/../../ExampleApp.msi", "/ExampleApp.msi", "C:/ExampleApp.msi", "..\\ExampleApp.msi" })
                Assert.AreEqual("zip installer: unsafe path",
                    Assert.ThrowsException<CacheDownloadException>(() => CacheDownloader.NestedInstallerEntry(new[] { bad }, "msi"), bad).Reason, bad);
        }

        static string TempFolder()
        {
            string dir = Path.Combine(Path.GetTempPath(), "LiteDeploy-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        static void WriteZip(string path, IDictionary<string, string> entries)
        {
            using (FileStream f = new FileStream(path, FileMode.CreateNew))
            using (ZipArchive zip = new ZipArchive(f, ZipArchiveMode.Create))
                foreach (KeyValuePair<string, string> e in entries)
                    using (StreamWriter w = new StreamWriter(zip.CreateEntry(e.Key).Open(), new UTF8Encoding(false)))
                        w.Write(e.Value);
        }

        [TestMethod]
        public void ExtractNestedInstaller_OnATemporaryZip()
        {
            string root = TempFolder();
            List<string> log = new List<string>();
            CacheDownloader downloader = new CacheDownloader(log.Add);
            try
            {
                string zipPath = Path.Combine(root, "download.zip");
                WriteZip(zipPath, new Dictionary<string, string> { { "Host/ExampleApp.msi", "fake msi content" }, { "readme.txt", "read me" } });
                Assert.IsTrue(CacheDownloader.IsZipFile(zipPath));
                string notZip = Path.Combine(root, "plain.msi");
                File.WriteAllText(notZip, "not a zip");
                Assert.IsFalse(CacheDownloader.IsZipFile(notZip));

                string outDir = Path.Combine(root, "out");
                Directory.CreateDirectory(outDir);
                string entry;
                string extracted = downloader.ExtractNestedInstaller(zipPath, "msi", outDir, CacheDownloader.MaxNestedBytes, out entry);
                Assert.AreEqual("Host/ExampleApp.msi", entry);
                Assert.AreEqual(Path.Combine(outDir, "nested.msi"), extracted, "never the name of the entry");
                Assert.AreEqual("fake msi content", File.ReadAllText(extracted));
                Assert.AreEqual(1, Directory.GetFiles(outDir, "*", SearchOption.AllDirectories).Length, "only the installer is extracted");

                // Larger than the limit: refused, whatever the zip announces
                string small = Path.Combine(root, "small");
                Directory.CreateDirectory(small);
                CacheDownloadException large = Assert.ThrowsException<CacheDownloadException>(() =>
                    downloader.ExtractNestedInstaller(zipPath, "msi", small, 5, out entry));
                Assert.AreEqual("zip installer: file too large", large.Reason);

                // Path traversal: refused, nothing written outside
                string evil = Path.Combine(root, "evil.zip");
                WriteZip(evil, new Dictionary<string, string> { { "../ExampleApp.msi", "x" } });
                string evilOut = Path.Combine(root, "evil-out");
                Directory.CreateDirectory(evilOut);
                Assert.AreEqual("zip installer: unsafe path", Assert.ThrowsException<CacheDownloadException>(() =>
                    downloader.ExtractNestedInstaller(evil, "msi", evilOut, CacheDownloader.MaxNestedBytes, out entry)).Reason);
                Assert.IsFalse(File.Exists(Path.Combine(root, "ExampleApp.msi")));
                Assert.AreEqual(0, Directory.GetFiles(evilOut).Length);

                // Two installers: download it manually
                string two = Path.Combine(root, "two.zip");
                WriteZip(two, new Dictionary<string, string> { { "ExampleApp.msi", "a" }, { "ExampleTools.msi", "b" } });
                string twoOut = Path.Combine(root, "two-out");
                Directory.CreateDirectory(twoOut);
                Assert.AreEqual("zip installer: download it manually", Assert.ThrowsException<CacheDownloadException>(() =>
                    downloader.ExtractNestedInstaller(two, "msi", twoOut, CacheDownloader.MaxNestedBytes, out entry)).Reason);
            }
            finally { Directory.Delete(root, true); }
        }

        // ---- Summary after the downloads ---------------------------------------------------------

        [TestMethod]
        public void Summary_OneLinePerProduct()
        {
            WebVersionEntry a = new WebVersionEntry { Key = "CachedInstaller/ExampleApp", Product = "Contoso Example App", WebVersion = "2.5.0" };
            WebVersionEntry b = new WebVersionEntry { Key = "CachedInstaller/ExampleTool", Product = "Contoso Example Tool", WebVersion = "3.1" };
            WebVersionEntry c = new WebVersionEntry { Key = "CachedInstaller/ExampleHost", Product = "Contoso Example Host", WebVersion = "15.2" };
            List<CacheDownloadOutcome> outcomes = new List<CacheDownloadOutcome>
            {
                new CacheDownloadOutcome { Entry = a, Result = new CacheDownloadResult { Entry = a, NewFileParameter = "CachedInstaller/Contoso/app-2.5.0.msi", OldFileMovedTo = @"C:\Cache\OLD\app-2.4.msi" } },
                new CacheDownloadOutcome { Entry = b, Refused = true, Reason = "signed by Fabrikam Inc. instead of Contoso Ltd",
                    Error = "The download is validly signed, but by Fabrikam Inc. instead of Contoso Ltd: refused. If this vendor renamed itself, add Fabrikam Inc. to the Publisher parameter of the instance (names separated by ;). Signer: CN=Fabrikam Inc., O=Fabrikam Inc., C=US" },
                new CacheDownloadOutcome { Entry = c, Reason = "HTTP 404 Not Found", Error = "HTTP 404 Not Found" }
            };
            string summary = CacheDownloadSummary.Summary(outcomes, 4);
            string[] lines = summary.Split(new[] { Environment.NewLine }, StringSplitOptions.None);
            Assert.AreEqual(4, lines.Length, summary);
            Assert.AreEqual("OK: Contoso Example App 2.5.0", lines[0]);
            Assert.AreEqual("Refused: Contoso Example Tool 3.1 - signed by Fabrikam Inc. instead of Contoso Ltd", lines[1]);
            Assert.AreEqual("Failed: Contoso Example Host 15.2 - HTTP 404 Not Found", lines[2]);
            Assert.AreEqual("Cancelled: 1 file(s) not processed.", lines[3]);
            Assert.IsTrue(CacheDownloadSummary.HasDetails(outcomes));
            Assert.IsFalse(CacheDownloadSummary.HasDetails(outcomes.Take(1).ToList()));

            string details = CacheDownloadSummary.Details(outcomes, @"C:\Logs\web-versions-20261118.log");
            StringAssert.Contains(details, "Contoso Example App 2.5.0 [CachedInstaller/ExampleApp]");
            StringAssert.Contains(details, "File parameter: CachedInstaller/Contoso/app-2.5.0.msi");
            StringAssert.Contains(details, "Signer: CN=Fabrikam Inc., O=Fabrikam Inc., C=US");
            Assert.IsTrue(details.EndsWith(@"Log file: C:\Logs\web-versions-20261118.log"));
        }

        [TestMethod]
        public void RunDownloads_KeepsTheShortReason()
        {
            WebVersionEntry a = new WebVersionEntry { Key = "CachedInstaller/ExampleApp", Product = "Contoso Example App" };
            WebVersionEntry b = new WebVersionEntry { Key = "CachedInstaller/ExampleTool", Product = "Contoso Example Tool" };
            List<CacheDownloadOutcome> outcomes = CacheDownloader.RunDownloads(new[] { a, b }, (e, step) =>
            {
                if (e == a) throw new CacheDownloadException("zip installer: download it manually", "Zip installer (2 .msi files in it): ...");
                throw new IOException("Disk full\nmore");
            }, null, System.Threading.CancellationToken.None, null);
            Assert.AreEqual("zip installer: download it manually", outcomes[0].Reason);
            Assert.IsTrue(outcomes[0].Refused);
            Assert.AreEqual("Disk full", outcomes[1].Reason);
            Assert.IsFalse(outcomes[1].Refused);
            Assert.AreEqual("Disk full\nmore", outcomes[1].Error);
        }
    }
}
