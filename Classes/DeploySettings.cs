// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    DeploySettings.cs
// Author:  LiteDeploy contributors
// Date:    2025-05-30
// Purpose: Saved deployment preset (former "Easy" menu, not loaded at the
//          moment).
// -----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace LiteDeploy
{
    public class DeploySettings
    {
        public bool UseSystem = false;
        public bool UseCredentials = false;

        public int ConnectionTimeout = 0;
        public bool CopyExecutable = false;
        public bool OverwriteExisting = false;
        public bool DontWait = false;

        public int RunPriorityIndex = 0;
        public string Executable;
        public string CommandArgs;

        public int Type = 0;
        public string Name;

        public DeploySettings( String n, bool uS, bool uC, int cT, bool cE, bool oE, bool dW, int rpI, String E, String cA, int t)
        {
            Name = n;
            UseSystem = uS;
            UseCredentials = uC;
            ConnectionTimeout = cT;
            CopyExecutable = cE;
            OverwriteExisting = oE;
            DontWait = dW;
            RunPriorityIndex = rpI;
            Executable = E;
            CommandArgs = cA;
            Type = t;
        }

        public DeploySettings()
        {
        }
    }
}
