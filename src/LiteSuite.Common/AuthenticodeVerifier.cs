// -----------------------------------------------------------------------------
// LiteSuite.Common - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    AuthenticodeVerifier.cs
// Author:  LiteSuite contributors
// Date:    2026-11-12
// Purpose: Authenticode verification of a file (executable, installer, script),
//          ported from LiteTask. WinVerifyTrust confirms that the embedded
//          signature matches the file content, then the signer certificate is
//          read so the caller can pin the publisher by thumbprint or check its
//          subject.
//
//          A self-signed publisher certificate (LiteSuite releases) ends in a
//          root that is not in the machine store: CERT_E_UNTRUSTEDROOT is
//          reported as ValidUntrustedRoot (the digest was verified) and the
//          identity check is left to the caller.
// -----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;

namespace LiteSuite
{
    public enum AuthenticodeStatus
    {
        /// <summary>Signature matches the file and the chain ends in a trusted root.</summary>
        Valid,
        /// <summary>Signature matches the file, but the chain ends in a root that is not trusted on this computer.</summary>
        ValidUntrustedRoot,
        /// <summary>The file carries no Authenticode signature.</summary>
        NotSigned,
        /// <summary>The signature does not match the file content (tampered or corrupted).</summary>
        HashMismatch,
        /// <summary>Any other failure (expired, revoked, distrusted, chain error, file missing...).</summary>
        Invalid
    }

    public class AuthenticodeResult
    {
        public AuthenticodeStatus Status = AuthenticodeStatus.Invalid;
        /// <summary>HRESULT returned by WinVerifyTrust (0 on success).</summary>
        public int ErrorCode;
        /// <summary>SHA-1 thumbprint of the signer, upper-case hex without separators; null when the signature is not valid.</summary>
        public string SignerThumbprint;
        /// <summary>Distinguished name of the signer; null when the signature is not valid.</summary>
        public string SignerSubject;

        /// <summary>True when the signature was verified against the file content.</summary>
        public bool IsSignatureValid
        {
            get { return Status == AuthenticodeStatus.Valid || Status == AuthenticodeStatus.ValidUntrustedRoot; }
        }

        public override string ToString()
        {
            return string.Format("{0} (0x{1:X8}) signer={2} thumbprint={3}", Status, ErrorCode, SignerSubject ?? "<none>", SignerThumbprint ?? "<none>");
        }
    }

    public static class AuthenticodeVerifier
    {
        const uint WTD_UI_NONE = 2;
        const uint WTD_REVOKE_NONE = 0;
        const uint WTD_CHOICE_FILE = 1;
        const uint WTD_STATEACTION_VERIFY = 1;
        const uint WTD_STATEACTION_CLOSE = 2;
        const uint WTD_REVOCATION_CHECK_NONE = 0x10;
        const uint WTD_UICONTEXT_EXECUTE = 0;

        public const int ERROR_SUCCESS = 0;
        public const int TRUST_E_NOSIGNATURE = unchecked((int)0x800B0100);
        public const int TRUST_E_BAD_DIGEST = unchecked((int)0x80096010);
        public const int CERT_E_UNTRUSTEDROOT = unchecked((int)0x800B0109);

