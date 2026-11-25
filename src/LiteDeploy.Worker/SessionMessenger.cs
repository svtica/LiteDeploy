// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    SessionMessenger.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-18
// Purpose: "message": a message box in every active session of the computer
//          that has a logged-on user, through WTSSendMessage (wtsapi32.dll,
//          present on every edition of Windows, unlike msg.exe which Home
//          editions lack). The worker runs as SYSTEM, in session 0, and does not
//          wait for the users: the box closes by itself after the display time
//          (0: when the user closes it). Which sessions get the message is pure
//          logic (Targets, unit-tested).
// -----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;

namespace LiteDeploy.Worker
{
    /// <summary>One session of the computer, as WTSEnumerateSessions lists it.</summary>
    internal sealed class SessionEntry
    {
        public int Id;
        public bool Active;
        /// <summary>The logged-on user, with its domain when there is one; empty: nobody.</summary>
        public string User;
    }

    /// <summary>What the message became on one computer.</summary>
    internal sealed class MessageOutcome
    {
        public List<string> Users = new List<string>();
        public int Sent;
        public List<string> Errors = new List<string>();
    }

    internal static class SessionMessenger
    {
        const int WtsActive = 0;
        const int WtsUserName = 5;
        const int WtsDomainName = 7;
        const int MbOk = 0x0, MbIconInformation = 0x40, MbSetForeground = 0x10000, MbTopMost = 0x40000;
        const int MbOkCancel = 0x1, MbIconWarning = 0x30;
        /// <summary>Answers of WTSSendMessage with wait: Cancel (the user postpones).</summary>
        const int IdCancel = 2;
        /// <summary>Between the domain and the user name.</summary>
        const char Backslash = (char)0x5C;

        [StructLayout(LayoutKind.Sequential)]
        struct WTS_SESSION_INFO
        {
            public int SessionId;
            public IntPtr WinStationName;
            public int State;
        }

        [DllImport("wtsapi32.dll", EntryPoint = "WTSEnumerateSessionsW", SetLastError = true)]
        static extern bool WTSEnumerateSessions(IntPtr server, int reserved, int version, out IntPtr sessionInfo, out int count);

        [DllImport("wtsapi32.dll", EntryPoint = "WTSQuerySessionInformationW", SetLastError = true)]
        static extern bool WTSQuerySessionInformation(IntPtr server, int sessionId, int infoClass, out IntPtr buffer, out int bytes);

