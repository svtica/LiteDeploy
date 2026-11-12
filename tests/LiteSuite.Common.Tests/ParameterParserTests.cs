// -----------------------------------------------------------------------------
// LiteSuite.Common - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    ParameterParserTests.cs
// Author:  LiteSuite contributors
// Date:    2026-11-12
// Purpose: Unit tests of ParameterParser (ported from LiteTask).
// -----------------------------------------------------------------------------
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LiteSuite.Tests
{
    [TestClass]
    public class ParameterParserTests
    {
        [TestMethod]
        public void Parse_SinglePair_ReturnsKeyAndValue()
        {
            var r = ParameterParser.Parse("key=value");
            Assert.AreEqual(1, r.Count);
            Assert.AreEqual("value", r["key"]);
        }

        [TestMethod]
        public void Parse_QuotedValueWithSpaces_KeepsValueIntact()
        {
            Assert.AreEqual("value with spaces", ParameterParser.Parse("key=\"value with spaces\"")["key"]);
        }

        [TestMethod]
        public void Parse_MixedQuotedAndUnquoted_BothHandled()
        {
            var r = ParameterParser.Parse("a=1 b=\"hello world\" c=3");
            Assert.AreEqual(3, r.Count);
            Assert.AreEqual("1", r["a"]);
            Assert.AreEqual("hello world", r["b"]);
            Assert.AreEqual("3", r["c"]);
        }

        [TestMethod]
        public void Parse_EmptyValues_YieldEmptyString()
        {
            var r = ParameterParser.Parse("a= b=\"\" c=3");
            Assert.AreEqual(3, r.Count);
            Assert.AreEqual("", r["a"]);
            Assert.AreEqual("", r["b"]);
        }

        [TestMethod]
        public void Parse_NullOrEmpty_ReturnsEmptyDictionary()
        {
            Assert.AreEqual(0, ParameterParser.Parse(null).Count);
            Assert.AreEqual(0, ParameterParser.Parse("").Count);
            Assert.AreEqual(0, ParameterParser.Parse("   ").Count);
        }

        [TestMethod]
        public void Parse_TokenWithoutEquals_IsSkipped()
        {
            var r = ParameterParser.Parse("a=1 garbage b=2");
            Assert.AreEqual(2, r.Count);
            Assert.IsFalse(r.ContainsKey("garbage"));
        }

        [TestMethod]
        public void Parse_QuotedPathFollowedByMore_ContinuesParsing()
        {
            var r = ParameterParser.Parse("path=\"C:\\Program Files\\App\" mode=fast");
            Assert.AreEqual("C:\\Program Files\\App", r["path"]);
            Assert.AreEqual("fast", r["mode"]);
        }

        [TestMethod]
        public void Parse_DuplicateKey_LastWriteWins()
        {
            var r = ParameterParser.Parse("a=1 a=2 a=3");
            Assert.AreEqual(1, r.Count);
            Assert.AreEqual("3", r["a"]);
        }

        [TestMethod]
        public void Parse_PowerShellStyle_NameValueAndSwitches()
        {
            var r = ParameterParser.Parse("-Force -WindowHours 96 -EmailTo \"user@example.com\" -Verbose");
            Assert.AreEqual(4, r.Count);
            Assert.IsNull(r["Force"]);
            Assert.AreEqual("96", r["WindowHours"]);
            Assert.AreEqual("user@example.com", r["EmailTo"]);
            Assert.IsNull(r["Verbose"]);
        }

        [TestMethod]
        public void Parse_PowerShellStyle_NameEqualsValue_Accepted()
        {
            var r = ParameterParser.Parse("-Mode=fast -Count=3");
            Assert.AreEqual("fast", r["Mode"]);
            Assert.AreEqual("3", r["Count"]);
        }

        [TestMethod]
        public void Parse_MixedStyles_BothParsed()
        {
            var r = ParameterParser.Parse("alpha=one -Force -Beta \"two three\"");
            Assert.AreEqual(3, r.Count);
            Assert.AreEqual("one", r["alpha"]);
            Assert.IsNull(r["Force"]);
            Assert.AreEqual("two three", r["Beta"]);
        }

        [TestMethod]
        public void Parse_KeyLookup_IsCaseInsensitive()
        {
            var r = ParameterParser.Parse("-WindowHours 96");
            Assert.AreEqual("96", r["windowhours"]);
            Assert.AreEqual("96", r["WINDOWHOURS"]);
        }
    }
}
