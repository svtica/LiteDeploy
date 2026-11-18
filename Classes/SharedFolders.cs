// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    SharedFolders.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-18
// Purpose: Folders that the administrators of a team share, usually network
//          shares typed once on each administrator computer (the settings are
//          per Windows user profile): the check of a folder before the options
//          are saved (exists and readable from this computer, with a time
//          limit for an unreachable server), and the default folder of the
//          catalogs (a "catalogs" folder next to the shared modules folder,
//          so every administrator imports the same catalogs). Unit-tested.
// -----------------------------------------------------------------------------
using System;
using System.IO;
using System.Security;
using System.Threading.Tasks;

namespace LiteDeploy
{
    /// <summary>What a folder check found.</summary>
    public enum FolderState
    {
        /// <summary>No path (the setting is optional).</summary>
        Empty,
        /// <summary>The folder exists and its content can be listed.</summary>
        Ok,
        /// <summary>Not found, or the server or share cannot be reached.</summary>
        NotFound,
        /// <summary>Found, but its content cannot be listed (permissions).</summary>
        NotReadable,
        /// <summary>Not a valid folder path.</summary>
        Invalid,
        /// <summary>No answer within the time limit (server unreachable or slow).</summary>
        TimedOut
    }

    public static class FolderCheck
    {
        /// <summary>Hint shown in an empty shared folder box (fictitious server and share).</summary>
        public static String ShareHint
        {
            get { return new String((char)0x5C, 2) + "server" + (char)0x5C + "share"; }
        }

        /// <summary>Exists and readable (its content can be listed) from this computer, as the current user.</summary>
        public static FolderState Probe(String path)
        {
            if (String.IsNullOrWhiteSpace(path)) return FolderState.Empty;
            String folder = path.Trim();
            try
            {
                if (folder.IndexOfAny(Path.GetInvalidPathChars()) >= 0 || folder.IndexOfAny(new[] { '*', '?' }) >= 0) return FolderState.Invalid;
                if (!Directory.Exists(folder)) return FolderState.NotFound;
                using (var entries = Directory.EnumerateFileSystemEntries(folder).GetEnumerator())
                    entries.MoveNext();
                return FolderState.Ok;
            }
            catch (UnauthorizedAccessException) { return FolderState.NotReadable; }
            catch (SecurityException) { return FolderState.NotReadable; }
            catch (DirectoryNotFoundException) { return FolderState.NotFound; }
            catch (IOException) { return FolderState.NotReadable; }
            catch (ArgumentException) { return FolderState.Invalid; }
            catch (NotSupportedException) { return FolderState.Invalid; }
        }

        /// <summary>Probe with a time limit: an unreachable server can take long to fail.</summary>
        public static FolderState Probe(String path, TimeSpan timeout)
        {
            if (String.IsNullOrWhiteSpace(path)) return FolderState.Empty;
            Task<FolderState> probe = Task.Run(() => Probe(path));
            return probe.Wait(timeout) ? probe.Result : FolderState.TimedOut;
        }

        /// <summary>Why a folder is not usable, for a message; null when it is (Ok or Empty).</summary>
        public static String Describe(FolderState state)
        {
            switch (state)
            {
                case FolderState.NotFound: return Localization.T("FolderCheck.NotFound", "not found, or the server or share cannot be reached from this computer");
                case FolderState.NotReadable: return Localization.T("FolderCheck.NotReadable", "found, but your account cannot read it (permissions)");
                case FolderState.Invalid: return Localization.T("FolderCheck.Invalid", "not a valid folder path");
                case FolderState.TimedOut: return Localization.T("FolderCheck.TimedOut", "no answer within a few seconds (server unreachable or slow)");
                default: return null;
            }
        }

        /// <summary>Where a folder dialog starts: the current value when one is typed, else null (default location).</summary>
        public static String StartFolder(String current)
        {
            return String.IsNullOrWhiteSpace(current) ? null : current.Trim();
        }
    }

    /// <summary>Default location of the catalogs of module instances (Import from catalog...).</summary>
    public static class CatalogLocation
    {
        /// <summary>Name of the shared folder of the catalogs, next to the shared modules folder.</summary>
        public const String FolderName = "catalogs";

        /// <summary>
        /// The shared catalogs folder: "catalogs" in the parent of the shared modules folder (modules in
        /// "...\LiteDeploy\modules": catalogs in "...\LiteDeploy\catalogs"). Null when no shared modules folder
        /// is set or it has no parent (a share root or a drive root).
        /// </summary>
        public static String SharedFolder(String sharedModulesFolder)
        {
            if (String.IsNullOrWhiteSpace(sharedModulesFolder)) return null;
            try
            {
                String modules = sharedModulesFolder.Trim().TrimEnd((char)0x5C, '/');
                if (modules.Length == 0) return null;
                String parent = Path.GetDirectoryName(modules);
                if (String.IsNullOrEmpty(parent)) return null;
                return Path.Combine(parent, FolderName);
            }
            catch (ArgumentException) { return null; }
            catch (PathTooLongException) { return null; }
        }

        /// <summary>
        /// Folder the import dialog opens: the shared catalogs folder when it exists, else the folder of the
        /// local catalog (<paramref name="localCatalog"/>, %LOCALAPPDATA%\LiteDeploy\catalog.local.json).
        /// </summary>
        public static String InitialFolder(String sharedModulesFolder, Func<String, bool> folderExists, String localCatalog)
        {
            String shared = SharedFolder(sharedModulesFolder);
            if (shared != null && folderExists != null && folderExists(shared)) return shared;
            return Path.GetDirectoryName(localCatalog);
        }
    }
}
