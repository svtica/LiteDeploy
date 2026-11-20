// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    InventoryTests.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-20
// Purpose: Unit tests of the hardware inventory: values kept from the worker
//          lines, the table, the CSV and the Excel workbook (read back from the
//          zip), and the numeric sort of the grid. Fictitious values only.
// -----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LiteDeploy.Tests
{
    [TestClass]
    public class InventoryTests
    {
        static WorkerMessage Line(params string[] keyValues)
        {
            return WorkerProtocol.Parse(WorkerProtocol.Format(keyValues));
        }

        static ComputerUpdateState Sample()
        {
            UpdateStateStore store = new UpdateStateStore();
            store.Apply("PC01", new List<WorkerMessage>
            {
                Line("type", "info", "manufacturer", "Contoso", "model", "Desk 100", "serial", "SN0001", "biosdate", "2021-03-15",
                    "cpu", "Example CPU 3.0GHz", "sockets", "1", "cores", "4", "threads", "8", "ram", "16", "disk", "238", "diskfree", "85.2")
            }, null, new DateTime(2026, 11, 20, 9, 0, 0));
            return store.Find("PC01");
        }

        [TestMethod]
        public void State_HardwareFromWorkerLines()
        {
            ComputerUpdateState c = Sample();
            Assert.AreEqual("Contoso", c.Manufacturer);
            Assert.AreEqual("Desk 100", c.Model);
            Assert.AreEqual("SN0001", c.Serial);
            Assert.AreEqual("2021-03-15", c.BiosDate);
            Assert.AreEqual("16", c.RamGb);
            Assert.AreEqual("85.2", c.DiskFreeGb);
            Assert.AreEqual(new DateTime(2026, 11, 20, 9, 0, 0), c.InventoryDate);

            // An older worker (no hardware): kept as is
            UpdateStateStore store = new UpdateStateStore();
            store.Computers.Add(c);
            store.Apply("PC01", new List<WorkerMessage> { Line("type", "start", "users", "") }, null, new DateTime(2026, 11, 21));
            Assert.AreEqual("Desk 100", store.Find("PC01").Model);
            Assert.AreEqual(new DateTime(2026, 11, 20, 9, 0, 0), store.Find("PC01").InventoryDate);
        }

        [TestMethod]
        public void YearAndCores()
        {
            Assert.AreEqual("2021", InventoryTable.Year("2021-03-15"));
            Assert.AreEqual("", InventoryTable.Year(null));
            Assert.AreEqual("", InventoryTable.Year("n/a"));
            Assert.AreEqual("4/8", InventoryTable.CoresText(Sample()));
            Assert.AreEqual("", InventoryTable.CoresText(null));
        }

        static InventoryTable Table()
        {
            return InventoryTable.Build(new[]
            {
                new KeyValuePair<ComputerRow, ComputerUpdateState>(new ComputerRow { Name = "PC01", Users = "EXAMPLE\\User1", Os = "Windows 11; Pro" }, Sample()),
                new KeyValuePair<ComputerRow, ComputerUpdateState>(new ComputerRow { Name = "PC02" }, null)
            });
        }

        [TestMethod]
        public void Csv_SeparatorQuotingAndNumbers()
        {
            InventoryTable t = Table();
            Assert.AreEqual(2, t.Rows.Count);
            String[] lines = t.ToCsv().Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
            Assert.AreEqual(3, lines.Length);
            StringAssert.StartsWith(lines[1], "PC01;;EXAMPLE\\User1;\"Windows 11; Pro\";");
            StringAssert.Contains(lines[1], ";2021;");
            StringAssert.Contains(lines[1], ";16;238;85.2;");
            StringAssert.StartsWith(lines[2], "PC02;");
            Assert.AreEqual("\"a \"\"b\"\"\"", InventoryTable.CsvField("a \"b\"", ';'));
            Assert.AreEqual("\" x\"", InventoryTable.CsvField(" x", ';'));
        }

        [TestMethod]
        public void ColumnNames()
        {
            Assert.AreEqual("A", InventoryTable.ColumnName(0));
            Assert.AreEqual("Z", InventoryTable.ColumnName(25));
            Assert.AreEqual("AA", InventoryTable.ColumnName(26));
            Assert.AreEqual("AZ", InventoryTable.ColumnName(51));
            Assert.AreEqual("BA", InventoryTable.ColumnName(52));
        }

        [TestMethod]
        public void Xlsx_IsAWorkbookWithTheValues()
        {
            using (MemoryStream stream = new MemoryStream())
            {
                Table().WriteXlsx(stream, "Inventory/2026");
                stream.Position = 0;
                using (ZipArchive zip = new ZipArchive(stream, ZipArchiveMode.Read))
                {
                    foreach (String part in new[] { "[Content_Types].xml", "_rels/.rels", "xl/workbook.xml", "xl/_rels/workbook.xml.rels", "xl/styles.xml", "xl/worksheets/sheet1.xml" })
                        Assert.IsNotNull(zip.GetEntry(part), part);

                    XmlDocument workbook = new XmlDocument();
                    using (Stream s = zip.GetEntry("xl/workbook.xml").Open()) workbook.Load(s);
                    StringAssert.Contains(workbook.OuterXml, "name=\"Inventory2026\"");

                    XmlDocument sheet = new XmlDocument();
                    using (Stream s = zip.GetEntry("xl/worksheets/sheet1.xml").Open()) sheet.Load(s);
                    XmlNamespaceManager ns = new XmlNamespaceManager(sheet.NameTable);
                    ns.AddNamespace("m", "http://schemas.openxmlformats.org/spreadsheetml/2006/main");
                    Assert.AreEqual(3, sheet.SelectNodes("//m:row", ns).Count);
                    Assert.AreEqual("1", sheet.SelectSingleNode("//m:c[@r='A1']/@s", ns).Value, "header in bold");
                    Assert.AreEqual("PC01", sheet.SelectSingleNode("//m:c[@r='A2']//m:t", ns).InnerText);
                    XmlNode ram = sheet.SelectNodes("//m:row[@r='2']/m:c", ns).Cast<XmlNode>().First(c => c.Attributes["t"] == null && c.InnerText == "16");
                    Assert.IsNotNull(ram, "a number is a number");
                }
            }
        }

        [TestMethod]
        public void Sort_NumbersByValue()
        {
            Assert.IsTrue(ListViewColumnSorter.CompareTexts("8", "16") < 0);
            Assert.IsTrue(ListViewColumnSorter.CompareTexts("85.2", "9") > 0);
            Assert.IsTrue(ListViewColumnSorter.CompareTexts("pc02", "PC10") < 0);
            Assert.AreEqual(0, ListViewColumnSorter.CompareTexts("pc01", "PC01"));
        }
    }
}
