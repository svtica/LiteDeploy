// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    InventoryTable.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-20
// Purpose: Inventory of the computers of the grid as a table (one row per
//          computer: Active Directory, state, user, operating system, hardware
//          read by the worker, network), written as a CSV file (separator ";",
//          UTF-8 with BOM: Excel opens it directly) or as an Excel workbook
//          (.xlsx, written here with System.IO.Compression: no third-party
//          library; header in bold, frozen, numbers as numbers). Pure logic,
//          unit-tested.
// -----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml;

namespace LiteDeploy
{
    internal sealed class InventoryTable
    {
        /// <summary>Column titles, in the language of the interface.</summary>
        public readonly List<String> Headers = new List<String>();
        /// <summary>One array per computer: String, Double (a number) or null (empty).</summary>
        public readonly List<Object[]> Rows = new List<Object[]>();

        /// <summary>The year of a BIOS date ("2021-03-15" gives "2021"); "" when unknown.</summary>
        public static String Year(String biosDate)
        {
            return !String.IsNullOrEmpty(biosDate) && biosDate.Length >= 4 && biosDate.Take(4).All(Char.IsDigit) ? biosDate.Substring(0, 4) : "";
        }

        /// <summary>"4/8": cores / threads; "" when unknown.</summary>
        public static String CoresText(ComputerUpdateState c)
        {
            if (c == null || String.IsNullOrEmpty(c.Cores)) return "";
            return String.IsNullOrEmpty(c.Threads) ? c.Cores : c.Cores + "/" + c.Threads;
        }

        static Object Number(String text)
        {
            double d;
            if (String.IsNullOrWhiteSpace(text)) return null;
            return Double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out d) ? (Object)d : text;
        }

        static Object Date(DateTime? time, String format)
        {
            return time.HasValue ? time.Value.ToString(format, CultureInfo.InvariantCulture) : null;
        }

        /// <summary>The table of these computers (their row of the grid and their saved state, which may be null).</summary>
        public static InventoryTable Build(IEnumerable<KeyValuePair<ComputerRow, ComputerUpdateState>> computers)
        {
            InventoryTable t = new InventoryTable();
            t.Headers.AddRange(new[]
            {
                Localization.T("InventoryTable.Computer", "Computer"),
                Localization.T("InventoryTable.State", "State"),
                Localization.T("InventoryTable.User", "User"),
                Localization.T("InventoryTable.Os", "Operating system"),
                Localization.T("InventoryTable.Ou", "OU"),
                Localization.T("InventoryTable.LastLogon", "Last logon (AD)"),
                Localization.T("InventoryTable.LastScan", "Last scan"),
                Localization.T("InventoryTable.Manufacturer", "Manufacturer"),
                Localization.T("InventoryTable.Model", "Model"),
                Localization.T("InventoryTable.Serial", "Serial number"),
                Localization.T("InventoryTable.BiosDate", "BIOS date"),
                Localization.T("InventoryTable.Year", "Year (BIOS)"),
                Localization.T("InventoryTable.Cpu", "Processor"),
                Localization.T("InventoryTable.Sockets", "Processors"),
                Localization.T("InventoryTable.Cores", "Cores"),
                Localization.T("InventoryTable.Threads", "Threads"),
                Localization.T("InventoryTable.Ram", "RAM (GB)"),
                Localization.T("InventoryTable.Disk", "Disk (GB)"),
                Localization.T("InventoryTable.DiskFree", "Free (GB)"),
                Localization.T("InventoryTable.Ips", "IPv4 addresses"),
                Localization.T("InventoryTable.Macs", "MAC addresses"),
                Localization.T("InventoryTable.TeamViewer", "TeamViewer ID"),
                Localization.T("InventoryTable.InventoryDate", "Inventory read")
            });
            foreach (KeyValuePair<ComputerRow, ComputerUpdateState> entry in computers ?? Enumerable.Empty<KeyValuePair<ComputerRow, ComputerUpdateState>>())
            {
                ComputerRow r = entry.Key;
                ComputerUpdateState c = entry.Value;
                if (r == null) continue;
                t.Rows.Add(new Object[]
                {
                    r.Name,
                    r.Online == true ? Localization.T("ComputerGrid.Online", "Online") : r.Online == false ? Localization.T("ComputerGrid.Offline", "Offline") : null,
                    r.Users,
                    r.Os,
                    String.IsNullOrEmpty(r.Ou) ? null : AdComputer.ShortOu(r.Ou),
                    Date(r.LastLogon, "yyyy-MM-dd"),
                    Date(r.LastScan, "yyyy-MM-dd HH:mm"),
                    c?.Manufacturer,
                    c?.Model,
                    c?.Serial,
                    c?.BiosDate,
                    Number(Year(c?.BiosDate)),
                    c?.Cpu,
                    Number(c?.Sockets),
                    Number(c?.Cores),
                    Number(c?.Threads),
                    Number(c?.RamGb),
                    Number(c?.DiskGb),
                    Number(c?.DiskFreeGb),
                    c?.Ips,
                    c?.Macs,
                    c?.TeamViewerId,
                    Date(c?.InventoryDate, "yyyy-MM-dd HH:mm")
                });
            }
            return t;
        }