        // WINTRUST_ACTION_GENERIC_VERIFY_V2
        static readonly Guid WintrustActionGenericVerifyV2 = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct WINTRUST_FILE_INFO
        {
            public uint cbStruct;
            [MarshalAs(UnmanagedType.LPWStr)] public string pcwszFilePath;
            public IntPtr hFile;
            public IntPtr pgKnownSubject;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct WINTRUST_DATA
        {
            public uint cbStruct;
            public IntPtr pPolicyCallbackData;
            public IntPtr pSIPClientData;
            public uint dwUIChoice;
            public uint fdwRevocationChecks;
            public uint dwUnionChoice;
            public IntPtr pFile;
            public uint dwStateAction;
            public IntPtr hWVTStateData;
            public IntPtr pwszURLReference;
            public uint dwProvFlags;
            public uint dwUIContext;
            public IntPtr pSignatureSettings;
        }

        [DllImport("wintrust.dll", ExactSpelling = true)]
        static extern int WinVerifyTrust(IntPtr hwnd, ref Guid pgActionID, IntPtr pWVTData);

        /// <summary>
        /// Verifies the Authenticode signature embedded in a file. Never throws: every failure is
        /// reported through <see cref="AuthenticodeResult.Status"/>.
        /// </summary>
        public static AuthenticodeResult Verify(string filePath)
        {
            AuthenticodeResult result = new AuthenticodeResult();

            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                result.Status = AuthenticodeStatus.Invalid;
                result.ErrorCode = TRUST_E_NOSIGNATURE;
                return result;
            }

            try
            {
                result.ErrorCode = CallWinVerifyTrust(Path.GetFullPath(filePath));
            }
            catch (Exception)
            {
                result.Status = AuthenticodeStatus.Invalid;
                return result;
            }

            switch (result.ErrorCode)
            {
                case ERROR_SUCCESS: result.Status = AuthenticodeStatus.Valid; break;
                case CERT_E_UNTRUSTEDROOT: result.Status = AuthenticodeStatus.ValidUntrustedRoot; break;
                case TRUST_E_NOSIGNATURE: result.Status = AuthenticodeStatus.NotSigned; break;
                case TRUST_E_BAD_DIGEST: result.Status = AuthenticodeStatus.HashMismatch; break;
                default: result.Status = AuthenticodeStatus.Invalid; break;
            }

            if (result.IsSignatureValid)
            {
                try
                {
                    // SYSLIB0057 (.NET 9+): X509CertificateLoader has no equivalent that reads the
                    // signer of a signed file, so this call stays on both frameworks
#pragma warning disable SYSLIB0057
                    using (X509Certificate signer = X509Certificate.CreateFromSignedFile(filePath))
#pragma warning restore SYSLIB0057
                    using (X509Certificate2 cert = new X509Certificate2(signer))
                    {
                        result.SignerThumbprint = NormalizeThumbprint(cert.Thumbprint);
                        result.SignerSubject = cert.Subject;
                    }
                }
                catch (Exception)
                {
                    // A verified signature whose signer cannot be read cannot be pinned: fail closed
                    result.Status = AuthenticodeStatus.Invalid;
                    result.SignerThumbprint = null;
                    result.SignerSubject = null;
                }
            }

            return result;
        }

        /// <summary>Upper-case hex digits only (separators and spaces removed); empty for null.</summary>
        public static string NormalizeThumbprint(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return new string(value.Where(Uri.IsHexDigit).ToArray()).ToUpperInvariant();
        }

        /// <summary>True when <paramref name="thumbprint"/> matches one of <paramref name="expected"/>, after normalization.</summary>
        public static bool ThumbprintMatches(string thumbprint, IEnumerable<string> expected)
        {
            string normalized = NormalizeThumbprint(thumbprint);
            if (normalized.Length == 0 || expected == null) return false;
            return expected.Any(e => NormalizeThumbprint(e) == normalized);
        }

        static int CallWinVerifyTrust(string filePath)
        {
            Guid actionId = WintrustActionGenericVerifyV2;
            IntPtr invalidHandle = new IntPtr(-1);

            WINTRUST_FILE_INFO fileInfo = new WINTRUST_FILE_INFO
            {
                pcwszFilePath = filePath,
                hFile = IntPtr.Zero,
                pgKnownSubject = IntPtr.Zero
            };
            fileInfo.cbStruct = (uint)Marshal.SizeOf(typeof(WINTRUST_FILE_INFO));

            IntPtr pFileInfo = IntPtr.Zero;
            IntPtr pTrustData = IntPtr.Zero;
            try
            {
                pFileInfo = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(WINTRUST_FILE_INFO)));
                Marshal.StructureToPtr(fileInfo, pFileInfo, false);

                WINTRUST_DATA trustData = new WINTRUST_DATA
                {
                    dwUIChoice = WTD_UI_NONE,
                    fdwRevocationChecks = WTD_REVOKE_NONE,
                    dwUnionChoice = WTD_CHOICE_FILE,
                    pFile = pFileInfo,
                    dwStateAction = WTD_STATEACTION_VERIFY,
                    dwProvFlags = WTD_REVOCATION_CHECK_NONE,
                    dwUIContext = WTD_UICONTEXT_EXECUTE
                };
                trustData.cbStruct = (uint)Marshal.SizeOf(typeof(WINTRUST_DATA));

