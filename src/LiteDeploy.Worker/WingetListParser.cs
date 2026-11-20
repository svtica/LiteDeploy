// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    WingetListParser.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-12
// Purpose: Reads the table printed by "winget list --id X -e": installed and
//          available version of one package. winget prints its headers in
//          the language of Windows, so the columns are found by position
//          (Name, Id, Version, [Available], Source), from the header line
//          that precedes the line of dashes. Pure logic, unit-tested.
// -----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Linq;

namespace LiteDeploy.Worker
{
    internal sealed class WingetListRow
    {
        public string Name;
        public string Id;
        public string Version;
        /// <summary>Available version; null when the package is up to date.</summary>
        public string Available;
    }

    internal static class WingetListParser
    {
        /// <summary>
        /// The row of <paramref name="id"/> (case-insensitive); null when absent. winget shortens a
        /// long Id with "\u2026" in a narrow table: a shortened Id that begins <paramref name="id"/>
        /// matches too.
        /// </summary>
        public static WingetListRow FindRow(string output, string id)
        {
            List<WingetListRow> rows = Parse(output);
            WingetListRow exact = rows.FirstOrDefault(r => string.Equals(r.Id, id, StringComparison.OrdinalIgnoreCase));
            if (exact != null || string.IsNullOrEmpty(id)) return exact;
            return rows.FirstOrDefault(r => r.Id.Length > 1 && r.Id[r.Id.Length - 1] == '\u2026'
                && id.StartsWith(r.Id.Substring(0, r.Id.Length - 1), StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// State of a package from "winget list --id X -e": WorkerProtocol.StatusOutdated,
        /// StatusUpToDate, StatusNotInstalled or StatusError. A package that is not installed is
        /// not an error, whatever the way winget says it: its "not installed" or "no package
        /// found" codes, or success without the package in its output (no table, or a table
        /// without that Id). Only the other exit codes are errors.
        /// </summary>
        public static string Classify(int exitCode, string output, string id, out WingetListRow row)
        {
            row = null;
            if (exitCode == WingetClient.PackageNotInstalled || exitCode == WingetClient.NoPackageFound)
                return WorkerProtocol.StatusNotInstalled;
            if (exitCode != 0) return WorkerProtocol.StatusError;
            row = FindRow(output, id);
            if (row == null) return WorkerProtocol.StatusNotInstalled;
            return row.Available == null ? WorkerProtocol.StatusUpToDate : WorkerProtocol.StatusOutdated;
        }

        public static List<WingetListRow> Parse(string output)
        {
            List<WingetListRow> rows = new List<WingetListRow>();
            List<string> lines = CleanLines(output);

            int dashes = lines.FindIndex(l => l.Length >= 10 && l.Trim().All(c => c == '-'));
            if (dashes < 1) return rows;

            string header = lines[dashes - 1];
            List<int> starts = ColumnStarts(header);
            // Name, Id, Version, then Available and Source. winget leaves the Source column out when the
            // list is limited to one source (--source winget), and Available out when nothing is newer:
            // 3 columns (up to date), 4 (Available, or Source with an older winget), 5 (both).
            if (starts.Count < 3) return rows;
            int availableColumn = starts.Count >= 5 ? 3 : -1;
            bool fourthMayBeSource = starts.Count == 4;
            bool fourthIsSourceHeader = fourthMayBeSource && IsSourceHeader(header.Substring(starts[3]).Trim());

            for (int i = dashes + 1; i < lines.Count; i++)
            {
                string line = lines[i];
                if (line.Trim().Length == 0) continue;
                // A footer such as "2 upgrades available." has no Id column
                if (line.Length <= starts[1]) continue;
                string[] cells = Cells(line, starts);
                string available = availableColumn >= 0 ? cells[availableColumn] : null;
                // A fourth column is Available unless its header says Source, or its value is a source name
                if (fourthMayBeSource && !fourthIsSourceHeader && !IsSourceName(cells[3])) available = cells[3];
                WingetListRow row = new WingetListRow
                {
                    Name = cells[0],
                    Id = cells[1],
                    Version = cells[2],
                    Available = string.IsNullOrEmpty(available) ? null : available
                };
                if (row.Id.Length > 0 && row.Id.IndexOf(' ') < 0) rows.Add(row);
            }
            return rows;
        }

        /// <summary>Header of the Source column in the languages of Windows (first word of the column).</summary>
        static readonly string[] SourceHeaders = { "Source", "Quelle", "Origen", "Origine", "Fonte", "Bron", "Kilde", "K\u00e4lla", "L\u00e4hde", "\u0179r\u00f3d\u0142o", "Zdroj", "Kaynak", "\u0418\u0441\u0442\u043e\u0447\u043d\u0438\u043a" };

        static bool IsSourceHeader(string word)
        {
            return SourceHeaders.Any(h => string.Equals(h, word, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Names of the winget sources: a value of the Source column, never a version.</summary>
        static bool IsSourceName(string value)
        {
            return string.Equals(value, "winget", StringComparison.OrdinalIgnoreCase) || string.Equals(value, "msstore", StringComparison.OrdinalIgnoreCase);
        }

        // Removes the progress spinner and bars winget writes before the table: keeps, for each line,
        // the text after its last carriage return.
        static List<string> CleanLines(string output)
        {
            List<string> lines = new List<string>();
            if (string.IsNullOrEmpty(output)) return lines;
            foreach (string raw in output.Split('\n'))
            {
                string line = raw;
                int cr = line.TrimEnd('\r').LastIndexOf('\r');
                if (cr >= 0) line = line.Substring(cr + 1);
                lines.Add(line.TrimEnd('\r'));
            }
            return lines;
        }

        // Start of each header word (a column starts after at least one space)
        static List<int> ColumnStarts(string header)
        {
            List<int> starts = new List<int>();
            for (int i = 0; i < header.Length; i++)
            {
                if (header[i] != ' ' && (i == 0 || header[i - 1] == ' '))
                    starts.Add(i);
            }
            return starts;
        }

        static string[] Cells(string line, List<int> starts)
        {
            string[] cells = new string[starts.Count];
            for (int c = 0; c < starts.Count; c++)
            {
                int from = starts[c];
                int to = c + 1 < starts.Count ? starts[c + 1] : line.Length;
                cells[c] = from >= line.Length ? "" : line.Substring(from, Math.Min(to, line.Length) - from).Trim();
            }
            return cells;
        }
    }
}
