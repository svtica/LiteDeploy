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
        /// <summary>The row of <paramref name="id"/> (exact, case-insensitive); null when absent.</summary>
        public static WingetListRow FindRow(string output, string id)
        {
            return Parse(output).FirstOrDefault(r => string.Equals(r.Id, id, StringComparison.OrdinalIgnoreCase));
        }

        public static List<WingetListRow> Parse(string output)
        {
            List<WingetListRow> rows = new List<WingetListRow>();
            List<string> lines = CleanLines(output);

            int dashes = lines.FindIndex(l => l.Length >= 10 && l.Trim().All(c => c == '-'));
            if (dashes < 1) return rows;

            string header = lines[dashes - 1];
            List<int> starts = ColumnStarts(header);
            if (starts.Count < 4) return rows;           // Name, Id, Version, Source at least
            bool hasAvailable = starts.Count >= 5;

            for (int i = dashes + 1; i < lines.Count; i++)
            {
                string line = lines[i];
                if (line.Trim().Length == 0) continue;
                // A footer such as "2 upgrades available." has no Id column
                if (line.Length <= starts[1]) continue;
                string[] cells = Cells(line, starts);
                WingetListRow row = new WingetListRow
                {
                    Name = cells[0],
                    Id = cells[1],
                    Version = cells[2],
                    Available = hasAvailable && cells[3].Length > 0 ? cells[3] : null
                };
                if (row.Id.Length > 0 && row.Id.IndexOf(' ') < 0) rows.Add(row);
            }
            return rows;
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
