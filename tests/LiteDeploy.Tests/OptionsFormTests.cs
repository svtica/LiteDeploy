// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    OptionsFormTests.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-23
// Purpose: Unit tests of the height of the Options window on small screens
//          (the settings scroll, Save and Cancel stay visible).
// -----------------------------------------------------------------------------
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LiteDeploy.Tests
{
    [TestClass]
    public class OptionsFormTests
    {
        [TestMethod]
        public void FittingHeight_ShorterThanTheScreenOnly()
        {
            Assert.AreEqual(850, OptionsForm.FittingHeight(850, 1040), "fits: unchanged");
            Assert.AreEqual(712, OptionsForm.FittingHeight(850, 728), "1366x768 with a task bar: shortened, margin kept");
            Assert.AreEqual(200, OptionsForm.FittingHeight(850, 100), "never below a usable height");
        }
    }
}
