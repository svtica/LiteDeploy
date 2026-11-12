// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    WorkerProtocol.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-12
// Purpose: Lines exchanged between LiteDeploy.Worker (standard output) and
//          LiteDeploy: "LDW1" then tab-separated key=value pairs, values
//          URI-escaped. Other lines (PsExec banner, winget noise) are ignored.
//          Compiled into both programs (and the tests).
// -----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Text;

namespace LiteDeploy
{
    /// <summary>One result line of the worker: its fields by name (case-insensitive).</summary>
    internal sealed class WorkerMessage
    {
        public readonly Dictionary<string, string> Fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public string this[string key]
        {
            get { string v; return Fields.TryGetValue(key, out v) ? v : null; }
            set { Fields[key] = value; }
        }

        public string Type { get { return this["type"]; } }
    }

    internal static class WorkerProtocol
    {
        public const string Prefix = "LDW1";

        // Message types
        public const string TypePackage = "package";   // state of one package (scan, and after an upgrade)
        public const string TypeUpgrade = "upgrade";   // outcome of one upgrade
        public const string TypeError = "error";       // the run could not be done (winget missing...)
        public const string TypeDone = "done";         // last line: summary
        public const string TypeModule = "module";     // written by an update module (status, versions, message)

        // Package states
        public const string StatusUpToDate = "uptodate";
        public const string StatusOutdated = "outdated";
        public const string StatusNotInstalled = "notinstalled";
        public const string StatusError = "error";

        // Upgrade outcomes
        public const string ResultUpgraded = "upgraded";
        public const string ResultFailed = "failed";
        public const string ResultSkipped = "skipped";

        /// <summary>Formats a message: LDW1, then key=value pairs in the given order (null values skipped).</summary>
        public static string Format(params string[] keyValues)
        {
            if (keyValues == null || keyValues.Length % 2 != 0)
                throw new ArgumentException("Key/value pairs expected.", "keyValues");
            StringBuilder sb = new StringBuilder(Prefix);
            for (int i = 0; i < keyValues.Length; i += 2)
            {
                if (keyValues[i + 1] == null) continue;
                sb.Append('\t').Append(keyValues[i]).Append('=').Append(Uri.EscapeDataString(keyValues[i + 1]));
            }
            return sb.ToString();
        }

        /// <summary>Parses one output line; null when it is not a worker message.</summary>
        public static WorkerMessage Parse(string line)
        {
            if (string.IsNullOrEmpty(line)) return null;
            line = line.Trim();
            if (!line.StartsWith(Prefix + "\t", StringComparison.Ordinal)) return null;
            WorkerMessage m = new WorkerMessage();
            foreach (string part in line.Substring(Prefix.Length + 1).Split('\t'))
            {
                int eq = part.IndexOf('=');
                if (eq <= 0) continue;
                try { m[part.Substring(0, eq)] = Uri.UnescapeDataString(part.Substring(eq + 1)); }
                catch (UriFormatException) { }
            }
            return m.Type == null ? null : m;
        }

        /// <summary>Every worker message found in a whole output.</summary>
        public static List<WorkerMessage> ParseAll(string output)
        {
            List<WorkerMessage> list = new List<WorkerMessage>();
            if (string.IsNullOrEmpty(output)) return list;
            foreach (string line in output.Split('\n'))
            {
                WorkerMessage m = Parse(line);
                if (m != null) list.Add(m);
            }
            return list;
        }
    }
}
