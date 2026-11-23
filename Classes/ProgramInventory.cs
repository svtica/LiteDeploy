// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    ProgramInventory.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-23
// Purpose: "Uninstall another program": the programs listed by the worker
//          ("programs", ProgramPolicy) on several computers, merged into one
//          list: one row per program (registry view and key, same name), with
//          its versions, its publisher, how it can be removed and the computers
//          that have it. Texts of the kinds and of the reasons. Pure logic,
//          unit-tested.
// -----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Linq;

namespace LiteDeploy
{
    internal sealed class ProgramRow
    {
        public String View;
        public String Key;
        public String Name;
        public String Publisher;
        public ProgramKind Kind;
        public String Reason;
        public readonly SortedSet<String> Versions = new SortedSet<String>(StringComparer.OrdinalIgnoreCase);
        public readonly List<String> Computers = new List<String>();

        public bool Removable { get { return Kind == ProgramKind.Msi || Kind == ProgramKind.QuietExe; } }
    }

    internal static class ProgramInventory
    {
        /// <summary>
        /// One row per program (view, key and name), from the worker lines of each computer (type=program); sorted
        /// by name. A program seen as removable on one computer and not on another keeps the stricter kind.
        /// </summary>
        public static List<ProgramRow> Merge(IEnumerable<KeyValuePair<String, IList<WorkerMessage>>> lists)
        {
            Dictionary<String, ProgramRow> rows = new Dictionary<String, ProgramRow>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<String, IList<WorkerMessage>> computer in lists ?? Enumerable.Empty<KeyValuePair<String, IList<WorkerMessage>>>())
            {
                foreach (WorkerMessage m in (computer.Value ?? new List<WorkerMessage>()).Where(m => m.Type == ProgramPolicy.TypeProgram))
                {
                    String view = m["view"] ?? "64", key = m["key"] ?? "", name = m["name"] ?? "";
                    if (key.Length == 0 || name.Length == 0) continue;
                    String id = view + "|" + key + "|" + name;
                    ProgramRow row;
                    if (!rows.TryGetValue(id, out row))
                    {
                        row = new ProgramRow { View = view, Key = key, Name = name, Publisher = m["publisher"] ?? "", Kind = ProgramPolicy.KindOf(m["kind"]), Reason = m["reason"] };
                        rows[id] = row;
                    }
                    else if (row.Removable && !IsRemovable(ProgramPolicy.KindOf(m["kind"])))
                    {
                        row.Kind = ProgramPolicy.KindOf(m["kind"]);
                        row.Reason = m["reason"];
                    }
                    if (!String.IsNullOrEmpty(m["version"])) row.Versions.Add(m["version"]);
                    if (!row.Computers.Contains(computer.Key, StringComparer.OrdinalIgnoreCase)) row.Computers.Add(computer.Key);
                }
            }
            return rows.Values.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.View).ToList();
        }

        static bool IsRemovable(ProgramKind k)
        {
            return k == ProgramKind.Msi || k == ProgramKind.QuietExe;
        }

        /// <summary>Rows whose name or publisher contains every word of <paramref name="search"/> (case ignored).</summary>
        public static List<ProgramRow> Filter(IEnumerable<ProgramRow> rows, String search)
        {
            String[] words = (search ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            return rows.Where(r => words.All(w => (r.Name + " " + r.Publisher).IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0)).ToList();
        }

        public static String KindText(ProgramRow r)
        {
            switch (r.Kind)
            {
                case ProgramKind.Msi: return "MSI";
                case ProgramKind.QuietExe: return Localization.T("ProgramInventory.QuietExe", "EXE (quiet)");
                default:
                    switch (r.Reason)
                    {
                        case "protected": return Localization.T("ProgramInventory.Protected", "Not removable: protected");
                        case "noremove": return Localization.T("ProgramInventory.NoRemove", "Not removable: the vendor forbids it");
                        case "location": return Localization.T("ProgramInventory.Location", "Not removable: uninstaller outside Program Files and Windows");
                        default: return Localization.T("ProgramInventory.NoQuiet", "Not removable: no quiet uninstall (add it to the catalog)");
                    }
            }
        }
    }
}
