// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    HiddenComputersTests.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-19
// Purpose: Unit tests of the computers left out of the grid: the computer
//          objects of Active Directory that are not computers (managed
//          service accounts, Seamless SSO account), and the computers removed
//          from the list by the user (Hidden flag, Hidden chip). Fictitious
//          names only.
// -----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LiteDeploy.Tests
{
    [TestClass]
    public class HiddenComputersTests
    {
        const String Schema = ",CN=Schema,CN=Configuration,DC=example,DC=com";

        static Func<String, Object> Props(Dictionary<String, Object> values)
        {
            return p => values.ContainsKey(p) ? values[p] : null;
        }

        [TestMethod]
        public void IsNotAComputer_ServiceAccountsAndKnownNames()
        {
            Assert.IsTrue(AdComputer.IsNotAComputer("SyncAccount01", "CN=ms-DS-Group-Managed-Service-Account" + Schema), "group managed service account");
            Assert.IsTrue(AdComputer.IsNotAComputer("SvcAccount01", "CN=ms-DS-Managed-Service-Account" + Schema), "managed service account");
            Assert.IsTrue(AdComputer.IsNotAComputer("SvcAccount02", "CN=ms-DS-Delegated-Managed-Service-Account" + Schema), "delegated managed service account");
            Assert.IsTrue(AdComputer.IsNotAComputer("AZUREADSSOACC", "CN=Computer" + Schema), "Seamless SSO account");
            Assert.IsTrue(AdComputer.IsNotAComputer("azureadssoacc$", null));

            Assert.IsFalse(AdComputer.IsNotAComputer("PC01", "CN=Computer" + Schema));
            Assert.IsFalse(AdComputer.IsNotAComputer("PC01", "cn=computer" + Schema));
            Assert.IsFalse(AdComputer.IsNotAComputer("PC01", null), "category not read: kept");
            Assert.IsFalse(AdComputer.IsNotAComputer("PC01", ""));
        }

        [TestMethod]
        public void FromProperties_ServiceAccount_IsNull()
        {
            Assert.IsNull(AdComputer.FromProperties(Props(new Dictionary<String, Object>
            {
                { "name", "SyncAccount01" },
                { "objectcategory", "CN=ms-DS-Group-Managed-Service-Account" + Schema }
            })));
            AdComputer pc = AdComputer.FromProperties(Props(new Dictionary<String, Object>
            {
                { "name", "PC01" },
                { "objectcategory", "CN=Computer" + Schema }
            }));
            Assert.IsNotNull(pc);
            Assert.AreEqual("PC01", pc.Name);
        }

        static List<ComputerRow> Rows()
        {
            return new List<ComputerRow>
            {
                new ComputerRow { Name = "PC01", InAd = true },
                new ComputerRow { Name = "PC02", InAd = true, Hidden = true },
                new ComputerRow { Name = "PC03", AddedByName = true }
            };
        }

        [TestMethod]
        public void Filter_HiddenOnlyWithTheHiddenChip()
        {
            DateTime now = new DateTime(2026, 11, 19);
            List<String> shown = ComputerFilter.Apply(Rows(), new ComputerFilterState(), now).Select(r => r.Name).ToList();
            CollectionAssert.AreEqual(new[] { "PC01", "PC03" }, shown);

            List<String> hidden = ComputerFilter.Apply(Rows(), new ComputerFilterState { Statuses = ComputerStatusFilter.Hidden }, now).Select(r => r.Name).ToList();
            CollectionAssert.AreEqual(new[] { "PC02" }, hidden);

            // Another chip alone never brings a hidden computer back
            List<String> notChecked = ComputerFilter.Apply(Rows(), new ComputerFilterState { Statuses = ComputerStatusFilter.NotChecked }, now).Select(r => r.Name).ToList();
            CollectionAssert.DoesNotContain(notChecked, "PC02");

            Assert.AreEqual(ComputerStatusFilter.Hidden, ComputerFilter.ParseStatuses(ComputerFilter.FormatStatuses(ComputerStatusFilter.Hidden)));
        }

        [TestMethod]
        public void HiddenFlag_RowAndMergedStates()
        {
            ComputerRow row = ComputerRow.Build("PC02", new AdComputer { Name = "PC02" }, new ComputerUpdateState { Name = "PC02", Hidden = true }, null, null);
            Assert.IsTrue(row.Hidden);

            ComputerUpdateState merged = UpdateStateStore.MergeStates(new List<ComputerUpdateState>
            {
                new ComputerUpdateState { Name = "PC02", LastChange = new DateTime(2026, 11, 1) },
                new ComputerUpdateState { Name = "pc02.example.com", Hidden = true, LastChange = new DateTime(2026, 10, 1) }
            });
            Assert.IsTrue(merged.Hidden, "hidden under any spelling");
        }
    }
}
