// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    WebVersionProgressTests.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-18
// Purpose: Unit tests of the progress of Tools > Web versions...: status line
//          texts (item, step, elapsed time, estimated time left, bytes), the
//          check loop (order, parallel limit, first item alone, cancellation
//          that keeps the partial results) with a fake check, the merge of the
//          partial results with the previous ones, and the download loop
//          (steps, failures, cancellation) with a fake download.
// -----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LiteDeploy.Tests
{
    [TestClass]
    public class WebVersionProgressTests
    {
        // Collects the reports synchronously (Progress<T> would post them to a thread pool)
        sealed class Collector : IProgress<WebVersionProgress>
        {
            public readonly List<WebVersionProgress> Reports = new List<WebVersionProgress>();
            public Action<WebVersionProgress> OnReport;
            public void Report(WebVersionProgress value)
            {
                lock (Reports) Reports.Add(value);
                if (OnReport != null) OnReport(value);
            }
        }

        [TestInitialize]
        public void Init()
        {
            Localization.Initialize(Localization.English);
        }

        static List<WebVersionTarget> Targets(int count)
        {
            return Enumerable.Range(1, count).Select(i => new WebVersionTarget
            {
                Module = "CachedInstaller", Instance = "App" + i, Product = "Example App " + i, WingetId = "Contoso.App" + i, Type = "msi"
            }).ToList();
        }

        static WebVersionEntry Result(WebVersionTarget t, String version)
        {
            return new WebVersionEntry { Key = t.Key, Module = t.Module, Instance = t.Instance, Product = t.Product, Status = WebVersionStatus.UpToDate, WebVersion = version };
        }

        // ---- Texts ------------------------------------------------------------------------------

        [TestMethod]
        public void Remaining_OnlyAfterTheFirstItems()
        {
            Assert.IsNull(WebVersionProgressText.Remaining(0, 12, TimeSpan.FromSeconds(5)));
            Assert.IsNull(WebVersionProgressText.Remaining(WebVersionProgressText.MinItemsForEstimate - 1, 12, TimeSpan.FromSeconds(20)));
            Assert.AreEqual(TimeSpan.FromSeconds(90), WebVersionProgressText.Remaining(3, 12, TimeSpan.FromSeconds(30)));
            Assert.IsNull(WebVersionProgressText.Remaining(12, 12, TimeSpan.FromSeconds(30)), "nothing left");
            Assert.IsNull(WebVersionProgressText.Remaining(5, 12, TimeSpan.Zero));
        }

        [TestMethod]
        public void Duration_MinutesAndHours()
        {
            Assert.AreEqual("0:00", WebVersionProgressText.Duration(TimeSpan.Zero));
            Assert.AreEqual("0:42", WebVersionProgressText.Duration(TimeSpan.FromSeconds(42.7)));
            Assert.AreEqual("12:05", WebVersionProgressText.Duration(new TimeSpan(0, 12, 5)));
            Assert.AreEqual("1:02:03", WebVersionProgressText.Duration(new TimeSpan(1, 2, 3)));
            Assert.AreEqual("0:00", WebVersionProgressText.Duration(TimeSpan.FromSeconds(-3)));
        }

        [TestMethod]
        public void Percent_AndBytes()
        {
            Assert.AreEqual(-1, WebVersionProgressText.Percent(500, 0), "length unknown");
            Assert.AreEqual(-1, WebVersionProgressText.Percent(500, -1));
            Assert.AreEqual(45, WebVersionProgressText.Percent(45, 100));
            Assert.AreEqual(100, WebVersionProgressText.Percent(120, 100));
            Assert.AreEqual("2 KB", WebVersionProgressText.Bytes(2048));
            Assert.AreEqual("1 KB", WebVersionProgressText.Bytes(1));
            Assert.AreEqual(String.Format("{0:0.0} MB", 5.5), WebVersionProgressText.Bytes(5 * 1024 * 1024 + 512 * 1024));
            Assert.AreEqual(String.Format("{0:0.00} GB", 2.0), WebVersionProgressText.Bytes(2L * 1024 * 1024 * 1024));
        }

        [TestMethod]
        public void StatusText_CheckWithStepAndTimes()
        {
            WebVersionProgress p = new WebVersionProgress
            {
                Operation = WebVersionOperation.Check, Index = 3, Total = 12, Done = 2, Product = "Example App", Step = WebVersionStep.Winget
            };
            Assert.AreEqual("Checking 3 / 12: Example App (winget)... - 0:20 elapsed", WebVersionProgressText.StatusText(p, TimeSpan.FromSeconds(20)));
            p.Done = 4;
            Assert.AreEqual("Checking 3 / 12: Example App (winget)... - 0:40 elapsed, about 1:20 left", WebVersionProgressText.StatusText(p, TimeSpan.FromSeconds(40)));
            p.Step = WebVersionStep.CacheFile;
            Assert.AreEqual("Checking (cache file version)...", WebVersionProgressText.RowText(p));
        }

        [TestMethod]
        public void StatusText_DownloadSteps()
        {
            WebVersionProgress p = new WebVersionProgress
            {
                Operation = WebVersionOperation.Download, Index = 1, Total = 2, Product = "Example App",
                Step = WebVersionStep.Downloading, BytesReceived = 1024, BytesTotal = 4096
            };
            Assert.AreEqual("File 1 / 2: Example App - downloading 25% (1 KB / 4 KB)", WebVersionProgressText.ItemText(p));
            Assert.AreEqual("Downloading 25% (1 KB / 4 KB)", WebVersionProgressText.RowText(p));
            p.BytesTotal = -1;
            Assert.AreEqual("File 1 / 2: Example App - downloading (1 KB)", WebVersionProgressText.ItemText(p));
            p.Step = WebVersionStep.VerifyingHash;
            Assert.AreEqual("Verifying the SHA256...", WebVersionProgressText.RowText(p));
            foreach (WebVersionStep s in new[] { WebVersionStep.Preparing, WebVersionStep.VerifyingSignature, WebVersionStep.CopyingToCache, WebVersionStep.UpdatingSettings })
            {
                p.Step = s;
                Assert.IsTrue(WebVersionProgressText.StepText(p).Length > 0, s.ToString());
            }
        }

        [TestMethod]
        public void Report_NeverThrows()
        {
            WebVersionProgressText.Report(null, new WebVersionProgress());
            Collector failing = new Collector { OnReport = v => { throw new ObjectDisposedException("window"); } };
            WebVersionProgressText.Report(failing, new WebVersionProgress());
        }

        // ---- Check loop -------------------------------------------------------------------------

        [TestMethod]
        public void RunChecks_ReportsEachStepAndResult()
        {
            List<WebVersionTarget> targets = Targets(4);
            Collector progress = new Collector();
            bool completed;
            Dictionary<String, WebVersionEntry> results = WebVersionChecker.RunChecks(targets, (t, step) =>
            {
                step(WebVersionStep.CacheFile);
                step(WebVersionStep.Winget);
                return Result(t, "2.0");
            }, 1, progress, CancellationToken.None, out completed);

            Assert.IsTrue(completed);
            Assert.AreEqual(4, results.Count);
            List<WebVersionProgress> done = progress.Reports.Where(r => r.Step == WebVersionStep.Done).ToList();
            CollectionAssert.AreEqual(new[] { 1, 2, 3, 4 }, done.Select(r => r.Done).ToArray());
            Assert.IsTrue(done.All(r => r.Entry != null && r.Total == 4));
            Assert.AreEqual(12, progress.Reports.Count, "2 steps + done per item");
            WebVersionProgress first = progress.Reports[0];
            Assert.AreEqual(1, first.Index);
            Assert.AreEqual(WebVersionStep.CacheFile, first.Step);
            Assert.AreEqual("Example App 1", first.Product);
        }

        [TestMethod]
        public void RunChecks_CancelStopsAfterTheCurrentItem_AndKeepsTheResults()
        {
            List<WebVersionTarget> targets = Targets(6);
            using (CancellationTokenSource cancel = new CancellationTokenSource())
            {
                List<String> started = new List<String>();
                bool completed;
                Dictionary<String, WebVersionEntry> results = WebVersionChecker.RunChecks(targets, (t, step) =>
                {
                    started.Add(t.Key);
                    if (started.Count == 2) cancel.Cancel();   // cancelled while the 2nd item runs
                    return Result(t, "2.0");
                }, 1, null, cancel.Token, out completed);

                Assert.IsFalse(completed);
                Assert.AreEqual(2, started.Count, "nothing started after the cancel");
                Assert.AreEqual(2, results.Count, "the current item finished and is kept");
                Assert.IsTrue(results.ContainsKey(targets[1].Key));
            }
        }

        [TestMethod]
        public void RunChecks_ParallelLimit_FirstItemAlone()
        {
            List<WebVersionTarget> targets = Targets(9);
            int running = 0, maxRunning = 0, firstMax = -1;
            Object gate = new Object();
            bool completed;
            Dictionary<String, WebVersionEntry> results = WebVersionChecker.RunChecks(targets, (t, step) =>
            {
                lock (gate)
                {
                    running++;
                    maxRunning = Math.Max(maxRunning, running);
                }
                Thread.Sleep(40);
                lock (gate)
                {
                    if (t.Instance == "App1") firstMax = running;
                    running--;
                }
                return Result(t, "2.0");
            }, 3, null, CancellationToken.None, out completed);

            Assert.IsTrue(completed);
            Assert.AreEqual(9, results.Count);
            Assert.IsTrue(maxRunning <= 3, "at most 3 at a time: " + maxRunning);
            Assert.AreEqual(1, firstMax, "the first item runs alone");
        }

        [TestMethod]
        public void RunChecks_ExceptionBecomesAnErrorResult()
        {
            List<WebVersionTarget> targets = Targets(2);
            bool completed;
            Dictionary<String, WebVersionEntry> results = WebVersionChecker.RunChecks(targets, (t, step) =>
            {
                if (t.Instance == "App1") throw new InvalidOperationException("boom");
                return Result(t, "2.0");
            }, 1, null, CancellationToken.None, out completed);
            Assert.IsTrue(completed);
            Assert.AreEqual(WebVersionStatus.Error, results[targets[0].Key].Status);
            Assert.AreEqual("boom", results[targets[0].Key].Message);
            Assert.AreEqual(WebVersionStatus.UpToDate, results[targets[1].Key].Status);
        }

        [TestMethod]
        public void Merge_PartialCheck_KeepsThePreviousResultsOfTheOthers()
        {
            List<WebVersionTarget> targets = Targets(3);
            List<WebVersionEntry> previous = new List<WebVersionEntry>
            {
                Result(targets[0], "1.0"), Result(targets[2], "1.0"),
                new WebVersionEntry { Key = "CachedInstaller/Removed", WebVersion = "1.0" }
            };
            Dictionary<String, WebVersionEntry> checkedNow = new Dictionary<String, WebVersionEntry>(StringComparer.OrdinalIgnoreCase)
            {
                { targets[0].Key, Result(targets[0], "2.0") }
            };
            List<WebVersionEntry> merged = WebVersionChecker.Merge(previous, targets, checkedNow);
            CollectionAssert.AreEqual(new[] { targets[0].Key, targets[2].Key }, merged.Select(e => e.Key).ToArray(),
                "order of the targets; never checked: no entry; removed instance: dropped");
            Assert.AreEqual("2.0", merged[0].WebVersion, "new result");
            Assert.AreEqual("1.0", merged[1].WebVersion, "not reached: previous result kept");
            Assert.AreEqual(0, WebVersionChecker.Merge(null, null, null).Count);
        }

        // ---- Download loop ----------------------------------------------------------------------

        [TestMethod]
        public void RunDownloads_StepsFailuresAndCancel()
        {
            List<WebVersionEntry> entries = Targets(3).Select(t => Result(t, "2.0")).ToList();
            Collector progress = new Collector();
            using (CancellationTokenSource cancel = new CancellationTokenSource())
            {
                List<CacheDownloadOutcome> outcomes = CacheDownloader.RunDownloads(entries, (e, step) =>
                {
                    step(WebVersionStep.Downloading, 512, 1024);
                    if (e.Instance == "App2")
                    {
                        cancel.Cancel();   // cancelled during the 2nd file, which then fails
                        throw new InvalidOperationException("refused");
                    }
                    return new CacheDownloadResult { Entry = e, NewFileParameter = e.Instance + ".msi" };
                }, progress, cancel.Token, null);

                Assert.AreEqual(2, outcomes.Count, "the 3rd file is not started");
                Assert.IsNotNull(outcomes[0].Result);
                Assert.IsNull(outcomes[1].Result);
                Assert.AreEqual("refused", outcomes[1].Error);
                WebVersionProgress downloading = progress.Reports.First(r => r.Step == WebVersionStep.Downloading);
                Assert.AreEqual(WebVersionOperation.Download, downloading.Operation);
                Assert.AreEqual(50, WebVersionProgressText.Percent(downloading.BytesReceived, downloading.BytesTotal));
                List<WebVersionProgress> done = progress.Reports.Where(r => r.Step == WebVersionStep.Done).ToList();
                Assert.AreEqual(2, done.Count);
                Assert.IsNotNull(done[0].Entry);
                Assert.AreEqual("refused", done[1].Error);
                Assert.AreEqual(2, done[1].Done);
            }
        }
    }
}