        [DllImport("wtsapi32.dll", EntryPoint = "WTSSendMessageW", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern bool WTSSendMessage(IntPtr server, int sessionId, string title, int titleBytes, string message, int messageBytes,
            int style, int timeoutSeconds, out int response, bool wait);

        [DllImport("wtsapi32.dll")]
        static extern void WTSFreeMemory(IntPtr memory);

        /// <summary>
        /// The sessions that get the message: active, with a logged-on user, never session 0 (services), each
        /// session once. A disconnected session shows nothing to anybody: left out.
        /// </summary>
        public static List<SessionEntry> Targets(IEnumerable<SessionEntry> sessions)
        {
            return (sessions ?? Enumerable.Empty<SessionEntry>())
                .Where(s => s != null && s.Id != 0 && s.Active && !string.IsNullOrWhiteSpace(s.User))
                .GroupBy(s => s.Id).Select(g => g.First())
                .OrderBy(s => s.Id).ToList();
        }

        /// <summary>The sessions of this computer (WTSEnumerateSessions); Win32Exception when they cannot be listed.</summary>
        public static List<SessionEntry> Sessions()
        {
            IntPtr list;
            int count;
            if (!WTSEnumerateSessions(IntPtr.Zero, 0, 1, out list, out count)) throw new Win32Exception(Marshal.GetLastWin32Error());
            List<SessionEntry> result = new List<SessionEntry>();
            try
            {
                int size = Marshal.SizeOf(typeof(WTS_SESSION_INFO));
                for (int i = 0; i < count; i++)
                {
                    WTS_SESSION_INFO info = (WTS_SESSION_INFO)Marshal.PtrToStructure(new IntPtr(list.ToInt64() + (long)i * size), typeof(WTS_SESSION_INFO));
                    string user = Query(info.SessionId, WtsUserName);
                    string domain = Query(info.SessionId, WtsDomainName);
                    result.Add(new SessionEntry
                    {
                        Id = info.SessionId,
                        Active = info.State == WtsActive,
                        User = string.IsNullOrEmpty(user) ? "" : (string.IsNullOrEmpty(domain) ? user : domain + Backslash + user)
                    });
                }
            }
            finally
            {
                WTSFreeMemory(list);
            }
            return result;
        }

        static string Query(int sessionId, int infoClass)
        {
            IntPtr buffer;
            int bytes;
            if (!WTSQuerySessionInformation(IntPtr.Zero, sessionId, infoClass, out buffer, out bytes)) return null;
            try { return Marshal.PtrToStringUni(buffer); }
            finally { WTSFreeMemory(buffer); }
        }

        /// <summary>
        /// Before an update closes applications: the message in each active session among <paramref name="sessionIds"/>
        /// (the sessions that run one of them), all at once, waiting for every answer or the end of the delay.
        /// Returns the users who chose Cancel (postpone, only when <paramref name="allowPostpone"/>); empty: go on
        /// (OK, no answer, nobody to warn, or the message could not be shown).
        /// </summary>
        public static List<string> AskBeforeClosing(IEnumerable<int> sessionIds, string title, string text, int seconds, bool allowPostpone)
        {
            List<string> postponedBy = new List<string>();
            List<SessionEntry> targets;
            try
            {
                HashSet<int> wanted = new HashSet<int>(sessionIds ?? Enumerable.Empty<int>());
                targets = Targets(Sessions()).Where(s => wanted.Contains(s.Id)).ToList();
            }
            catch (Exception) { return postponedBy; }
            string body = text.Replace("\n", "\r\n");
            int style = (allowPostpone ? MbOkCancel : MbOk) | MbIconWarning | MbSetForeground | MbTopMost;
            List<System.Threading.Thread> threads = new List<System.Threading.Thread>();
            foreach (SessionEntry s in targets)
            {
                SessionEntry session = s;
                System.Threading.Thread t = new System.Threading.Thread(() =>
                {
                    int response;
                    if (WTSSendMessage(IntPtr.Zero, session.Id, title, title.Length * 2, body, body.Length * 2, style, seconds, out response, true)
                        && allowPostpone && response == IdCancel)
                        lock (postponedBy) postponedBy.Add(session.User);
                }) { IsBackground = true, Name = "Close warning" };
                t.Start();
                threads.Add(t);
            }
            // The message closes itself at the end of the delay; a little more for the answer to come back
            foreach (System.Threading.Thread t in threads) t.Join(TimeSpan.FromSeconds(seconds + 30));
            lock (postponedBy) return postponedBy.ToList();
        }

        /// <summary>Sends the message to every target session, without waiting for the users.</summary>
        public static MessageOutcome Send(string title, string text, int seconds)
        {
            MessageOutcome outcome = new MessageOutcome();
            List<SessionEntry> targets;
            try { targets = Targets(Sessions()); }
            catch (Exception ex)
            {
                outcome.Errors.Add("Sessions not listed: " + ex.Message);
                return outcome;
            }
            // MessageBox line breaks
            string body = text.Replace("\n", "\r\n");
            foreach (SessionEntry s in targets)
            {
                outcome.Users.Add(s.User);
                int response;
                if (WTSSendMessage(IntPtr.Zero, s.Id, title, title.Length * 2, body, body.Length * 2,
                        MbOk | MbIconInformation | MbSetForeground | MbTopMost, seconds, out response, false))
                    outcome.Sent++;
                else
                    outcome.Errors.Add("Session " + s.Id + ": " + new Win32Exception(Marshal.GetLastWin32Error()).Message);
            }
            return outcome;
        }
    }
}
