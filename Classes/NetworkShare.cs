// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    NetworkShare.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-12
// Purpose: Connection to a share of a computer (\\computer\ADMIN$) with a
//          stored credential, for the copies LiteDeploy makes there (update
//          modules, logs). Without credential, the current user is used.
// -----------------------------------------------------------------------------
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using LiteSuite;

namespace LiteDeploy
{
    internal sealed class NetworkShare : IDisposable
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        class NETRESOURCE
        {
            public int Scope;
            public int Type = 1;            // RESOURCETYPE_DISK
            public int DisplayType;
            public int Usage;
            public string LocalName;
            public string RemoteName;
            public string Comment;
            public string Provider;
        }

        [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
        static extern int WNetAddConnection2(NETRESOURCE resource, string password, string userName, int flags);

        [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
        static extern int WNetCancelConnection2(string name, int flags, bool force);

        const int ERROR_SESSION_CREDENTIAL_CONFLICT = 1219;

        readonly string share;
        readonly bool connected;

        /// <summary>Connects to <paramref name="share"/> with the credential (null: current user, nothing to do).</summary>
        public NetworkShare(string share, StoredCredential credential)
        {
            this.share = share;
            if (credential == null) return;
            int error = WNetAddConnection2(new NETRESOURCE { RemoteName = share }, credential.Password, credential.UserName, 0);
            // Already connected with other credentials (an Explorer window, for example): use that connection
            if (error == ERROR_SESSION_CREDENTIAL_CONFLICT) return;
            if (error != 0) throw new Win32Exception(error);
            connected = true;
        }

        public void Dispose()
        {
            if (connected) WNetCancelConnection2(share, 0, true);
        }
    }
}
