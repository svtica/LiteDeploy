// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    PsExecCommandBuilderTests.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-11
// Purpose: Tests of the PsExec command line (quoting ported from LiteTask,
//          LiteDeploy options, masked password).
// -----------------------------------------------------------------------------
using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LiteDeploy.Tests
{
    [TestClass]
    public class PsExecCommandBuilderTests
    {
        static PsExecOptions Options()
        {
            PsExecOptions o = new PsExecOptions();
            o.Computer = "PC01";
            o.Arguments = "cmd /c echo hi";
            return o;
        }

        [TestMethod]
        public void BuildArguments_Defaults_AcceptsEulaAndElevates()
        {
            Assert.AreEqual("-accepteula -nobanner \\\\PC01 -h cmd /c echo hi", PsExecCommandBuilder.BuildArguments(Options()));
        }

        [TestMethod]
        public void BuildArguments_AllOptions_InOrder()
        {
            PsExecOptions o = Options();
            o.Interactive = true; o.Session = 2; o.DontWait = true; o.WorkingDirectory = @"C:\Work Dir";
            o.CopyExecutable = true; o.OverwriteExisting = true; o.ConnectionTimeout = 5; o.PriorityIndex = 0;
            o.Executable = @"C:\Program Files\App\setup.exe"; o.Arguments = "/quiet";
            Assert.AreEqual("-accepteula -nobanner \\\\PC01 -h -i 2 -d -w \"C:\\Work Dir\" -c -f -n 5 -low \"C:\\Program Files\\App\\setup.exe\" /quiet",
                            PsExecCommandBuilder.BuildArguments(o));
        }

        [TestMethod]
        public void BuildArguments_Credentials_UserQuotedOnlyWhenNeeded_PasswordAlwaysQuoted()
        {
            PsExecOptions o = Options();
            o.UseCredentials = true; o.UserName = @"DOMAIN\deploy"; o.Password = "p@ss";
            StringAssert.Contains(PsExecCommandBuilder.BuildArguments(o), " -u DOMAIN\\deploy -p \"p@ss\" ");
        }

        [TestMethod]
        public void BuildArguments_CredentialsWinOverSystem()
        {
            PsExecOptions o = Options();
            o.UseCredentials = true; o.UserName = "u"; o.Password = "p"; o.RunAsSystem = true;
            Assert.IsFalse(PsExecCommandBuilder.BuildArguments(o).Contains(" -s"));
        }

        [TestMethod]
        public void BuildArguments_System()
        {
            PsExecOptions o = Options();
            o.RunAsSystem = true;
            StringAssert.Contains(PsExecCommandBuilder.BuildArguments(o), " -h -s cmd");
        }

        [TestMethod]
        public void BuildArguments_PasswordWithSpacesAndQuotes_IsEscaped()
        {
            PsExecOptions o = Options();
            o.UseCredentials = true; o.UserName = "u"; o.Password = "my \"secret\" pw";
            StringAssert.Contains(PsExecCommandBuilder.BuildArguments(o), " -p \"my \\\"secret\\\" pw\" ");
        }

        [TestMethod]
        public void BuildArguments_EmptyPassword_PassesEmptyQuotedValue()
        {
            PsExecOptions o = Options();
            o.UseCredentials = true; o.UserName = "u"; o.Password = "";
            StringAssert.Contains(PsExecCommandBuilder.BuildArguments(o), " -p \"\" ");
        }

        [TestMethod]
        public void DisplayArguments_MasksThePassword()
        {
            PsExecOptions o = Options();
            o.UseCredentials = true; o.UserName = "u"; o.Password = "TopSecret";
            String shown = PsExecCommandBuilder.DisplayArguments(o);
            Assert.IsFalse(shown.Contains("TopSecret"), shown);
            StringAssert.Contains(shown, " -p ****** ");
        }

        [TestMethod]
        public void BuildArguments_PriorityNormal_NoFlag()
        {
            PsExecOptions o = Options();
            o.PriorityIndex = 2;
            Assert.IsFalse(PsExecCommandBuilder.BuildArguments(o).Contains("-low"));
            o.PriorityIndex = 5;
            StringAssert.Contains(PsExecCommandBuilder.BuildArguments(o), " -realtime ");
        }

        [TestMethod]
        public void BuildArguments_MissingComputer_Throws()
        {
            PsExecOptions o = Options();
            o.Computer = " ";
            try
            {
                PsExecCommandBuilder.BuildArguments(o);
                Assert.Fail("no exception");
            }
            catch (ArgumentException) { }
        }
    }
}
