// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    SiwReport.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-23
// Purpose: XML report of SIW (System Information for Windows), as written by
//          "/log:xml": <report computer_name= date=> then <software>,
//          <hardware>, <network>, each with <page Title=> holding <item
//          Property=> groups of <item Property= Value=> lines (a line can also
//          sit directly under the page). Read without DTD or external entity.
//          Gives the values of "Page/Group/Property" fields (summary of
//          several reports, the SiwSummaryFields setting) and a plain HTML view
//          of the report (every value encoded). Pure logic, unit-tested.
// -----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Xml;

namespace LiteDeploy
{
    internal sealed class SiwLine
    {
        public String Property = "";
        public String Value = "";
    }

    internal sealed class SiwGroup
    {
        /// <summary>Property of the group item ("Computer", "Disk 0", "Monitor"); "" for lines directly under the page.</summary>
        public String Name = "";
        public readonly List<SiwLine> Lines = new List<SiwLine>();
    }

    internal sealed class SiwPage
    {
        /// <summary>software, hardware or network.</summary>
        public String Section = "";
        public String Title = "";
        public readonly List<SiwGroup> Groups = new List<SiwGroup>();
    }

    /// <summary>One field of the summary: page, group ("*": any group) and property, compared without regard to case.</summary>
    internal sealed class SiwField
    {
        public String Page;
        public String Group;
        public String Property;

        public override String ToString()
        {
            return Page + "/" + Group + "/" + Property;
        }
    }

    internal sealed class SiwReport
    {
        /// <summary>Fields of the summary when the SiwSummaryFields setting is empty.</summary>
        public const String DefaultSummaryFields =
            "System Summary/Computer/Manufacturer;System Summary/Computer/Family;System Summary/Computer/Model;" +
            "System Summary/Computer/Serial Number;System Summary/Computer/TPM 2.0;System Summary/Computer/Secure Boot State;" +
            "System Summary/Computer/Firmware Type;BIOS/BIOS Properties/BIOS Version;BIOS/BIOS Properties/BIOS Date;" +
            "CPU/*/CPU Name;CPU/Summary/Number of Cores;Memory/Memory Summary/Installed Memory;Memory/Memory Summary/Memory Slots;" +
            "Memory/Memory Summary/Memory Type;Storage Devices/*/Model;Storage Devices/*/Size;Storage Devices/*/Power-On Hours Count;" +
            "Storage Devices/*/Status;Operating System/*/Product Name;Operating System/*/Version;Operating System/*/Installation Time;" +
            "Video/Monitor/Model;Video/Monitor/Serial Number";

        /// <summary>Separator of several matches of one field (several disks or monitors).</summary>
        public const String Joiner = " | ";

        public String Computer = "";
        /// <summary>Date of the report as SIW wrote it ("yyyy-MM-dd HH:mm:ss", then a localized date in parentheses).</summary>
        public String Date = "";
        /// <summary>Title of the report: version and edition of SIW.</summary>
        public String Title = "";
        public readonly List<SiwPage> Pages = new List<SiwPage>();

