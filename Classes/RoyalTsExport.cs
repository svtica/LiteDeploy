// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    RoyalTsExport.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-18
// Purpose: "Export to Royal TS...": Remote Desktop connections of the
//          computers, for Royal TS (Royal Apps), in one of two formats:
//          - CSV (Data > Import > Comma Separated File): one header line
//            (Name, ComputerName, Description, Path), comma-separated, values
//            quoted when needed (RFC 4180), UTF-8 with a byte order mark so
//            accented names survive. The import wizard maps each column to a
//            property ($Name$, $ComputerName$...).
//          - RoyalJSON (.rjson document, or the content of a Dynamic Folder):
//            { "Name": ..., "Objects": [ { "Type": "RemoteDesktopConnection",
//            "Name", "ComputerName", "Description", "Path" } ] }.
//          Description: the operating system; Path: the OU as a folder path
//          (AdComputer.ShortOu). Pure logic (unit-tested).
// -----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace LiteDeploy
{
    /// <summary>One Remote Desktop connection to export.</summary>
    public sealed class RoyalTsConnection
    {
        public String Name;
        public String ComputerName;
        public String Description;
        /// <summary>Folder path, "/"-separated ("Workstations/Laptops"); empty: the root of the import.</summary>
        public String Path;
    }

    static public class RoyalTsExport
    {
        public const String CsvExtension = ".csv";
        public const String JsonExtension = ".rjson";

        static readonly String[] CsvHeader = { "Name", "ComputerName", "Description", "Path" };

        /// <summary>A connection from what is known of a computer (row may be null: the name only).</summary>
        static public RoyalTsConnection FromRow(String computer, ComputerRow row)
        {
            RoyalTsConnection c = new RoyalTsConnection { Name = computer, ComputerName = computer, Description = "", Path = "" };
            if (row != null)
            {
                c.Description = String.IsNullOrEmpty(row.Os) ? "" : row.Os + (String.IsNullOrEmpty(row.OsVersion) ? "" : " (" + row.OsVersion + ")");
                c.Path = String.IsNullOrEmpty(row.Ou) ? "" : AdComputer.ShortOu(row.Ou);
            }
            return c;
        }

        /// <summary>The CSV text: header, then one line per connection (CRLF line ends).</summary>
        static public String ToCsv(IEnumerable<RoyalTsConnection> connections)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(String.Join(",", CsvHeader.Select(CsvField))).Append("\r\n");
            foreach (RoyalTsConnection c in connections)
                sb.Append(String.Join(",", new[] { c.Name, c.ComputerName, c.Description, c.Path }.Select(CsvField))).Append("\r\n");
            return sb.ToString();
        }

        /// <summary>A CSV field: quoted (quotes doubled) when it holds a comma, a semicolon, a quote, a line break or edge spaces.</summary>
        static public String CsvField(String value)
        {
            String v = value ?? "";
            bool quote = v.IndexOfAny(new[] { ',', ';', '"', '\r', '\n' }) >= 0 || (v.Length > 0 && (v[0] == ' ' || v[v.Length - 1] == ' '));
            return quote ? "\"" + v.Replace("\"", "\"\"") + "\"" : v;
        }

        /// <summary>The RoyalJSON document (an .rjson file, or what a Dynamic Folder script returns).</summary>
        static public String ToRoyalJson(String documentName, IEnumerable<RoyalTsConnection> connections)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("{\r\n  \"Name\": ").Append(JsonString(documentName ?? "LiteDeploy")).Append(",\r\n  \"Objects\": [");
            bool first = true;
            foreach (RoyalTsConnection c in connections)
            {
                sb.Append(first ? "\r\n" : ",\r\n");
                first = false;
                sb.Append("    { \"Type\": \"RemoteDesktopConnection\", \"Name\": ").Append(JsonString(c.Name))
                  .Append(", \"ComputerName\": ").Append(JsonString(c.ComputerName));
                if (!String.IsNullOrEmpty(c.Description)) sb.Append(", \"Description\": ").Append(JsonString(c.Description));
                if (!String.IsNullOrEmpty(c.Path)) sb.Append(", \"Path\": ").Append(JsonString(c.Path));
                sb.Append(" }");
            }
            sb.Append(first ? "]\r\n}\r\n" : "\r\n  ]\r\n}\r\n");
            return sb.ToString();
        }

        /// <summary>A JSON string literal (quotes, backslashes and control characters escaped).</summary>
        static public String JsonString(String value)
        {
            StringBuilder sb = new StringBuilder("\"");
            foreach (char ch in value ?? "")
            {
                switch (ch)
                {
                    case '"': sb.Append("\\\""); break;
                    case (char)0x5C: sb.Append((char)0x5C).Append((char)0x5C); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (ch < 0x20) sb.Append("\\u").Append(((int)ch).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(ch);
                        break;
                }
            }
            return sb.Append('"').ToString();
        }

        /// <summary>Writes the file in the format of its extension (.rjson: RoyalJSON, else CSV), UTF-8 with a byte order mark for the CSV.</summary>
        static public void Save(String path, IEnumerable<RoyalTsConnection> connections)
        {
            List<RoyalTsConnection> list = connections.ToList();
            if (String.Equals(System.IO.Path.GetExtension(path), JsonExtension, StringComparison.OrdinalIgnoreCase))
                File.WriteAllText(path, ToRoyalJson(System.IO.Path.GetFileNameWithoutExtension(path), list), new UTF8Encoding(false));
            else
                File.WriteAllText(path, ToCsv(list), new UTF8Encoding(true));
        }
    }
}
