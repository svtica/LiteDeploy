// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    QuickCommands.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-18
// Purpose: The quick commands of the computer actions: a fixed table of
//          non-interactive Windows commands (group policy refresh, DNS cache,
//          print spooler, restart) run as SYSTEM by the worker
//          ("LiteDeploy.Worker.exe quick <id>"). Only the identifier travels to
//          the computer: no free text is ever run. Compiled into both programs
//          (and the tests).
// -----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Linq;

namespace LiteDeploy
{
    /// <summary>One program run by a quick command (in %windir%\System32).</summary>
    internal sealed class QuickCommandStep
    {
        public string FileName;
        public string Arguments;
        /// <summary>A failure of this step does not stop the next ones (net stop of a stopped service).</summary>
        public bool IgnoreFailure;
    }

    /// <summary>One quick command: identifier, English label (translated by LiteDeploy), steps.</summary>
    internal sealed class QuickCommand
    {
        public string Id;
        public string Label;
        /// <summary>Asks for a confirmation before it runs (it disturbs the users of the computer).</summary>
        public bool Confirm;
        public List<QuickCommandStep> Steps = new List<QuickCommandStep>();

        /// <summary>The steps as typed in a console ("gpupdate.exe /force"), for the output window.</summary>
        public string CommandText
        {
            get { return string.Join(" & ", Steps.Select(s => s.FileName + (string.IsNullOrEmpty(s.Arguments) ? "" : " " + s.Arguments))); }
        }
    }

    internal static class QuickCommands
    {
        /// <summary>Worker command: "quick &lt;id&gt;".</summary>
        public const string WorkerCommand = "quick";

        /// <summary>Message of each line written by a step (type=output, text=...), then one type=quick line per command.</summary>
        public const string TypeOutput = "output";
        public const string TypeResult = "quick";

        /// <summary>Delay of the restart, in seconds: the logged-on users are warned and can save their work.</summary>
        public const int RestartDelaySeconds = 300;

        public const string GroupPolicy = "gpupdate";
        public const string GroupPolicyComputer = "gpupdate-computer";
        public const string FlushDns = "flushdns";
        public const string RestartSpooler = "restart-spooler";
        public const string Restart = "restart";
        public const string CancelRestart = "cancel-restart";

        /// <summary>The table, in the order of the menu.</summary>
        public static readonly IList<QuickCommand> All = new List<QuickCommand>
        {
            Make(GroupPolicy, "Refresh the group policies (gpupdate /force)", false, Step("gpupdate.exe", "/force")),
            Make(GroupPolicyComputer, "Refresh the computer policies only (gpupdate /target:computer /force)", false, Step("gpupdate.exe", "/target:computer /force")),
            Make(FlushDns, "Flush the DNS cache (ipconfig /flushdns)", false, Step("ipconfig.exe", "/flushdns")),
            Make(RestartSpooler, "Restart the print spooler", true,
                new QuickCommandStep { FileName = "net.exe", Arguments = "stop spooler /y", IgnoreFailure = true },
                Step("net.exe", "start spooler")),
            Make(Restart, "Restart the computer in 5 minutes (users are warned)", true,
                Step("shutdown.exe", "/r /t " + RestartDelaySeconds + " /d p:0:0 /c \"Restart requested by the administrator (LiteDeploy).\"")),
            Make(CancelRestart, "Cancel a planned restart (shutdown /a)", false, Step("shutdown.exe", "/a"))
        }.AsReadOnly();

        static QuickCommand Make(string id, string label, bool confirm, params QuickCommandStep[] steps)
        {
            return new QuickCommand { Id = id, Label = label, Confirm = confirm, Steps = steps.ToList() };
        }

        static QuickCommandStep Step(string file, string arguments)
        {
            return new QuickCommandStep { FileName = file, Arguments = arguments };
        }

        /// <summary>The command of that identifier (case-insensitive); null when there is none.</summary>
        public static QuickCommand Find(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return null;
            return All.FirstOrDefault(c => string.Equals(c.Id, id.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Worker arguments of a quick command: "quick &lt;id&gt;". Unknown identifier: ArgumentException.</summary>
        public static string WorkerArguments(string id)
        {
            QuickCommand c = Find(id);
            if (c == null) throw new ArgumentException("Unknown quick command: " + id, "id");
            return WorkerCommand + " " + c.Id;
        }
    }
}
