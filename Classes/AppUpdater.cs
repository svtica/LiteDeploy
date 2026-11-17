// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    AppUpdater.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-17
// Purpose: Update of LiteDeploy itself from its GitHub releases (Help > Check
//          for updates, and at start-up when the option is on), through the
//          SelfUpdater of LiteSuite.Common: check, offer, download and verify
//          (nothing replaced yet), then the replacement script runs once
//          LiteDeploy has closed and restarts it. The files of the user next
//          to LiteDeploy.exe are kept.
// -----------------------------------------------------------------------------
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows.Forms;
using LiteSuite;

namespace LiteDeploy
{
    static internal class AppUpdater
    {
        public const String ApplicationName = "LiteDeploy";
        public const String Repository = "LiteDeploy";
        public const String PackagePattern = @"^LiteDeploy.*\.zip$";
        public const String PackageName = "LiteDeploy.zip";
        public const String ExecutableName = "LiteDeploy.exe";

        /// <summary>Longest release notes shown in the offer (characters, then lines).</summary>
        public const int MaxNotesLength = 1500;
        public const int MaxNotesLines = 25;

        /// <summary>
        /// Files of the user next to LiteDeploy.exe, never overwritten by an update (the script deletes
        /// nothing, so the user's own files, such as the modules added to modules\, stay too).
        /// </summary>
        static internal readonly String[] PreservedFiles =
        {
            "adcache.xml",              // Active Directory computer cache (ActiveDirectoryCache)
            "selectedComputers.txt",
            "subnets.txt",              // subnet list of the team (SubnetList.SharedPath)
            "settings.xml",
            "config.json",
            "*.local.*",
            "RemoteManagement.msc"      // management console, may be customized by the team
        };

        // One check at a time (menu and start-up)
        static bool running;

        /// <summary>Options of the running LiteDeploy.</summary>
        static public SelfUpdaterOptions CreateOptions()
        {
            return CreateOptions(Application.ProductVersion, Application.ExecutablePath);
        }

        static public SelfUpdaterOptions CreateOptions(String currentVersion, String executablePath)
        {
            SelfUpdaterOptions options = new SelfUpdaterOptions
            {
                ApplicationName = ApplicationName,
                Repository = Repository,
                PackagePattern = PackagePattern,
                PackageName = PackageName,
                ExecutableName = ExecutableName,
                CurrentVersion = currentVersion,
                CurrentExecutablePath = executablePath
            };
            options.PreservePatterns.AddRange(PreservedFiles);
            return options;
        }

        /// <summary>Release notes for the offer: line ends normalized, shortened when too long.</summary>
        static public String ReleaseNotes(String body)
        {
            if (String.IsNullOrWhiteSpace(body)) return Localization.T("AppUpdater.NoNotes", "(no release notes)");
            String[] lines = body.Replace("\r\n", "\n").Replace('\r', '\n').Trim().Split('\n');
            bool shortened = lines.Length > MaxNotesLines;
            String notes = String.Join(Environment.NewLine, lines, 0, Math.Min(lines.Length, MaxNotesLines)).TrimEnd();
            if (notes.Length > MaxNotesLength)
            {
                notes = notes.Substring(0, MaxNotesLength).TrimEnd();
                shortened = true;
            }
            return shortened ? notes + Environment.NewLine + "..." : notes;
        }