        /// <summary>The date without the localized part: its first 19 characters when they are a date and time.</summary>
        public String ShortDate
        {
            get
            {
                String d = (Date ?? "").Trim();
                DateTime parsed;
                if (d.Length >= 19 && DateTime.TryParseExact(d.Substring(0, 19), "yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.None, out parsed))
                    return d.Substring(0, 19);
                return d;
            }
        }

        /// <summary>Reads a report (text of the XML file). Throws XmlException or InvalidDataException when it is not a SIW report.</summary>
        public static SiwReport Parse(String xml)
        {
            XmlDocument doc = new XmlDocument { XmlResolver = null };
            XmlReaderSettings settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
            using (XmlReader reader = XmlReader.Create(new StringReader(xml ?? ""), settings))
                doc.Load(reader);
            XmlElement root = doc.DocumentElement;
            if (root == null || root.Name != "report") throw new InvalidDataException("Not a SIW XML report (no <report> root).");

            SiwReport report = new SiwReport
            {
                Computer = root.GetAttribute("computer_name"),
                Date = root.GetAttribute("date"),
                Title = root.GetAttribute("Title")
            };
            foreach (XmlElement section in root.ChildNodes.OfType<XmlElement>())
            {
                foreach (XmlElement pageNode in section.ChildNodes.OfType<XmlElement>().Where(e => e.Name == "page"))
                {
                    SiwPage page = new SiwPage { Section = section.Name, Title = pageNode.GetAttribute("Title") };
                    SiwGroup loose = null;
                    foreach (XmlElement item in pageNode.ChildNodes.OfType<XmlElement>().Where(e => e.Name == "item"))
                    {
                        List<XmlElement> children = item.ChildNodes.OfType<XmlElement>().Where(e => e.Name == "item").ToList();
                        if (children.Count == 0)
                        {
                            // A line directly under the page
                            if (loose == null) { loose = new SiwGroup(); page.Groups.Add(loose); }
                            loose.Lines.Add(Line(item));
                            continue;
                        }
                        SiwGroup group = new SiwGroup { Name = item.GetAttribute("Property") };
                        foreach (XmlElement child in children) group.Lines.Add(Line(child));
                        page.Groups.Add(group);
                    }
                    report.Pages.Add(page);
                }
            }
            return report;
        }

        static SiwLine Line(XmlElement item)
        {
            return new SiwLine { Property = item.GetAttribute("Property"), Value = item.GetAttribute("Value") };
        }

        /// <summary>
        /// Parses "Page/Group/Property" fields separated by ';' (empty entries and entries without three parts are
        /// skipped). Empty or null: the default fields.
        /// </summary>
        public static List<SiwField> ParseFields(String setting)
        {
            String text = String.IsNullOrWhiteSpace(setting) ? DefaultSummaryFields : setting;
            List<SiwField> fields = new List<SiwField>();
            foreach (String entry in text.Split(';'))
            {
                String[] parts = entry.Split('/');
                if (parts.Length != 3) continue;
                SiwField f = new SiwField { Page = parts[0].Trim(), Group = parts[1].Trim(), Property = parts[2].Trim() };
                if (f.Page.Length == 0 || f.Group.Length == 0 || f.Property.Length == 0) continue;
                fields.Add(f);
            }
            return fields;
        }

        /// <summary>The values of a field (every matching line, in the order of the report; empty values skipped), joined; "" when none.</summary>
        public String Value(SiwField field)
        {
            List<String> values = new List<String>();
            foreach (SiwPage page in Pages.Where(p => Same(p.Title, field.Page)))
                foreach (SiwGroup group in page.Groups.Where(g => field.Group == "*" || Same(g.Name, field.Group)))
                    foreach (SiwLine line in group.Lines.Where(l => Same(l.Property, field.Property)))
                        if (!String.IsNullOrWhiteSpace(line.Value)) values.Add(line.Value.Trim());
            return String.Join(Joiner, values);
        }

        static bool Same(String a, String b)
        {
            return String.Equals((a ?? "").Trim(), (b ?? "").Trim(), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Summary of several computers: one row per computer (its name, then the value of each field, or the
        /// failure in the last column when it has no report). Headers: the property, with the page when two
        /// fields share a property.
        /// </summary>
        public static InventoryTable Summary(IList<SiwField> fields, IEnumerable<KeyValuePair<String, SiwReport>> reports,
            IDictionary<String, String> failures)
        {
            InventoryTable t = new InventoryTable();
            t.Headers.Add(Localization.T("InventoryTable.Computer", "Computer"));
            foreach (SiwField f in fields)
            {
                bool shared = fields.Count(o => Same(o.Property, f.Property)) > 1;
                t.Headers.Add(shared ? f.Page + " - " + f.Property : f.Property);
            }
            t.Headers.Add(Localization.T("SiwReport.Date", "Report date"));
            t.Headers.Add(Localization.T("SiwReport.Failure", "Not read"));
            foreach (KeyValuePair<String, SiwReport> entry in reports ?? Enumerable.Empty<KeyValuePair<String, SiwReport>>())
            {
                Object[] row = new Object[fields.Count + 3];
                row[0] = entry.Key;
                if (entry.Value != null)
                {
                    for (int i = 0; i < fields.Count; i++)
                    {
                        String v = entry.Value.Value(fields[i]);
                        row[i + 1] = v.Length == 0 ? null : v;
                    }
                    row[fields.Count + 1] = entry.Value.ShortDate;
                }
                String failure;
                if (failures != null && failures.TryGetValue(entry.Key, out failure)) row[fields.Count + 2] = failure;
                t.Rows.Add(row);
            }
            return t;
        }

        /// <summary>A plain HTML page of the report: one table per page, every text encoded, no script.</summary>
        public String ToHtml()
        {
            StringBuilder sb = new StringBuilder();
            String title = Localization.F("SiwReport.HtmlTitle", "SIW report - {0}", Computer);
            sb.Append("<!DOCTYPE html>\n<html><head><meta charset=\"utf-8\">");
            sb.Append("<meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; style-src 'unsafe-inline'\">");
            sb.Append("<title>").Append(E(title)).Append("</title>");
            sb.Append("<style>body{font-family:Segoe UI,sans-serif;font-size:13px;margin:16px}h1{font-size:20px}h2{font-size:16px;margin-top:24px;border-bottom:1px solid #999}" +
                      "table{border-collapse:collapse;margin-bottom:12px}td,th{border:1px solid #ccc;padding:2px 6px;text-align:left;vertical-align:top}" +
                      "th{background:#eee}td.p{white-space:nowrap;color:#444}nav a{margin-right:10px}</style></head><body>\n");
            sb.Append("<h1>").Append(E(title)).Append("</h1>\n<p>").Append(E(Title)).Append(" - ").Append(E(ShortDate)).Append("</p>\n<nav>");
            for (int i = 0; i < Pages.Count; i++)
                sb.Append("<a href=\"#p").Append(i).Append("\">").Append(E(Pages[i].Title)).Append("</a>");
            sb.Append("</nav>\n");
            for (int i = 0; i < Pages.Count; i++)
            {
                SiwPage page = Pages[i];
                sb.Append("<h2 id=\"p").Append(i).Append("\">").Append(E(page.Title)).Append("</h2>\n<table>");
                foreach (SiwGroup group in page.Groups)
                {
                    if (group.Name.Length > 0) sb.Append("<tr><th colspan=\"2\">").Append(E(group.Name)).Append("</th></tr>");
                    foreach (SiwLine line in group.Lines)
                        sb.Append("<tr><td class=\"p\">").Append(E(line.Property)).Append("</td><td>").Append(E(line.Value)).Append("</td></tr>");
                }
                sb.Append("</table>\n");
            }
            sb.Append("</body></html>\n");
            return sb.ToString();
        }

        static String E(String text)
        {
            return WebUtility.HtmlEncode(text ?? "");
        }
    }
}
