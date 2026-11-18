// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    ModuleParametersTests.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-17
// Purpose: Unit tests of the module parameters (docs/modules.md): header
//          parsing, module-settings.xml round-trip, parameter file, worker side
//          mapping to LITEDEPLOY_PARAM_ variables, required and default values,
//          and that a secret value never shows in arguments or messages.
// -----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;
using LiteDeploy.Worker;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LiteDeploy.Tests
{
    [TestClass]
    public class ModuleParametersTests
    {
        const string Secret = "s3cr3t-T0ken/with=special&chars";

        static UpdateModule ExampleModule()
        {
            return UpdateModules.Parse("Example", new[]
            {
                "# LiteDeploy-Module",
                "# Label: Example product",
                "# Parameter: Environment default=Production Name of the environment",
                "# Parameter: InstallerFile default=\"example 1.2.msi\" File name in the cache",
                "# Parameter: Proxy default= Optional proxy",
                "# Parameter: EnrollmentToken secret Enrollment token",
                "param([string] $Action)"
            }, "Example.ps1");
        }

        // ---- Header -----------------------------------------------------------------

        [TestMethod]
        public void Header_ParsesNameFlagsDefaultAndDescription()
        {
            List<ModuleParameter> list = ExampleModule().Parameters;
            CollectionAssert.AreEqual(new[] { "Environment", "InstallerFile", "Proxy", "EnrollmentToken" }, list.Select(p => p.Name).ToArray());

            Assert.AreEqual("Production", list[0].Default);
            Assert.AreEqual("Name of the environment", list[0].Description);
            Assert.IsFalse(list[0].Secret);
            Assert.IsFalse(list[0].Required);

            Assert.AreEqual("example 1.2.msi", list[1].Default);
            Assert.AreEqual("File name in the cache", list[1].Description);

            Assert.AreEqual("", list[2].Default, "default= is an empty default: optional");
            Assert.IsFalse(list[2].Required);

            Assert.IsTrue(list[3].Secret);
            Assert.IsTrue(list[3].Required);
            Assert.IsNull(list[3].Default);
            Assert.AreEqual("Enrollment token", list[3].Description);
        }

        [TestMethod]
        public void Header_FlagsInAnyOrder_CaseInsensitiveKey()
        {
            ModuleParameter p = ModuleParameters.ParseHeaderLine("\uFEFF  # parameter:  Key_1  default=abc  SECRET  The key  ");
            Assert.AreEqual("Key_1", p.Name);
            Assert.IsTrue(p.Secret);
            Assert.AreEqual("abc", p.Default);
            Assert.AreEqual("The key", p.Description);

            ModuleParameter bare = ModuleParameters.ParseHeaderLine("# Parameter: Mode");
            Assert.AreEqual("Mode", bare.Name);
            Assert.AreEqual("", bare.Description);
            Assert.IsTrue(bare.Required);
        }

        [TestMethod]
        public void Header_RejectsInvalidNamesAndOtherLines()
        {
            Assert.IsNull(ModuleParameters.ParseHeaderLine("# Parameter: 1abc"));
            Assert.IsNull(ModuleParameters.ParseHeaderLine("# Parameter: a-b"));
            Assert.IsNull(ModuleParameters.ParseHeaderLine("# Parameter: a$b"));
            Assert.IsNull(ModuleParameters.ParseHeaderLine("# Parameter: " + new string('a', 33)));
            Assert.IsNotNull(ModuleParameters.ParseHeaderLine("# Parameter: " + new string('a', 32)));
            Assert.IsNull(ModuleParameters.ParseHeaderLine("# Parameter:"));
            Assert.IsNull(ModuleParameters.ParseHeaderLine("# Label: x"));
            Assert.IsNull(ModuleParameters.ParseHeaderLine(null));
        }

        [TestMethod]
        public void Header_DuplicateName_FirstWins_AndNoParametersForV1Module()
        {
            List<ModuleParameter> list = ModuleParameters.ParseHeader(new[] { "# Parameter: A default=1", "# Parameter: a default=2" });
            Assert.AreEqual(1, list.Count);
            Assert.AreEqual("1", list[0].Default);

            UpdateModule v1 = UpdateModules.Parse("Old", new[] { "# LiteDeploy-Module", "# Label: Old" }, "Old.ps1");
            Assert.AreEqual(0, v1.Parameters.Count);
        }

        [TestMethod]
        public void Template_DeclaresParameters()
        {
            string path = Path.Combine(RepoRoot(), "docs", "modules", "Template-Module.ps1");
            UpdateModule m = UpdateModules.Read(path);
            Assert.IsNotNull(m);
            CollectionAssert.AreEqual(new[] { "Environment", "InstallerFile", "EnrollmentToken" }, m.Parameters.Select(p => p.Name).ToArray());
            Assert.IsTrue(m.Parameters.Single(p => p.Name == "EnrollmentToken").Secret);
        }

        // ---- module-settings.xml ----------------------------------------------------

        [TestMethod]
        public void Settings_RoundTrip_ValuesAndCredentials()
        {
            ModuleSettings s = new ModuleSettings();
            s.SetValue("Example", "Environment", "Test <&> \"quoted\"");
            s.SetCredential("Example", "EnrollmentToken", "Example token");
            s.SetValue("Other", "Mode", "x");
            s.SetValue("Other", "Mode", "");   // removed

            ModuleSettings loaded = ModuleSettings.Parse(s.ToXml());
            Assert.AreEqual("Test <&> \"quoted\"", loaded.Get("example", "environment").Value);
            Assert.IsNull(loaded.Get("Example", "Environment").Credential);
            Assert.AreEqual("Example token", loaded.Get("Example", "EnrollmentToken").Credential);
            Assert.IsNull(loaded.Get("Example", "EnrollmentToken").Value);
            Assert.IsNull(loaded.Get("Other", "Mode"));
            CollectionAssert.AreEqual(new[] { "Example" }, loaded.Modules.ToArray());
        }

        [TestMethod]
        public void Settings_File_SaveLoad_AndInvalidContent()
        {
            string dir = Path.Combine(Path.GetTempPath(), "litedeploy-modsettings-" + Guid.NewGuid().ToString("N"));
            string path = Path.Combine(dir, "module-settings.xml");
            try
            {
                Assert.AreEqual(0, ModuleSettings.Load(path).Modules.Count(), "missing file: empty");
                ModuleSettings s = new ModuleSettings();
                s.SetValue("Example", "Environment", "Production");
                s.Save(path);
                Assert.AreEqual("Production", ModuleSettings.Load(path).Get("Example", "Environment").Value);

                File.WriteAllText(path, "not xml");
                Assert.AreEqual(0, ModuleSettings.Load(path).Modules.Count(), "unreadable: empty");

                File.WriteAllText(path, "<ModuleSettings><Module name=\"../evil\"><Parameter name=\"A\" value=\"1\" /></Module>"
                                        + "<Module name=\"Ok\"><Parameter name=\"bad-name\" value=\"1\" /><Parameter name=\"B\" value=\"2\" /></Module></ModuleSettings>");
                ModuleSettings filtered = ModuleSettings.Load(path);
                CollectionAssert.AreEqual(new[] { "Ok" }, filtered.Modules.ToArray());
                Assert.AreEqual("2", filtered.Get("Ok", "B").Value);
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch (Exception) { }
            }
        }

        [TestMethod]
        public void Settings_SecretIsStoredAsCredentialNameOnly()
        {
            ModuleSettings s = new ModuleSettings();
            s.SetCredential("Example", "EnrollmentToken", "Example token");
            string xml = s.ToXml();
            StringAssert.Contains(xml, "credential=\"Example token\"");
            Assert.IsFalse(xml.Contains("value="), xml);
        }

        // ---- Values of a run (LiteDeploy side) --------------------------------------

        [TestMethod]
        public void RunValues_SecretFromCredential_PlainFromValue()
        {
            ModuleSettings s = new ModuleSettings();
            s.SetValue("Example", "Environment", "Test");
            s.SetCredential("Example", "EnrollmentToken", "Example token");
            // Wrong kinds are ignored: a plain value for a secret, a credential for a plain one
            s.SetCredential("Example", "InstallerFile", "Example token");
            string error;
            Dictionary<string, string> values = UpdateModules.BuildParameterValues(ExampleModule(), s,
                name => name == "Example token" ? Secret : null, out error);
            Assert.IsNull(error);
            Assert.AreEqual("Test", values["Environment"]);
            Assert.AreEqual(Secret, values["EnrollmentToken"]);
            Assert.IsFalse(values.ContainsKey("InstallerFile"), "a credential never fills a plain parameter");

            ModuleSettings plainSecret = new ModuleSettings();
            plainSecret.SetValue("Example", "EnrollmentToken", "typed");
            Assert.AreEqual(0, UpdateModules.BuildParameterValues(ExampleModule(), plainSecret, n => Secret, out error).Count);
        }

        [TestMethod]
        public void RunValues_MissingCredential_ErrorNamesItWithoutValues()
        {
            ModuleSettings s = new ModuleSettings();
            s.SetValue("Example", "Environment", "Test");
            s.SetCredential("Example", "EnrollmentToken", "Gone");
            string error;
            Dictionary<string, string> values = UpdateModules.BuildParameterValues(ExampleModule(), s, n => null, out error);
            Assert.AreEqual(0, values.Count);
            StringAssert.Contains(error, "Gone");
            StringAssert.Contains(error, "EnrollmentToken");
            Assert.IsFalse(error.Contains("Test"), error);
        }

        [TestMethod]
        public void ParameterFile_IsProtectedWhenCreated()
        {
            string dir = Path.Combine(Path.GetTempPath(), "litedeploy-params-" + Guid.NewGuid().ToString("N"));
            string path = Path.Combine(dir, "Example" + ModuleParameters.FileExtension);
            try
            {
                Directory.CreateDirectory(dir);
                File.WriteAllText(path, "old");   // an earlier file is replaced
                UpdateModules.WriteParameterFile(path, new Dictionary<string, string> { { "EnrollmentToken", Secret } });

                FileSecurity security = File.GetAccessControl(path);
                Assert.IsTrue(security.AreAccessRulesProtected, "no inherited access");
                SecurityIdentifier system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
                SecurityIdentifier admins = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
                List<FileSystemAccessRule> rules = security.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>().ToList();
                Assert.IsTrue(rules.Count > 0);
                foreach (FileSystemAccessRule r in rules)
                    Assert.IsTrue(r.IdentityReference.Equals(system) || r.IdentityReference.Equals(admins), "unexpected access for " + r.IdentityReference);
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch (Exception) { }
            }
        }

        // ---- Parameter file and worker side ------------------------------------------

        [TestMethod]
        public void ParameterFile_RoundTrip_SpecialCharacters()
        {
            Dictionary<string, string> values = new Dictionary<string, string>
            {
                { "Environment", "a=b\tc\r\nd \u00e9" },
                { "EnrollmentToken", Secret },
                { "bad-name", "dropped" }
            };
            string content = ModuleParameters.FormatFile(values);
            Assert.IsTrue(content.StartsWith(ModuleParameters.FileHeader + "\n"));
            Assert.IsFalse(content.Contains("bad-name"));
            Dictionary<string, string> read = ModuleParameters.ParseFile(content);
            Assert.AreEqual(2, read.Count);
            Assert.AreEqual("a=b\tc\r\nd \u00e9", read["environment"]);
            Assert.AreEqual(Secret, read["EnrollmentToken"]);

            Assert.AreEqual(0, ModuleParameters.ParseFile("Environment=x\n").Count, "unknown format");
            Assert.AreEqual(0, ModuleParameters.ParseFile(null).Count);
        }

        [TestMethod]
        public void Worker_Resolve_MapsDeclaredParametersToEnvironmentVariables()
        {
            Dictionary<string, string> environment;
            List<string> secrets;
            string error = ModuleParameters.Resolve(ExampleModule().Parameters,
                new Dictionary<string, string> { { "environment", "Test" }, { "EnrollmentToken", Secret }, { "Undeclared", "ignored" } },
                out environment, out secrets);
            Assert.IsNull(error);
            Assert.AreEqual("Test", environment["LITEDEPLOY_PARAM_Environment"]);
            Assert.AreEqual("example 1.2.msi", environment["LITEDEPLOY_PARAM_InstallerFile"], "default of the header");
            Assert.IsFalse(environment.ContainsKey("LITEDEPLOY_PARAM_Proxy"), "an empty value sets no variable");
            Assert.AreEqual(Secret, environment["LITEDEPLOY_PARAM_EnrollmentToken"]);
            Assert.IsFalse(environment.Keys.Any(k => k.EndsWith("Undeclared")), "only declared parameters are passed");
            CollectionAssert.AreEqual(new[] { Secret }, secrets);
        }

        [TestMethod]
        public void Worker_Resolve_RequiredWithoutValue_IsAnErrorWithoutValues()
        {
            Dictionary<string, string> environment;
            List<string> secrets;
            string error = ModuleParameters.Resolve(ExampleModule().Parameters,
                new Dictionary<string, string> { { "Environment", "VisibleValue" } }, out environment, out secrets);
            Assert.IsNotNull(error);
            StringAssert.Contains(error, "EnrollmentToken");
            StringAssert.Contains(error, "not started");
            Assert.IsFalse(error.Contains("VisibleValue"), error);

            // No parameter declared (version 1 module): nothing to set, nothing missing
            Assert.IsNull(ModuleParameters.Resolve(new List<ModuleParameter>(), null, out environment, out secrets));
            Assert.AreEqual(0, environment.Count);
        }

        [TestMethod]
        public void Worker_ApplyEnvironment_ForTheModuleProcessOnly()
        {
            ProcessStartInfo psi = new ProcessStartInfo("powershell.exe");
            psi.EnvironmentVariables["LITEDEPLOY_PARAM_Stale"] = "from the worker environment";
            psi.EnvironmentVariables["LITEDEPLOY_CACHE"] = @"C:\cache";
            ModuleRunner.ApplyEnvironment(psi.EnvironmentVariables, new Dictionary<string, string> { { "LITEDEPLOY_PARAM_EnrollmentToken", Secret } });
            Assert.AreEqual(Secret, psi.EnvironmentVariables["LITEDEPLOY_PARAM_EnrollmentToken"]);
            Assert.IsFalse(psi.EnvironmentVariables.ContainsKey("LITEDEPLOY_PARAM_Stale"), "inherited parameters are removed");
            Assert.AreEqual(@"C:\cache", psi.EnvironmentVariables["LITEDEPLOY_CACHE"]);
            Assert.IsFalse(psi.Arguments.Contains(Secret));
            Assert.IsNull(Environment.GetEnvironmentVariable("LITEDEPLOY_PARAM_EnrollmentToken"), "the worker's own environment is unchanged");
        }

        // ---- Secret masking ------------------------------------------------------------

        [TestMethod]
        public void Secret_MaskedInModuleResults()
        {
            string output = WorkerProtocol.Format("type", "module", "status", "error", "message", "Enrollment failed with token " + Secret);
            ModuleResult r = ModuleRunner.MaskSecrets(ModuleRunner.Interpret(1, output, "Exception: bad token " + Uri.EscapeDataString(Secret)), new[] { Secret });
            Assert.AreEqual("error", r.Status);
            Assert.IsFalse(r.Message.Contains(Secret), r.Message);
            Assert.IsFalse(r.Message.Contains(Uri.EscapeDataString(Secret)), r.Message);
            StringAssert.Contains(r.Message, ModuleParameters.Mask);

            Assert.AreEqual("plain text", ModuleParameters.MaskSecrets("plain text", new[] { "", null }));
            Assert.IsNull(ModuleParameters.MaskSecrets(null, new[] { Secret }));
        }

        [TestMethod]
        public void Secret_NeverInWorkerArguments()
        {
            List<UpdatePackage> packages = new List<UpdatePackage> { ExampleModule().ToPackage(), UpdatePackage.Parse("7zip.7zip | 7zFM | 7-Zip") };
            string args = UpdateRunner.BuildWorkerArguments("upgrade", packages, false, null);
            Assert.IsFalse(args.Contains(Secret), args);
            Assert.IsFalse(args.Contains("LITEDEPLOY_PARAM"), args);
            Assert.AreEqual("module:Example||Example product", packages[0].ToArgument());
        }

        static string RepoRoot()
        {
            string dir = AppDomain.CurrentDomain.BaseDirectory;
            while (dir != null && !File.Exists(Path.Combine(dir, "LiteDeploy.sln")))
                dir = Path.GetDirectoryName(dir);
            Assert.IsNotNull(dir, "repository root not found");
            return dir;
        }
    }
}
