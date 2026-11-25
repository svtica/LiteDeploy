// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    CloseWarning.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-25
// Purpose: Warning of the users before an update closes their applications:
//          the worker options (--warn-close <seconds>, --allow-postpone), the
//          readable names of common processes and the text of the message
//          (French on a French Windows, English otherwise). The worker shows it
//          in the sessions that run one of the processes, waits for OK, Cancel
//          (postpone, when allowed) or the end of the delay, then closes them.
//          Compiled into both programs (and the tests).
// -----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Linq;

namespace LiteDeploy
{
    internal static class CloseWarning
    {
        /// <summary>Worker option: seconds of warning before the processes of a package are closed (0 or absent: none).</summary>
        public const string WarnOption = "--warn-close";
        /// <summary>Worker option: the users may postpone the update (Cancel).</summary>
        public const string PostponeOption = "--allow-postpone";

        public const int DefaultSeconds = 120;
        public const int MaxSeconds = 900;

        /// <summary>Result message of a package postponed by a user.</summary>
        public const string PostponedMessage = "Postponed by the user";

        /// <summary>A delay within 0..MaxSeconds (0: no warning).</summary>
        public static int ClampSeconds(int seconds)
        {
            return Math.Max(0, Math.Min(MaxSeconds, seconds));
        }

        static readonly Dictionary<string, string> Names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "WINWORD", "Word" }, { "EXCEL", "Excel" }, { "POWERPNT", "PowerPoint" }, { "OUTLOOK", "Outlook" },
            { "MSACCESS", "Access" }, { "ONENOTE", "OneNote" }, { "MSPUB", "Publisher" }, { "VISIO", "Visio" },
            { "WINPROJ", "Project" }, { "Acrobat", "Adobe Acrobat" }, { "AcroRd32", "Adobe Acrobat Reader" },
            { "AcroCEF", "Adobe Acrobat" }, { "RdrCEF", "Adobe Acrobat Reader" },
            { "1Password", "1Password" }, { "1Password-BrowserSupport", "1Password" },
            { "TeamViewer", "TeamViewer" }, { "TeamViewer_Desktop", "TeamViewer" },
            { "firefox", "Firefox" }, { "chrome", "Google Chrome" }, { "msedge", "Microsoft Edge" }, { "ms-teams", "Microsoft Teams" }
        };

        /// <summary>The readable names of processes, without duplicates, in their order ("WINWORD" -> "Word"; unknown: the process name).</summary>
        public static List<string> ReadableNames(IEnumerable<string> processes)
        {
            List<string> list = new List<string>();
            foreach (string p in processes ?? Enumerable.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(p)) continue;
                string name;
                if (!Names.TryGetValue(p.Trim(), out name)) name = p.Trim();
                if (!list.Contains(name, StringComparer.OrdinalIgnoreCase)) list.Add(name);
            }
            return list;
        }

        /// <summary>Title of the message.</summary>
        public static string Title(bool french)
        {
            return french ? "Mise à jour de logiciel" : "Software update";
        }

        /// <summary>Text of the message: what is updated, the applications closed, what OK and Cancel do, the delay.</summary>
        public static string Text(string product, IEnumerable<string> applications, int seconds, bool allowPostpone, bool french)
        {
            string apps = string.Join(", ", applications ?? Enumerable.Empty<string>());
            string delay = seconds >= 120 && seconds % 60 == 0
                ? (seconds / 60) + (french ? " minutes" : " minutes")
                : seconds + (french ? " secondes" : " seconds");
            if (french)
                return product + " va être mis à jour.\n\n"
                       + "Ces applications seront fermées : " + apps + ".\n"
                       + "Enregistrez votre travail.\n\n"
                       + (allowPostpone ? "OK : fermer maintenant et mettre à jour.\nAnnuler : reporter la mise à jour.\n\n" : "OK : fermer maintenant.\n\n")
                       + "Sans réponse, elles seront fermées dans " + delay + ".";
            return product + " is about to be updated.\n\n"
                   + "These applications will be closed: " + apps + ".\n"
                   + "Please save your work.\n\n"
                   + (allowPostpone ? "OK: close them now and update.\nCancel: postpone the update.\n\n" : "OK: close them now.\n\n")
                   + "Without an answer, they will be closed in " + delay + ".";
        }
    }
}
