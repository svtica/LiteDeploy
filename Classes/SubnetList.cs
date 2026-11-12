// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    SubnetList.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-12
// Purpose: Saved subnets of the computer list (Subnets menu), in addition to the
//          subnets of this computer. One subnet per line: "10.1.2.0/24 Garage",
//          "10.1.2 Garage", "10.1.0.0/22 Main building"; "#" starts a comment;
//          ";", "," or a tab may separate the label (CSV import). Prefixes /20
//          to /24 are split into /24 subnets, the unit of the DNS scan.
//          Files: subnets.txt next to LiteDeploy.exe (list shared by the team,
//          read only) and %LOCALAPPDATA%\LiteDeploy\subnets.txt (edited in
//          LiteDeploy).
// -----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace LiteDeploy
{
    /// <summary>A /24 subnet: Prefix is "a.b.c." as used by the DNS scan.</summary>
    public sealed class SubnetEntry
    {
        public String Prefix { get; set; }
        public String Label { get; set; }
        public bool Shared { get; set; }

        public String Display
        {
            get { return String.IsNullOrEmpty(Label) ? Prefix + "0/24" : Prefix + "0/24 - " + Label; }
        }
    }

    public static class SubnetList
    {
        public const int SmallestPrefix = 20;   // at most 16 subnets /24 per line

        /// <summary>The list edited in LiteDeploy.</summary>
        public static String UserPath
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LiteDeploy", "subnets.txt"); }
        }

        /// <summary>The team's list, next to LiteDeploy.exe (read only in LiteDeploy).</summary>
        public static String SharedPath
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "subnets.txt"); }
        }

        /// <summary>Reads one line. Empty or comment line: true with no entry.</summary>
        public static bool TryParseLine(String line, out List<SubnetEntry> entries, out String error)
        {
            entries = new List<SubnetEntry>();
            error = null;
            if (line == null) return true;
            int hash = line.IndexOf('#');
            if (hash >= 0) line = line.Substring(0, hash);
            line = line.Trim();
            if (line.Length == 0) return true;

            int cut = line.IndexOfAny(new[] { ' ', '\t', ';', ',' });
            String network = cut < 0 ? line : line.Substring(0, cut);
            String label = cut < 0 ? null : line.Substring(cut + 1).Trim(' ', '\t', ';', ',', '"').Trim();
            if (String.IsNullOrEmpty(label)) label = null;

            int prefixLength = 24;
            int slash = network.IndexOf('/');
            if (slash >= 0)
            {
                if (!Int32.TryParse(network.Substring(slash + 1), out prefixLength) || prefixLength < SmallestPrefix || prefixLength > 24)
                {
                    error = Localization.F("SubnetList.BadPrefix", "Only /{0} to /24 are accepted: {1}", SmallestPrefix, network);
                    return false;
                }
                network = network.Substring(0, slash);
            }

            String[] parts = network.TrimEnd('.').Split('.');
            if (parts.Length < 3 || parts.Length > 4)
            {
                error = Localization.F("SubnetList.BadAddress", "Not an IPv4 subnet: {0}", network);
                return false;
            }
            int[] octets = new int[4];
            for (int i = 0; i < parts.Length; i++)
            {
                if (!Int32.TryParse(parts[i], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out octets[i]) || octets[i] > 255)
                {
                    error = Localization.F("SubnetList.BadAddress", "Not an IPv4 subnet: {0}", network);
                    return false;
                }
            }

            uint address = ((uint)octets[0] << 24) | ((uint)octets[1] << 16) | ((uint)octets[2] << 8);
            uint mask = prefixLength == 0 ? 0 : 0xFFFFFFFFu << (32 - prefixLength);
            uint first = address & mask;
            int count = 1 << (24 - prefixLength);
            for (int i = 0; i < count; i++)
            {
                uint a = first + ((uint)i << 8);
                entries.Add(new SubnetEntry
                {
                    Prefix = String.Format("{0}.{1}.{2}.", a >> 24, (a >> 16) & 255, (a >> 8) & 255),
                    Label = label
                });
            }
            return true;
        }

        /// <summary>Reads lines; invalid lines are reported in errors ("line n: ...") and skipped.
        /// A subnet listed twice is kept once (first label wins).</summary>
        public static List<SubnetEntry> Parse(IEnumerable<String> lines, List<String> errors)
        {
            List<SubnetEntry> result = new List<SubnetEntry>();
            HashSet<String> seen = new HashSet<String>(StringComparer.Ordinal);
            int number = 0;
            foreach (String line in lines)
            {
                number++;
                List<SubnetEntry> entries;
                String error;
                if (!TryParseLine(line, out entries, out error))
                {
                    if (errors != null) errors.Add(Localization.F("SubnetList.Line", "Line {0}: {1}", number, error));
                    continue;
                }
                foreach (SubnetEntry e in entries)
                    if (seen.Add(e.Prefix)) result.Add(e);
            }
            return result;
        }

        /// <summary>The team's list, then the user's (a subnet in both is kept once).</summary>
        public static List<SubnetEntry> Load()
        {
            List<SubnetEntry> result = new List<SubnetEntry>();
            HashSet<String> seen = new HashSet<String>(StringComparer.Ordinal);
            foreach (String path in new[] { SharedPath, UserPath })
            {
                List<SubnetEntry> entries;
                try
                {
                    if (!File.Exists(path)) continue;
                    entries = Parse(File.ReadAllLines(path, Encoding.UTF8), null);
                }
                catch (IOException) { continue; }
                catch (UnauthorizedAccessException) { continue; }
                bool shared = path == SharedPath;
                foreach (SubnetEntry e in entries)
                {
                    e.Shared = shared;
                    if (seen.Add(e.Prefix)) result.Add(e);
                }
            }
            return result;
        }

        /// <summary>Text of the user's list ("" when there is none).</summary>
        public static String ReadUserText()
        {
            try { return File.Exists(UserPath) ? File.ReadAllText(UserPath, Encoding.UTF8) : ""; }
            catch (IOException) { return ""; }
        }

        public static void SaveUserText(String text)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(UserPath));
            File.WriteAllText(UserPath, text ?? "", new UTF8Encoding(false));
        }
    }
}
