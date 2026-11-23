// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    PortableToolTests.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-23
// Purpose: Unit tests of the portable report tools: the fixed table, the tool
//          and worker command lines, the parsing of the worker arguments, the
//          server guard, where a tool is found and its signature check, and
//          the CSV reports (encodings, quoted fields, HTML view, one table for
//          several computers). Hand-written fictitious samples only.
// -----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using LiteSuite;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LiteDeploy.Tests
{
    [TestClass]
    public class PortableToolTests
    {
        const string NirSoftSubject = "CN=Nir Sofer, O=Nir Sofer, C=IL";

        static AuthenticodeResult Signature(AuthenticodeStatus status, string subject)
        {
            return new AuthenticodeResult { Status = status, SignerSubject = subject, SignerThumbprint = "AB" };
        }

        // ---- Table and command lines ------------------------------------------------------

        [TestMethod]
        public void Table_IdsUniqueAndValid_ReportPathOnlyPlaceholder()
        {
            CollectionAssert.AllItemsAreUnique(PortableToolPolicy.All.Select(t => t.Id).ToList());
            foreach (PortableTool t in PortableToolPolicy.All)
            {
                Assert.IsTrue(t.Id.All(c => c >= 'a' && c <= 'z' || c >= '0' && c <= '9'), t.Id);
                StringAssert.Contains(t.ArgumentsTemplate, "{report}", t.Id);
                Assert.IsTrue(SiwPolicy.IsValidPublisher(t.Publisher), t.Id);
                Assert.AreEqual(t.ArgumentsTemplate.Length - "{report}".Length, t.ArgumentsTemplate.Replace("{report}", "").Length, "one placeholder: " + t.Id);
            }
            Assert.IsNotNull(PortableToolPolicy.Find("UninstallView"));
            Assert.IsNull(PortableToolPolicy.Find("cports"), "not in the table");
        }

        [TestMethod]
        public void Arguments_QuotedReportPath()
        {
            PortableTool uv = PortableToolPolicy.Find("uninstallview");
            Assert.AreEqual(@"/SaveFileEncoding 2 /scomma ""C:\w\report.csv""", PortableToolPolicy.Arguments(uv, @"C:\w\report.csv"));
            foreach (string bad in new[] { null, " ", @"C:\a""b.csv", @"C:\dir\" })
            {
                try { PortableToolPolicy.Arguments(uv, bad); Assert.Fail("accepted: " + bad); }
                catch (ArgumentException) { }
            }
            Assert.AreEqual("report.csv", PortableToolPolicy.ReportFileName(uv));
        }

        [TestMethod]
        public void WorkerArguments_RoundTrip()
        {
            PortableToolRequest r = new PortableToolRequest { Tool = PortableToolPolicy.Find("winupdatesview"), TimeoutSeconds = 60, AllowServers = true };
            string text = PortableToolPolicy.WorkerArguments(r);
            Assert.AreEqual("tool winupdatesview --timeout 60 --allow-servers", text);
            PortableToolRequest back;
            Assert.IsNull(PortableToolPolicy.ParseArguments(text.Split(' '), out back));
            Assert.AreEqual("winupdatesview", back.Tool.Id);
            Assert.AreEqual(60, back.TimeoutSeconds);
            Assert.IsTrue(back.AllowServers);
            Assert.IsFalse(back.AllowDomainControllers);
            StringAssert.Contains(PortableToolPolicy.WorkerArguments(new PortableToolRequest { Tool = r.Tool, TimeoutSeconds = 1 }), "--timeout 10");
            StringAssert.EndsWith(PortableToolPolicy.WorkerArguments(new PortableToolRequest { Tool = r.Tool, AllowDomainControllers = true }), "--timeout 120");
        }

        [TestMethod]
        public void WorkerArguments_UnknownTool_Throws()
        {
            try
            {
                PortableToolPolicy.WorkerArguments(new PortableToolRequest { Tool = new PortableTool { Id = "other", ArgumentsTemplate = "{report}" } });
                Assert.Fail();
            }
            catch (ArgumentException) { }
        }

        [TestMethod]
        public void ParseArguments_Refused()
        {
            PortableToolRequest r;
            string[][] bad =
            {
                null,
                new[] { "tool" },
                new[] { "tool", "cports" },
                new[] { "scan", "uninstallview" },
                new[] { "tool", "uninstallview", "--timeout", "5" },
                new[] { "tool", "uninstallview", "--timeout", "x" },
                new[] { "tool", "uninstallview", "/scomma", @"C:\x.csv" },
                new[] { "tool", "uninstallview", "--args", "/sxml" }
            };
            foreach (string[] args in bad)
                Assert.IsNotNull(PortableToolPolicy.ParseArguments(args, out r), args == null ? "null" : string.Join(" ", args));
        }

        [TestMethod]
        public void ServerGuard_AppliesToTools()
        {
            Assert.AreEqual(ServerDecision.RefusedServer, ServerPolicy.Decide(PortableToolPolicy.Command, ServerPolicy.ProductServer, false, false));
            Assert.AreEqual(ServerDecision.Allowed, ServerPolicy.Decide(PortableToolPolicy.Command, ServerPolicy.ProductWorkstation, false, false));
            Assert.AreEqual(ServerDecision.RefusedDomainController, ServerPolicy.Decide(PortableToolPolicy.Command, ServerPolicy.ProductDomainController, true, false));
        }

        [TestMethod]
        public void RequestFrom_Settings()
        {
            PortableTool uv = PortableToolPolicy.Find("uninstallview");
            PortableToolRequest r = PortableToolReports.RequestFrom(new UserSettings { IncludeServers = false, AllowDomainControllers = true, ToolTimeoutSeconds = 99999 }, uv);
            Assert.IsFalse(r.AllowServers || r.AllowDomainControllers);
            Assert.AreEqual(PortableToolPolicy.MaxTimeoutSeconds, r.TimeoutSeconds);
            Assert.AreEqual(PortableToolPolicy.DefaultTimeoutSeconds, new UserSettings().ToolTimeoutSeconds);
        }

        // ---- Folder and signature -----------------------------------------------------------

        [TestMethod]
        public void Locate_FolderThenProductSubFolder()
        {
            PortableTool uv = PortableToolPolicy.Find("uninstallview");
            string folder = Path.Combine(Path.GetTempPath(), "Tools");
            string direct = Path.Combine(folder, "UninstallView.exe");
            string sub = Path.Combine(folder, "UninstallView", "UninstallView.exe");
            Assert.AreEqual(direct, PortableToolReports.Locate(folder, uv, p => true));
            Assert.AreEqual(sub, PortableToolReports.Locate(folder, uv, p => p == sub));
            Assert.IsNull(PortableToolReports.Locate(folder, uv, p => false));
            Assert.IsNull(PortableToolReports.Locate(" ", uv, p => true));
        }

        [TestMethod]
        public void Check_SignatureOfTheTablePublisher()
        {
            PortableTool uv = PortableToolPolicy.Find("uninstallview");
            string folder = Path.Combine(Path.GetTempPath(), "Tools");
            Assert.IsNull(PortableToolReports.Check(folder, uv, p => true, p => Signature(AuthenticodeStatus.Valid, NirSoftSubject)));
            Assert.IsNotNull(PortableToolReports.Check("", uv, p => true, p => Signature(AuthenticodeStatus.Valid, NirSoftSubject)));
            Assert.IsNotNull(PortableToolReports.Check(folder, uv, p => false, p => Signature(AuthenticodeStatus.Valid, NirSoftSubject)));
            Assert.IsNotNull(PortableToolReports.Check(folder, uv, p => true, p => Signature(AuthenticodeStatus.NotSigned, null)), "unsigned");
            Assert.IsNotNull(PortableToolReports.Check(folder, uv, p => true, p => Signature(AuthenticodeStatus.Valid, "CN=Other, O=Example Corp")), "other signer");
            Assert.IsNotNull(PortableToolReports.Check(folder, uv, p => true, p => Signature(AuthenticodeStatus.HashMismatch, NirSoftSubject)), "tampered");
        }

        // ---- CSV reports ---------------------------------------------------------------------

        const string Sample =
            "Display Name,Display Version,Publisher,Uninstall String\r\n" +
            "Example App,1.2.3,Example Corp,\"MsiExec.exe /X{00000000-0000-0000-0000-000000000001}\"\r\n" +
            "\"App, with comma\",2.0,\"Vendor \"\"Quoted\"\"\",\"line one\r\nline two\"\r\n" +
            "\r\n" +
            "Short row,3.0\r\n";

        [TestMethod]
        public void Parse_QuotedFieldsLineBreaksShortRows()
        {
            CsvReport r = CsvReport.Parse(Sample);
            CollectionAssert.AreEqual(new[] { "Display Name", "Display Version", "Publisher", "Uninstall String" }, r.Headers);
            Assert.AreEqual(3, r.Rows.Count, "empty line skipped");
            Assert.AreEqual("App, with comma", r.Rows[1][0]);
            Assert.AreEqual("Vendor \"Quoted\"", r.Rows[1][2]);
            Assert.AreEqual("line one\r\nline two", r.Rows[1][3]);
            Assert.AreEqual("", r.Rows[2][3], "padded");
            Assert.AreEqual(0, CsvReport.Parse("").Rows.Count);
            Assert.AreEqual(0, CsvReport.Parse(null).Headers.Count);
        }

        [TestMethod]
        public void Decode_ByteOrderMarks()
        {
            string text = "Title,KB Number\r\nMise à jour,KB0000001\r\n";
            Assert.AreEqual(text, CsvReport.Decode(new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes(text)).ToArray()));
            Assert.AreEqual(text, CsvReport.Decode(Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(text)).ToArray()));
            Assert.AreEqual(text, CsvReport.Decode(Encoding.Default.GetBytes(text)), "no mark: ANSI");
            Assert.AreEqual("", CsvReport.Decode(null));
        }

        [TestMethod]
        public void ToHtml_Encoded()
        {
            CsvReport r = CsvReport.Parse("Name\r\n<script>alert(1)</script>\r\n");
            string html = r.ToHtml("UninstallView - PC01");
            StringAssert.Contains(html, "&lt;script&gt;");
            Assert.IsFalse(html.Contains("<script"));
            StringAssert.Contains(html, "Content-Security-Policy");
        }

        [TestMethod]
        public void Combine_ComputerFirstColumnUnionAndFailures()
        {
            CsvReport a = CsvReport.Parse("Name,Version\r\nApp A,1\r\nApp B,2\r\n");
            CsvReport b = CsvReport.Parse("Name,Publisher\r\nApp C,Example Corp\r\n");
            InventoryTable t = CsvReport.Combine(new[]
            {
                new KeyValuePair<string, CsvReport>("PC01", a),
                new KeyValuePair<string, CsvReport>("PC02", b),
                new KeyValuePair<string, CsvReport>("PC03", null)
            }, new Dictionary<string, string> { { "PC03", "Timed out" } });
            Assert.AreEqual(5, t.Headers.Count);
            CollectionAssert.AreEqual(new[] { "Name", "Version", "Publisher" }, t.Headers.Skip(1).Take(3).ToList());
            Assert.AreEqual(4, t.Rows.Count);
            Assert.AreEqual("PC01", t.Rows[1][0]);
            Assert.AreEqual("App B", t.Rows[1][1]);
            Assert.IsNull(t.Rows[2][2], "PC02 has no Version");
            Assert.AreEqual("Example Corp", t.Rows[2][3]);
            Assert.AreEqual("PC03", t.Rows[3][0]);
            Assert.AreEqual("Timed out", t.Rows[3][4]);
        }
    }
}
