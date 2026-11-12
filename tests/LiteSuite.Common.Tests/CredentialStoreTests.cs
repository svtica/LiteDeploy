// -----------------------------------------------------------------------------
// LiteSuite.Common - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    CredentialStoreTests.cs
// Author:  LiteSuite contributors
// Date:    2026-11-12
// Purpose: Unit tests of CredentialStore: names, and a real round trip in the
//          Windows Credential Manager of the test account (cleaned up).
// -----------------------------------------------------------------------------
using System;
using System.ComponentModel;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LiteSuite.Tests
{
    [TestClass]
    public class CredentialStoreTests
    {
        [TestMethod]
        public void Names()
        {
            Assert.IsTrue(CredentialStore.IsValidName("Compte deploiement"));
            Assert.IsTrue(CredentialStore.IsValidName("svc_deploy@domain"));
            Assert.IsFalse(CredentialStore.IsValidName(""));
            Assert.IsFalse(CredentialStore.IsValidName(" x"));
            Assert.IsFalse(CredentialStore.IsValidName("a:b"));
            Assert.IsFalse(CredentialStore.IsValidName(new string('a', 65)));
        }

        [TestMethod]
        public void SaveReadListDelete_RoundTrip()
        {
            string name = "Test " + Guid.NewGuid().ToString("N").Substring(0, 8);
            try
            {
                CredentialStore.Save(name, @"DOMAIN\user", "p@ss w\"ord");
            }
            catch (Win32Exception ex)
            {
                Assert.Inconclusive("Credential Manager not available for this account: " + ex.Message);
            }
            try
            {
                StoredCredential c = CredentialStore.Read(name);
                Assert.AreEqual(@"DOMAIN\user", c.UserName);
                Assert.AreEqual("p@ss w\"ord", c.Password);
                Assert.IsTrue(CredentialStore.List().Any(x => x.Name == name && x.Password == null));
            }
            finally
            {
                Assert.IsTrue(CredentialStore.Delete(name));
            }
            Assert.IsNull(CredentialStore.Read(name));
            Assert.IsFalse(CredentialStore.Delete(name));
        }
    }
}
