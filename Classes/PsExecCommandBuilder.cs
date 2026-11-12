// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    PsExecCommandBuilder.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-11
// Purpose: Builds the PsExec command line of a deployment from the options of
//          the main window. Pure string logic (unit-tested), ported from
//          LiteTask's PsExecCommandBuilder: -accepteula -nobanner, arguments
//          quoted with the CommandLineToArgvW rules (LiteSuite.PsExecCommandLine,
//          shared with LiteTask).
//
//          The password is on the PsExec command line: PsExec has no other
//          way to receive it (when -p is omitted it prompts on the console).
//          A process command line is readable by the same user and by local
//          administrators only; the console and logs get DisplayArguments,
//          where the password is masked.
// -----------------------------------------------------------------------------
using System;
using System.Text;
using LiteSuite;

namespace LiteDeploy
{
    /// <summary>Options of one PsExec run (one target computer).</summary>
    public class PsExecOptions
    {
        public String Computer;
        public bool Interactive;
        public int Session;
        public bool DontWait;                 // -d
        public String WorkingDirectory;       // -w (null or empty: none)
        public bool CopyExecutable;           // -c
        public bool OverwriteExisting;        // -f
        public int ConnectionTimeout;         // -n, seconds (0: none)
        public bool UseCredentials;           // -u / -p
        public String UserName;
        public String Password;
        public bool RunAsSystem;              // -s
        public int PriorityIndex = 2;         // 0 low, 1 below normal, 2 normal, 3 above normal, 4 high, 5 realtime
        public String Executable;             // quoted; empty: the arguments are the command
        public String Arguments;              // appended as typed
    }

    static public class PsExecCommandBuilder
    {
        public const String MaskedPassword = "******";

        static readonly String[] PriorityFlags = { "-low", "-belownormal", null, "-abovenormal", "-high", "-realtime" };

        /// <summary>Arguments passed to PsExec.</summary>
        static public String BuildArguments(PsExecOptions o)
        {
            return Build(o, false);
        }

        /// <summary>Same arguments with the password masked, for the console and the logs.</summary>
        static public String DisplayArguments(PsExecOptions o)
        {
            return Build(o, true);
        }

        static String Build(PsExecOptions o, bool maskPassword)
        {
            if (o == null)
                throw new ArgumentNullException("o");
            if (String.IsNullOrWhiteSpace(o.Computer))
                throw new ArgumentException("A target computer is required.", "o");

            StringBuilder args = new StringBuilder("-accepteula -nobanner");
            args.Append(" \\\\").Append(o.Computer.Trim());
            args.Append(" -h");

            if (o.Interactive)
                args.Append(" -i ").Append(o.Session);

            if (o.DontWait)
                args.Append(" -d");

            if (!String.IsNullOrEmpty(o.WorkingDirectory))
                args.Append(" -w ").Append(QuoteArgument(o.WorkingDirectory, true));

            if (o.CopyExecutable)
                args.Append(" -c");

            if (o.OverwriteExisting)
                args.Append(" -f");

            if (o.ConnectionTimeout > 0)
                args.Append(" -n ").Append(o.ConnectionTimeout);

            if (o.UseCredentials)
            {
                args.Append(" -u ").Append(QuoteArgument((o.UserName ?? "").Trim()));
                // Always quoted: spaces and metacharacters survive; an empty password is ""
                // so PsExec does not prompt on the console
                args.Append(" -p ").Append(maskPassword ? MaskedPassword : QuoteArgument(o.Password ?? "", true));
            }

            else if (o.RunAsSystem)
            {
                args.Append(" -s");
            }

            if (o.PriorityIndex >= 0 && o.PriorityIndex < PriorityFlags.Length && PriorityFlags[o.PriorityIndex] != null)
                args.Append(' ').Append(PriorityFlags[o.PriorityIndex]);

            if (!String.IsNullOrWhiteSpace(o.Executable))
                args.Append(' ').Append(QuoteArgument(o.Executable.Trim(), true));

            String arguments = (o.Arguments ?? "").Trim();
            if (arguments.Length > 0)
                args.Append(' ').Append(arguments);

            return args.ToString();
        }

        static String QuoteArgument(String value, bool forceQuotes = false)
        {
            return PsExecCommandLine.QuoteArgument(value, forceQuotes);
        }
    }
}
