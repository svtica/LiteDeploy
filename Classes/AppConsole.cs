// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    AppConsole.cs
// Author:  LiteDeploy contributors
// Date:    2025-05-30
// Purpose: Console of the main window: the last 500 timestamped messages,
//          newest first, password masked.
// -----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace LiteDeploy
{
    class AppConsole
    {
        // Maximum number of messages kept in the console. Older messages are
        // dropped: the console used to keep (and re-render) every message
        // since start-up, which grew without bound over a long session.
        public const int MaxMessages = 500;

        // UI Black Box
        static RichTextBox display;
        static public MainForm parent;
        static readonly LinkedList<String> messages = new LinkedList<String>();   // newest first
        static readonly Object messagesLock = new Object();
        static List < String > que = new List<String>();
        static List <DateTime> queTime = new List<DateTime>();

        static public void  Init(RichTextBox d, MainForm p)
        {
            display = d;
            parent = p;

            List<String> queued = new List<String>();

            lock (messagesLock)
            {
                for (int i = 0; i < que.Count; i++)
                    queued.Add(queTime[i].ToShortDateString() + " " + queTime[i].ToLongTimeString() + "> " + que[i]);

                que.Clear();
                queTime.Clear();
            }

            // Masking reads controls of the main window: never inside the lock
            // (the UI thread may be waiting for it in BuildText).
            for (int i = 0; i < queued.Count; i++)
                queued[i] = Mask(queued[i]);

            lock (messagesLock)
            {
                foreach (String message in queued)
                    AddMessage(message);
            }

            WriteToDisplay();
        }

        static public void WriteLine(String s)
        {
            if (parent != null)
            {
                s = Mask(DateTime.Now.ToShortDateString() + " " + DateTime.Now.ToLongTimeString() + "> " + s);

                lock (messagesLock)
                {
                    AddMessage(s);
                }

                WriteToDisplay();
            }

            else
            {
                lock (messagesLock)
                {
                    que.Add(s);
                    queTime.Add(DateTime.Now);
                }
            }
        }

        static public void Clear()
        {
            lock (messagesLock)
            {
                messages.Clear();
            }

            WriteToDisplay();
        }

        // Caller holds messagesLock.
        static private void AddMessage(String message)
        {
            messages.AddFirst(message);

            while (messages.Count > MaxMessages)
                messages.RemoveLast();
        }

        static private String Mask(String message)
        {
            // Never show the password typed in the main window.
            if (parent != null && parent.credentials.Checked && parent.password.Text.Length > 0)
                return message.Replace(parent.password.Text, "******");

            return message;
        }

        static private String BuildText()
        {
            lock (messagesLock)
            {
                StringBuilder sb = new StringBuilder();

                foreach (String m in messages)
                {
                    sb.Append(m);
                    sb.Append("\n\n");
                }

                return sb.ToString();
            }
        }

        private delegate void WriteToDisplayDel();
        static private void WriteToDisplay()
        {
            if (display == null)
                return;

            if (display.InvokeRequired)
            {
                WriteToDisplayDel a = new WriteToDisplayDel(WriteToDisplay);
                parent.Invoke(a);
            }

            else
            {
                try
                {
                    display.Text = BuildText();
                    display.Select(display.Text.Length, 0);
                }
                catch
                {
                    // The display is not available (e.g. during shutdown):
                    // the messages stay in the bounded list.
                }
            }
        }
    }
}
