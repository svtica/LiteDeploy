using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace LiteDeploy
{
    static public class MainFormAccess
    {
        static public Form1 mainForm;

        static public void Init(Form1 f)
        {
            mainForm = f;
        }
    }
}
