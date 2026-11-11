// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    AppSettings.cs
// Author:  LiteDeploy contributors
// Date:    2025-05-30
// Purpose: Application-wide settings (logging folder, update folder, ping
//          threads, Active Directory refresh).
// -----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace LiteDeploy
{
    static public class AppSettings
    {
        static public String LOCAL_UPDATE_DIR = "";
        static public String LOGGING_DIR = "\\\\%computername%\\C$\\LiteDeploy";

        static public int MAX_NUMBER_PING_THREADS = 256;
        static public bool AUTO_UPDATE_AD_ON_STARTUP = true;
    }
}
