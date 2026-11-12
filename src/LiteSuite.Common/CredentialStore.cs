// -----------------------------------------------------------------------------
// LiteSuite.Common - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    CredentialStore.cs
// Author:  LiteSuite contributors
// Date:    2026-11-12
// Purpose: Credentials of the LiteSuite tools in the Windows Credential Manager
//          (generic credentials of the current user, target "LiteSuite:<name>"):
//          Windows protects them with the user's DPAPI keys, nothing is written
//          by the tools themselves. Taken from the "Windows Vault" part of
//          LiteTask's CredentialManager, so LiteTask and LiteDeploy can share
//          the same stored accounts.
// -----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace LiteSuite
{
    public sealed class StoredCredential
    {
        public string Name;
        public string UserName;
        public string Password;
    }

    public static class CredentialStore
    {
        public const string Prefix = "LiteSuite:";

        const int CRED_TYPE_GENERIC = 1;
        const int CRED_PERSIST_LOCAL_MACHINE = 2;
        const int ERROR_NOT_FOUND = 1168;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct CREDENTIAL
        {
            public uint Flags;
            public uint Type;
            public string TargetName;
            public string Comment;
            public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
            public uint CredentialBlobSize;
            public IntPtr CredentialBlob;
            public uint Persist;
            public uint AttributeCount;
            public IntPtr Attributes;
            public string TargetAlias;
            public string UserName;
        }

        [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool CredWrite(ref CREDENTIAL credential, uint flags);

        [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool CredRead(string target, int type, int flags, out IntPtr credential);

        [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool CredDelete(string target, int type, int flags);

        [DllImport("advapi32.dll", EntryPoint = "CredEnumerateW", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool CredEnumerate(string filter, int flags, out int count, out IntPtr credentials);

        [DllImport("advapi32.dll")]
        static extern void CredFree(IntPtr buffer);

        /// <summary>A credential name: letters, digits, spaces and . - _ @ (64 at most).</summary>
        public static bool IsValidName(string name)
        {
            return !string.IsNullOrWhiteSpace(name) && name.Length <= 64 && name.Trim() == name
                && name.All(c => char.IsLetterOrDigit(c) || c == ' ' || c == '.' || c == '-' || c == '_' || c == '@');
        }

        /// <summary>Creates or replaces a credential.</summary>
        public static void Save(string name, string userName, string password)
        {
            if (!IsValidName(name)) throw new ArgumentException("Invalid credential name.", "name");
            if (string.IsNullOrWhiteSpace(userName)) throw new ArgumentException("A user name is required.", "userName");
            byte[] blob = Encoding.Unicode.GetBytes(password ?? string.Empty);
            IntPtr pBlob = Marshal.AllocHGlobal(Math.Max(1, blob.Length));
            try
            {
                Marshal.Copy(blob, 0, pBlob, blob.Length);
                CREDENTIAL c = new CREDENTIAL
                {
                    Type = CRED_TYPE_GENERIC,
                    TargetName = Prefix + name,
                    Comment = "LiteSuite",
                    CredentialBlobSize = (uint)blob.Length,
                    CredentialBlob = pBlob,
                    Persist = CRED_PERSIST_LOCAL_MACHINE,
                    UserName = userName.Trim()
                };
                if (!CredWrite(ref c, 0)) throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            finally
            {
                // The password does not stay in unmanaged memory
                for (int i = 0; i < blob.Length; i++) Marshal.WriteByte(pBlob, i, 0);
                Array.Clear(blob, 0, blob.Length);
                Marshal.FreeHGlobal(pBlob);
            }
        }

        /// <summary>The credential, or null when it does not exist.</summary>
        public static StoredCredential Read(string name)
        {
            IntPtr p;
            if (!CredRead(Prefix + name, CRED_TYPE_GENERIC, 0, out p))
            {
                int error = Marshal.GetLastWin32Error();
                if (error == ERROR_NOT_FOUND) return null;
                throw new Win32Exception(error);
            }
            try
            {
                CREDENTIAL c = (CREDENTIAL)Marshal.PtrToStructure(p, typeof(CREDENTIAL));
                string password = c.CredentialBlobSize == 0 ? string.Empty
                    : Marshal.PtrToStringUni(c.CredentialBlob, (int)c.CredentialBlobSize / 2);
                return new StoredCredential { Name = name, UserName = c.UserName, Password = password };
            }
            finally
            {
                CredFree(p);
            }
        }

        /// <summary>Deletes a credential; false when it did not exist.</summary>
        public static bool Delete(string name)
        {
            if (CredDelete(Prefix + name, CRED_TYPE_GENERIC, 0)) return true;
            int error = Marshal.GetLastWin32Error();
            if (error == ERROR_NOT_FOUND) return false;
            throw new Win32Exception(error);
        }

        /// <summary>Names and user names of the LiteSuite credentials (no password).</summary>
        public static List<StoredCredential> List()
        {
            List<StoredCredential> list = new List<StoredCredential>();
            int count;
            IntPtr p;
            if (!CredEnumerate(Prefix + "*", 0, out count, out p))
            {
                int error = Marshal.GetLastWin32Error();
                if (error == ERROR_NOT_FOUND) return list;
                throw new Win32Exception(error);
            }
            try
            {
                for (int i = 0; i < count; i++)
                {
                    IntPtr item = Marshal.ReadIntPtr(p, i * IntPtr.Size);
                    CREDENTIAL c = (CREDENTIAL)Marshal.PtrToStructure(item, typeof(CREDENTIAL));
                    if (c.Type == CRED_TYPE_GENERIC && c.TargetName != null && c.TargetName.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
                        list.Add(new StoredCredential { Name = c.TargetName.Substring(Prefix.Length), UserName = c.UserName });
                }
            }
            finally
            {
                CredFree(p);
            }
            return list.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }
    }
}
