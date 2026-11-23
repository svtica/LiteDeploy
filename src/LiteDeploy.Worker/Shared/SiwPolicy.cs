// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    SiwPolicy.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-23
// Purpose: SIW report (System Information for Windows, a third-party tool that
//          LiteDeploy never ships: each administrator points App > Options to
//          a folder that holds it). Rules shared by LiteDeploy and the worker:
//          the worker command ("siw" and its fixed options), the SIW command
//          line built from numbers only (sections as bit masks, never free
//          text; the sections that hold secrets or directory dumps are always
//          removed), the executable for the architecture of the computer, the
//          expected publisher of the signature (O= of the signer). Pure logic,
//          compiled into both programs (and the tests).
// -----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace LiteDeploy
{
    internal enum SiwFormat
    {
        /// <summary>XML report only (default): read by LiteDeploy for the view and the summary.</summary>
        Xml,
        /// <summary>The XML report, then the HTML report of SIW itself (SIW runs twice).</summary>
        Html
    }

    internal enum SiwArchitecture
    {
        X86,
        X64,
        Arm64
    }

    /// <summary>What LiteDeploy asks the worker: format, sections, expected publisher, time limit, server guard.</summary>
    internal sealed class SiwRequest
    {
        public SiwFormat Format = SiwFormat.Xml;
        public ulong Software = SiwPolicy.DefaultSoftware;
        public ulong Hardware = SiwPolicy.DefaultHardware;
        public ulong Network = SiwPolicy.DefaultNetwork;
        public string Publisher = SiwPolicy.DefaultPublisher;
        public int TimeoutSeconds = SiwPolicy.DefaultTimeoutSeconds;
        public bool AllowServers;
        public bool AllowDomainControllers;
    }

    internal static class SiwPolicy
    {
        /// <summary>Worker command.</summary>
        public const string Command = "siw";
        /// <summary>Type of the worker line that gives the result of the run.</summary>
        public const string TypeResult = "siw";

        /// <summary>O= of the signer of the SIW executables (vendor of SIW).</summary>
        public const string DefaultPublisher = "Topala Software Solutions";

        /// <summary>Software sections: Operating System (1) and Installed Programs (4).</summary>
        public const ulong DefaultSoftware = 0x5;
        /// <summary>
        /// Hardware sections: System Summary (1), Motherboard (2), BIOS (4), CPU (8), Memory (128),
        /// Video (256), Storage Devices (512), Network Adapters (4096), Logical Disks (131072).
        /// </summary>
        public const ulong DefaultHardware = 136079;
        /// <summary>Network sections: Network Information (1).</summary>
        public const ulong DefaultNetwork = 0x1;

        /// <summary>Software section never requested: Licenses (product keys in clear text).</summary>
        public const ulong ForbiddenSoftware = 0x40000000;
        /// <summary>
        /// Network sections never requested: accounts and directory dumps (System Accounts 128, Local Groups
        /// 256, Local User Accounts 512, Domain Groups 1024, Domain User Accounts 2048, Computers, Groups and
        /// Users from Active Directory 4096, 8192, 16384).
        /// </summary>
        public const ulong ForbiddenNetwork = 0x80 | 0x100 | 0x200 | 0x400 | 0x800 | 0x1000 | 0x2000 | 0x4000;

        public const int DefaultTimeoutSeconds = 300;
        public const int MinTimeoutSeconds = 30;
        public const int MaxTimeoutSeconds = 1800;

        /// <summary>Folder of the run under %windir%\LiteDeploy\work (administrators and SYSTEM write there).</summary>
        public const string WorkFolderName = "SIW";
        /// <summary>Name of the reports in the work folder, without extension.</summary>
        public const string ReportBaseName = "report";
        /// <summary>License file of SIW, copied next to the executable when the folder has one.</summary>
        public const string LicenseFileName = "siw.lic";
        /// <summary>Settings file SIW may write next to itself: never copied, always deleted.</summary>
        public const string ConfigFileName = "siw.cfg";

        public const string AllowServersOption = ServerPolicy.AllowServersOption;
        public const string AllowDomainControllersOption = ServerPolicy.AllowDomainControllersOption;

        /// <summary>The SIW executables LiteDeploy may run (Windows 10 and 11).</summary>
        public static readonly string[] Executables = { "siw64.exe", "siw-arm64.exe", "siw32.exe" };

        /// <summary>
        /// The executable for an architecture. The launcher siw.exe is never used: it returns at once while
        /// the real executable keeps running, so its end says nothing about the report.
        /// </summary>
        public static string ExecutableFor(SiwArchitecture architecture)
        {
            switch (architecture)
            {
                case SiwArchitecture.Arm64: return "siw-arm64.exe";
                case SiwArchitecture.X64: return "siw64.exe";
                default: return "siw32.exe";
            }
        }

        /// <summary>
        /// The architecture of a Windows from its folders: SysArm32 only exists on ARM64, SysWOW64 on a 64-bit
        /// Windows (x64 or ARM64). Read through ADMIN$ by LiteDeploy, locally by the worker.
        /// </summary>
        public static SiwArchitecture ArchitectureFrom(bool hasSysArm32, bool hasSysWow64)
        {
            if (hasSysArm32) return SiwArchitecture.Arm64;
            return hasSysWow64 ? SiwArchitecture.X64 : SiwArchitecture.X86;
        }

        /// <summary>The sections actually requested: the forbidden ones removed.</summary>
        public static ulong SafeSoftware(ulong mask) { return mask & ~ForbiddenSoftware; }
        public static ulong SafeNetwork(ulong mask) { return mask & ~ForbiddenNetwork; }

        /// <summary>"xml" or "html": the extension and the /log: format of SIW.</summary>
        public static string Extension(SiwFormat format)
        {
            return format == SiwFormat.Html ? "html" : "xml";
        }

        /// <summary>
        /// The SIW command line of one report: /log:&lt;ext&gt;="path" /silent /s: /h: /n: (forbidden sections
        /// removed). Never /all, /upload, /email, /shutdown or any free text. Throws on a path that cannot be
        /// quoted safely.
        /// </summary>
        public static string SiwArguments(string reportPath, SiwFormat format, ulong software, ulong hardware, ulong network)
        {
            if (string.IsNullOrWhiteSpace(reportPath) || reportPath.IndexOfAny(new[] { '"', '\r', '\n' }) >= 0 || reportPath.EndsWith("\\", StringComparison.Ordinal))
                throw new ArgumentException("Invalid report path", "reportPath");
            return string.Format(CultureInfo.InvariantCulture, "/log:{0}=\"{1}\" /silent /s:{2} /h:{3} /n:{4}",
                Extension(format), reportPath, SafeSoftware(software), hardware, SafeNetwork(network));
        }

        /// <summary>A publisher name LiteDeploy passes on a command line: letters, digits, spaces and . , &amp; ' ( ) -</summary>
        public static bool IsValidPublisher(string publisher)
        {
            if (string.IsNullOrWhiteSpace(publisher) || publisher.Length > 100 || publisher != publisher.Trim()) return false;
            return publisher.All(c => char.IsLetterOrDigit(c) || " .,&'()-".IndexOf(c) >= 0);
        }

        /// <summary>
        /// Valid signature whose signer subject has O=<paramref name="publisher"/> exactly (quoted or not). The
        /// certificate itself is not pinned: SIW is signed with short-lived certificates (a new thumbprint for
        /// each release).
        /// </summary>
        public static bool IsSignedBy(bool signatureValid, string signerSubject, string publisher)
        {
            if (!signatureValid || string.IsNullOrEmpty(signerSubject) || !IsValidPublisher(publisher)) return false;
            string pattern = @"(^|,\s*)O=(""?)" + Regex.Escape(publisher) + @"\2\s*(,|$)";
            return Regex.IsMatch(signerSubject, pattern);
        }

        /// <summary>The worker command line of a request (fixed options, numbers and a checked publisher only).</summary>
        public static string WorkerArguments(SiwRequest request)
        {
            if (request == null) throw new ArgumentNullException("request");
            if (!IsValidPublisher(request.Publisher)) throw new ArgumentException("Invalid publisher: " + request.Publisher, "request");
            StringBuilder sb = new StringBuilder(Command);
            sb.Append(" --format ").Append(Extension(request.Format));
            sb.Append(" --software ").Append(SafeSoftware(request.Software).ToString(CultureInfo.InvariantCulture));
            sb.Append(" --hardware ").Append(request.Hardware.ToString(CultureInfo.InvariantCulture));
            sb.Append(" --network ").Append(SafeNetwork(request.Network).ToString(CultureInfo.InvariantCulture));
            sb.Append(" --publisher \"").Append(request.Publisher).Append('"');
            sb.Append(" --timeout ").Append(ClampTimeout(request.TimeoutSeconds).ToString(CultureInfo.InvariantCulture));
            if (request.AllowServers)
            {
                sb.Append(' ').Append(AllowServersOption);
                if (request.AllowDomainControllers) sb.Append(' ').Append(AllowDomainControllersOption);
            }
            return sb.ToString();
        }

        /// <summary>A time limit kept between MinTimeoutSeconds and MaxTimeoutSeconds.</summary>
        public static int ClampTimeout(int seconds)
        {
            return Math.Max(MinTimeoutSeconds, Math.Min(MaxTimeoutSeconds, seconds));
        }

        /// <summary>
        /// Parses "siw --format xml|html --software N --hardware N --network N --publisher P --timeout S
        /// [--allow-servers [--allow-domain-controllers]]" (every option but the guard has a default).
        /// Returns null, or why the arguments are refused.
        /// </summary>
        public static string ParseArguments(string[] args, out SiwRequest request)
        {
            request = new SiwRequest();
            if (args == null || args.Length == 0 || !string.Equals(args[0], Command, StringComparison.OrdinalIgnoreCase)) return "Not a siw command";
            for (int i = 1; i < args.Length; i++)
            {
                string a = args[i];
                bool hasValue = i + 1 < args.Length;
                ulong mask;
                int seconds;
                switch (a)
                {
                    case "--format":
                        if (!hasValue) return "--format needs a value";
                        string f = args[++i].ToLowerInvariant();
                        if (f == "xml") request.Format = SiwFormat.Xml;
                        else if (f == "html") request.Format = SiwFormat.Html;
                        else return "Invalid format: " + args[i];
                        break;
                    case "--software":
                        if (!hasValue || !ulong.TryParse(args[++i], NumberStyles.None, CultureInfo.InvariantCulture, out mask)) return "Invalid --software";
                        request.Software = SafeSoftware(mask);
                        break;
                    case "--hardware":
                        if (!hasValue || !ulong.TryParse(args[++i], NumberStyles.None, CultureInfo.InvariantCulture, out mask)) return "Invalid --hardware";
                        request.Hardware = mask;
                        break;
                    case "--network":
                        if (!hasValue || !ulong.TryParse(args[++i], NumberStyles.None, CultureInfo.InvariantCulture, out mask)) return "Invalid --network";
                        request.Network = SafeNetwork(mask);
                        break;
                    case "--publisher":
                        if (!hasValue || !IsValidPublisher(args[++i])) return "Invalid --publisher";
                        request.Publisher = args[i];
                        break;
                    case "--timeout":
                        if (!hasValue || !int.TryParse(args[++i], NumberStyles.None, CultureInfo.InvariantCulture, out seconds)
                            || seconds < MinTimeoutSeconds || seconds > MaxTimeoutSeconds) return "Invalid --timeout";
                        request.TimeoutSeconds = seconds;
                        break;
                    case AllowServersOption:
                        request.AllowServers = true;
                        break;
                    case AllowDomainControllersOption:
                        request.AllowDomainControllers = true;
                        break;
                    default:
                        return "Unknown argument: " + a;
                }
            }
            if (request.Software == 0 && request.Hardware == 0 && request.Network == 0) return "No section requested";
            return null;
        }

        /// <summary>The report files of a format, in the order they are made: report.xml, then report.html.</summary>
        public static List<string> ReportFiles(SiwFormat format)
        {
            List<string> files = new List<string> { ReportBaseName + ".xml" };
            if (format == SiwFormat.Html) files.Add(ReportBaseName + ".html");
            return files;
        }
    }
}
