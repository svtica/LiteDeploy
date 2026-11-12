// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    PsExecDownloaderTests.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-12
// Purpose: Unit tests of the signature rule of the PsExec download: only a
//          valid, trusted signature of Microsoft Corporation is accepted.
// -----------------------------------------------------------------------------
using LiteSuite;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LiteDeploy.Tests
{
    [TestClass]
    public class PsExecDownloaderTests
    {
        const string Microsoft = "CN=Microsoft Corporation, O=Microsoft Corporation, L=Redmond, S=Washington, C=US";

        static AuthenticodeResult Result(AuthenticodeStatus status, string subject)
        {
            return new AuthenticodeResult { Status = status, SignerSubject = subject, SignerThumbprint = "AB" };
        }

        [TestMethod]
        public void MicrosoftSignature_TrustedRoot_Accepted()
        {
            Assert.IsTrue(PsExecDownloader.IsTrustedPublisher(Result(AuthenticodeStatus.Valid, Microsoft)));
        }

        [TestMethod]
        public void UntrustedRootOrOtherStatus_Refused()
        {
            Assert.IsFalse(PsExecDownloader.IsTrustedPublisher(Result(AuthenticodeStatus.ValidUntrustedRoot, Microsoft)));
            Assert.IsFalse(PsExecDownloader.IsTrustedPublisher(Result(AuthenticodeStatus.NotSigned, null)));
            Assert.IsFalse(PsExecDownloader.IsTrustedPublisher(Result(AuthenticodeStatus.HashMismatch, Microsoft)));
            Assert.IsFalse(PsExecDownloader.IsTrustedPublisher(null));
        }

        [TestMethod]
        public void OtherPublisher_Refused_EvenWithMicrosoftInTheName()
        {
            Assert.IsFalse(PsExecDownloader.IsTrustedPublisher(Result(AuthenticodeStatus.Valid, "CN=Microsoft Corporation, O=Evil Corp, C=US")));
            Assert.IsFalse(PsExecDownloader.IsTrustedPublisher(Result(AuthenticodeStatus.Valid, "CN=Contoso, O=Microsoft Corporation Fake, C=US")));
        }

        [TestMethod]
        public void ToolDownloads_OnlyGitHubReleaseLinksOverHttps()
        {
            Assert.IsTrue(ToolManager.IsSafeDownloadUrl("https://github.com/svtica/LitePM/releases/download/v1.2.0/LitePM.zip"));
            Assert.IsFalse(ToolManager.IsSafeDownloadUrl("http://github.com/svtica/LitePM/releases/download/v1.2.0/LitePM.zip"));
            Assert.IsFalse(ToolManager.IsSafeDownloadUrl("https://evil.example/svtica/LitePM/releases/download/v1/LitePM.zip"));
            Assert.IsFalse(ToolManager.IsSafeDownloadUrl("https://github.com/svtica/LitePM/archive/main.zip"));
            Assert.IsFalse(ToolManager.IsSafeDownloadUrl(null));
        }
    }
}
