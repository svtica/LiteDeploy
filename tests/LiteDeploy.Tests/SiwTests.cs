// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    SiwTests.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-23
// Purpose: Unit tests of the SIW report: command lines (SIW and worker, the
//          forbidden sections removed), parsing of the worker arguments, the
//          publisher check of the signature, the SIW folder check, the
//          executable of each architecture, the server guard, the names of the
//          local report files, the reading of a hand-written sample report
//          (fictitious values only, never a real report), the summary and the
//          HTML view. Also the IP address column of the grid and its sorting.
// -----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using LiteSuite;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LiteDeploy.Tests
{
    [TestClass]
    public class SiwTests
    {
        const string Vendor = "CN=Topala Software Solutions, O=Topala Software Solutions, L=Maple, S=Ontario, C=CA";

        // Hand-written sample in the format of SIW (fictitious values)
        const string Sample =
            "<?xml version=\"1.0\" encoding=\"UTF-8\" ?>\n" +
            "<report Title=\"SIW 2026 v16.16 64-bit Technician&#39;s Version\" computer_name=\"PC01\" language=\"LNG_EN\" date=\"2026-11-23 10:15:45 (23 novembre 2026)\" xml_version=\"2.7\">\n" +
            " <software Title=\"Software\">\n" +
            "  <page Title=\"Operating System\" H1=\"Property\" H2=\"Value\" __type__=\"2\">\n" +
            "   <item Property=\"Windows 11\"><item Property=\"Product Name\" Value=\"Windows 11 Pro\" /><item Property=\"Version\" Value=\"25H2\" /></item>\n" +
            "  </page>\n" +
            " </software>\n" +
            " <hardware Title=\"Hardware\">\n" +
            "  <page Title=\"System Summary\" H1=\"Property\" H2=\"Value\" __type__=\"2\">\n" +
            "   <item Property=\"Computer\"><item Property=\"Manufacturer\" Value=\"Example Corp\" /><item Property=\"Model\" Value=\"Model 1000\" />" +
            "<item Property=\"Serial Number\" Value=\"SN0001\" /></item>\n" +
            "  </page>\n" +
            "  <page Title=\"Video\" H1=\"Property\" H2=\"Value\" __type__=\"2\">\n" +
            "   <item Property=\"Monitor\"><item Property=\"Model\" Value=\"Screen A\" /><item Property=\"Serial Number\" Value=\"M001\" /></item>\n" +
            "   <item Property=\"Monitor\"><item Property=\"Model\" Value=\"Screen B &lt;script&gt;\" /><item Property=\"Serial Number\" Value=\"\" /></item>\n" +
            "  </page>\n" +
            "  <page Title=\"Storage Devices\" H1=\"Property\" H2=\"Value\" __type__=\"2\">\n" +
            "   <item Property=\"Disk 0\"><item Property=\"Model\" Value=\"Disk X\" /><item Property=\"Status\" Value=\"Good\" /></item>\n" +
            "  </page>\n" +
            " </hardware>\n" +
            " <network Title=\"Network\">\n" +
            "  <page Title=\"Network Information\" H1=\"Property\" H2=\"Value\" __type__=\"2\">\n" +
            "   <item Property=\"IPv4\" Value=\"192.0.2.10\" />\n" +
            "  </page>\n" +
            " </network>\n" +
            "</report>\n";

        static AuthenticodeResult Signature(AuthenticodeStatus status, string subject)
        {
            return new AuthenticodeResult { Status = status, SignerSubject = subject, SignerThumbprint = "AB" };
        }

        static SiwField Field(string text)
        {
            return SiwReport.ParseFields(text).Single();
        }

        // ---- Arguments ------------------------------------------------------------------

        [TestMethod]
        public void SiwArguments_Defaults_FixedNumbersAndQuotedPath()
        {
            Assert.AreEqual(@"/log:xml=""C:\Windows\LiteDeploy\work\SIW\report.xml"" /silent /s:5 /h:136079 /n:1",
                SiwPolicy.SiwArguments(@"C:\Windows\LiteDeploy\work\SIW\report.xml", SiwFormat.Xml,
                    SiwPolicy.DefaultSoftware, SiwPolicy.DefaultHardware, SiwPolicy.DefaultNetwork));
            StringAssert.StartsWith(SiwPolicy.SiwArguments(@"C:\x\report.html", SiwFormat.Html, 1, 1, 0), @"/log:html=""C:\x\report.html"" /silent");
        }

        [TestMethod]
        public void SiwArguments_ForbiddenSectionsAlwaysRemoved()
        {
            // Licenses (product keys) and the accounts / Active Directory sections
            string args = SiwPolicy.SiwArguments(@"C:\x\report.xml", SiwFormat.Xml, 0x40000000UL | 0x1, 1, 0x4000UL | 0x200 | 0x1);
            StringAssert.Contains(args, "/s:1 ");
            StringAssert.EndsWith(args, "/n:1");
            Assert.AreEqual(0UL, SiwPolicy.SafeSoftware(SiwPolicy.ForbiddenSoftware));
            Assert.AreEqual(0UL, SiwPolicy.SafeNetwork(SiwPolicy.ForbiddenNetwork));
            Assert.IsFalse(args.Contains("/all") || args.Contains("/upload") || args.Contains("/email"));
        }

        [TestMethod]
        public void SiwArguments_PathThatCannotBeQuoted_Throws()
        {
            foreach (string bad in new[] { null, "", @"C:\a""b.xml", @"C:\dir\", @"C:\a" + "\n" + "b.xml" })
            {
                try { SiwPolicy.SiwArguments(bad, SiwFormat.Xml, 1, 1, 1); Assert.Fail("accepted: " + bad); }
                catch (ArgumentException) { }
            }
        }

        [TestMethod]
        public void WorkerArguments_RoundTrip()
        {
            SiwRequest r = new SiwRequest { Format = SiwFormat.Html, Software = 1, Hardware = 2, Network = 0, TimeoutSeconds = 120, AllowServers = true, AllowDomainControllers = true };
            string text = SiwPolicy.WorkerArguments(r);
            Assert.AreEqual("siw --format html --software 1 --hardware 2 --network 0 --publisher \"Topala Software Solutions\" --timeout 120 --allow-servers --allow-domain-controllers", text);

            string[] args = new[] { "siw", "--format", "html", "--software", "1", "--hardware", "2", "--network", "0",
                "--publisher", "Topala Software Solutions", "--timeout", "120", "--allow-servers", "--allow-domain-controllers" };
            SiwRequest back;
            Assert.IsNull(SiwPolicy.ParseArguments(args, out back));
            Assert.AreEqual(SiwFormat.Html, back.Format);
            Assert.AreEqual(1UL, back.Software);
            Assert.AreEqual(2UL, back.Hardware);
            Assert.AreEqual(120, back.TimeoutSeconds);
            Assert.IsTrue(back.AllowServers && back.AllowDomainControllers);
        }

        [TestMethod]
        public void WorkerArguments_DomainControllersOnlyWithServers_TimeoutClamped()
        {
            string text = SiwPolicy.WorkerArguments(new SiwRequest { AllowDomainControllers = true, TimeoutSeconds = 99999 });
            Assert.IsFalse(text.Contains("--allow"), text);
            StringAssert.Contains(text, "--timeout 1800");
            StringAssert.Contains(SiwPolicy.WorkerArguments(new SiwRequest { TimeoutSeconds = 1 }), "--timeout 30");
        }

        [TestMethod]
        public void ParseArguments_Defaults_AndForbiddenSectionsRemoved()
        {
            SiwRequest r;
            Assert.IsNull(SiwPolicy.ParseArguments(new[] { "siw" }, out r));
            Assert.AreEqual(SiwFormat.Xml, r.Format);
            Assert.AreEqual(SiwPolicy.DefaultHardware, r.Hardware);
            Assert.IsFalse(r.AllowServers);
            Assert.IsNull(SiwPolicy.ParseArguments(new[] { "siw", "--software", (0x40000000UL | 4).ToString(), "--network", "16384" }, out r));
            Assert.AreEqual(4UL, r.Software);
            Assert.AreEqual(0UL, r.Network);
        }

        [TestMethod]
        public void ParseArguments_Refused()
        {
            SiwRequest r;
            string[][] bad =
            {
                null,
                new string[0],
                new[] { "scan" },
                new[] { "siw", "--format", "json" },
                new[] { "siw", "--format" },
                new[] { "siw", "--software", "-1" },
                new[] { "siw", "--hardware", "0x10" },
                new[] { "siw", "--timeout", "5" },
                new[] { "siw", "--timeout", "4000" },
                new[] { "siw", "--publisher", "Evil\" /upload=ftp://x" },
                new[] { "siw", "/log:xml=C:\\x.xml" },
                new[] { "siw", "--all" },
                new[] { "siw", "--software", "0", "--hardware", "0", "--network", "0" }
            };
            foreach (string[] args in bad)
                Assert.IsNotNull(SiwPolicy.ParseArguments(args, out r), args == null ? "null" : String.Join(" ", args));
        }

        // ---- Publisher, folder, executable, server guard --------------------------------

        [TestMethod]
        public void IsSignedBy_ExactOrganisationOnly()
        {
            Assert.IsTrue(SiwPolicy.IsSignedBy(true, Vendor, SiwPolicy.DefaultPublisher));
            Assert.IsTrue(SiwPolicy.IsSignedBy(true, "CN=X, O=\"Topala Software Solutions\", C=CA", SiwPolicy.DefaultPublisher));
            Assert.IsFalse(SiwPolicy.IsSignedBy(false, Vendor, SiwPolicy.DefaultPublisher), "invalid signature");
            Assert.IsFalse(SiwPolicy.IsSignedBy(true, "CN=Topala Software Solutions, O=Example Corp", SiwPolicy.DefaultPublisher), "CN only");
            Assert.IsFalse(SiwPolicy.IsSignedBy(true, "O=Topala Software Solutions Evil, C=CA", SiwPolicy.DefaultPublisher), "longer name");
            Assert.IsFalse(SiwPolicy.IsSignedBy(true, "O=Evil Topala Software Solutions", SiwPolicy.DefaultPublisher), "prefix");
            Assert.IsFalse(SiwPolicy.IsSignedBy(true, null, SiwPolicy.DefaultPublisher));
            Assert.IsFalse(SiwPolicy.IsSignedBy(true, Vendor, ".*"), "not a regular expression");
        }

        [TestMethod]
        public void CheckFolder_NeedsSignedExecutables()
        {
            string folder = Path.Combine(Path.GetTempPath(), "SIW");
            Func<string, bool> onlyX64 = p => p.EndsWith("siw64.exe", StringComparison.OrdinalIgnoreCase);
            Assert.IsNull(SiwReports.CheckFolder(folder, SiwPolicy.DefaultPublisher, onlyX64, p => Signature(AuthenticodeStatus.Valid, Vendor)));
            Assert.IsNotNull(SiwReports.CheckFolder("", SiwPolicy.DefaultPublisher, onlyX64, p => Signature(AuthenticodeStatus.Valid, Vendor)), "not set");
            Assert.IsNotNull(SiwReports.CheckFolder(folder, SiwPolicy.DefaultPublisher, p => false, p => Signature(AuthenticodeStatus.Valid, Vendor)), "empty folder");
            Assert.IsNotNull(SiwReports.CheckFolder(folder, SiwPolicy.DefaultPublisher, onlyX64, p => Signature(AuthenticodeStatus.HashMismatch, Vendor)), "tampered");
            Assert.IsNotNull(SiwReports.CheckFolder(folder, SiwPolicy.DefaultPublisher, onlyX64, p => Signature(AuthenticodeStatus.Valid, "CN=Other, O=Example Corp")), "other signer");
            Assert.IsNotNull(SiwReports.CheckFolder(folder, "Bad\"Name", onlyX64, p => Signature(AuthenticodeStatus.Valid, Vendor)), "bad publisher setting");
            // Every executable present is checked, not only the first one
            Assert.IsNotNull(SiwReports.CheckFolder(folder, SiwPolicy.DefaultPublisher, p => true,
                p => Signature(p.EndsWith("siw32.exe") ? AuthenticodeStatus.NotSigned : AuthenticodeStatus.Valid, Vendor)));
        }

        [TestMethod]
        public void ExecutableFor_Architecture_NeverTheLauncher()
        {
            Assert.AreEqual("siw-arm64.exe", SiwPolicy.ExecutableFor(SiwPolicy.ArchitectureFrom(true, true)));
            Assert.AreEqual("siw64.exe", SiwPolicy.ExecutableFor(SiwPolicy.ArchitectureFrom(false, true)));
            Assert.AreEqual("siw32.exe", SiwPolicy.ExecutableFor(SiwPolicy.ArchitectureFrom(false, false)));
            CollectionAssert.DoesNotContain(SiwPolicy.Executables, "siw.exe");
        }

        [TestMethod]
        public void ServerGuard_AppliesToSiw()
        {
            Assert.AreEqual(ServerDecision.Allowed, ServerPolicy.Decide(SiwPolicy.Command, ServerPolicy.ProductWorkstation, false, false));
            Assert.AreEqual(ServerDecision.RefusedServer, ServerPolicy.Decide(SiwPolicy.Command, ServerPolicy.ProductServer, false, false));
            Assert.AreEqual(ServerDecision.Allowed, ServerPolicy.Decide(SiwPolicy.Command, ServerPolicy.ProductServer, true, false));
            Assert.AreEqual(ServerDecision.RefusedDomainController, ServerPolicy.Decide(SiwPolicy.Command, ServerPolicy.ProductDomainController, true, false));
            Assert.AreEqual(ServerDecision.Allowed, ServerPolicy.Decide(SiwPolicy.Command, ServerPolicy.ProductDomainController, true, true));
        }

        [TestMethod]
        public void RequestFrom_Settings()
        {
            UserSettings s = new UserSettings { SiwHtml = true, IncludeServers = false, AllowDomainControllers = true, SiwTimeoutSeconds = 5, SiwPublisher = " " };
            SiwRequest r = SiwReports.RequestFrom(s);
            Assert.AreEqual(SiwFormat.Html, r.Format);
            Assert.IsFalse(r.AllowServers);
            Assert.IsFalse(r.AllowDomainControllers, "never without Include servers");
            Assert.AreEqual(SiwPolicy.MinTimeoutSeconds, r.TimeoutSeconds);
            Assert.AreEqual(SiwPolicy.DefaultPublisher, r.Publisher);
            Assert.AreEqual(SiwFormat.Xml, SiwReports.RequestFrom(new UserSettings()).Format, "XML by default");
            Assert.AreEqual(6, SiwReports.WorkerMinutes(new SiwRequest { TimeoutSeconds = 300 }));
            Assert.AreEqual(11, SiwReports.WorkerMinutes(new SiwRequest { TimeoutSeconds = 300, Format = SiwFormat.Html }));
        }

        // ---- Local report files -----------------------------------------------------------

        [TestMethod]
        public void LocalPath_ComputerFolderAndDate()
        {
            DateTime when = new DateTime(2026, 11, 23, 9, 5, 7);
            Assert.AreEqual(Path.Combine(@"C:\root", "PC01", "20261123-090507.xml"), SiwReports.LocalPath(@"C:\root", "PC01", when, "", "xml"));
            Assert.AreEqual(Path.Combine(@"C:\root", "SRV01", "20261123-090507-siw.html"), SiwReports.LocalPath(@"C:\root", " SRV01 ", when, "-siw", "html"));
            Assert.AreEqual(Path.Combine(@"C:\root", "_", "20261123-090507.xml"), SiwReports.LocalPath(@"C:\root", "..", when, "", "xml"));
            Assert.AreEqual(Path.Combine(@"C:\root", "PC01_.._x", "20261123-090507.xml"), SiwReports.LocalPath(@"C:\root", @"PC01\..\x", when, "", "xml"));
            Assert.AreEqual(Path.Combine(@"C:\root", "_", "20261123-090507.xml"), SiwReports.LocalPath(@"C:\root", null, when, null, ".x/m:l"));
        }

        // ---- Report ------------------------------------------------------------------------

        [TestMethod]
        public void Parse_Sample()
        {
            SiwReport r = SiwReport.Parse(Sample);
            Assert.AreEqual("PC01", r.Computer);
            Assert.AreEqual("2026-11-23 10:15:45", r.ShortDate);
            Assert.AreEqual("SIW 2026 v16.16 64-bit Technician's Version", r.Title);
            Assert.AreEqual(5, r.Pages.Count);
            Assert.AreEqual("hardware", r.Pages[1].Section);
            Assert.AreEqual("Windows 11 Pro", r.Value(Field("Operating System/*/Product Name")));
            Assert.AreEqual("SN0001", r.Value(Field("System Summary/Computer/Serial Number")));
            Assert.AreEqual("SN0001", r.Value(Field("system summary/COMPUTER/serial number")), "case does not matter");
            Assert.AreEqual("Screen A | Screen B <script>", r.Value(Field("Video/Monitor/Model")), "every match, joined");
            Assert.AreEqual("M001", r.Value(Field("Video/Monitor/Serial Number")), "empty values skipped");
            Assert.AreEqual("192.0.2.10", r.Value(Field("Network Information/*/IPv4")), "line directly under the page");
            Assert.AreEqual("", r.Value(Field("BIOS/BIOS Properties/BIOS Version")), "missing page");
        }

        [TestMethod]
        public void Parse_RefusesDtdAndOtherDocuments()
        {
            string withDtd = "<?xml version=\"1.0\"?><!DOCTYPE report [<!ENTITY x SYSTEM \"file:///C:/Windows/win.ini\">]><report computer_name=\"&x;\" />";
            try { SiwReport.Parse(withDtd); Assert.Fail("DTD accepted"); }
            catch (XmlException) { }
            try { SiwReport.Parse("<other />"); Assert.Fail("not a report"); }
            catch (InvalidDataException) { }
        }

        [TestMethod]
        public void ParseFields_DefaultAndInvalidEntries()
        {
            Assert.IsTrue(SiwReport.ParseFields(null).Count > 10);
            Assert.AreEqual(SiwReport.ParseFields("").Count, SiwReport.ParseFields(SiwReport.DefaultSummaryFields).Count);
            List<SiwField> f = SiwReport.ParseFields(" BIOS / BIOS Properties / BIOS Version ;;a/b;a/b/c/d;/x/y;Video/*/Model");
            Assert.AreEqual(2, f.Count);
            Assert.AreEqual("BIOS/BIOS Properties/BIOS Version", f[0].ToString());
            Assert.AreEqual("*", f[1].Group);
        }

        [TestMethod]
        public void Summary_OneRowPerComputer_FailuresKept()
        {
            List<SiwField> fields = SiwReport.ParseFields("System Summary/Computer/Model;Video/Monitor/Model;System Summary/Computer/Serial Number");
            List<KeyValuePair<string, SiwReport>> reports = new List<KeyValuePair<string, SiwReport>>
            {
                new KeyValuePair<string, SiwReport>("PC01", SiwReport.Parse(Sample)),
                new KeyValuePair<string, SiwReport>("PC02", null)
            };
            InventoryTable t = SiwReport.Summary(fields, reports, new Dictionary<string, string> { { "PC02", "Timed out" } });
            Assert.AreEqual(6, t.Headers.Count);
            Assert.AreEqual("System Summary - Model", t.Headers[1], "two fields share Model: the page is added");
            Assert.AreEqual("Serial Number", t.Headers[3]);
            Assert.AreEqual("Model 1000", t.Rows[0][1]);
            Assert.AreEqual("Screen A | Screen B <script>", t.Rows[0][2]);
            Assert.AreEqual("2026-11-23 10:15:45", t.Rows[0][4]);
            Assert.IsNull(t.Rows[0][5]);
            Assert.AreEqual("PC02", t.Rows[1][0]);
            Assert.IsNull(t.Rows[1][1]);
            Assert.AreEqual("Timed out", t.Rows[1][5]);
            StringAssert.Contains(t.ToCsv(), "PC01;Model 1000;Screen A | Screen B <script>;SN0001;");
        }

        [TestMethod]
        public void ToHtml_EncodesEveryValue_NoScript()
        {
            string html = SiwReport.Parse(Sample).ToHtml();
            StringAssert.Contains(html, "Screen B &lt;script&gt;");
            Assert.IsFalse(html.Contains("<script"), "nothing from the report is markup");
            StringAssert.Contains(html, "Content-Security-Policy");
            StringAssert.Contains(html, "<th colspan=\"2\">Monitor</th>");
        }

        // ---- IP address column -------------------------------------------------------------

        [TestMethod]
        public void IpText_JoinsTheAddresses()
        {
            Assert.AreEqual("", ComputerGrid.IpText(null));
            Assert.AreEqual("", ComputerGrid.IpText("  "));
            Assert.AreEqual("192.0.2.10", ComputerGrid.IpText("192.0.2.10"));
            Assert.AreEqual("192.0.2.10, 198.51.100.7", ComputerGrid.IpText("192.0.2.10  198.51.100.7 192.0.2.10"));
        }

        [TestMethod]
        public void CompareTexts_IPv4ByValue()
        {
            Assert.IsTrue(ListViewColumnSorter.CompareTexts("192.0.2.9", "192.0.2.10") < 0);
            Assert.IsTrue(ListViewColumnSorter.CompareTexts("198.51.100.1, 192.0.2.1", "192.0.2.200") > 0, "first address of the cell");
            Assert.IsTrue(ListViewColumnSorter.CompareTexts("8", "16") < 0, "numbers still by value");
            Assert.IsTrue(ListViewColumnSorter.CompareTexts("", "192.0.2.1") < 0);
            Assert.IsTrue(ListViewColumnSorter.CompareTexts("192.0.2.300", "192.0.2.4") < 0, "not an address: text order");
        }
    }
}
