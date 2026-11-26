// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    QuickCommands.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-18
// Purpose: The quick commands of the computer actions: a fixed table of
//          non-interactive Windows commands (group policy refresh, DNS cache,
//          print spooler, TeamViewer service, remote desktop, sleep and
//          hibernation, restart) run as SYSTEM by the worker
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
        /// <summary>Exit codes other than 0 that mean success for this step (sc start of a running service: 1056).</summary>
        public int[] AcceptedCodes;

        /// <summary>Whether <paramref name="code"/> is a success for this step: 0 or one of AcceptedCodes.</summary>
        public bool IsSuccess(int code)
        {
            return code == 0 || (AcceptedCodes != null && Array.IndexOf(AcceptedCodes, code) >= 0);
        }
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

        /// <summary>Key of the remote desktop switch (fDenyTSConnections: 0 allows the connections, 1 denies them).</summary>
        const string TerminalServerKey = @"""HKLM\SYSTEM\CurrentControlSet\Control\Terminal Server""";

        /// <summary>Windows Firewall rule group "Remote Desktop", by its resource name (the same in every language).</summary>
        const string RemoteDesktopRuleGroup = "group=\"@FirewallAPI.dll,-28752\"";

        /// <summary>sc.exe start: the service is already running (ERROR_SERVICE_ALREADY_RUNNING).</summary>
        public const int ErrorServiceAlreadyRunning = 1056;

        /// <summary>Delay of the restart, in seconds: the logged-on users are warned and can save their work.</summary>
        public const int RestartDelaySeconds = 300;

        public const string GroupPolicy = "gpupdate";
        public const string FlushDns = "flushdns";
        public const string RestartSpooler = "restart-spooler";
        public const string StartTeamViewer = "start-teamviewer";
        public const string EnableRemoteDesktop = "enable-rdp";
        public const string DisableRemoteDesktop = "disable-rdp";
        public const string DisableSleep = "disable-sleep";
        public const string Restart = "restart";
        public const string CancelRestart = "cancel-restart";

        /// <summary>The table, in the order of the menu.</summary>
        public static readonly IList<QuickCommand> All = new List<QuickCommand>
        {
            Make(GroupPolicy, "Refresh the group policies (gpupdate /force)", false, Step("gpupdate.exe", "/force")),
            Make(FlushDns, "Flush the DNS cache (ipconfig /flushdns)", false, Step("ipconfig.exe", "/flushdns")),
            Make(RestartSpooler, "Restart the print spooler", true,
                new QuickCommandStep { FileName = "net.exe", Arguments = "stop spooler /y", IgnoreFailure = true },
                Step("net.exe", "start spooler")),
            // Name of the service installed by TeamViewer (full client and Host). The startup type is set to
            // automatic first: a disabled service cannot start. 1060: not installed (the step fails and says so);
            // 1056: already running (success).
            Make(StartTeamViewer, "Start the TeamViewer service (automatic startup)", true,
                Step("sc.exe", "config TeamViewer start= auto"),
                new QuickCommandStep { FileName = "sc.exe", Arguments = "start TeamViewer", AcceptedCodes = new[] { ErrorServiceAlreadyRunning } }),
            // Remote desktop: the switch of the Terminal Server key, then the rules of the firewall group (a firewall
            // managed by group policy may keep them closed: the step then fails and says so). Network Level
            // Authentication is left as it is.
            Make(EnableRemoteDesktop, "Allow remote desktop connections (and its firewall rules)", true,
                Step("reg.exe", "add " + TerminalServerKey + " /v fDenyTSConnections /t REG_DWORD /d 0 /f"),
                Step("netsh.exe", "advfirewall firewall set rule " + RemoteDesktopRuleGroup + " new enable=Yes")),
            Make(DisableRemoteDesktop, "Deny remote desktop connections", true,
                Step("reg.exe", "add " + TerminalServerKey + " /v fDenyTSConnections /t REG_DWORD /d 1 /f"),
                new QuickCommandStep { FileName = "netsh.exe", Arguments = "advfirewall firewall set rule " + RemoteDesktopRuleGroup + " new enable=No", IgnoreFailure = true }),
            // Power: no sleep and no hibernation on AC power (the battery settings of laptops are left alone), then
            // hibernation off (no hiberfil.sys, no fast startup). Applied to the active power plan of the computer.
            Make(DisableSleep, "Disable sleep and hibernation (on AC power)", false,
                Step("powercfg.exe", "/change standby-timeout-ac 0"),
                Step("powercfg.exe", "/change hibernate-timeout-ac 0"),
                Step("powercfg.exe", "/hibernate off")),
            Make(Restart, "Restart the computer in 5 minutes (users are warned)", true,
                Step("shutdown.exe", "/r /t " + RestartDelaySeconds + " /d p:0:0 /c \"" + RestartComment(false) + "\"")),
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

        /// <summary>Comment of the restart, shown to the users by Windows (in the language of the messages).</summary>
        public static string RestartComment(bool french)
        {
            return french ? "Redémarrage demandé par l'administrateur (LiteDeploy)." : "Restart requested by the administrator (LiteDeploy).";
        }

        /// <summary>
        /// The arguments of a step in the language of the messages to the users: the restart gets its comment in French
        /// when <paramref name="french"/>; any other step is unchanged.
        /// </summary>
        public static string LocalizedArguments(QuickCommand command, QuickCommandStep step, bool french)
        {
            if (french && command != null && command.Id == Restart && step.FileName == "shutdown.exe")
                return step.Arguments.Replace("\"" + RestartComment(false) + "\"", "\"" + RestartComment(true) + "\"");
            return step.Arguments;
        }

        /// <summary>Worker arguments with the language of the messages: "quick &lt;id&gt; --message-language fr".</summary>
        public static string WorkerArguments(string id, string messageLanguage)
        {
            string args = WorkerArguments(id);
            return CloseWarning.IsValidLanguage(messageLanguage) ? args + " " + CloseWarning.LanguageOption + " " + messageLanguage : args;
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
