// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    SettingsFileTests.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-18
// Purpose: Unit tests of settings.xml as written by UserSettings: the comment
//          before each setting that App > Options does not show, a valid XML
//          comment, and a file that still loads with every value.
// -----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml;
using System.Xml.Serialization;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LiteDeploy.Tests
{
    [TestClass]
    public class SettingsFileTests
    {
        [TestMethod]
        public void FileComments_NameExistingSettings_NoDoubleDash()
        {
            HashSet<String> fields = new HashSet<String>(typeof(UserSettings).GetFields(BindingFlags.Instance | BindingFlags.Public).Select(f => f.Name));
            foreach (KeyValuePair<String, String> c in UserSettings.FileComments)
            {
                Assert.IsTrue(fields.Contains(c.Key), c.Key);
                Assert.IsFalse(c.Value.Contains("--"), c.Key);
                Assert.IsFalse(String.IsNullOrWhiteSpace(c.Value), c.Key);
            }
            CollectionAssert.AllItemsAreUnique(UserSettings.FileComments.Select(c => c.Key).ToList());
        }

        [TestMethod]
        public void ToXml_CommentBeforeEachHiddenSetting()
        {
            XmlDocument doc = UserSettings.ToXml(new UserSettings());
            Assert.IsInstanceOfType(doc.FirstChild, typeof(XmlComment), "header comment first, no utf-16 declaration");
            foreach (KeyValuePair<String, String> c in UserSettings.FileComments)
            {
                XmlNode element = doc.DocumentElement[c.Key];
                Assert.IsNotNull(element, c.Key);
                XmlComment comment = element.PreviousSibling as XmlComment;
                Assert.IsNotNull(comment, "comment before " + c.Key);
                StringAssert.StartsWith(comment.Value, " " + c.Key + ": ");
            }
            // A setting of App > Options has no comment
            Assert.IsNotInstanceOfType(doc.DocumentElement["Language"].PreviousSibling, typeof(XmlComment));
        }

        [TestMethod]
        public void UpdatesParallelism_DefaultAndBounds()
        {
            Assert.AreEqual(4, new UserSettings().UpdatesParallelism, "4 computers at a time, as before the option");
            Assert.AreEqual(10, UserSettings.ClampParallelism(10));
            Assert.AreEqual(1, UserSettings.ClampParallelism(0), "0 or less (edited by hand): one at a time");
            Assert.AreEqual(1, UserSettings.ClampParallelism(-5));
            Assert.AreEqual(32, UserSettings.ClampParallelism(500), "never more than 32");
            // An older settings.xml without the element: the default
            UserSettings old = (UserSettings)new XmlSerializer(typeof(UserSettings)).Deserialize(new StringReader("<LiteDeploySettings><IncludeServers>true</IncludeServers></LiteDeploySettings>"));
            Assert.AreEqual(4, old.UpdatesParallelism);
        }

        [TestMethod]
        public void ToXml_LoadsBackWithEveryValue()
        {
            UserSettings s = new UserSettings
            {
                AllowDomainControllers = true,
                ExtraLogFolders = "myscript|Temp\\MyScript|10",
                UpdatesStaleDays = 45,
                WebCheckTime = "06:30",
                IncludeServers = true,
                UpdatesParallelism = 10
            };
            String path = Path.Combine(Path.GetTempPath(), "LiteDeploy-settings-" + Guid.NewGuid().ToString("N") + ".xml");
            try
            {
                using (XmlWriter writer = XmlWriter.Create(path, new XmlWriterSettings { Indent = true, Encoding = new System.Text.UTF8Encoding(false) }))
                {
                    UserSettings.ToXml(s).Save(writer);
                }
                UserSettings back;
                using (FileStream file = File.OpenRead(path))
                {
                    back = (UserSettings)new XmlSerializer(typeof(UserSettings)).Deserialize(file);
                }
                Assert.IsTrue(back.AllowDomainControllers);
                Assert.AreEqual("myscript|Temp\\MyScript|10", back.ExtraLogFolders);
                Assert.AreEqual(45, back.UpdatesStaleDays);
                Assert.AreEqual("06:30", back.WebCheckTime);
                Assert.IsTrue(back.IncludeServers);
                Assert.AreEqual(10, back.UpdatesParallelism);
                StringAssert.Contains(File.ReadAllText(path), "<!-- AllowDomainControllers: ");
            }
            finally
            {
                File.Delete(path);
            }
        }
    }
}
