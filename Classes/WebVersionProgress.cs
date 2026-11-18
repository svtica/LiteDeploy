// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    WebVersionProgress.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-18
// Purpose: Progress of Tools > Web versions... (Check now, Download to the
//          cache) and of the daily check: one report per step of an item
//          (version of the cache file, winget; download with its bytes, SHA256,
//          signature, copy to the cache, parameters) and when the item is done,
//          with its result. The texts of the status line (item, elapsed time,
//          estimated time left) are pure logic, unit-tested.
// -----------------------------------------------------------------------------
using System;
using System.Globalization;

namespace LiteDeploy
{
    public enum WebVersionOperation
    {
        Check,
        Download
    }

    public enum WebVersionStep
    {
        /// <summary>Check: reading the version of the file of the instance in the cache.</summary>
        CacheFile,
        /// <summary>Check: "winget show".</summary>
        Winget,
        /// <summary>Download: the parameters of the instance and the write access to its cache folder.</summary>
        Preparing,
        /// <summary>Download: bytes received (BytesTotal when the server gives the length).</summary>
        Downloading,
        VerifyingHash,
        /// <summary>Download of a zip installer: extracting the installer it holds.</summary>
        Extracting,
        VerifyingSignature,
        CopyingToCache,
        UpdatingSettings,
        /// <summary>The item is finished: Entry (or Error for a failed download).</summary>
        Done
    }

    /// <summary>One progress report of a web check or of a download to the cache.</summary>
    public sealed class WebVersionProgress
    {
        public WebVersionOperation Operation;
        /// <summary>Position of the item in the run, from 1.</summary>
        public int Index;
        public int Total;
        /// <summary>Items finished so far (this one included when Step is Done).</summary>
        public int Done;
        public String Key;
        public String Product;
        public WebVersionStep Step;
        /// <summary>Step Done: the result of the item (null for a failed download).</summary>
        public WebVersionEntry Entry;
        /// <summary>Step Done of a failed download: the reason.</summary>
        public String Error;
        public long BytesReceived;
        /// <summary>Length of the download; 0 or less: unknown.</summary>
        public long BytesTotal;
    }

    internal static class WebVersionProgressText
    {
        /// <summary>Items finished before a time left is estimated (the first ones are not representative).</summary>
        public const int MinItemsForEstimate = 3;

        /// <summary>
        /// Estimated time left: the average time of the finished items times the items left; null
        /// before <see cref="MinItemsForEstimate"/> items or when nothing is left. Pure logic, unit-tested.
        /// </summary>
        static public TimeSpan? Remaining(int done, int total, TimeSpan elapsed)
        {
            if (done < MinItemsForEstimate || total <= done || elapsed <= TimeSpan.Zero) return null;
            return TimeSpan.FromTicks(elapsed.Ticks / done * (total - done));
        }

        /// <summary>"0:42", "12:05", "1:02:03". Pure logic, unit-tested.</summary>
        static public String Duration(TimeSpan t)
        {
            if (t < TimeSpan.Zero) t = TimeSpan.Zero;
            int hours = (int)t.TotalHours;
            return hours > 0
                ? hours.ToString(CultureInfo.InvariantCulture) + ":" + t.Minutes.ToString("00", CultureInfo.InvariantCulture) + ":" + t.Seconds.ToString("00", CultureInfo.InvariantCulture)
                : t.Minutes.ToString(CultureInfo.InvariantCulture) + ":" + t.Seconds.ToString("00", CultureInfo.InvariantCulture);
        }

        /// <summary>Percentage received, -1 when the length is unknown. Pure logic, unit-tested.</summary>
        static public int Percent(long received, long total)
        {
            if (total <= 0) return -1;
            return (int)Math.Max(0, Math.Min(100, received * 100 / total));
        }

        /// <summary>"512 KB", "12.3 MB", "1.25 GB" (decimal separator of the current culture).</summary>
        static public String Bytes(long bytes)
        {
            if (bytes < 0) bytes = 0;
            if (bytes < 1024L * 1024) return Localization.F("WebVersions.Progress.KB", "{0:0} KB", Math.Ceiling(bytes / 1024.0));
            if (bytes < 1024L * 1024 * 1024) return Localization.F("WebVersions.Progress.MB", "{0:0.0} MB", bytes / (1024.0 * 1024));
            return Localization.F("WebVersions.Progress.GB", "{0:0.00} GB", bytes / (1024.0 * 1024 * 1024));
        }

