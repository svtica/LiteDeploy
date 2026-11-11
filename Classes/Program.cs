using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using System.IO;

namespace LiteDeploy
{
    static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main(string[] args)
        {
            // LiteDeploy reads and writes its files (adcache.xml, RemoteManagement.msc,
            // RDPWInst.exe...) next to LiteDeploy.exe, whatever folder it is started from
            // (LiteTask, shortcut, command line).
            Directory.SetCurrentDirectory(AppDomain.CurrentDomain.BaseDirectory);

            if (PsExecLocator.Find(AppDomain.CurrentDomain.BaseDirectory) == null)
            {
                MessageBox.Show("PsExec is missing. The application cannot continue.\n\n" +
                                "Put PsExec.exe (Sysinternals) next to LiteDeploy.exe, or install LiteDeploy from LiteTask (Tools menu), " +
                                "which provides PsExec64.exe in its tools folder.",
                                "File Missing", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            else
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
#if DEBUG
                Application.Run(new Form1());                
#else
                try
                {
                    Application.Run(new Form1());
                }

                catch (Exception e)
                {
                    MessageBox.Show(e.Message + "\n" + e.StackTrace + "\n" + e.Data, e.Source, MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
#endif
            }
        }
    }
}
