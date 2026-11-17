// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    AppUpdaterTests.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-17
// Purpose: Unit tests of the update of LiteDeploy itself (AppUpdater): options
//          given to SelfUpdater (repository, package, files of the user kept),
//          release notes shortened, a reason for every failure, and the
//          start-up option saved in the user settings.
// -----------------------------------------------------------------------------
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Serialization;
using LiteSuite;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LiteDeploy.Tests
{
    [TestClass]
    public class AppUpdaterTests
    {
        static SelfUpdaterOptions Options()
        {
            return AppUpdater.CreateOptions("1.0.12.0", @"C:\Apps\LiteDeploy\LiteDeploy.exe");
        }

        [TestMethod]
        public void Options_RepositoryPackageAndExecutable()
        {
            SelfUpdaterOptions o = Options();
            Assert.AreEqual("svtica", o.Owner);
            Assert.AreEqual("LiteDeploy", o.Repository);
            Assert.AreEqual("LiteDeploy.exe", o.ExecutableName);
            Assert.AreEqual("LiteDeploy.zip", o.PackageName);
            Assert.AreEqual("1.0.12.0", o.CurrentVersion);
            Assert.AreEqual(SelfUpdaterOptions.LiteSuiteThumbprint, o.PinnedThumbprint);

            Regex package = new Regex(o.PackagePattern, RegexOptions.IgnoreCase);
            Assert.IsTrue(package.IsMatch("LiteDeploy.zip"));
            Assert.IsTrue(package.IsMatch("LiteDeploy-1.0.13.0.zip"));
            Assert.IsFalse(package.IsMatch("LitePM.zip"));
            Assert.IsFalse(package.IsMatch("LiteDeploy.zip.sha256"));
        }

        [TestMethod]
        public void Options_KeepTheFilesOfTheUser()
        {
            SelfUpdaterOptions o = Options();
            foreach (string file in new[] { "adcache.xml", "selectedComputers.txt", "subnets.txt", "settings.xml", "config.json", "*.local.*", "RemoteManagement.msc" })
                CollectionAssert.Contains(o.PreservePatterns, file);
            Assert.IsFalse(o.PreservePatterns.Any(p => p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)),
                           "the program files must be replaced");
        }

        [TestMethod]
        public void Options_AcceptedBySelfUpdater()
        {
            using (SelfUpdater updater = new SelfUpdater(Options()))
            {
                Assert.AreEqual(@"C:\Apps\LiteDeploy", updater.Options.ApplicationDirectory);
                StringAssert.EndsWith(updater.LogPath, "LiteDeploy-update.log");
            }
        }

        [TestMethod]
        public void ReleaseNotes_ShortenedWhenTooLong()
        {
            Assert.AreEqual("Fixed: one\r\nAdded: two".Replace("\r\n", Environment.NewLine), AppUpdater.ReleaseNotes("Fixed: one\nAdded: two\n"));
            Assert.IsFalse(String.IsNullOrWhiteSpace(AppUpdater.ReleaseNotes(null)));

            string manyLines = String.Join("\n", Enumerable.Range(1, 100).Select(i => "line " + i));
            string notes = AppUpdater.ReleaseNotes(manyLines);
            Assert.IsTrue(notes.Split(new[] { Environment.NewLine }, StringSplitOptions.None).Length <= AppUpdater.MaxNotesLines + 1);
            StringAssert.EndsWith(notes, "...");

            string longText = new string('x', AppUpdater.MaxNotesLength * 2);
            Assert.IsTrue(AppUpdater.ReleaseNotes(longText).Length <= AppUpdater.MaxNotesLength + Environment.NewLine.Length + 3);
        }

        [TestMethod]
        public void EveryFailure_HasItsOwnMessage()
        {
            foreach (UpdateFailure failure in Enum.GetValues(typeof(UpdateFailure)).Cast<UpdateFailure>().Where(f => f != UpdateFailure.SignatureRejected))
                Assert.IsFalse(String.IsNullOrWhiteSpace(AppUpdater.FailureText(new UpdateException(failure, "detail"))), failure.ToString());

            string unsigned = AppUpdater.FailureText(new UpdateException(UpdateSignatureVerdict.RejectedUnsigned, new AuthenticodeResult(), "refused"));
            string tampered = AppUpdater.FailureText(new UpdateException(UpdateSignatureVerdict.RejectedTampered, new AuthenticodeResult(), "refused"));
            Assert.IsFalse(String.IsNullOrWhiteSpace(unsigned));
            Assert.AreNotEqual(unsigned, tampered, "the message follows the verdict");
        }

        [TestMethod]
        public void EveryStage_HasAText()
        {
            foreach (UpdateStage stage in Enum.GetValues(typeof(UpdateStage)))
                Assert.AreNotEqual(stage.ToString(), AppUpdater.StageText(stage, "1.0.13.0"), stage.ToString());
        }

        [TestMethod]
        public void Settings_CheckUpdatesOnStartup_SavedAndOnByDefault()
        {
            Assert.IsTrue(new UserSettings().CheckUpdatesOnStartup);

            XmlSerializer serializer = new XmlSerializer(typeof(UserSettings));
            StringWriter writer = new StringWriter();
            serializer.Serialize(writer, new UserSettings { CheckUpdatesOnStartup = false });
            StringAssert.Contains(writer.ToString(), "<CheckUpdatesOnStartup>false</CheckUpdatesOnStartup>");
            UserSettings read = (UserSettings)serializer.Deserialize(new StringReader(writer.ToString()));
            Assert.IsFalse(read.CheckUpdatesOnStartup);

            // A settings file of an older LiteDeploy: the check is on
            UserSettings old = (UserSettings)serializer.Deserialize(new StringReader("<LiteDeploySettings><Language>en</Language></LiteDeploySettings>"));
            Assert.IsTrue(old.CheckUpdatesOnStartup);

            Assert.IsFalse(new UserSettings { CheckUpdatesOnStartup = false }.Clone().CheckUpdatesOnStartup);
        }
    }
}
