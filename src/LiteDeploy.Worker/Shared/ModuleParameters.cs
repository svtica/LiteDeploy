// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    ModuleParameters.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-17
// Purpose: Parameters of the update modules (docs/modules.md, contract v2):
//          the "# Parameter:" header lines of a module, the parameter file that
//          LiteDeploy writes next to the module on the computer (one per run and
//          per instance, deleted by the worker), and how the worker turns the values into
//          LITEDEPLOY_PARAM_<Name> environment variables of the module process.
//          Pure logic, compiled into both programs (and the tests).
// -----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace LiteDeploy
{
    /// <summary>One "# Parameter:" line of a module header.</summary>
    internal sealed class ModuleParameter
    {
        public string Name;
        /// <summary>The value is the password of a stored credential; never shown, logged or put on a command line.</summary>
        public bool Secret;
        /// <summary>Value used when none is set; null: none (the parameter is required).</summary>
        public string Default;
        public string Description = "";
        /// <summary>values=a|b|c: the values offered in a list by LiteDeploy (empty: free text). The module still checks its value.</summary>
        public string[] Values = new string[0];
        /// <summary>files=Folder/*.msi|Folder/*.exe: files of the shared cache offered by LiteDeploy (null: none). Path relative to the cache.</summary>
        public string Files;

        public bool Required { get { return Default == null; } }
    }

    internal static class ModuleParameters
    {
        /// <summary>Header key: "# Parameter: Name [secret] [default=value] [values=a|b] [files=Folder/*.ext] description".</summary>
        public const string HeaderKey = "Parameter";
        /// <summary>Environment variable of a parameter for the module process: LITEDEPLOY_PARAM_Name.</summary>
        public const string EnvironmentPrefix = "LITEDEPLOY_PARAM_";
        /// <summary>Parameter file of a module: &lt;Name&gt;.params next to &lt;Name&gt;.ps1.</summary>
        public const string FileExtension = ".params";
        /// <summary>First line of a parameter file (format version).</summary>
        public const string FileHeader = "LDP1";
        /// <summary>Longest value accepted (a value ends up in an environment variable).</summary>
        public const int MaxValueLength = 8192;
        /// <summary>Replaces a secret value in the messages of a module.</summary>
        public const string Mask = "***";

        /// <summary>Header line of a module that can run as several instances ("module:Name/Instance").</summary>
        public const string InstancesHeader = "# Instances: yes";
        /// <summary>Separates the module and the instance in the name of a parameter file: Name@Instance.params.</summary>
        public const char InstanceFileSeparator = '@';

        static readonly Regex NamePattern = new Regex("^[A-Za-z][A-Za-z0-9_]{0,31}$", RegexOptions.CultureInvariant);

        /// <summary>
        /// File name of the parameter file of a package: "Name.params" for a module,
        /// "Name@Instance.params" for an instance ("@" cannot appear in a module or instance name,
        /// so two packages never share a file); null for a winget package.
        /// </summary>
        public static string FileName(UpdatePackage package)
        {
            if (package == null || !package.IsModule) return null;
            string instance = package.InstanceName;
            return package.ModuleName + (instance == null ? "" : InstanceFileSeparator + instance) + FileExtension;
        }

        /// <summary>True when the header (first lines of the script) has the line "# Instances: yes".</summary>
        public static bool DeclaresInstances(IEnumerable<string> firstLines)
        {
            if (firstLines == null) return false;
            return firstLines.Any(l => l != null && l.Trim().TrimStart('﻿').Trim().Equals(InstancesHeader, StringComparison.OrdinalIgnoreCase));
        }

        public static bool IsValidName(string name)
        {
            return name != null && NamePattern.IsMatch(name);
        }

        /// <summary>
        /// Parses one header line; null when it is not a "# Parameter:" line or its name is invalid.
        /// Flags come right after the name, in any order: "secret", "default=value" or default="value with spaces",
        /// "values=a|b|c" (the values LiteDeploy offers in a list) and "files=Folder/*.msi" (files of the shared
        /// cache LiteDeploy offers); the rest of the line is the description. An earlier LiteDeploy reads an
        /// unknown flag as the start of the description.
        /// </summary>
        public static ModuleParameter ParseHeaderLine(string line)
        {
            if (line == null) return null;
            line = line.Trim().TrimStart('﻿').Trim();
            string prefix = "# " + HeaderKey + ":";
            if (!line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;
            string rest = line.Substring(prefix.Length).Trim();

            string name = FirstToken(rest);
            if (!IsValidName(name)) return null;
            ModuleParameter p = new ModuleParameter { Name = name };
            rest = rest.Substring(name.Length).TrimStart();

            while (rest.Length > 0)
            {
                string token = FirstToken(rest);
                if (token.Equals("secret", StringComparison.OrdinalIgnoreCase))
                {
                    p.Secret = true;
                    rest = rest.Substring(token.Length).TrimStart();
                }
                else if (token.StartsWith("default=", StringComparison.OrdinalIgnoreCase))
                {
                    rest = rest.Substring("default=".Length);
                    if (rest.StartsWith("\"", StringComparison.Ordinal))
                    {
                        int end = rest.IndexOf('"', 1);
                        if (end < 0) end = rest.Length;
                        p.Default = rest.Substring(1, end - 1);
                        rest = end < rest.Length ? rest.Substring(end + 1) : "";
                    }
                    else
                    {
                        string value = FirstToken(rest);
                        p.Default = value;
                        rest = rest.Substring(value.Length);
                    }
                    rest = rest.TrimStart();
                }
                else if (token.StartsWith("values=", StringComparison.OrdinalIgnoreCase))
                {
                    p.Values = token.Substring("values=".Length).Split('|').Select(v => v.Trim()).Where(v => v.Length > 0)
                        .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                    rest = rest.Substring(token.Length).TrimStart();
                }
                else if (token.StartsWith("files=", StringComparison.OrdinalIgnoreCase))
                {
                    string files = token.Substring("files=".Length).Trim();
                    p.Files = files.Length > 0 ? files : null;
                    rest = rest.Substring(token.Length).TrimStart();
                }
                else break;
            }
            p.Description = rest.Trim();
            return p;
        }

        static string FirstToken(string text)
        {
            int i = 0;
            while (i < text.Length && !char.IsWhiteSpace(text[i])) i++;
            return text.Substring(0, i);
        }

        /// <summary>The parameters of a module header (first lines of the script); a name declared twice: the first wins.</summary>
        public static List<ModuleParameter> ParseHeader(IEnumerable<string> firstLines)
        {
            List<ModuleParameter> list = new List<ModuleParameter>();
            if (firstLines == null) return list;
            foreach (string line in firstLines)
            {
                ModuleParameter p = ParseHeaderLine(line);
                if (p != null && !list.Any(x => string.Equals(x.Name, p.Name, StringComparison.OrdinalIgnoreCase)))
                    list.Add(p);
            }
            return list;
        }

        /// <summary>Content of a parameter file: "LDP1", then one "Name=value" line per value (URI-escaped).</summary>
        public static string FormatFile(IDictionary<string, string> values)
        {
            StringBuilder sb = new StringBuilder(FileHeader).Append('\n');
            if (values != null)
            {
                foreach (KeyValuePair<string, string> v in values)
                {
                    if (!IsValidName(v.Key) || v.Value == null) continue;
                    sb.Append(v.Key).Append('=').Append(Uri.EscapeDataString(v.Value)).Append('\n');
                }
            }
            return sb.ToString();
        }

        /// <summary>Values of a parameter file (names case-insensitive); empty for an unknown format.</summary>
        public static Dictionary<string, string> ParseFile(string content)
        {
            Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(content)) return values;
            string[] lines = content.TrimStart('﻿').Split('\n').Select(l => l.TrimEnd('\r')).ToArray();
            if (lines.Length == 0 || lines[0].Trim() != FileHeader) return values;
            for (int i = 1; i < lines.Length; i++)
            {
                int eq = lines[i].IndexOf('=');
                if (eq <= 0) continue;
                string name = lines[i].Substring(0, eq).Trim();
                if (!IsValidName(name) || values.ContainsKey(name)) continue;
                try { values[name] = Uri.UnescapeDataString(lines[i].Substring(eq + 1).Trim()); }
                catch (UriFormatException) { }
            }
            return values;
        }

        /// <summary>
        /// Values of the declared parameters for the module process: the value supplied, else the
        /// default of the header. Returns null, or the error when a required parameter has no value
        /// (the module is then not started). Only declared parameters are passed; an empty value
        /// sets no variable. <paramref name="secrets"/>: the secret values, to mask in the messages.
        /// </summary>
        public static string Resolve(IEnumerable<ModuleParameter> declared, IDictionary<string, string> supplied,
            out Dictionary<string, string> environment, out List<string> secrets)
        {
            environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            secrets = new List<string>();
            Dictionary<string, string> given = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (supplied != null)
                foreach (KeyValuePair<string, string> v in supplied)
                    if (v.Key != null) given[v.Key] = v.Value;

            List<string> missing = new List<string>();
            foreach (ModuleParameter p in declared ?? Enumerable.Empty<ModuleParameter>())
            {
                string value;
                if (!given.TryGetValue(p.Name, out value) || string.IsNullOrEmpty(value)) value = p.Default;
                if (value == null) { missing.Add(p.Name); continue; }
                if (value.Length > MaxValueLength) return "Value of the parameter " + p.Name + " too long (" + MaxValueLength + " characters at most). Module not started.";
                if (value.Length == 0) continue;
                environment[EnvironmentPrefix + p.Name] = value;
                if (p.Secret) secrets.Add(value);
            }
            if (missing.Count > 0)
                return "Missing value for the required parameter" + (missing.Count > 1 ? "s " : " ") + string.Join(", ", missing)
                       + ": set it in LiteDeploy (Package list > Parameters...). Module not started.";
            return null;
        }

        /// <summary>The text with every secret value replaced by ***.</summary>
        public static string MaskSecrets(string text, IEnumerable<string> secrets)
        {
            if (string.IsNullOrEmpty(text) || secrets == null) return text;
            foreach (string s in secrets.Where(x => !string.IsNullOrEmpty(x)).OrderByDescending(x => x.Length))
            {
                text = text.Replace(s, Mask);
                // Also the escaped form a module may have written
                string escaped = Uri.EscapeDataString(s);
                if (escaped != s) text = text.Replace(escaped, Mask);
            }
            return text;
        }
    }
}
