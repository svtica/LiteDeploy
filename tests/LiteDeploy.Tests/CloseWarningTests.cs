// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    CloseWarningTests.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-25
// Purpose: Warning of the users before an update closes their applications:
//          worker options, readable names, text of the message.
// -----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LiteDeploy.Tests
{
    [TestClass]
    public class CloseWarningTests
    {
        static readonly List<UpdatePackage> Packages = new List<UpdatePackage> { UpdatePackage.Parse("module:AdobeReader|Acrobat,WINWORD|Adobe Acrobat Reader") };

        [TestMethod]
        public void WorkerArguments_UpgradeOnly()
        {
            String upgrade = UpdateRunner.BuildWorkerArguments("upgrade", Packages, false, null, false, false, 120, true);
            StringAssert.Contains(upgrade, " --warn-close 120 --allow-postpone");
            StringAssert.EndsWith(UpdateRunner.BuildWorkerArguments("upgrade", Packages, false, null, false, false, 60, false), " --warn-close 60");
            Assert.IsFalse(UpdateRunner.BuildWorkerArguments("upgrade", Packages, false, null, false, false, 0, true).Contains("--warn-close"), "0: no warning");
            Assert.IsFalse(UpdateRunner.BuildWorkerArguments("scan", Packages, false, null, false, false, 120, true).Contains("--warn-close"), "a check closes nothing");
            Assert.IsFalse(UpdateRunner.BuildWorkerArguments("install", Packages, false, null, false, false, 120, true).Contains("--warn-close"), "an install closes nothing");
            StringAssert.Contains(UpdateRunner.BuildWorkerArguments("upgrade", Packages, false, null, false, false, 5000, false), " --warn-close 900", "at most 900 seconds");
        }

        [TestMethod]
        public void Worker_ParseArguments_WarningOptions()
        {
            string command, cache;
            List<UpdatePackage> packages;
            int timeout;
            bool unsigned, servers, dcs;
            Assert.IsNull(Worker.Program.ParseArguments(new[] { "upgrade", "--package", "Example.Product|example|", "--warn-close", "120", "--allow-postpone" },
                out command, out packages, out timeout, out unsigned, out cache, out servers, out dcs));
            Assert.AreEqual(120, Worker.Program.CloseWarningSeconds);
            Assert.IsTrue(Worker.Program.AllowPostpone);

            Assert.IsNull(Worker.Program.ParseArguments(new[] { "upgrade", "--package", "Example.Product|example|" },
                out command, out packages, out timeout, out unsigned, out cache, out servers, out dcs));
            Assert.AreEqual(0, Worker.Program.CloseWarningSeconds, "reset at each parse: no warning by default");
            Assert.IsFalse(Worker.Program.AllowPostpone);

            Assert.IsNotNull(Worker.Program.ParseArguments(new[] { "upgrade", "--package", "Example.Product||", "--warn-close", "901" },
                out command, out packages, out timeout, out unsigned, out cache, out servers, out dcs));
            Assert.IsNotNull(Worker.Program.ParseArguments(new[] { "upgrade", "--package", "Example.Product||", "--warn-close", "abc" },
                out command, out packages, out timeout, out unsigned, out cache, out servers, out dcs));
        }

        [TestMethod]
        public void ReadableNames_AndText()
        {
            CollectionAssert.AreEqual(new[] { "Word", "Outlook", "Adobe Acrobat", "MyApp" },
                CloseWarning.ReadableNames(new[] { "WINWORD", "outlook", "Acrobat", "AcroCEF", "MyApp", "" }).ToArray());

            String fr = CloseWarning.Text("Adobe Acrobat Reader", new[] { "Word", "Adobe Acrobat" }, 120, true, true);
            StringAssert.StartsWith(fr, "Adobe Acrobat Reader va \u00eatre mis \u00e0 jour.");
            StringAssert.Contains(fr, "Word, Adobe Acrobat");
            StringAssert.Contains(fr, "Annuler : reporter");
            StringAssert.Contains(fr, "2 minutes");

            String en = CloseWarning.Text("1Password", new[] { "1Password" }, 90, false, false);
            StringAssert.Contains(en, "OK: close them now.");
            Assert.IsFalse(en.Contains("Cancel"), "no postpone: no Cancel");
            StringAssert.Contains(en, "90 seconds");
            Assert.AreEqual(0, CloseWarning.ClampSeconds(-1));
            Assert.AreEqual(120, new UserSettings().CloseWarningSeconds);
            Assert.IsTrue(new UserSettings().CloseWarningPostpone);
        }
    }
}
