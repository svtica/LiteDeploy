// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    Program.cs
// Author:  LiteDeploy contributors
// Date:    2025-05-30
// Purpose: Entry point: working folder, PsExec lookup (download offered when
//          missing), main window; "/schedule <name>" runs a scheduled update
//          without a window (Windows task of the scheduler).
// -----------------------------------------------------------------------------
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
            // logs...) next to LiteDeploy.exe, whatever folder it is started from
            // (LiteTask, shortcut, command line).
            Directory.SetCurrentDirectory(AppDomain.CurrentDomain.BaseDirectory);

            // Settings of the user, then the interface language (English, or lang\fr.xml)
            UserSettings.Load();
            Localization.Initialize(UserSettings.Current.Language);

            // "LiteDeploy.exe /schedule <name>": scheduled run started by the Windows task, no window
            if (args.Length == 2 && args[0].Equals("/schedule", StringComparison.OrdinalIgnoreCase))
            {
                PsExecLocator.Find(AppDomain.CurrentDomain.BaseDirectory);
                String missing = RemoteRunner.Prerequisite();
                if (missing != null)
                {
                    ScheduleRunner.Log(missing + ": schedule \"" + args[1] + "\" not run");
                    Environment.Exit(2);
                }
                int failed = ScheduleRunner.RunByName(args[1]);
                Environment.Exit(failed < 0 ? 1 : Math.Min(failed, 99));
            }

            // PsExec is optional: the software updates use WinRM by default. It is offered at start
            // only when something shown needs it (standalone deployments, or the PsExec method).
            if (PsExecLocator.Find(AppDomain.CurrentDomain.BaseDirectory) == null
                && (UserSettings.Current.ShowStandaloneDeployments || RemoteRunner.Method == RemoteRunner.PsExec))
            {
                OfferPsExecDownload();
            }

            {
                // A PsExec or worker copied from a browser download carries the Mark of the Web: the
                // Windows security prompt would block the deployments, so it is removed (like Unblock-File)
                if (PsExecLocator.Path != null) LiteSuite.MarkOfTheWeb.RemoveFromFile(PsExecLocator.Path);
                LiteSuite.MarkOfTheWeb.RemoveFromFile(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, UpdateRunner.WorkerFileName));

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
#if DEBUG
                Application.Run(new MainForm());                
#else
                try
                {
                    Application.Run(new MainForm());
                }

                catch (Exception e)
                {
                    MessageBox.Show(e.Message + "\n" + e.StackTrace + "\n" + e.Data, e.Source, MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
#endif
            }
        }

        // PsExec missing: offer to download it from live.sysinternals.com. True when it is now available.
        internal static bool OfferPsExecDownload()
        {
            if (MessageBox.Show(Localization.T("Program.PsExecDownload",
                                    "PsExec (Microsoft Sysinternals) is missing.\n\n" +
                                    "Download PsExec64.exe from live.sysinternals.com now? Its signature is checked " +
                                    "(Microsoft Corporation) before it is used. PsExec is subject to the Sysinternals license terms."),
                                Localization.T("Program.PsExecMissing.Title", "File Missing"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return false;

            try
            {
                Cursor.Current = Cursors.WaitCursor;
                PsExecDownloader.Download(AppDomain.CurrentDomain.BaseDirectory);
            }
            catch (Exception e)
            {
                MessageBox.Show(Localization.F("Program.PsExecDownloadFailed", "PsExec could not be downloaded: {0}", e.Message),
                                Localization.T("Program.PsExecMissing.Title", "File Missing"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
            finally
            {
                Cursor.Current = Cursors.Default;
            }
            return PsExecLocator.Find(AppDomain.CurrentDomain.BaseDirectory) != null;
        }
    }
}
