// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    ProgramPolicy.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-23
// Purpose: "Uninstall another program" (uninstall mode): rules for the
//          programs of the Uninstall keys of HKLM (64 and 32-bit views; never
//          the per-user entries, whose command a user can change). How an entry
//          can be removed: MSI (key = product code {GUID}, Windows Installer
//          flag): msiexec /x {GUID} /qn /norestart; EXE: only its
//          QuietUninstallString, and only when its executable is under Program
//          Files or the Windows folder; anything else is not removable here.
//          System components, updates, entries marked NoRemove and protected
//          products (LiteSuite, security agents, Windows and runtime
//          components) are never offered. Worker commands "programs" (list)
//          and "uninstall-program" (one entry, read again on the computer).
//          Pure logic, compiled into both programs (and the tests).
// -----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace LiteDeploy
{
    internal enum ProgramKind
    {
        /// <summary>Not listed: system component, update, no name.</summary>
        Hidden,
        /// <summary>Listed, but LiteDeploy will not remove it (the reason says why).</summary>
        NotRemovable,
        /// <summary>Windows Installer product: msiexec /x {GUID}.</summary>
        Msi,
        /// <summary>Quiet uninstall command of the vendor, executable under Program Files or Windows.</summary>
        QuietExe
    }

    /// <summary>One entry of HKLM\SOFTWARE\(WOW6432Node\)Microsoft\Windows\CurrentVersion\Uninstall.</summary>
    internal sealed class ProgramEntry
    {
        /// <summary>Name of the registry key (product code for an MSI).</summary>
        public string Key = "";
        /// <summary>"64" or "32": the registry view.</summary>
        public string View = "64";
        public string Name = "";
        public string Version = "";
        public string Publisher = "";
        public bool WindowsInstaller;
        public string QuietUninstallString;
        public bool SystemComponent;
        public bool NoRemove;
        /// <summary>An update of another product (ParentKeyName, ReleaseType).</summary>
        public bool IsUpdate;
    }

    internal static class ProgramPolicy
    {
        public const string ListCommand = "programs";
        public const string UninstallCommand = "uninstall-program";
        public const string TypeProgram = "program";
        public const string TypeResult = "programresult";
        public const int TimeoutMinutes = 15;

        /// <summary>Parts of the name or publisher of a product that is never removed (case ignored).</summary>
        public static readonly string[] ProtectedParts =
        {
            "LiteDeploy", "LiteTask", "LiteSuite", "LitePM",
            "Trend Micro", "TrendMicro", "Fortinet", "FortiClient", "Defender", "Sophos", "CrowdStrike", "SentinelOne",
            "Microsoft Edge", "Microsoft Visual C++", "Microsoft .NET", "Microsoft Windows Desktop Runtime", "Windows App Runtime",
            "Microsoft Update Health", "Microsoft Intune", "Configuration Manager Client", "Windows Driver Package"
        };

        static readonly Regex Guid = new Regex(@"^\{[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}\}$");

        public static bool IsProductCode(string key)
        {
            return key != null && Guid.IsMatch(key);
        }

        /// <summary>A registry key name the worker accepts: 1 to 255 characters, no backslash, no control character.</summary>
        public static bool IsValidKey(string key)
        {
            return !string.IsNullOrEmpty(key) && key.Length <= 255 && key.IndexOf('\\') < 0 && !key.Any(char.IsControl);
        }

        public static bool IsProtected(ProgramEntry e)
        {
            string text = (e.Name ?? "") + " | " + (e.Publisher ?? "");
            return ProtectedParts.Any(p => text.IndexOf(p, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        /// <summary>
        /// The executable and the arguments of a command line: a quoted path, else the text up to ".exe" (paths
        /// with spaces written without quotes). Null when no .exe is found.
        /// </summary>
        public static string[] SplitCommand(string command)
        {
            if (string.IsNullOrWhiteSpace(command)) return null;
            string c = command.Trim();
            string exe, args;
            if (c[0] == '"')
            {
                int end = c.IndexOf('"', 1);
                if (end < 0) return null;
                exe = c.Substring(1, end - 1);
                args = c.Substring(end + 1).Trim();
            }
            else
            {
                int i = c.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
                if (i < 0) return null;
                exe = c.Substring(0, i + 4);
                args = c.Substring(i + 4).Trim();
            }
            if (!exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return null;
            return new[] { exe.Trim(), args };
        }

        /// <summary>
        /// The executable is a full path under one of <paramref name="allowedRoots"/> (Program Files, Program Files
        /// (x86), Windows), without "..". Case ignored.
        /// </summary>
        public static bool IsAllowedExecutable(string exe, IEnumerable<string> allowedRoots)
        {
            if (string.IsNullOrWhiteSpace(exe) || exe.Contains("..") || exe.IndexOfAny(Path.GetInvalidPathChars()) >= 0) return false;
            if (!Path.IsPathRooted(exe) || exe.StartsWith(@"\\", StringComparison.Ordinal)) return false;
            foreach (string root in allowedRoots ?? Enumerable.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(root)) continue;
                string r = root.TrimEnd('\\') + "\\";
                if (exe.StartsWith(r, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        /// <summary>How the entry can be removed; <paramref name="reason"/>: why not, when NotRemovable.</summary>
        public static ProgramKind Classify(ProgramEntry e, IEnumerable<string> allowedRoots, out string reason)
        {
            reason = null;
            if (e == null || string.IsNullOrWhiteSpace(e.Name) || e.SystemComponent || e.IsUpdate) return ProgramKind.Hidden;
            if (IsProtected(e)) { reason = "protected"; return ProgramKind.NotRemovable; }
            if (e.NoRemove) { reason = "noremove"; return ProgramKind.NotRemovable; }
            if (e.WindowsInstaller && IsProductCode(e.Key)) return ProgramKind.Msi;
            string[] quiet = SplitCommand(e.QuietUninstallString);
            if (quiet == null) { reason = "noquiet"; return ProgramKind.NotRemovable; }
            if (!IsAllowedExecutable(quiet[0], allowedRoots)) { reason = "location"; return ProgramKind.NotRemovable; }
            return ProgramKind.QuietExe;
        }

        /// <summary>The msiexec arguments of an MSI entry (product code checked).</summary>
        public static string MsiArguments(string productCode, string logPath)
        {
            if (!IsProductCode(productCode)) throw new ArgumentException("Not a product code", "productCode");
            if (string.IsNullOrEmpty(logPath) || logPath.IndexOf('"') >= 0) throw new ArgumentException("Invalid log path", "logPath");
            return "/x " + productCode + " /qn /norestart /l*v \"" + logPath + "\"";
        }

        /// <summary>Exit code of an uninstall: 0 removed, 3010 / 1641 removed and restart needed, else failed.</summary>
        public static bool IsSuccess(int code, out bool restart)
        {
            restart = code == 3010 || code == 1641;
            return code == 0 || restart;
        }

        public static string Encode(string text)
        {
            return Convert.ToBase64String(new UTF8Encoding(false).GetBytes(text ?? ""));
        }

        public static string Decode(string base64)
        {
            try { return new UTF8Encoding(false).GetString(Convert.FromBase64String(base64 ?? "")); }
            catch (FormatException) { return null; }
        }

        /// <summary>"uninstall-program --view 64 --key K --name &lt;base64&gt; [--allow-servers [--allow-domain-controllers]]".</summary>
        public static string UninstallArguments(string view, string key, string name, bool allowServers, bool allowDomainControllers)
        {
            if (view != "64" && view != "32") throw new ArgumentException("Invalid view", "view");
            if (!IsValidKey(key) || key.IndexOf('"') >= 0) throw new ArgumentException("Invalid key", "key");
            StringBuilder sb = new StringBuilder(UninstallCommand);
            sb.Append(" --view ").Append(view).Append(" --key \"").Append(key).Append('"').Append(" --name ").Append(Encode(name));
            if (allowServers)
            {
                sb.Append(' ').Append(ServerPolicy.AllowServersOption);
                if (allowDomainControllers) sb.Append(' ').Append(ServerPolicy.AllowDomainControllersOption);
            }
            return sb.ToString();
        }

        /// <summary>Parses the arguments of "uninstall-program"; null, or why they are refused.</summary>
        public static string ParseUninstall(string[] args, out string view, out string key, out string name, out bool allowServers, out bool allowDomainControllers)
        {
            view = key = name = null;
            allowServers = allowDomainControllers = false;
            if (args == null || args.Length == 0 || args[0] != UninstallCommand) return "Not an uninstall-program command";
            for (int i = 1; i < args.Length; i++)
            {
                string a = args[i];
                bool hasValue = i + 1 < args.Length;
                if (a == "--view" && hasValue) view = args[++i];
                else if (a == "--key" && hasValue) key = args[++i];
                else if (a == "--name" && hasValue) { name = Decode(args[++i]); if (name == null) return "Invalid --name"; }
                else if (a == ServerPolicy.AllowServersOption) allowServers = true;
                else if (a == ServerPolicy.AllowDomainControllersOption) allowDomainControllers = true;
                else return "Unknown argument: " + a;
            }
            if (view != "64" && view != "32") return "Invalid --view";
            if (!IsValidKey(key)) return "Invalid --key";
            if (string.IsNullOrWhiteSpace(name)) return "Missing --name";
            return null;
        }

        /// <summary>The worker line of a listed entry.</summary>
        public static string ToLine(ProgramEntry e, ProgramKind kind, string reason)
        {
            return WorkerProtocol.Format("type", TypeProgram, "view", e.View, "key", e.Key, "name", e.Name, "version", e.Version,
                "publisher", e.Publisher, "kind", kind.ToString().ToLowerInvariant(), "reason", reason ?? "");
        }

        public static ProgramKind KindOf(string text)
        {
            ProgramKind k;
            return Enum.TryParse(text, true, out k) ? k : ProgramKind.NotRemovable;
        }
    }
}
