// -----------------------------------------------------------------------------
// LiteSuite.Common - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    PsExecCommandLineTests.cs
// Author:  LiteSuite contributors
// Date:    2026-11-12
// Purpose: Unit tests of PsExecCommandLine (ported from LiteTask).
// -----------------------------------------------------------------------------
using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LiteSuite.Tests
{
    [TestClass]
    public class PsExecCommandLineTests
    {
        [TestMethod]
        public void BuildArguments_ServiceContext_NoInteractiveFlag()
        {
            Assert.AreEqual("-accepteula -nobanner -u DOMAIN\\svc -p \"p@ss\" whoami.exe",
                PsExecCommandLine.BuildArguments("DOMAIN\\svc", "p@ss", false, false, "whoami.exe"));
        }

        [TestMethod]
        public void BuildArguments_InteractiveAndElevated_AddsFlagsInOrder()
        {
            Assert.AreEqual("-accepteula -nobanner -i -h -u user -p \"pw\" whoami.exe",
                PsExecCommandLine.BuildArguments("user", "pw", true, true, "whoami.exe"));
        }

        [TestMethod]
        public void BuildArguments_PasswordWithSpacesAndQuotes_IsEscaped()
        {
            string args = PsExecCommandLine.BuildArguments("user", "my \"secret\" pw", false, false, "whoami.exe");
            StringAssert.Contains(args, " -p \"my \\\"secret\\\" pw\" ");
        }

        [TestMethod]
        public void BuildArguments_PasswordEndingWithBackslash_DoublesTrailingBackslash()
        {
            StringAssert.Contains(PsExecCommandLine.BuildArguments("user", "abc\\", false, false, "whoami.exe"), " -p \"abc\\\\\" ");
        }

        [TestMethod]
        public void BuildArguments_EmptyPassword_PassesEmptyQuotedValue()
        {
            StringAssert.Contains(PsExecCommandLine.BuildArguments("user", "", false, false, "whoami.exe"), " -p \"\" ");
        }

        [TestMethod]
        public void BuildArguments_MissingUsernameOrCommand_Throws()
        {
            Assert.ThrowsException<ArgumentException>(() => PsExecCommandLine.BuildArguments("", "pw", false, false, "whoami.exe"));
            Assert.ThrowsException<ArgumentException>(() => PsExecCommandLine.BuildArguments("user", "pw", false, false, " "));
        }

        [TestMethod]
        public void BuildExecutableCommand_TargetWithSpaces_IsQuoted()
        {
            Assert.AreEqual("\"C:\\Program Files\\App\\tool.exe\" -a 1", PsExecCommandLine.BuildExecutableCommand("C:\\Program Files\\App\\tool.exe", "-a 1"));
        }

        [TestMethod]
        public void BuildExecutableCommand_NoParameters_NoTrailingSpace()
        {
            Assert.AreEqual("\"whoami.exe\"", PsExecCommandLine.BuildExecutableCommand("whoami.exe", null));
        }

        [TestMethod]
        public void BuildExecutableCommand_EmptyTarget_RunsParametersThroughCmd()
        {
            Assert.AreEqual("cmd.exe /c echo hello", PsExecCommandLine.BuildExecutableCommand("", "echo hello"));
        }

        [TestMethod]
        public void BuildBatchCommand_PassesScriptAndArgs()
        {
            Assert.AreEqual("cmd.exe /c \"C:\\Temp\\batch_1.bat\" arg1 \"arg two\"", PsExecCommandLine.BuildBatchCommand("C:\\Temp\\batch_1.bat", "arg1 \"arg two\""));
        }

        [TestMethod]
        public void QuoteArgument_PlainValue_Unchanged()
        {
            Assert.AreEqual("simple", PsExecCommandLine.QuoteArgument("simple"));
        }

        [TestMethod]
        public void QuoteArgument_EmptyOrNull_IsEmptyQuotes()
        {
            Assert.AreEqual("\"\"", PsExecCommandLine.QuoteArgument(""));
            Assert.AreEqual("\"\"", PsExecCommandLine.QuoteArgument(null));
        }

        [TestMethod]
        public void QuoteArgument_BackslashesBeforeQuote_AreDoubled()
        {
            // a\"b must come back from argv parsing as a\"b
            Assert.AreEqual("\"a\\\\\\\"b\"", PsExecCommandLine.QuoteArgument("a\\\"b"));
        }

        [TestMethod]
        public void QuoteArgument_TrailingBackslash_IsDoubled()
        {
            Assert.AreEqual("\"abc\\\\\"", PsExecCommandLine.QuoteArgument("abc\\", true));
        }

        [TestMethod]
        public void QuoteArgument_InternalBackslashes_LeftAlone()
        {
            Assert.AreEqual("\"C:\\dir\\file.exe\"", PsExecCommandLine.QuoteArgument("C:\\dir\\file.exe", true));
        }

        [TestMethod]
        public void IsStatusLine_PsExecProgressMessages_AreStatus()
        {
            string[] lines =
            {
                "Connecting to local system...",
                "Starting PSEXESVC service on local system...",
                "Copying authentication key to SERVER01...",
                "Connecting with PsExec service on SERVER01...",
                "Starting C:\\Program Files\\App\\tool.exe on SERVER01...",
                "C:\\Program Files\\App\\tool.exe exited on SERVER01 with error code 0.",
                "cmd.exe exited on SERVER01 with error code -1073741510."
            };
            foreach (string line in lines)
                Assert.IsTrue(PsExecCommandLine.IsStatusLine(line), line);
        }

        [TestMethod]
        public void IsStatusLine_RealErrorsAndBlankLines_AreNotStatus()
        {
            string[] lines =
            {
                "", "   ", "Access is denied.", "Couldn't access SERVER01:",
                "The user name or password is incorrect.", "PsExec could not start cmd.exe on SERVER01:",
                "Starting import failed"
            };
            foreach (string line in lines)
                Assert.IsFalse(PsExecCommandLine.IsStatusLine(line), "'" + line + "'");
            Assert.IsFalse(PsExecCommandLine.IsStatusLine(null));
        }
    }
}
