// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    MainFormAccess.cs
// Author:  LiteDeploy contributors
// Date:    2025-05-30
// Purpose: Static access to the main window for the worker threads.
// -----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace LiteDeploy
{
    static public class MainFormAccess
    {
        static public MainForm mainForm;

        static public void Init(MainForm f)
        {
            mainForm = f;
        }
    }
}
