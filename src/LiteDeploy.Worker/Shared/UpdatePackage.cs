// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    UpdatePackage.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-12
// Purpose: A package of the software update catalog (packages.txt), same format
//          as the MAJ-Logiciels tool: Id | processes to close | label | ignored.
//          The worker receives each package as one --package argument.
// -----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LiteDeploy
{
    internal sealed class UpdatePackage
    {
        /// <summary>winget package Id (exact match), e.g. Notepad++.Notepad++.</summary>
        public string Id;
        /// <summary>Processes closed before the upgrade (names without .exe).</summary>
        public string[] Processes = new string[0];
        /// <summary>Name shown to the user; the Id when empty.</summary>
        public string Label;

        public string DisplayName { get { return string.IsNullOrEmpty(Label) ? Id : Label; } }

        /// <summary>Parses "Id | proc1, proc2 | label [| anything]"; null for a blank or comment line or an invalid Id.</summary>
        public static UpdatePackage Parse(string line)
        {
            if (line == null) return null;
            line = line.Trim();
            if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) return null;
            string[] parts = line.Split('|').Select(p => p.Trim()).ToArray();
            if (!IsValidId(parts[0])) return null;
            UpdatePackage p2 = new UpdatePackage { Id = parts[0] };
            if (parts.Length > 1)
                p2.Processes = parts[1].Split(',').Select(s => s.Trim()).Where(IsValidProcessName).ToArray();
            if (parts.Length > 2 && parts[2].Length > 0)
                p2.Label = parts[2];
            return p2;
        }

        /// <summary>The form passed on the worker command line: "Id|proc1,proc2|label".</summary>
        public string ToArgument()
        {
            return Id + "|" + string.Join(",", Processes) + "|" + (Label ?? "");
        }

        public static List<UpdatePackage> ParseFile(string path)
        {
            List<UpdatePackage> list = new List<UpdatePackage>();
            if (!File.Exists(path)) return list;
            foreach (string line in File.ReadAllLines(path))
            {
                UpdatePackage p = Parse(line);
                if (p != null && !list.Any(x => string.Equals(x.Id, p.Id, StringComparison.OrdinalIgnoreCase)))
                    list.Add(p);
            }
            return list;
        }

        // A winget Id: letters, digits and . - _ + only (it ends up on a command line run as SYSTEM)
        public static bool IsValidId(string id)
        {
            if (string.IsNullOrEmpty(id) || id.Length > 128) return false;
            return id.All(c => char.IsLetterOrDigit(c) || c == '.' || c == '-' || c == '_' || c == '+');
        }

        static bool IsValidProcessName(string name)
        {
            if (string.IsNullOrEmpty(name) || name.Length > 64) return false;
            return name.All(c => char.IsLetterOrDigit(c) || c == '.' || c == '-' || c == '_' || c == '+' || c == ' ');
        }
    }
}
