// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    PsExecDownloader.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-12
// Purpose: Downloads PsExec64.exe from live.sysinternals.com (Microsoft's
//          official Sysinternals site) when it is missing, at the user's
//          request. The file is kept only if its Authenticode signature is
//          valid, chains to a trusted root and was made by Microsoft
//          Corporation. Saved next to LiteDeploy.exe, or in
//          %LOCALAPPDATA%\LiteDeploy\tools when that folder is read-only, without
//          Mark of the Web.
// -----------------------------------------------------------------------------
using System;
using System.IO;
using System.Net;
using LiteSuite;

namespace LiteDeploy
{
    static public class PsExecDownloader
    {
        public const String Url = "https://live.sysinternals.com/PsExec64.exe";
        public const String FileName = "PsExec64.exe";

        /// <summary>Folder used when the LiteDeploy folder cannot be written to.</summary>
        static public String UserToolsDir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LiteDeploy", "tools"); }
        }

        /// <summary>
        /// Only Microsoft's own signature is accepted: verified against the file content, chained to
        /// a root trusted by Windows, signer organisation "Microsoft Corporation".
        /// </summary>
        static public bool IsTrustedPublisher(AuthenticodeResult result)
        {
            if (result == null || result.Status != AuthenticodeStatus.Valid || String.IsNullOrEmpty(result.SignerSubject))
                return false;
            foreach (String part in result.SignerSubject.Split(','))
            {
                String p = part.Trim();
                if (p.Equals("O=Microsoft Corporation", StringComparison.Ordinal)) return true;
            }
            return false;
        }

        /// <summary>
        /// Downloads PsExec64.exe and returns its path. Throws with a readable message when the
        /// download fails or the signature is not Microsoft's (nothing is left on disk then).
        /// </summary>
        static public String Download(String appDir)
        {
            String dir = IsWritable(appDir) ? appDir : UserToolsDir;
            Directory.CreateDirectory(dir);
            String target = Path.Combine(dir, FileName);
            String temp = target + ".download";

            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                using (WebClient client = new WebClient())
                {
                    // Corporate proxy: the Windows settings, with the user's credentials
                    client.Proxy = WebRequest.GetSystemWebProxy();
                    client.Proxy.Credentials = CredentialCache.DefaultCredentials;
                    client.DownloadFile(Url, temp);
                }

                AuthenticodeResult signature = AuthenticodeVerifier.Verify(temp);
                if (!IsTrustedPublisher(signature))
                    throw new InvalidDataException(Localization.F("PsExecDownloader.BadSignature",
                        "The downloaded file is not signed by Microsoft ({0}). It was deleted.", signature.ToString()));

                if (File.Exists(target)) File.Delete(target);
                File.Move(temp, target);
                // Normally absent (WebClient does not add it), removed in case a security product did
                MarkOfTheWeb.RemoveFromFile(target);
                return target;
            }
            finally
            {
                try { if (File.Exists(temp)) File.Delete(temp); } catch (IOException) { }
            }
        }

        static bool IsWritable(String dir)
        {
            try
            {
                String probe = Path.Combine(dir, ".write-test-" + Guid.NewGuid().ToString("N"));
                File.WriteAllText(probe, "");
                File.Delete(probe);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