                pTrustData = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(WINTRUST_DATA)));
                Marshal.StructureToPtr(trustData, pTrustData, false);

                int status = WinVerifyTrust(invalidHandle, ref actionId, pTrustData);

                // Release the state data allocated by the verify call
                trustData = (WINTRUST_DATA)Marshal.PtrToStructure(pTrustData, typeof(WINTRUST_DATA));
                trustData.dwStateAction = WTD_STATEACTION_CLOSE;
                Marshal.StructureToPtr(trustData, pTrustData, false);
                WinVerifyTrust(invalidHandle, ref actionId, pTrustData);

                return status;
            }
            finally
            {
                if (pFileInfo != IntPtr.Zero)
                {
                    Marshal.DestroyStructure(pFileInfo, typeof(WINTRUST_FILE_INFO));
                    Marshal.FreeHGlobal(pFileInfo);
                }
                if (pTrustData != IntPtr.Zero)
                    Marshal.FreeHGlobal(pTrustData);
            }
        }
    }

    /// <summary>Outcome of <see cref="UpdateSignaturePolicy.Evaluate"/>.</summary>
    public enum UpdateSignatureVerdict
    {
        /// <summary>Signed by an expected publisher: apply it.</summary>
        Accepted,
        /// <summary>Unsigned, but so is the running executable (development build): apply it with a warning.</summary>
        AcceptedUnsignedDevBuild,
        /// <summary>Unsigned while the running executable is signed: refuse it.</summary>
        RejectedUnsigned,
        /// <summary>The signature does not match the content: refuse it.</summary>
        RejectedTampered,
        /// <summary>Validly signed, but not by an expected publisher: refuse it.</summary>
        RejectedPublisherMismatch,
        /// <summary>The signature could not be validated for another reason: refuse it.</summary>
        RejectedInvalidSignature
    }

    /// <summary>
    /// Decides whether an update package may be applied, from the Authenticode result of the package,
    /// that of the running executable and the expected publisher thumbprints. Ported from LiteTask.
    /// </summary>
    public static class UpdateSignaturePolicy
    {
        public static UpdateSignatureVerdict Evaluate(AuthenticodeResult package, AuthenticodeResult running, IEnumerable<string> expectedThumbprints)
        {
            if (package == null) return UpdateSignatureVerdict.RejectedInvalidSignature;

            if (package.IsSignatureValid)
            {
                return AuthenticodeVerifier.ThumbprintMatches(package.SignerThumbprint, expectedThumbprints)
                    ? UpdateSignatureVerdict.Accepted
                    : UpdateSignatureVerdict.RejectedPublisherMismatch;
            }

            switch (package.Status)
            {
                case AuthenticodeStatus.NotSigned:
                    // Only an unsigned (development) build may install an unsigned package; when the
                    // running executable cannot be assessed, fail closed
                    return running != null && running.Status == AuthenticodeStatus.NotSigned
                        ? UpdateSignatureVerdict.AcceptedUnsignedDevBuild
                        : UpdateSignatureVerdict.RejectedUnsigned;
                case AuthenticodeStatus.HashMismatch:
                    return UpdateSignatureVerdict.RejectedTampered;
                default:
                    return UpdateSignatureVerdict.RejectedInvalidSignature;
            }
        }

        /// <summary>The pinned release certificate plus, when the running executable is validly signed, its own signer.</summary>
        public static List<string> ExpectedThumbprints(string pinnedThumbprint, AuthenticodeResult running)
        {
            List<string> list = new List<string>();
            string pinned = AuthenticodeVerifier.NormalizeThumbprint(pinnedThumbprint);
            if (pinned.Length > 0) list.Add(pinned);
            if (running != null && running.IsSignatureValid)
            {
                string own = AuthenticodeVerifier.NormalizeThumbprint(running.SignerThumbprint);
                if (own.Length > 0 && !list.Contains(own)) list.Add(own);
            }
            return list;
        }
    }
}
