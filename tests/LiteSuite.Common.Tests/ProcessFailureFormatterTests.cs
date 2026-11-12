// -----------------------------------------------------------------------------
// LiteSuite.Common - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    ProcessFailureFormatterTests.cs
// Author:  LiteSuite contributors
// Date:    2026-11-12
// Purpose: Unit tests of ProcessFailureFormatter (ported from LiteTask).
// -----------------------------------------------------------------------------
using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LiteSuite.Tests
{
    [TestClass]
    public class ProcessFailureFormatterTests
    {
        static readonly TimeSpan Duration = TimeSpan.FromSeconds(39.8);

        [TestMethod]
        public void BuildFailureMessage_NoOutput_HasExitCodeAndDurationOnly()
        {
            Assert.AreEqual("Error: Process exited with code 1 after 39.8 s", ProcessFailureFormatter.BuildFailureMessage(1, "", "", Duration));
        }

        [TestMethod]
        public void BuildFailureMessage_ErrorOnStdout_IsIncluded()
        {
            string stdout = "Msg 547, Level 16, State 0, Server SQLHOST, Line 12\r\nThe INSERT statement conflicted with the FOREIGN KEY constraint";
            string msg = ProcessFailureFormatter.BuildFailureMessage(1, stdout, "", Duration);
            StringAssert.Contains(msg, "Msg 547");
            StringAssert.Contains(msg, "stdout (last 2 of 2 lines):");
            Assert.IsFalse(msg.Contains("stderr:"), msg);
        }

        [TestMethod]
        public void BuildFailureMessage_StderrBeforeStdout()
        {
            string msg = ProcessFailureFormatter.BuildFailureMessage(3, "out", "err", Duration);
            Assert.IsTrue(msg.StartsWith("Error:", StringComparison.Ordinal));
            int errPos = msg.IndexOf("stderr: err", StringComparison.Ordinal);
            int outPos = msg.IndexOf("stdout (last 1 of 1 lines): out", StringComparison.Ordinal);
            Assert.IsTrue(errPos > 0 && outPos > errPos, msg);
        }

        [TestMethod]
        public void BuildFailureMessage_LongStdout_KeepsLastFortyLines()
        {
            string stdout = string.Join("\r\n", Enumerable.Range(1, 100).Select(i => "line " + i));
            string msg = ProcessFailureFormatter.BuildFailureMessage(1, stdout, "", Duration);
            StringAssert.Contains(msg, "stdout (last 40 of 100 lines):");
            Assert.IsFalse(msg.Contains("line 60" + Environment.NewLine), msg);
            Assert.IsTrue(msg.EndsWith("line 100", StringComparison.Ordinal), msg);
        }

        [TestMethod]
        public void TailLines_AndTailChars_EdgeCases()
        {
            Assert.AreEqual(string.Empty, ProcessFailureFormatter.TailLines(null, 5));
            Assert.AreEqual("a\nb", ProcessFailureFormatter.TailLines("a\nb", 5));
            Assert.AreEqual(string.Empty, ProcessFailureFormatter.TailChars(null, 5));
            Assert.AreEqual("abc", ProcessFailureFormatter.TailChars("abc", 0));
            Assert.AreEqual("...TAIL", ProcessFailureFormatter.TailChars("aaaaTAIL", 4));
        }
    }
}
