// -----------------------------------------------------------------------------
// LiteSuite.Common - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    ProcessFailureFormatter.cs
// Author:  LiteSuite contributors
// Date:    2026-11-12
// Purpose: Failure message of a child process that exits with a non-zero code:
//          exit code, duration, stderr and the last lines of stdout (some tools,
//          sqlcmd for example, write their errors on stdout). Ported from
//          LiteTask. The message always starts with "Error:".
// -----------------------------------------------------------------------------
using System;
using System.Globalization;
using System.Text;

namespace LiteSuite
{
    public static class ProcessFailureFormatter
    {
        public const int DefaultTailLines = 40;
        public const int DefaultTailChars = 8000;

        public static string BuildFailureMessage(int exitCode, string standardOutput, string standardError, TimeSpan duration,
                                                 int maxTailLines = DefaultTailLines, int maxTailChars = DefaultTailChars)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(string.Format(CultureInfo.InvariantCulture, "Error: Process exited with code {0} after {1:F1} s", exitCode, duration.TotalSeconds));

            string err = (standardError ?? string.Empty).Trim();
            string output = (standardOutput ?? string.Empty).Trim();

            if (err.Length > 0)
            {
                sb.AppendLine();
                sb.Append("stderr: ");
                sb.Append(TailChars(err, maxTailChars));
            }

            if (output.Length > 0)
            {
                string tail = TailLines(output, maxTailLines);
                int total = CountLines(output);
                // Lines kept, not counting the "..." line that TailLines adds when it truncates
                int kept = maxTailLines <= 0 ? total : Math.Min(total, maxTailLines);
                sb.AppendLine();
                sb.Append(string.Format(CultureInfo.InvariantCulture, "stdout (last {0} of {1} lines): ", kept, total));
                sb.Append(TailChars(tail, maxTailChars));
            }

            return sb.ToString();
        }

        /// <summary>Last <paramref name="maxLines"/> lines of the text (any line ending), prefixed with "..." when truncated.</summary>
        public static string TailLines(string text, int maxLines)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            string[] lines = text.Replace("\r\n", "\n").Split('\n');
            if (maxLines <= 0 || lines.Length <= maxLines) return text;
            return "..." + Environment.NewLine + string.Join(Environment.NewLine, lines, lines.Length - maxLines, maxLines);
        }

        /// <summary>Last <paramref name="maxChars"/> characters, prefixed with "..." when truncated.</summary>
        public static string TailChars(string text, int maxChars)
        {
            if (text == null) return string.Empty;
            if (maxChars <= 0 || text.Length <= maxChars) return text;
            return "..." + text.Substring(text.Length - maxChars);
        }

        public static int CountLines(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            return text.Replace("\r\n", "\n").Split('\n').Length;
        }
    }
}
