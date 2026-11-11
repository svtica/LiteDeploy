// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    LocalizationTests.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-11
// Purpose: Tests of the French localization: fallback to English, lang\fr.xml
//          loads, typography, and every key of fr.xml still used by the code
//          or the designer files (no dead or misspelled key).
// -----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LiteDeploy.Tests
{
    [TestClass]
    public class LocalizationTests
    {
        static String RepoRoot()
        {
            String dir = AppDomain.CurrentDomain.BaseDirectory;
            while (dir != null && !File.Exists(Path.Combine(dir, "LiteDeploy.sln")))
                dir = Path.GetDirectoryName(dir);
            Assert.IsNotNull(dir, "LiteDeploy.sln not found above the test folder");
            return dir;
        }

        [TestCleanup]
        public void Cleanup()
        {
            Environment.SetEnvironmentVariable("LITEDEPLOY_LANG", null);
            Localization.Initialize(Localization.English);
        }

        [TestMethod]
        public void English_ReturnsTheEnglishText()
        {
            Localization.Initialize(Localization.English);
            Assert.AreEqual("Start Deploy", Localization.T("MainForm.startDeploy.Text", "Start Deploy"));
        }

        [TestMethod]
        public void French_TranslatesAndKeepsQuebecTypography()
        {
            Localization.Initialize(Localization.French);
            Assert.AreEqual(Localization.French, Localization.Language);
            Assert.AreEqual("Démarrer le déploiement", Localization.T("MainForm.startDeploy.Text", "Start Deploy"));
            Assert.AreEqual("Mot de passe :", Localization.T("MainForm.label2.Text", "Password:"));
        }

        [TestMethod]
        public void French_MissingKey_FallsBackToEnglish()
        {
            Localization.Initialize(Localization.French);
            Assert.AreEqual("Untranslated", Localization.T("No.Such.Key", "Untranslated"));
        }

        [TestMethod]
        public void Format_FillsPlaceholders()
        {
            Localization.Initialize(Localization.French);
            Assert.AreEqual("Total : 3", Localization.F("PingAllForm.Total", "Total: {0}", 3));
        }

        [TestMethod]
        public void EnvironmentVariable_OverridesTheChoice()
        {
            Environment.SetEnvironmentVariable("LITEDEPLOY_LANG", "en");
            Localization.Initialize(Localization.French);
            Assert.AreEqual(Localization.English, Localization.Language);
        }

        [TestMethod]
        public void EveryFrenchKey_IsUsedByTheCodeOrADesigner()
        {
            String root = RepoRoot();
            XmlDocument doc = new XmlDocument();
            doc.Load(Path.Combine(root, "lang", "fr.xml"));

            Dictionary<String, String> sources = Directory.GetFiles(Path.Combine(root, "Forms"), "*.cs")
                .Concat(Directory.GetFiles(Path.Combine(root, "Classes"), "*.cs"))
                .ToDictionary(f => f, f => File.ReadAllText(f));

            List<String> unused = new List<String>();
            foreach (XmlNode node in doc.SelectNodes("/translations/translation"))
            {
                String key = node.Attributes["key"].Value;
                String[] parts = key.Split('.');
                bool used = sources.Values.Any(s => s.Contains("\"" + key + "\""));

                if (!used && parts.Length >= 2)
                {
                    // Form.Text, Form.field.Text / .Title / .Description / .Items.N
                    String designer = Path.Combine(root, "Forms", parts[0] + ".Designer.cs");
                    if (File.Exists(designer))
                    {
                        String text = sources[designer];
                        used = parts.Length == 2 ? parts[1] == "Text" : Regex.IsMatch(text, @"this\." + Regex.Escape(parts[1]) + @"\b");
                    }
                }

                if (!used)
                    unused.Add(key);
            }

            Assert.AreEqual(0, unused.Count, "Keys of fr.xml used nowhere: " + String.Join(", ", unused));
        }
    }
}