        static String Text(Object value)
        {
            if (value == null) return "";
            if (value is double) return ((double)value).ToString("0.##", CultureInfo.InvariantCulture);
            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        /// <summary>A CSV field: quoted when it holds the separator, a quote, a line break or edge spaces.</summary>
        public static String CsvField(String value, char separator)
        {
            if (String.IsNullOrEmpty(value)) return "";
            bool quote = value.IndexOfAny(new[] { separator, '"', '\r', '\n' }) >= 0 || value != value.Trim();
            return quote ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
        }

        /// <summary>The table as CSV (separator ";", lines ended by CRLF).</summary>
        public String ToCsv()
        {
            const char Separator = ';';
            StringBuilder sb = new StringBuilder();
            sb.Append(String.Join(Separator.ToString(), Headers.Select(h => CsvField(h, Separator)))).Append("\r\n");
            foreach (Object[] row in Rows)
                sb.Append(String.Join(Separator.ToString(), row.Select(v => CsvField(Text(v), Separator)))).Append("\r\n");
            return sb.ToString();
        }

        /// <summary>Writes the CSV file (UTF-8 with BOM, for Excel).</summary>
        public void SaveCsv(String path)
        {
            File.WriteAllText(path, ToCsv(), new UTF8Encoding(true));
        }

        // ---- Excel workbook (Office Open XML, one sheet) ------------------------------------

        const String MainNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        /// <summary>"A", "B", ... "Z", "AA": the letters of a column (0-based).</summary>
        public static String ColumnName(int index)
        {
            String name = "";
            for (int n = index + 1; n > 0; n = (n - 1) / 26) name = (char)('A' + (n - 1) % 26) + name;
            return name;
        }

        // Characters an XML document cannot hold are dropped (a value read on a computer can have some)
        static String XmlSafe(String text)
        {
            return new String(text.Where(XmlConvert.IsXmlChar).ToArray());
        }

        /// <summary>Writes an Excel workbook (.xlsx): one sheet, header in bold and frozen.</summary>
        public void SaveXlsx(String path, String sheetName)
        {
            using (FileStream file = new FileStream(path, FileMode.Create, FileAccess.Write))
                WriteXlsx(file, sheetName);
        }

        public void WriteXlsx(Stream output, String sheetName)
        {
            String sheet = XmlSafe(String.IsNullOrWhiteSpace(sheetName) ? "Inventory" : sheetName);
            sheet = new String(sheet.Where(ch => "[]:*?/\\".IndexOf(ch) < 0).ToArray());
            if (sheet.Length > 31) sheet = sheet.Substring(0, 31);
            using (ZipArchive zip = new ZipArchive(output, ZipArchiveMode.Create, true))
            {
                Part(zip, "[Content_Types].xml",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                    "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
                    "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
                    "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
                    "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" +
                    "<Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>" +
                    "<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>" +
                    "</Types>");
                Part(zip, "_rels/.rels",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                    "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                    "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>" +
                    "</Relationships>");
                Part(zip, "xl/_rels/workbook.xml.rels",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                    "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                    "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/>" +
                    "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>" +
                    "</Relationships>");
                Part(zip, "xl/workbook.xml",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                    "<workbook xmlns=\"" + MainNs + "\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">" +
                    "<sheets><sheet name=\"" + System.Security.SecurityElement.Escape(sheet) + "\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>");
                Part(zip, "xl/styles.xml",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                    "<styleSheet xmlns=\"" + MainNs + "\">" +
                    "<fonts count=\"2\"><font><sz val=\"11\"/><name val=\"Calibri\"/></font><font><b/><sz val=\"11\"/><name val=\"Calibri\"/></font></fonts>" +
                    "<fills count=\"2\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill></fills>" +
                    "<borders count=\"1\"><border><left/><right/><top/><bottom/><diagonal/></border></borders>" +
                    "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>" +
                    "<cellXfs count=\"2\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/>" +
                    "<xf numFmtId=\"0\" fontId=\"1\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\"/></cellXfs>" +
                    "<cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles>" +
                    "</styleSheet>");

                ZipArchiveEntry entry = zip.CreateEntry("xl/worksheets/sheet1.xml");
                using (Stream s = entry.Open())
                using (XmlWriter w = XmlWriter.Create(s, new XmlWriterSettings { Encoding = new UTF8Encoding(false) }))
                {
                    w.WriteStartDocument(true);
                    w.WriteStartElement("worksheet", MainNs);
                    // Header row frozen
                    w.WriteStartElement("sheetViews", MainNs);
                    w.WriteStartElement("sheetView", MainNs);
                    w.WriteAttributeString("workbookViewId", "0");
                    w.WriteStartElement("pane", MainNs);
                    w.WriteAttributeString("ySplit", "1");
                    w.WriteAttributeString("topLeftCell", "A2");
                    w.WriteAttributeString("activePane", "bottomLeft");
                    w.WriteAttributeString("state", "frozen");
                    w.WriteEndElement();
                    w.WriteEndElement();
                    w.WriteEndElement();
                    w.WriteStartElement("sheetData", MainNs);
                    WriteRow(w, 1, Headers.Cast<Object>().ToArray(), true);
                    for (int i = 0; i < Rows.Count; i++) WriteRow(w, i + 2, Rows[i], false);
                    w.WriteEndElement();
                    w.WriteEndElement();
                    w.WriteEndDocument();
                }
            }
        }

        static void WriteRow(XmlWriter w, int number, Object[] values, bool header)
        {
            w.WriteStartElement("row", MainNs);
            w.WriteAttributeString("r", number.ToString(CultureInfo.InvariantCulture));
            for (int i = 0; i < values.Length; i++)
            {
                Object v = values[i];
                if (v == null) continue;
                w.WriteStartElement("c", MainNs);
                w.WriteAttributeString("r", ColumnName(i) + number.ToString(CultureInfo.InvariantCulture));
                if (header) w.WriteAttributeString("s", "1");
                if (v is double)
                {
                    w.WriteElementString("v", MainNs, ((double)v).ToString("R", CultureInfo.InvariantCulture));
                }
                else
                {
                    w.WriteAttributeString("t", "inlineStr");
                    w.WriteStartElement("is", MainNs);
                    w.WriteStartElement("t", MainNs);
                    String text = XmlSafe(Convert.ToString(v, CultureInfo.InvariantCulture));
                    if (text != text.Trim()) w.WriteAttributeString("xml", "space", null, "preserve");
                    w.WriteString(text);
                    w.WriteEndElement();
                    w.WriteEndElement();
                }
                w.WriteEndElement();
            }
            w.WriteEndElement();
        }

        static void Part(ZipArchive zip, String name, String content)
        {
            ZipArchiveEntry entry = zip.CreateEntry(name);
            using (StreamWriter writer = new StreamWriter(entry.Open(), new UTF8Encoding(false)))
                writer.Write(content);
        }
    }
}
