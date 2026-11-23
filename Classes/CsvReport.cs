// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    CsvReport.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-23
// Purpose: CSV report of a portable tool (PortableToolPolicy): read from its
//          bytes (encoding from the byte order mark: UTF-8 or UTF-16, else the
//          ANSI code page of Windows), comma separated, fields in double quotes
//          may hold commas, quotes ("") and line breaks; the first line gives
//          the column names. Gives a plain HTML view (every value encoded, no
//          script) and, for several computers, one table of every row with
//          the computer in the first column (InventoryTable: xlsx or CSV).
//          Pure logic, unit-tested.
// -----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;

namespace LiteDeploy
{
    internal sealed class CsvReport
    {
        public readonly List<String> Headers = new List<String>();
        public readonly List<String[]> Rows = new List<String[]>();

        /// <summary>The text of a report file: UTF-8 or UTF-16 when it starts with their byte order mark, else ANSI.</summary>
        public static String Decode(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return "";
            using (StreamReader reader = new StreamReader(new MemoryStream(bytes), Encoding.Default, true))
                return reader.ReadToEnd();
        }

        /// <summary>Reads the CSV text; the first record gives the headers. Short records are padded, long ones cut.</summary>
        public static CsvReport Parse(String text)
        {
            CsvReport report = new CsvReport();
            List<List<String>> records = Records(text ?? "");
            if (records.Count == 0) return report;
            report.Headers.AddRange(records[0].Select(h => h.Trim()));
            foreach (List<String> r in records.Skip(1))
            {
                if (r.Count == 1 && r[0].Length == 0) continue;   // empty line
                String[] row = new String[report.Headers.Count];
                for (int i = 0; i < row.Length; i++) row[i] = i < r.Count ? r[i] : "";
                report.Rows.Add(row);
            }
            return report;
        }

        static List<List<String>> Records(String text)
        {
            List<List<String>> records = new List<List<String>>();
            List<String> record = new List<String>();
            StringBuilder field = new StringBuilder();
            bool quoted = false, any = false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                any = true;
                if (quoted)
                {
                    if (c == '"')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                        else quoted = false;
                    }
                    else field.Append(c);
                    continue;
                }
                if (c == '"') quoted = true;
                else if (c == ',') { record.Add(field.ToString()); field.Clear(); }
                else if (c == '\r' || c == '\n')
                {
                    if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                    record.Add(field.ToString());
                    field.Clear();
                    records.Add(record);
                    record = new List<String>();
                    any = false;
                }
                else field.Append(c);
            }
            if (any || field.Length > 0 || record.Count > 0)
            {
                record.Add(field.ToString());
                records.Add(record);
            }
            return records;
        }

        /// <summary>A plain HTML page: one table, every text encoded, no script.</summary>
        public String ToHtml(String title)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("<!DOCTYPE html>\n<html><head><meta charset=\"utf-8\">");
            sb.Append("<meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; style-src 'unsafe-inline'\">");
            sb.Append("<title>").Append(E(title)).Append("</title>");
            sb.Append("<style>body{font-family:Segoe UI,sans-serif;font-size:13px;margin:16px}h1{font-size:20px}" +
                      "table{border-collapse:collapse}td,th{border:1px solid #ccc;padding:2px 6px;text-align:left;vertical-align:top}" +
                      "th{background:#eee;position:sticky;top:0}</style></head><body>\n");
            sb.Append("<h1>").Append(E(title)).Append("</h1>\n<p>").Append(Rows.Count).Append("</p>\n<table><tr>");
            foreach (String h in Headers) sb.Append("<th>").Append(E(h)).Append("</th>");
            sb.Append("</tr>\n");
            foreach (String[] row in Rows)
            {
                sb.Append("<tr>");
                foreach (String v in row) sb.Append("<td>").Append(E(v)).Append("</td>");
                sb.Append("</tr>\n");
            }
            sb.Append("</table></body></html>\n");
            return sb.ToString();
        }

        /// <summary>
        /// Every row of several computers in one table: the computer first, then the union of the columns (in the
        /// order they are met); a computer without a report gets one row with the failure in the last column.
        /// </summary>
        public static InventoryTable Combine(IEnumerable<KeyValuePair<String, CsvReport>> reports, IDictionary<String, String> failures)
        {
            List<KeyValuePair<String, CsvReport>> list = (reports ?? Enumerable.Empty<KeyValuePair<String, CsvReport>>()).ToList();
            List<String> columns = new List<String>();
            foreach (KeyValuePair<String, CsvReport> r in list.Where(r => r.Value != null))
                foreach (String h in r.Value.Headers)
                    if (!columns.Contains(h, StringComparer.OrdinalIgnoreCase)) columns.Add(h);

            InventoryTable t = new InventoryTable();
            t.Headers.Add(Localization.T("InventoryTable.Computer", "Computer"));
            t.Headers.AddRange(columns);
            t.Headers.Add(Localization.T("SiwReport.Failure", "Not read"));
            foreach (KeyValuePair<String, CsvReport> r in list)
            {
                String failure;
                if (r.Value == null)
                {
                    Object[] empty = new Object[columns.Count + 2];
                    empty[0] = r.Key;
                    if (failures != null && failures.TryGetValue(r.Key, out failure)) empty[columns.Count + 1] = failure;
                    t.Rows.Add(empty);
                    continue;
                }
                int[] index = columns.Select(c => r.Value.Headers.FindIndex(h => String.Equals(h, c, StringComparison.OrdinalIgnoreCase))).ToArray();
                foreach (String[] row in r.Value.Rows)
                {
                    Object[] line = new Object[columns.Count + 2];
                    line[0] = r.Key;
                    for (int i = 0; i < columns.Count; i++)
                        line[i + 1] = index[i] >= 0 && !String.IsNullOrEmpty(row[index[i]]) ? row[index[i]] : null;
                    t.Rows.Add(line);
                }
            }
            return t;
        }

        static String E(String text)
        {
            return WebUtility.HtmlEncode(text ?? "");
        }
    }
}
