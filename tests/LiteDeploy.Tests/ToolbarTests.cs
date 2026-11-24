// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    ToolbarTests.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-24
// Purpose: Unit tests of the tool bars of the grid: the number of active
//          filters and the text of Update checked.
// -----------------------------------------------------------------------------
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LiteDeploy.Tests
{
    [TestClass]
    public class ToolbarTests
    {
        [TestMethod]
        public void ActiveFilterCount_ChipsAndSearch()
        {
            Assert.AreEqual(0, ComputerGrid.ActiveFilterCount(new[] { false, false }, " "));
            Assert.AreEqual(2, ComputerGrid.ActiveFilterCount(new[] { true, false, true }, ""));
            Assert.AreEqual(3, ComputerGrid.ActiveFilterCount(new[] { true, true }, "PC01"));
            Assert.AreEqual(1, ComputerGrid.ActiveFilterCount(null, "PC01"));
        }

        [TestMethod]
        public void UpdateButtonText_NumberOfCheckedRows()
        {
            Localization.Initialize(Localization.English);
            Assert.AreEqual("Update checked", ComputerGrid.UpdateButtonText(0));
            Assert.AreEqual("Update checked (3)", ComputerGrid.UpdateButtonText(3));
        }

        [TestMethod]
        public void CheckButtonText_NumberOfCheckedComputers()
        {
            Localization.Initialize(Localization.English);
            Assert.AreEqual("Check", ComputerGrid.CheckButtonText(0), "none checked: every computer shown");
            Assert.AreEqual("Check (2)", ComputerGrid.CheckButtonText(2));
        }
    }
}