        /// <summary>What is being done on the item ("winget", "downloading 45% (12.3 MB / 27.1 MB)").</summary>
        static public String StepText(WebVersionProgress p)
        {
            switch (p.Step)
            {
                case WebVersionStep.CacheFile: return Localization.T("WebVersions.Step.CacheFile", "cache file version");
                case WebVersionStep.Winget: return "winget";
                case WebVersionStep.Preparing: return Localization.T("WebVersions.Step.Preparing", "checking the parameters");
                case WebVersionStep.Downloading:
                    int percent = Percent(p.BytesReceived, p.BytesTotal);
                    return percent >= 0
                        ? Localization.F("WebVersions.Step.DownloadingPercent", "downloading {0}% ({1} / {2})", percent, Bytes(p.BytesReceived), Bytes(p.BytesTotal))
                        : Localization.F("WebVersions.Step.DownloadingBytes", "downloading ({0})", Bytes(p.BytesReceived));
                case WebVersionStep.VerifyingHash: return Localization.T("WebVersions.Step.Hash", "verifying the SHA256");
                case WebVersionStep.Extracting: return Localization.T("WebVersions.Step.Extract", "extracting the installer from the zip");
                case WebVersionStep.VerifyingSignature: return Localization.T("WebVersions.Step.Signature", "verifying the signature");
                case WebVersionStep.CopyingToCache: return Localization.T("WebVersions.Step.Copy", "copying to the cache");
                case WebVersionStep.UpdatingSettings: return Localization.T("WebVersions.Step.Settings", "updating the parameters");
                default: return "";
            }
        }

        /// <summary>"Checking 3 / 12: Example App (winget)..." or "File 1 / 2: Example App - verifying the SHA256".</summary>
        static public String ItemText(WebVersionProgress p)
        {
            return p.Operation == WebVersionOperation.Check
                ? Localization.F("WebVersions.Progress.Check", "Checking {0} / {1}: {2} ({3})...", p.Index, p.Total, p.Product, StepText(p))
                : Localization.F("WebVersions.Progress.Download", "File {0} / {1}: {2} - {3}", p.Index, p.Total, p.Product, StepText(p));
        }

        /// <summary>"0:12 elapsed", then "1:05 elapsed, about 2:10 left" once enough items are finished.</summary>
        static public String TimeText(int done, int total, TimeSpan elapsed)
        {
            TimeSpan? left = Remaining(done, total, elapsed);
            return left.HasValue
                ? Localization.F("WebVersions.Progress.ElapsedLeft", "{0} elapsed, about {1} left", Duration(elapsed), Duration(left.Value))
                : Localization.F("WebVersions.Progress.Elapsed", "{0} elapsed", Duration(elapsed));
        }

        /// <summary>The status line: the item and its step, then the times.</summary>
        static public String StatusText(WebVersionProgress p, TimeSpan elapsed)
        {
            return ItemText(p) + " - " + TimeText(p.Done, p.Total, elapsed);
        }

        /// <summary>Status cell of the row being worked on: "Checking (winget)...", "Downloading 45% (...)".</summary>
        static public String RowText(WebVersionProgress p)
        {
            if (p.Operation == WebVersionOperation.Check)
                return Localization.F("WebVersions.Progress.Row", "Checking ({0})...", StepText(p));
            String step = StepText(p);
            if (step.Length == 0) return step;
            return Char.ToUpper(step[0], CultureInfo.CurrentCulture) + step.Substring(1) + (p.Step == WebVersionStep.Downloading ? "" : "...");
        }

        /// <summary>Reports without ever stopping the run (a closed window, a failing handler).</summary>
        static public void Report(IProgress<WebVersionProgress> progress, WebVersionProgress value)
        {
            if (progress == null) return;
            try { progress.Report(value); }
            catch (Exception) { }
        }
    }
}
