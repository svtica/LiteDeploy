// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    UserMessage.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-18
// Purpose: A message shown to the users logged on to a computer
//          ("LiteDeploy.Worker.exe message --text <base64> [--seconds N]
//          [--title <base64>]"): cleaning and length limit of the text, the
//          worker arguments (the text and the title travel as base64 UTF-8,
//          never as raw text on a command line: no quoting, no injection) and
//          their parsing by the worker. Compiled into both programs (and the
//          tests).
// -----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Text;

namespace LiteDeploy
{
    internal static class UserMessage
    {
        /// <summary>Worker command: "message --text &lt;base64&gt; [--seconds N] [--title &lt;base64&gt;]".</summary>
        public const string WorkerCommand = "message";

        /// <summary>Result line of the worker: type=message, sessions=N, users=..., failed=N, error=...</summary>
        public const string TypeResult = "message";

        /// <summary>Longest text, in characters (the limit of the old "msg" command).</summary>
        public const int MaxLength = 255;

        /// <summary>Longest title of the message box, in characters.</summary>
        public const int MaxTitleLength = 64;

        /// <summary>Display time by default, in seconds; 0: until the user closes it.</summary>
        public const int DefaultSeconds = 300;

        /// <summary>Longest display time, in seconds (one day).</summary>
        public const int MaxSeconds = 86400;

        /// <summary>
        /// The text as it is sent: line breaks as "\n", other control characters removed, spaces trimmed at
        /// both ends. Null: "".
        /// </summary>
        public static string Clean(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            string normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
            StringBuilder sb = new StringBuilder(normalized.Length);
            foreach (char ch in normalized)
            {
                if (ch == '\n' || !char.IsControl(ch)) sb.Append(ch);
                else if (ch == '\t') sb.Append(' ');
            }
            return sb.ToString().Trim();
        }

        /// <summary>Why the text cannot be sent (empty, too long); null when it can.</summary>
        public static string Validate(string text)
        {
            string clean = Clean(text);
            if (clean.Length == 0) return "The message is empty";
            if (clean.Length > MaxLength) return "The message is longer than " + MaxLength + " characters";
            return null;
        }

        /// <summary>Base64 of the UTF-8 bytes of the text.</summary>
        public static string Encode(string text)
        {
            return Convert.ToBase64String(new UTF8Encoding(false).GetBytes(text ?? ""));
        }

        /// <summary>The text of a base64 UTF-8 value; null when it is not valid base64 or not valid UTF-8.</summary>
        public static string Decode(string base64)
        {
            if (base64 == null) return null;
            try
            {
                return new UTF8Encoding(false, true).GetString(Convert.FromBase64String(base64.Trim()));
            }
            catch (FormatException) { return null; }
            catch (ArgumentException) { return null; }   // invalid UTF-8 (DecoderFallbackException)
        }

        /// <summary>
        /// Worker arguments: "message --text &lt;base64&gt; --seconds N [--title &lt;base64&gt;]". An empty or too long
        /// text, a time out of 0..MaxSeconds: ArgumentException.
        /// </summary>
        public static string WorkerArguments(string text, int seconds, string title)
        {
            string error = Validate(text);
            if (error != null) throw new ArgumentException(error, "text");
            if (seconds < 0 || seconds > MaxSeconds) throw new ArgumentOutOfRangeException("seconds");
            string args = WorkerCommand + " --text " + Encode(Clean(text)) + " --seconds " + seconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
            string cleanTitle = CleanTitle(title);
            if (cleanTitle.Length > 0) args += " --title " + Encode(cleanTitle);
            return args;
        }

        /// <summary>A title on one line, at most MaxTitleLength characters.</summary>
        public static string CleanTitle(string title)
        {
            string clean = Clean(title).Replace('\n', ' ');
            return clean.Length > MaxTitleLength ? clean.Substring(0, MaxTitleLength) : clean;
        }

        /// <summary>
        /// The worker's reading of "message ..." (args[0] is "message"): the text, the display time (DefaultSeconds
        /// when absent) and the title (null when absent). Returns why the arguments are invalid, or null.
        /// </summary>
        public static string ParseArguments(IList<string> args, out string text, out int seconds, out string title)
        {
            text = null;
            title = null;
            seconds = DefaultSeconds;
            if (args == null || args.Count == 0 || !string.Equals(args[0], WorkerCommand, StringComparison.OrdinalIgnoreCase))
                return "Not a message command";
            for (int i = 1; i < args.Count; i++)
            {
                string option = args[i];
                if (i + 1 >= args.Count) return option + " needs a value";
                string value = args[++i];
                switch (option.ToLowerInvariant())
                {
                    case "--text":
                        text = Decode(value);
                        if (text == null) return "--text is not base64 UTF-8";
                        break;
                    case "--title":
                        title = Decode(value);
                        if (title == null) return "--title is not base64 UTF-8";
                        title = CleanTitle(title);
                        break;
                    case "--seconds":
                        int s;
                        if (!int.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out s) || s > MaxSeconds)
                            return "Invalid --seconds: " + value;
                        seconds = s;
                        break;
                    default:
                        return "Unknown option: " + option;
                }
            }
            if (text == null) return "--text is missing";
            string error = Validate(text);
            if (error != null) return error;
            text = Clean(text);
            return null;
        }
    }
}
