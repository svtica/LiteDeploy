// -----------------------------------------------------------------------------
// LiteSuite.Common - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    AuthenticodeTests.cs
// Author:  LiteSuite contributors
// Date:    2026-11-12
// Purpose: Unit tests of UpdateSignaturePolicy (ported from LiteTask) and of
//          AuthenticodeVerifier on files without a signature.
// -----------------------------------------------------------------------------
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LiteSuite.Tests
{
    [TestClass]
    public class AuthenticodeTests
    {
        const string Pinned = "D21B5D39EA4C7BDC69C94C86FC4602E8A1CA15B4";
        const string Other = "0123456789ABCDEF0123456789ABCDEF01234567";

        static AuthenticodeResult Signed(string thumbprint, AuthenticodeStatus status = AuthenticodeStatus.ValidUntrustedRoot)
        {
            return new AuthenticodeResult { Status = status, SignerThumbprint = thumbprint, SignerSubject = "CN=Test" };
        }

        static AuthenticodeResult WithStatus(AuthenticodeStatus status)
        {
            return new AuthenticodeResult { Status = status, ErrorCode = AuthenticodeVerifier.TRUST_E_NOSIGNATURE };
        }

        [TestMethod]
        public void Evaluate_SignedByPinnedPublisher_Accepted()
        {
            Assert.AreEqual(UpdateSignatureVerdict.Accepted, UpdateSignaturePolicy.Evaluate(Signed(Pinned), Signed(Pinned), new[] { Pinned }));
            Assert.AreEqual(UpdateSignatureVerdict.Accepted, UpdateSignaturePolicy.Evaluate(Signed(Pinned, AuthenticodeStatus.Valid), null, new[] { Pinned }));
        }

        [TestMethod]
        public void Evaluate_ThumbprintComparisonIgnoresCaseAndSeparators()
        {
            Assert.AreEqual(UpdateSignatureVerdict.Accepted,
                UpdateSignaturePolicy.Evaluate(Signed("d2:1b:5d:39:ea:4c:7b:dc:69:c9:4c:86:fc:46:02:e8:a1:ca:15:b4"), null, new[] { Pinned }));
        }

        [TestMethod]
        public void Evaluate_SignedByOtherPublisher_Rejected_EvenOnDevBuild()
        {
            Assert.AreEqual(UpdateSignatureVerdict.RejectedPublisherMismatch, UpdateSignaturePolicy.Evaluate(Signed(Other), Signed(Pinned), new[] { Pinned }));
            Assert.AreEqual(UpdateSignatureVerdict.RejectedPublisherMismatch, UpdateSignaturePolicy.Evaluate(Signed(Other), WithStatus(AuthenticodeStatus.NotSigned), new[] { Pinned }));
        }

        [TestMethod]
        public void Evaluate_Unsigned_RejectedUnlessRunningBuildIsUnsigned()
        {
            Assert.AreEqual(UpdateSignatureVerdict.RejectedUnsigned, UpdateSignaturePolicy.Evaluate(WithStatus(AuthenticodeStatus.NotSigned), Signed(Pinned), new[] { Pinned }));
            Assert.AreEqual(UpdateSignatureVerdict.RejectedUnsigned, UpdateSignaturePolicy.Evaluate(WithStatus(AuthenticodeStatus.NotSigned), null, new[] { Pinned }));
            Assert.AreEqual(UpdateSignatureVerdict.AcceptedUnsignedDevBuild,
                UpdateSignaturePolicy.Evaluate(WithStatus(AuthenticodeStatus.NotSigned), WithStatus(AuthenticodeStatus.NotSigned), new[] { Pinned }));
        }

        [TestMethod]
        public void Evaluate_TamperedOrInvalidOrMissing_Rejected()
        {
            Assert.AreEqual(UpdateSignatureVerdict.RejectedTampered,
                UpdateSignaturePolicy.Evaluate(WithStatus(AuthenticodeStatus.HashMismatch), WithStatus(AuthenticodeStatus.NotSigned), new[] { Pinned }));
            Assert.AreEqual(UpdateSignatureVerdict.RejectedInvalidSignature, UpdateSignaturePolicy.Evaluate(WithStatus(AuthenticodeStatus.Invalid), Signed(Pinned), new[] { Pinned }));
            Assert.AreEqual(UpdateSignatureVerdict.RejectedInvalidSignature, UpdateSignaturePolicy.Evaluate(null, Signed(Pinned), new[] { Pinned }));
        }

        [TestMethod]
        public void ExpectedThumbprints_PinnedPlusValidRunningSigner()
        {
            CollectionAssert.AreEquivalent(new[] { Pinned, Other }, UpdateSignaturePolicy.ExpectedThumbprints(Pinned, Signed(Other)));
            CollectionAssert.AreEquivalent(new[] { Pinned }, UpdateSignaturePolicy.ExpectedThumbprints(Pinned, Signed(Other, AuthenticodeStatus.HashMismatch)));
            CollectionAssert.AreEquivalent(new[] { Pinned }, UpdateSignaturePolicy.ExpectedThumbprints(Pinned, Signed(Pinned.ToLowerInvariant())));
        }

        [TestMethod]
        public void NormalizeAndMatchThumbprints()
        {
            Assert.AreEqual("ABCDEF01", AuthenticodeVerifier.NormalizeThumbprint(" ab:cd ef-01 "));
            Assert.AreEqual(string.Empty, AuthenticodeVerifier.NormalizeThumbprint(null));
            Assert.IsFalse(AuthenticodeVerifier.ThumbprintMatches(null, new[] { Pinned }));
            Assert.IsFalse(AuthenticodeVerifier.ThumbprintMatches(Pinned, null));
            Assert.IsTrue(AuthenticodeVerifier.ThumbprintMatches(Pinned, new[] { Other, Pinned }));
        }

        [TestMethod]
        public void Verify_UnsignedFile_IsNotSigned_MissingFile_IsInvalid()
        {
            string path = Path.Combine(Path.GetTempPath(), "litesuite-unsigned-" + System.Guid.NewGuid().ToString("N") + ".ps1");
            File.WriteAllText(path, "Write-Output 'hello'\r\n");
            try
            {
                AuthenticodeResult result = AuthenticodeVerifier.Verify(path);
                Assert.AreEqual(AuthenticodeStatus.NotSigned, result.Status, result.ToString());
                Assert.IsNull(result.SignerThumbprint);
            }
            finally
            {
                File.Delete(path);
            }

            Assert.AreEqual(AuthenticodeStatus.Invalid, AuthenticodeVerifier.Verify(path).Status);
            Assert.AreEqual(AuthenticodeStatus.Invalid, AuthenticodeVerifier.Verify(null).Status);
        }
    }
}
