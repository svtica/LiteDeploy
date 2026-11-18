// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    UpdatePackage.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-12
// Purpose: A package of the software update catalog (packages.txt), same format
//          as an earlier update script: Id | processes to close | label | ignored.
//          The worker receives each package as one --package argument.
//          An Id "module:<Name>" is an update module (signed PowerShell script,
//          docs/modules.md) instead of a winget package.
//          An Id "module:<Name>/<Instance>" is one instance of a module that
//          declares "# Instances: yes": the same script, run with the parameter
//          values of that instance, with its own row and its own state.
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

        public const string ModulePrefix = "module:";

        public string DisplayName { get { return string.IsNullOrEmpty(Label) ? Id : Label; } }

        /// <summary>True for an update module (Id "module:Name").</summary>
        public bool IsModule { get { return Id != null && Id.StartsWith(ModulePrefix, StringComparison.OrdinalIgnoreCase); } }

        /// <summary>Separates the module name from the instance name: "module:Name/Instance".</summary>
        public const char InstanceSeparator = '/';

        /// <summary>Name of the module (file name without .ps1); null for a winget package.</summary>
        public string ModuleName
        {
            get
            {
                if (!IsModule) return null;
                string rest = Id.Substring(ModulePrefix.Length);
                int slash = rest.IndexOf(InstanceSeparator);
                return slash < 0 ? rest : rest.Substring(0, slash);
            }
        }

        /// <summary>Instance of the module ("module:Name/Instance"); null for a plain module or a winget package.</summary>
        public string InstanceName
        {
            get
            {
                if (!IsModule) return null;
                int slash = Id.IndexOf(InstanceSeparator);
                return slash < 0 ? null : Id.Substring(slash + 1);
            }
        }

        /// <summary>
        /// Key of the parameter values in module-settings.xml: "Name" for a module, "Name/Instance"
        /// for an instance; null for a winget package.
        /// </summary>
        public string SettingsKey { get { return IsModule ? Id.Substring(ModulePrefix.Length) : null; } }

        /// <summary>The Id of a module ("module:Name") or of one of its instances ("module:Name/Instance").</summary>
        public static string ModuleId(string module, string instance)
        {
            return ModulePrefix + module + (string.IsNullOrEmpty(instance) ? "" : InstanceSeparator + instance);
        }

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

        // A winget Id: letters, digits and . - _ + only (it ends up on a command line run as SYSTEM).
        // A module: "module:" then a name of letters, digits and . - _ (it becomes a file name),
        // optionally "/" and an instance name with the same rules (also part of a file name)
        public static bool IsValidId(string id)
        {
            if (string.IsNullOrEmpty(id) || id.Length > 128) return false;
            if (id.StartsWith(ModulePrefix, StringComparison.OrdinalIgnoreCase))
            {
                string[] parts = id.Substring(ModulePrefix.Length).Split(InstanceSeparator);
                return parts.Length <= 2 && IsValidModuleName(parts[0]) && (parts.Length == 1 || IsValidInstanceName(parts[1]));
            }
            return id.All(c => char.IsLetterOrDigit(c) || c == '.' || c == '-' || c == '_' || c == '+');
        }

        /// <summary>A module name (unchanged rule): letters, digits and . - _ only, not starting with a dot.</summary>
        public static bool IsValidModuleName(string name)
        {
            return !string.IsNullOrEmpty(name) && name[0] != '.' && name.All(c => char.IsLetterOrDigit(c) || c == '.' || c == '-' || c == '_');
        }

        /// <summary>
        /// An instance name: ASCII letters, digits and . - _ only (A-Z a-z 0-9 . - _), not starting
        /// with a dot, 64 characters at most. It ends up in a file name on the computer.
        /// </summary>
        public static bool IsValidInstanceName(string name)
        {
            if (string.IsNullOrEmpty(name) || name.Length > 64 || name[0] == '.') return false;
            return name.All(c => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '.' || c == '-' || c == '_');
        }

        static bool IsValidProcessName(string name)
        {
            if (string.IsNullOrEmpty(name) || name.Length > 64) return false;
            return name.All(c => char.IsLetterOrDigit(c) || c == '.' || c == '-' || c == '_' || c == '+' || c == ' ');
        }
    }
}
