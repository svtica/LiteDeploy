// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    LogEntry.cs
// Author:  LiteDeploy contributors
// Date:    2025-05-30
// Purpose: One line of the process report (time, computer, raw output,
//          message).
// -----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace LiteDeploy
{
    public class LogEntry
    {
        public DateTime LoggedTime;
        public String LogLineMessage;
        public String HostIP;
        public String LogLineOriginalMesasge;

        // Icons of the process report (state image list of ProcessReportsForm.log)
        public const int IconStarted = 0;
        public const int IconUnreachable = 1;
        public const int IconOther = 2;
        public const int IconDeployment = 3;
        public const int IconAuthentication = 4;

        /// <summary>Icon of the line, chosen when the line is created (never from its translated text).</summary>
        public int Icon = IconOther;

        public LogEntry(DateTime t, String hI, String originatingMessage, String message, int icon)
            : this(t, hI, originatingMessage, message)
        {
            Icon = icon;
        }

        public LogEntry(DateTime t, String hI, String originatingMessage, String message)
        {
            LoggedTime = t;
            HostIP = hI;
            LogLineMessage = message;
            LogLineOriginalMesasge = originatingMessage;
        }
    }
}
