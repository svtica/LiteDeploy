// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    PortableToolPolicy.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-23
// Purpose: Fixed table of the portable third-party report tools (never
//          shipped: each administrator points App > Options to a folder that
//          holds them). Each entry gives its executable, the expected signer
//          (O= of the certificate), its fixed command line (only the report
//          path is filled in) and the format of its report. Shared by
//          LiteDeploy and the worker command "tool <id>" (PortableToolRunner).
//          A tool is added by adding an entry here, never by typing a command
//          line. SIW keeps its own command ("siw", SiwPolicy): its sections and
//          formats need options of their own. Pure logic, compiled into both
//          programs (and the tests).
// -----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace LiteDeploy
{
    /// <summary>One portable report tool of the fixed table.</summary>
    internal sealed class PortableTool
    {
        /// <summary>Identifier (worker argument, work folder, report folder): lower-case letters and digits.</summary>
        public string Id;
        /// <summary>Product name, as its vendor writes it.</summary>
        public string Product;
        /// <summary>Executable file name (the same on every architecture: 32-bit tools run everywhere).</summary>
        public string Executable;
        /// <summary>O= of the signer of the executable.</summary>
        public string Publisher;
        /// <summary>Command line of the tool; {report} is replaced by the quoted report path.</summary>
        public string ArgumentsTemplate;
        /// <summary>Extension of the report ("csv").</summary>
        public string ReportExtension;
    }

    /// <summary>What LiteDeploy asks the worker for one tool.</summary>
    internal sealed class PortableToolRequest
    {
        public PortableTool Tool;
        public int TimeoutSeconds = PortableToolPolicy.DefaultTimeoutSeconds;
        public bool AllowServers;
        public bool AllowDomainControllers;
    }

    internal static class PortableToolPolicy
    {
        /// <summary>Worker command: "tool &lt;id&gt; --timeout S [--allow-servers [--allow-domain-controllers]]".</summary>
        public const string Command = "tool";
        /// <summary>Type of the worker line that gives the result of the run.</summary>
        public const string TypeResult = "tool";
        /// <summary>Folder of the runs under %windir%\LiteDeploy\work (one sub-folder per tool).</summary>
        public const string WorkFolderName = "Tools";
        /// <summary>Name of the report in the work folder, without extension.</summary>
        public const string ReportBaseName = "report";

        public const int DefaultTimeoutSeconds = 120;
        public const int MinTimeoutSeconds = 10;
        public const int MaxTimeoutSeconds = 1800;

        public const string NirSoft = "Nir Sofer";

        /// <summary>
        /// The table. NirSoft tools: "/SaveFileEncoding 2" asks for a Unicode file (read by its byte order
        /// mark), "/scomma" exports every row to a CSV file and exits without a window.
        /// </summary>
        public static readonly PortableTool[] All =
        {
            new PortableTool
            {
                Id = "uninstallview", Product = "UninstallView", Executable = "UninstallView.exe", Publisher = NirSoft,
                ArgumentsTemplate = "/SaveFileEncoding 2 /scomma {report}", ReportExtension = "csv"
            },
            new PortableTool
            {
                Id = "winupdatesview", Product = "WinUpdatesView", Executable = "WinUpdatesView.exe", Publisher = NirSoft,
                ArgumentsTemplate = "/SaveFileEncoding 2 /scomma {report}", ReportExtension = "csv"
            }
        };

        /// <summary>The tool of an identifier (case ignored); null when the table has none.</summary>
        public static PortableTool Find(string id)
        {
            return All.FirstOrDefault(t => string.Equals(t.Id, (id ?? "").Trim(), StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>The command line of one run: the template with the quoted report path. Throws on a path that cannot be quoted safely.</summary>
        public static string Arguments(PortableTool tool, string reportPath)
        {
            if (tool == null) throw new ArgumentNullException("tool");
            if (string.IsNullOrWhiteSpace(reportPath) || reportPath.IndexOfAny(new[] { '"', '\r', '\n' }) >= 0 || reportPath.EndsWith("\\", StringComparison.Ordinal))
                throw new ArgumentException("Invalid report path", "reportPath");
            return tool.ArgumentsTemplate.Replace("{report}", "\"" + reportPath + "\"");
        }

        /// <summary>The report file name of a tool: report.&lt;ext&gt;.</summary>
        public static string ReportFileName(PortableTool tool)
        {
            return ReportBaseName + "." + tool.ReportExtension;
        }

        public static int ClampTimeout(int seconds)
        {
            return Math.Max(MinTimeoutSeconds, Math.Min(MaxTimeoutSeconds, seconds));
        }

        /// <summary>The worker command line of a request (the identifier of the table and numbers only).</summary>
        public static string WorkerArguments(PortableToolRequest request)
        {
            if (request == null || request.Tool == null || Find(request.Tool.Id) == null) throw new ArgumentException("Unknown tool", "request");
            StringBuilder sb = new StringBuilder(Command);
            sb.Append(' ').Append(request.Tool.Id);
            sb.Append(" --timeout ").Append(ClampTimeout(request.TimeoutSeconds).ToString(CultureInfo.InvariantCulture));
            if (request.AllowServers)
            {
                sb.Append(' ').Append(ServerPolicy.AllowServersOption);
                if (request.AllowDomainControllers) sb.Append(' ').Append(ServerPolicy.AllowDomainControllersOption);
            }
            return sb.ToString();
        }

        /// <summary>Parses "tool &lt;id&gt; [--timeout S] [--allow-servers [--allow-domain-controllers]]". Returns null, or why it is refused.</summary>
        public static string ParseArguments(string[] args, out PortableToolRequest request)
        {
            request = new PortableToolRequest();
            if (args == null || args.Length < 2 || !string.Equals(args[0], Command, StringComparison.OrdinalIgnoreCase)) return "Usage: tool <id> [--timeout seconds]";
            request.Tool = Find(args[1]);
            if (request.Tool == null) return "Unknown tool: " + args[1];
            for (int i = 2; i < args.Length; i++)
            {
                string a = args[i];
                int seconds;
                if (a == "--timeout" && i + 1 < args.Length)
                {
                    if (!int.TryParse(args[++i], NumberStyles.None, CultureInfo.InvariantCulture, out seconds) || seconds < MinTimeoutSeconds || seconds > MaxTimeoutSeconds)
                        return "Invalid --timeout";
                    request.TimeoutSeconds = seconds;
                }
                else if (a == ServerPolicy.AllowServersOption) request.AllowServers = true;
                else if (a == ServerPolicy.AllowDomainControllersOption) request.AllowDomainControllers = true;
                else return "Unknown argument: " + a;
            }
            return null;
        }
    }
}
