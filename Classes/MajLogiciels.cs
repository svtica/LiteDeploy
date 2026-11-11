using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;

namespace LiteDeploy
{
    /// <summary>
    /// Entry point to MAJ-Logiciels (third-party software updates through winget, by Patrick Bedard),
    /// shipped in the MAJ-Logiciels folder next to LiteDeploy.exe. The panel runs from its installed copy
    /// (Installer.ps1 puts it in &lt;MajBase&gt;\Outils with the site settings, default C:\MAJ-Logiciels).
    /// </summary>
    static public class MajLogiciels
    {
        public const String DefaultMajBase = @"C:\MAJ-Logiciels";

        static public String BundledFolder
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "MAJ-Logiciels"); }
        }

        static public String InstalledPanel
        {
            get { return Path.Combine(DefaultMajBase, @"Outils\outils-maj.ps1"); }
        }

        static public void OpenPanel(IWin32Window owner)
        {
            if (File.Exists(InstalledPanel))
            {
                ProcessStartInfo info = new ProcessStartInfo("powershell.exe",
                    "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"" + InstalledPanel + "\"");
                info.UseShellExecute = false;
                info.CreateNoWindow = true;
                info.WorkingDirectory = Path.GetDirectoryName(InstalledPanel);

                using (Process.Start(info)) { }
                return;
            }

            String message = "MAJ-Logiciels is not installed on this computer (" + InstalledPanel + " not found).\n\n" +
                             "Install it once with Installer.ps1 and a site profile (see README.md in the MAJ-Logiciels folder).";

            if (Directory.Exists(BundledFolder))
            {
                DialogResult result = MessageBox.Show(owner, message + "\n\nOpen the MAJ-Logiciels folder shipped with LiteDeploy?",
                                                      "Software updates", MessageBoxButtons.YesNo, MessageBoxIcon.Information);

                if (result == DialogResult.Yes)
                    using (Process.Start("explorer.exe", "\"" + BundledFolder + "\"")) { }
            }

            else
            {
                MessageBox.Show(owner, message, "Software updates", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
    }
}
