// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    SubnetListTests.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-12
// Purpose: Saved subnets of the computer list: accepted forms, labels, CSV,
//          ranges split into /24, rejected lines, duplicates.
// -----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LiteDeploy.Tests
{
    [TestClass]
    public class SubnetListTests
    {
        static List<SubnetEntry> One(String line)
        {
            List<SubnetEntry> entries;
            String error;
            Assert.IsTrue(SubnetList.TryParseLine(line, out entries, out error), error);
            return entries;
        }

        [TestMethod]
        public void AcceptedForms_GiveTheSamePrefix()
        {
            foreach (String line in new[] { "10.1.2", "10.1.2.", "10.1.2.0", "10.1.2.0/24", "  10.1.2.77/24  " })
            {
                List<SubnetEntry> e = One(line);
                Assert.AreEqual(1, e.Count, line);
                Assert.AreEqual("10.1.2.", e[0].Prefix, line);
                Assert.IsNull(e[0].Label, line);
            }
        }

        [TestMethod]
        public void Labels_SpaceTabSemicolonComma()
        {
            Assert.AreEqual("Garage nord", One("10.1.2.0/24 Garage nord")[0].Label);
            Assert.AreEqual("Garage", One("10.1.2.0/24\tGarage")[0].Label);
            Assert.AreEqual("Garage", One("10.1.2.0/24;Garage")[0].Label);
            Assert.AreEqual("Garage", One("10.1.2.0/24,\"Garage\"")[0].Label);
            Assert.AreEqual("Garage", One("10.1.2 Garage # comment")[0].Label);
            Assert.AreEqual("10.1.2.0/24 - Garage", One("10.1.2 Garage")[0].Display);
        }

        [TestMethod]
        public void Ranges_AreSplitInto24()
        {
            List<SubnetEntry> e = One("10.1.5.0/22 Main");
            CollectionAssert.AreEqual(new[] { "10.1.4.", "10.1.5.", "10.1.6.", "10.1.7." }, e.Select(x => x.Prefix).ToArray());
            Assert.IsTrue(e.All(x => x.Label == "Main"));
            Assert.AreEqual(16, One("172.16.0.0/20").Count);
        }

        [TestMethod]
        public void CommentsAndBlankLines_GiveNothing()
        {
            Assert.AreEqual(0, One("").Count);
            Assert.AreEqual(0, One("   ").Count);
            Assert.AreEqual(0, One("# 10.1.2.0/24").Count);
            Assert.AreEqual(0, One(null).Count);
        }

        [TestMethod]
        public void InvalidLines_AreRefused()
        {
            foreach (String line in new[] { "10.1", "10.1.2.3.4", "10.1.256.0", "10.1.2.0/16", "10.1.2.0/25", "10.1.2.0/x", "abc", "-1.2.3", "10.1.2.0/24/24", "10.1.+2" })
            {
                List<SubnetEntry> entries;
                String error;
                Assert.IsFalse(SubnetList.TryParseLine(line, out entries, out error), line);
                Assert.IsFalse(String.IsNullOrEmpty(error), line);
            }
        }

        [TestMethod]
        public void Parse_ReportsLineNumbers_AndKeepsFirstDuplicate()
        {
            List<String> errors = new List<String>();
            List<SubnetEntry> e = SubnetList.Parse(new[] { "10.1.2.0/24 A", "bad", "10.1.2 B", "10.1.3" }, errors);
            CollectionAssert.AreEqual(new[] { "10.1.2.", "10.1.3." }, e.Select(x => x.Prefix).ToArray());
            Assert.AreEqual("A", e[0].Label);
            Assert.AreEqual(1, errors.Count);
            StringAssert.Contains(errors[0], "2");
        }
    }
}
