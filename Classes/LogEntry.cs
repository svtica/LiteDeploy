
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

        public LogEntry(DateTime t, String hI, String originatingMessage, String message)
        {
            LoggedTime = t;
            HostIP = hI;
            LogLineMessage = message;
            LogLineOriginalMesasge = originatingMessage;
        }
    }
}
