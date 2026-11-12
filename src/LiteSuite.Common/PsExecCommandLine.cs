// -----------------------------------------------------------------------------
// LiteSuite.Common - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    PsExecCommandLine.cs
// Author:  LiteSuite contributors
// Date:    2026-11-12
// Purpose: PsExec command line pieces shared by LiteTask and LiteDeploy, ported
//          from LiteTask's PsExecCommandBuilder: argument quoting with the
//          CommandLineToArgvW rules, the credential form used by LiteTask, the
//          executable and batch command shapes, and the recognition of PsExec
//          progress lines.
//
//          The password is on the PsExec command line: PsExec has no other way
//          to receive it (when -p is omitted it prompts on the console). A
//          process command line is readable by the same user and by local
//          administrators only.
// -----------------------------------------------------------------------------
using System;
using System.Text;
using System.Text.RegularExpressions;

namespace LiteSuite
{
    public static class PsExecCommandLine
    {
        /// <summary>
        /// Arguments for running <paramref name="command"/> under a stored credential (LiteTask form):
        /// -accepteula -nobanner [-i] [-h] -u user -p "password" command.
        /// </summary>
        public static string BuildArguments(string username, string password, bool requiresElevation, bool interactive, string command)
        {
            if (string.IsNullOrWhiteSpace(username))
                throw new ArgumentException("A username is required to run through PsExec.", "username");
            if (string.IsNullOrWhiteSpace(command))
                throw new ArgumentException("A command is required to run through PsExec.", "command");

            StringBuilder args = new StringBuilder("-accepteula -nobanner");
            // -i is only valid in a user session: under a service there is no visible desktop
            if (interactive) args.Append(" -i");
            if (requiresElevation) args.Append(" -h");
            args.Append(" -u ").Append(QuoteArgument(username.Trim()));
            // Always quoted; an empty password is "" so PsExec does not prompt on the console
            args.Append(" -p ").Append(QuoteArgument(password ?? string.Empty, true));
            args.Append(' ').Append(command.Trim());
            return args.ToString();
        }

        /// <summary>"target" parameters, or cmd.exe /c parameters when there is no target.</summary>
        public static string BuildExecutableCommand(string target, string parameters)
        {
            string p = (parameters ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(target))
                return ("cmd.exe /c " + p).TrimEnd();
            return (QuoteArgument(target.Trim(), true) + " " + p).TrimEnd();
        }

        /// <summary>cmd.exe /c "batch file" parameters.</summary>
        public static string BuildBatchCommand(string batchFile, string parameters)
        {
            if (string.IsNullOrWhiteSpace(batchFile))
                throw new ArgumentException("A batch file path is required.", "batchFile");
            string p = (parameters ?? string.Empty).Trim();
            return ("cmd.exe /c " + QuoteArgument(batchFile.Trim(), true) + " " + p).TrimEnd();
        }

        /// <summary>
        /// Quotes one argument with the Windows C runtime / CommandLineToArgvW rules (backslashes are
        /// only special before a quote), so the child receives exactly <paramref name="value"/> in its
        /// argv entry. Without <paramref name="forceQuotes"/>, a value that needs no quoting is returned
        /// unchanged.
        /// </summary>
        public static string QuoteArgument(string value, bool forceQuotes = false)
        {
            if (value == null) value = string.Empty;

            bool needsQuotes = forceQuotes || value.Length == 0 || value.IndexOfAny(new[] { ' ', '\t', '\r', '\n', '"' }) >= 0;
            if (!needsQuotes) return value;

            StringBuilder sb = new StringBuilder();
            sb.Append('"');
            int i = 0;
            while (i < value.Length)
            {
                int backslashes = 0;
                while (i < value.Length && value[i] == '\\')
                {
                    backslashes++;
                    i++;
                }

                if (i == value.Length)
                {
                    // Trailing backslashes: doubled so the closing quote stays a delimiter
                    sb.Append('\\', backslashes * 2);
                }
                else if (value[i] == '"')
                {
                    // Backslashes before a quote: doubled, then the quote is escaped
                    sb.Append('\\', backslashes * 2 + 1);
                    sb.Append('"');
                    i++;
                }
                else
                {
                    sb.Append('\\', backslashes);
                    sb.Append(value[i]);
                    i++;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }

        static readonly Regex StatusLinePattern = new Regex(
            @"^(Connecting (to|with) .+\.\.\.|Starting .+ on .+\.\.\.|Copying .+ to .+\.\.\.|.+ exited on .+ with error code -?\d+\.?)$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>True for a PsExec progress line (connecting, starting, copying, exited with code).</summary>
        public static bool IsStatusLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return false;
            return StatusLinePattern.IsMatch(line.Trim());
        }
    }
}