        /// <summary>
        /// Checks for a new version and offers it. <paramref name="interactive"/> (Help menu): every
        /// result is shown. At start-up: nothing is shown when there is no new version or the check
        /// fails (a line in the console only). Never throws.
        /// </summary>
        static public async Task RunAsync(MainForm owner, bool interactive)
        {
            if (running) return;
            running = true;
            bool engaged = interactive;     // the user asked for it, or accepted the offer: failures are shown
            try
            {
                using (SelfUpdater updater = new SelfUpdater(CreateOptions()))
                {
                    if (interactive)
                    {
                        owner.UseWaitCursor = true;
                        AppConsole.WriteLine(StageText(UpdateStage.Checking, null));
                    }

                    UpdateCheckResult check;
                    try
                    {
                        // Task.Run: the first request of .NET Framework may resolve the proxy on the calling thread
                        check = await Task.Run(() => updater.CheckAsync());
                    }
                    finally
                    {
                        SetWaitCursor(owner, false);
                    }
                    if (owner.IsDisposed) return;

                    if (!check.IsUpdateAvailable)
                    {
                        if (interactive) ShowNoUpdate(owner, check);
                        return;
                    }

                    // At start-up, a window of LiteDeploy may be open (modal): no offer over it
                    if (!interactive && !owner.CanFocus)
                    {
                        AppConsole.WriteLine(Localization.F("AppUpdater.AvailableLater", "LiteDeploy {0} is available: Help > Check for updates.", check.LatestVersion));
                        return;
                    }

                    if (!AskInstall(owner, check)) return;
                    engaged = true;

                    PreparedUpdate update;
                    owner.UseWaitCursor = true;
                    try
                    {
                        String version = check.LatestVersion;
                        IProgress<UpdateStage> progress = new Progress<UpdateStage>(stage => AppConsole.WriteLine(StageText(stage, version)));
                        update = await Task.Run(() => updater.PrepareAsync(check, progress));
                    }
                    finally
                    {
                        SetWaitCursor(owner, false);
                    }
                    if (owner.IsDisposed)
                    {
                        update.Discard();
                        return;
                    }

                    if (update.IsUnsigned && !ConfirmUnsigned(owner))
                    {
                        update.Discard();
                        AppConsole.WriteLine(Localization.T("AppUpdater.Cancelled", "Update of LiteDeploy cancelled."));
                        return;
                    }

                    // Deployments or scheduled updates in progress: same question as when closing
                    if (!owner.ConfirmCloseForUpdate())
                    {
                        update.Discard();
                        AppConsole.WriteLine(Localization.T("AppUpdater.Cancelled", "Update of LiteDeploy cancelled."));
                        return;
                    }

                    AppConsole.WriteLine(StageText(UpdateStage.LaunchingScript, update.Version));
                    try
                    {
                        int processId;
                        using (Process current = Process.GetCurrentProcess()) processId = current.Id;
                        updater.Apply(update, processId);
                    }
                    catch
                    {
                        update.Discard();
                        throw;
                    }
                    AppConsole.WriteLine(Localization.F("AppUpdater.LogFile", "Update log: {0}", updater.LogPath));

                    // The script waits for LiteDeploy to exit, replaces the files and restarts it
                    owner.CloseForUpdate();
                }
            }
            catch (UpdateException ex)
            {
                AppConsole.WriteLine(Localization.F("AppUpdater.FailedLog", "Update of LiteDeploy failed: {0}", ex.Message));
                if (engaged && !owner.IsDisposed)
                    MessageBox.Show(owner, FailureText(ex), Title, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (Exception ex)
            {
                AppConsole.WriteLine(Localization.F("AppUpdater.FailedLog", "Update of LiteDeploy failed: {0}", ex.Message));
                if (engaged && !owner.IsDisposed)
                    MessageBox.Show(owner, Localization.F("AppUpdater.Failed", "The update of LiteDeploy failed:\n{0}", ex.Message), Title, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                running = false;
                SetWaitCursor(owner, false);
            }
        }

        static String Title
        {
            get { return Localization.T("AppUpdater.Title", "LiteDeploy Update"); }
        }

        static void SetWaitCursor(Form owner, bool wait)
        {
            if (!owner.IsDisposed) owner.UseWaitCursor = wait;
        }

        static void ShowNoUpdate(IWin32Window owner, UpdateCheckResult check)
        {
            String text;
            MessageBoxIcon icon = MessageBoxIcon.Information;
            if (check.Package == null && ReleaseVersion.IsNewer(check.CurrentVersion, check.LatestVersion))
            {
                // A newer release without a LiteDeploy package (still being published, or another asset name)
                text = Localization.F("AppUpdater.NoPackage", "The release {0} has no LiteDeploy package. Try again later.", check.LatestVersion);
                icon = MessageBoxIcon.Warning;
            }
            else
            {
                text = Localization.F("AppUpdater.UpToDate", "LiteDeploy is up to date (version {0}).", check.CurrentVersion);
            }
            MessageBox.Show(owner, text, Title, MessageBoxButtons.OK, icon);
        }

        static bool AskInstall(IWin32Window owner, UpdateCheckResult check)
        {
            String notes = ReleaseNotes(check.Release == null ? null : check.Release.Body);
            String text = Localization.F("AppUpdater.Available",
                "A new version of LiteDeploy is available.\n\nCurrent version: {0}\nNew version: {1}\n\nRelease notes:\n{2}\n\n" +
                "Install now? LiteDeploy will be closed, then restarted. Your files next to LiteDeploy.exe are kept; " +
                "Windows may ask for administrator rights.",
                check.CurrentVersion, check.LatestVersion, notes);
            return MessageBox.Show(owner, text, Title, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
        }

        static bool ConfirmUnsigned(IWin32Window owner)
        {
            return MessageBox.Show(owner, Localization.T("AppUpdater.Unsigned",
                       "Warning: the update package is not signed. It is accepted only because this LiteDeploy is not " +
                       "signed either (development build).\n\nInstall it anyway?"),
                   Title, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) == DialogResult.Yes;
        }

        /// <summary>Console line of an update step.</summary>
        static internal String StageText(UpdateStage stage, String version)
        {
            switch (stage)
            {
                case UpdateStage.Checking:
                    return Localization.T("AppUpdater.Checking", "Checking for a new version of LiteDeploy...");
                case UpdateStage.Downloading:
                    return Localization.F("AppUpdater.Downloading", "Downloading LiteDeploy {0}...", version);
                case UpdateStage.Extracting:
                    return Localization.T("AppUpdater.Extracting", "Extracting the update...");
                case UpdateStage.VerifyingSignature:
                    return Localization.T("AppUpdater.Verifying", "Checking the signature of the update...");
                case UpdateStage.SignatureAccepted:
                    return Localization.T("AppUpdater.SignatureAccepted", "Signature of the update accepted.");
                case UpdateStage.UnsignedAccepted:
                    return Localization.T("AppUpdater.UnsignedAccepted", "The update is not signed (development build).");
                case UpdateStage.LaunchingScript:
                    return Localization.F("AppUpdater.Launching", "Installing LiteDeploy {0}: LiteDeploy closes, then restarts once its files are replaced.", version);
                default:
                    return stage.ToString();
            }
        }

        /// <summary>Message of a failed update, from its reason.</summary>
        static internal String FailureText(UpdateException ex)
        {
            switch (ex.Failure)
            {
                case UpdateFailure.CheckFailed:
                    return Localization.F("AppUpdater.CheckFailed", "The latest version of LiteDeploy could not be read from GitHub:\n{0}", ex.Message);
                case UpdateFailure.NoPackage:
                    return Localization.T("AppUpdater.NoPackageFailure", "The latest release has no LiteDeploy package.");
                case UpdateFailure.UnsafeUrl:
                    return Localization.T("AppUpdater.UnsafeUrl", "The download link of the update was refused: only a GitHub release of LiteDeploy, over HTTPS, is accepted.");
                case UpdateFailure.DownloadFailed:
                    return Localization.F("AppUpdater.DownloadFailed", "The update could not be downloaded:\n{0}", ex.Message);
                case UpdateFailure.InvalidPackage:
                    return Localization.F("AppUpdater.InvalidPackage", "The update package is invalid:\n{0}", ex.Message);
                case UpdateFailure.ExecutableNotFound:
                    return Localization.T("AppUpdater.ExecutableNotFound", "LiteDeploy.exe is not in the update package.");
                case UpdateFailure.SignatureRejected:
                    return SignatureText(ex.Verdict);
                case UpdateFailure.LaunchFailed:
                    return Localization.F("AppUpdater.LaunchFailed", "The update could not be started:\n{0}", ex.Message);
                default:
                    return Localization.F("AppUpdater.Failed", "The update of LiteDeploy failed:\n{0}", ex.Message);
            }
        }

        static String SignatureText(UpdateSignatureVerdict? verdict)
        {
            switch (verdict.GetValueOrDefault(UpdateSignatureVerdict.RejectedInvalidSignature))
            {
                case UpdateSignatureVerdict.RejectedUnsigned:
                    return Localization.T("AppUpdater.RejectedUnsigned", "The update was refused: it is not signed, while this LiteDeploy is.");
                case UpdateSignatureVerdict.RejectedTampered:
                    return Localization.T("AppUpdater.RejectedTampered", "The update was refused: its signature does not match its content (modified file).");
                case UpdateSignatureVerdict.RejectedPublisherMismatch:
                    return Localization.T("AppUpdater.RejectedPublisher", "The update was refused: it is not signed by the LiteSuite publisher.");
                default:
                    return Localization.T("AppUpdater.RejectedInvalid", "The update was refused: its signature could not be validated.");
            }
        }
    }
}
