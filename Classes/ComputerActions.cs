// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    ComputerActions.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-18
// Purpose: Actions on computers of the grid of the main window
//          (IComputerActionHost, ComputerGrid): computer card, TeamViewer
//          ID, remote desktop (one window per computer), command prompt on
//          the computer (RemoteConsole), quick commands (QuickCommands, run
//          as SYSTEM by the worker), a message to the logged-on users
//          (UserMessage, worker "message"; also "Ask the users to restart"
//          in the quick commands), Wake-on-LAN, logs, pause and "outside
//          business hours only" flags (updates.xml), export to Royal TS
//          (RoyalTsExport), SIW report (SiwReports), reports of the portable
//          tools (PortableToolReports). Cards, quick commands, messages and
//          reports run in a progress window (RemoteRunForm). Targets: the checked computers,
//          else the selected one(s) (Targets, unit-tested).
// -----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace LiteDeploy
{
    /// <summary>The window an action runs from: its owner, the update state store, what is known of a computer.</summary>
    internal interface IComputerActionHost
    {
        Form Owner { get; }
        /// <summary>No check, update or install in progress (pause and flags are changed only then).</summary>
        bool Idle { get; }
        /// <summary>Runs <paramref name="action"/> on the update state store, under its lock; <paramref name="save"/>: written when changed (errors are thrown).</summary>
        void WithStore(Action<UpdateStateStore> action, bool save);
        /// <summary>What is known of a computer (Active Directory, saved state); null when nothing.</summary>
        ComputerRow Row(String computer);
        /// <summary>A status line; null: the idle status of the window.</summary>
        void SetStatus(String text);
        /// <summary>The store changed (UI thread): refresh what shows it.</summary>
        void StoreChanged();
    }

    internal enum MessageResultKind
    {
        /// <summary>Shown in at least one session.</summary>
        Sent,
        /// <summary>Reached the computer, but nobody is logged on (or every session is disconnected).</summary>
        NoUser,
        /// <summary>Not shown: the computer could not be reached, or no session could get it.</summary>
        Failed
    }

    /// <summary>What the worker said of a message on one computer (its type=message line).</summary>
    internal sealed class MessageResult
    {
        public MessageResultKind Kind;
        public int Sessions;
        public String Users = "";
        public String Error = "";

        /// <summary>From the worker's messages, or the failure of the run when it wrote nothing.</summary>
        public static MessageResult From(IList<WorkerMessage> messages, String failure)
        {
            WorkerMessage line = (messages ?? new List<WorkerMessage>()).FirstOrDefault(m => m.Type == UserMessage.TypeResult);
            if (line == null)
            {
                WorkerMessage error = (messages ?? new List<WorkerMessage>()).FirstOrDefault(m => m.Type == WorkerProtocol.TypeError);
                return new MessageResult { Kind = MessageResultKind.Failed, Error = error != null ? error["message"] ?? "" : failure ?? "" };
            }
            int sessions, failed;
            Int32.TryParse(line["sessions"], out sessions);
            Int32.TryParse(line["failed"], out failed);
            return new MessageResult
            {
                Kind = sessions > 0 ? MessageResultKind.Sent : failed > 0 ? MessageResultKind.Failed : MessageResultKind.NoUser,
                Sessions = sessions,
                Users = line["users"] ?? "",
                Error = line["error"] ?? ""
            };
        }
    }

    internal sealed class ComputerActions
    {
        /// <summary>Computers read at the same time (cards, TeamViewer IDs, quick commands).</summary>
        const int Parallelism = 4;

        /// <summary>Windows (remote desktop, command prompt) opened without a question; more: confirmation.</summary>
        public const int MaxWindowsWithoutQuestion = 5;

        readonly IComputerActionHost host;

        public ComputerActions(IComputerActionHost host)
        {
            this.host = host;
        }

        Form Owner { get { return host.Owner; } }

        /// <summary>
        /// The computers an action applies to: the checked ones, else the selected ones; duplicates (the same
        /// computer under two spellings: ComputerName) and empty names removed, sorted by name.
        /// </summary>
        public static List<String> Targets(IEnumerable<String> checkedNames, IEnumerable<String> selectedNames)
        {
            List<String> source = (checkedNames ?? Enumerable.Empty<String>()).Where(n => ComputerName.Key(n).Length > 0).ToList();
            if (source.Count == 0) source = (selectedNames ?? Enumerable.Empty<String>()).Where(n => ComputerName.Key(n).Length > 0).ToList();
            List<String> result = source.Select(n => n.Trim()).Distinct(ComputerName.Comparer).ToList();
            result.Sort(StringComparer.OrdinalIgnoreCase);
            return result;
        }

        /// <summary>True when that many windows may open at once without a question.</summary>
        public static bool NeedsWindowConfirmation(int count)
        {
            return count > MaxWindowsWithoutQuestion;
        }

        /// <summary>
        /// Runs <paramref name="work"/> for each computer on worker threads (<see cref="Parallelism"/> at a time),
        /// in a progress window shown at once (RemoteRunForm: bar, elapsed time, each result as soon as it is
        /// known, Cancel), the main window staying usable. At the end, on the UI thread, <paramref name="finalText"/>
        /// makes the text of the window from the results in the order of the targets (a computer left out by
        /// Cancel has a line saying so), then <paramref name="after"/> runs (may be null).
        /// </summary>
        void RunWithProgress(String title, List<String> targets, Func<String, String> work, Func<String[], String> finalText, Action after)
        {
            RemoteRunForm form = new RemoteRunForm(title, targets.Count);
            form.Show(Owner);
            String[] results = new String[targets.Count];
            int next = -1;
            Action<Action> onForm = a =>
            {
                try
                {
                    if (!form.IsDisposed && form.IsHandleCreated) form.BeginInvoke(new MethodInvoker(() => { if (!form.IsDisposed) a(); }));
                }
                catch (InvalidOperationException) { }   // window closed meanwhile
            };
            new Thread(() =>
            {
                List<Thread> readers = new List<Thread>();
                for (int t = 0; t < Math.Min(Parallelism, targets.Count); t++)
                {
                    Thread reader = new Thread(() =>
                    {
                        int i;
                        while (!form.CancelRequested && (i = Interlocked.Increment(ref next)) < targets.Count)
                        {
                            onForm(form.Started);
                            String text;
                            try { text = work(targets[i]); }
                            catch (Exception ex) { text = "== " + targets[i] + " ==\n" + ex.Message; }
                            results[i] = text;
                            onForm(() => form.Completed(text));
                        }
                    }) { IsBackground = true, Name = "Computer action" };
                    readers.Add(reader);
                    reader.Start();
                }
                foreach (Thread reader in readers) reader.Join();
                if (Owner.IsDisposed) return;
                try
                {
                    Owner.BeginInvoke(new MethodInvoker(() =>
                    {
                        String text = finalText(RemoteRunProgress.Complete(targets, results));
                        if (!form.IsDisposed) form.Finish(text);
                        if (after != null) after();
                    }));
                }
                catch (InvalidOperationException) { }   // main window closed meanwhile
            }) { IsBackground = true }.Start();
        }

        // ---- Computer card ------------------------------------------------------------

        /// <summary>The cards of the computers (read a few at a time), in one window that can be copied.</summary>
        public void ShowCards(List<String> targets)
        {
            if (targets.Count == 0) return;
            Dictionary<String, String> directory = targets.ToDictionary(c => c, DirectoryText, StringComparer.OrdinalIgnoreCase);
            host.SetStatus(targets.Count == 1
                ? Localization.F("ComputerGrid.ReadingCard", "Reading the computer card of {0}...", targets[0])
                : Localization.F("ComputerGrid.ReadingCards", "Reading the computer cards of {0} computer(s)...", targets.Count));
            String title = targets.Count == 1
                ? Localization.F("ComputerGrid.CardTitle", "Computer card - {0}", targets[0])
                : Localization.F("ComputerGrid.CardsTitle", "Computer cards - {0} computer(s)", targets.Count);
            RunWithProgress(title, targets, c => ReadCard(c, directory[c]), cards => String.Join("\n\n", cards), () =>
            {
                host.SetStatus(null);
                host.StoreChanged();
            });
        }

        /// <summary>What Active Directory says of a computer (first lines of its card).</summary>
        public String DirectoryText(String computer)
        {
            ComputerRow row = host.Row(computer);
            if (row == null || !row.InAd)
                return Localization.T("ComputerGrid.NotInAdTip", "Not in Active Directory");
            return Localization.F("ComputerGrid.CardDirectory", "Operating system (AD): {0}\nOU: {1}\nLast logon (AD): {2}",
                String.IsNullOrEmpty(row.Os) ? "?" : row.Os + (String.IsNullOrEmpty(row.OsVersion) ? "" : " (" + row.OsVersion + ")"),
                String.IsNullOrEmpty(row.Ou) ? "?" : row.Ou,
                row.LastLogon.HasValue ? row.LastLogon.Value.ToString("yyyy-MM-dd HH:mm") : Localization.T("ComputerGrid.Never", "never"));
        }

        // Runs on a reader thread: the info of the worker, saved (adapters, operating system, TeamViewer ID)
        WorkerMessage ReadInfo(String computer, out String failure)
        {
            UpdateRunResult r = UpdateRunner.Info(computer);
            WorkerMessage info = r.Messages.FirstOrDefault(m => m.Type == "info");
            failure = info == null ? r.Failure ?? "?" : null;
            if (info != null)
            {
                try { host.WithStore(store => store.Apply(computer, r.Messages, null, DateTime.Now), true); }
                catch (Exception) { }
            }
            return info;
        }

        // Runs on a reader thread
        String ReadCard(String computer, String directory)
        {
            String failure;
            WorkerMessage info = ReadInfo(computer, out failure);
            StringBuilder sb = new StringBuilder();
            sb.Append("== ").Append(computer).Append(" ==\n").Append(directory).Append('\n');
            if (info == null)
            {
                sb.Append(Localization.F("ComputerGrid.CardFailed", "Not read: {0}", failure));
                return sb.ToString();
            }
            String none = Localization.T("ComputerGrid.CardNone", "(none)");
            sb.Append(Localization.F("ComputerGrid.Card",
                "Logged-on users: {0}\nLast restart: {1}\nRestart pending: {2}\nWindows: {3}\nType: {7}\nFree space on C: {4}\nAdapters (Wake-on-LAN): {5}\nIPv4 addresses: {6}\nTeamViewer ID: {8}",
                Empty(info["users"], none), info["lastboot"], info["pendingrestart"] == "yes" ? Localization.T("ComputerGrid.Yes", "yes") : Localization.T("ComputerGrid.No", "no"),
                info["os"], info["freec"], Empty(info["macs"], none), Empty(info["ips"], none),
                Empty(ComputerUpdateState.OsTypeText(info["ostype"]), Localization.T("ComputerGrid.CardUnknown", "(unknown)")),
                info["teamviewer"] == null ? Localization.T("ComputerGrid.CardUnknown", "(unknown)")
                    : Empty(info["teamviewer"], Localization.T("ComputerActions.TeamViewerNotInstalled", "(TeamViewer not installed)"))));
            // Hardware (newer workers)
            if (info.Fields.ContainsKey("model") || info.Fields.ContainsKey("cpu"))
            {
                String unknown = Localization.T("ComputerGrid.CardUnknown", "(unknown)");
                sb.Append('\n').Append(Localization.F("ComputerGrid.CardHardware",
                    "Manufacturer and model: {0} {1}\nSerial number: {2}\nBIOS date: {3}\nProcessor: {4} ({5} core(s), {6} thread(s))\nMemory: {7} GB\nSystem disk: {8} GB, {9} GB free",
                    Empty(info["manufacturer"], ""), Empty(info["model"], unknown), Empty(info["serial"], unknown), Empty(info["biosdate"], unknown),
                    Empty(info["cpu"], unknown), Empty(info["cores"], "?"), Empty(info["threads"], "?"), Empty(info["ram"], "?"),
                    Empty(info["disk"], "?"), Empty(info["diskfree"], "?")));
            }
            return sb.ToString();
        }

        static String Empty(String value, String none)
        {
            return String.IsNullOrEmpty(value) ? none : value;
        }

        // ---- TeamViewer ID ----------------------------------------------------------------

        /// <summary>The TeamViewer IDs of the computers: the saved ones at once, read from the computers when none is known (or on request).</summary>
        public void ShowTeamViewer(List<String> targets)
        {
            if (targets.Count == 0) return;
            Dictionary<String, String> known = new Dictionary<String, String>(StringComparer.OrdinalIgnoreCase);
            host.WithStore(store =>
            {
                foreach (String c in targets)
                {
                    ComputerUpdateState s = store.Find(c);
                    known[c] = s == null ? null : s.TeamViewerId;
                }
            }, false);
            using (TeamViewerIdForm form = new TeamViewerIdForm(targets, known, ReadTeamViewerId))
            {
                form.ShowDialog(Owner);
            }
            host.StoreChanged();
        }

        // Runs on a background thread: the ID read by the worker ("" when TeamViewer is not installed), or the failure (thrown)
        String ReadTeamViewerId(String computer)
        {
            String failure;
            WorkerMessage info = ReadInfo(computer, out failure);
            if (info == null) throw new InvalidOperationException(failure);
            return info["teamviewer"] ?? "";
        }

        // ---- Remote desktop, command prompt -----------------------------------------------

        bool ConfirmWindows(int count, String question)
        {
            if (!NeedsWindowConfirmation(count)) return true;
            return MessageBox.Show(Owner, question, Owner.Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes;
        }

        /// <summary>One remote desktop window per computer (a question above MaxWindowsWithoutQuestion).</summary>
        public void RemoteDesktop(List<String> targets)
        {
            if (targets.Count == 0 || !ConfirmWindows(targets.Count,
                    Localization.F("ComputerActions.ConfirmRemoteDesktops", "Open {0} remote desktop windows at once?", targets.Count))) return;
            foreach (String computer in targets)
            {
                try { RemoteActions.RemoteDesktop(computer); }
                catch (Exception ex) { MessageBox.Show(Owner, computer + ": " + ex.Message, Owner.Text, MessageBoxButtons.OK, MessageBoxIcon.Error); return; }
            }
        }

        /// <summary>A command prompt window per computer (RemoteConsole: PsExec or WinRM, App > Options).</summary>
        public void CommandPrompt(List<String> targets)
        {
            if (targets.Count == 0 || !ConfirmWindows(targets.Count,
                    Localization.F("ComputerActions.ConfirmCommandPrompts", "Open {0} command prompt windows at once?", targets.Count))) return;
            if (RemoteRunner.Method == RemoteRunner.PsExec && PsExecLocator.Path == null && !Program.OfferPsExecDownload()) return;
            LiteSuite.StoredCredential credential;
            try { credential = UpdateRunner.UpdateCredential(); }
            catch (Exception ex) { MessageBox.Show(Owner, ex.Message, Owner.Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            foreach (String computer in targets)
            {
                try { RemoteConsole.Open(computer, credential); }
                catch (Exception ex) { MessageBox.Show(Owner, computer + ": " + ex.Message, Owner.Text, MessageBoxButtons.OK, MessageBoxIcon.Error); return; }
            }
        }

        // ---- Quick commands ---------------------------------------------------------------

        /// <summary>The menu text of a quick command.</summary>
        public static String QuickCommandText(QuickCommand c)
        {
            switch (c.Id)
            {
                case QuickCommands.GroupPolicy: return Localization.T("ComputerActions.QuickGroupPolicy", "Refresh the group policies (gpupdate /force)");
                case QuickCommands.GroupPolicyComputer: return Localization.T("ComputerActions.QuickGroupPolicyComputer", "Refresh the computer policies only (gpupdate /target:computer /force)");
                case QuickCommands.FlushDns: return Localization.T("ComputerActions.QuickFlushDns", "Flush the DNS cache (ipconfig /flushdns)");
                case QuickCommands.RestartSpooler: return Localization.T("ComputerActions.QuickRestartSpooler", "Restart the print spooler");
                case QuickCommands.Restart: return Localization.T("ComputerActions.QuickRestart", "Restart the computer in 5 minutes (users are warned)");
                case QuickCommands.CancelRestart: return Localization.T("ComputerActions.QuickCancelRestart", "Cancel a planned restart (shutdown /a)");
                default: return c.Label;
            }
        }

        /// <summary>Fills a "Quick commands" item (once) with one entry per command of the table; <paramref name="targets"/> is asked at click time.</summary>
        public void FillQuickCommands(ToolStripMenuItem parent, Func<List<String>> targets)
        {
            if (parent.DropDownItems.Count > 0) return;
            foreach (QuickCommand command in QuickCommands.All)
            {
                QuickCommand c = command;
                ToolStripMenuItem item = new ToolStripMenuItem(QuickCommandText(c)) { ToolTipText = c.CommandText };
                item.Click += (s, e) => RunQuickCommand(c, targets());
                parent.DropDownItems.Add(item);
            }
            // Not a command run on the computer: the message window, with the restart request ready
            parent.DropDownItems.Add(new ToolStripSeparator());
            ToolStripMenuItem askRestart = new ToolStripMenuItem(Localization.T("ComputerActions.AskRestart", "Ask the users to restart..."));
            askRestart.Click += (s, e) => SendMessage(targets(), RestartRequestText);
            parent.DropDownItems.Add(askRestart);
            Theme.Apply(parent.DropDown);
        }

        // ---- Message to the logged-on users -------------------------------------------------

        static String RestartRequestText
        {
            get { return Localization.T("ComputerActions.MessageRestart", "Please restart your computer when convenient. Thank you!"); }
        }

        /// <summary>The ready texts of the message window.</summary>
        public static List<String> MessagePresets()
        {
            return new List<String>
            {
                RestartRequestText,
                Localization.T("ComputerActions.MessageSaveWork", "Please save your work: a maintenance of your computer starts in a few minutes. Thank you!"),
                Localization.T("ComputerActions.MessageLogOff", "Please close your session when you are done. Thank you!")
            };
        }

        /// <summary>
        /// The message window, then (after a confirmation that names the number of computers) the message sent to the
        /// users logged on to each computer, a few computers at a time; one line per computer in a text window.
        /// </summary>
        public void SendMessage(List<String> targets, String text)
        {
            if (targets.Count == 0) return;
            String message;
            int seconds;
            using (UserMessageForm form = new UserMessageForm(targets.Count, MessagePresets(), text))
            {
                if (form.ShowDialog(Owner) != DialogResult.OK) return;
                message = form.MessageText;
                seconds = form.Seconds;
            }
            if (UserMessage.Validate(message) != null) return;
            String names = String.Join(", ", targets.Take(10)) + (targets.Count > 10 ? ", ..." : "");
            if (MessageBox.Show(Owner, Localization.F("ComputerActions.ConfirmMessage", "Send this message to the users logged on to {0} computer(s)?\n\n{1}\n\n{2}", targets.Count, names, message),
                    Owner.Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;
            if (RemoteRunner.Prerequisite() != null)
            {
                MessageBox.Show(Owner, Localization.T("ComputerGrid.MissingTools", "PsExec or LiteDeploy.Worker.exe is missing next to LiteDeploy.exe."),
                    Owner.Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            String title = Localization.T("ComputerActions.MessageTitle", "Message from your administrator");
            host.SetStatus(Localization.F("ComputerActions.SendingMessage", "Sending the message to {0} computer(s)...", targets.Count));
            MessageResult[] results = new MessageResult[targets.Count];
            RunWithProgress(Localization.T("UserMessageForm.Text", "Send a message"), targets, c =>
            {
                UpdateRunResult r = UpdateRunner.Message(c, message, seconds, title);
                MessageResult m = MessageResult.From(r.Messages, r.Failure);
                results[targets.IndexOf(c)] = m;
                return FormatMessageResult(c, m);
            }, lines =>
            {
                String summary = Localization.F("ComputerActions.MessageSummary", "Sent: {0} computer(s), no user logged on: {1}, failed: {2}.",
                    results.Count(m => m != null && m.Kind == MessageResultKind.Sent),
                    results.Count(m => m != null && m.Kind == MessageResultKind.NoUser),
                    results.Count(m => m == null || m.Kind == MessageResultKind.Failed));
                return summary + "\n\n" + String.Join("\n", lines);
            }, () => host.SetStatus(null));
        }

        /// <summary>The line shown for one computer: sent to N session(s) (and the users), no user logged on, or failed.</summary>
        public static String FormatMessageResult(String computer, MessageResult result)
        {
            switch (result.Kind)
            {
                case MessageResultKind.Sent:
                    return Localization.F("ComputerActions.MessageSent", "{0}: sent to {1} session(s) ({2})", computer, result.Sessions, result.Users)
                        + (result.Error.Length > 0 ? " - " + result.Error : "");
                case MessageResultKind.NoUser:
                    return Localization.F("ComputerActions.MessageNoUser", "{0}: no user logged on", computer);
                default:
                    return Localization.F("ComputerActions.MessageFailed", "{0}: failed: {1}", computer, result.Error.Length > 0 ? result.Error : "?");
            }
        }

        /// <summary>Runs a quick command on the computers (a few at a time), after a question when it disturbs the users or targets several computers.</summary>
        public void RunQuickCommand(QuickCommand command, List<String> targets)
        {
            if (targets.Count == 0) return;
            if (command.Confirm || targets.Count > 1)
            {
                String names = String.Join(", ", targets.Take(10)) + (targets.Count > 10 ? ", ..." : "");
                if (MessageBox.Show(Owner, Localization.F("ComputerActions.ConfirmQuick", "Run \"{0}\" on {1} computer(s)?\n\n{2}", QuickCommandText(command), targets.Count, names),
                        Owner.Text, MessageBoxButtons.YesNo, command.Confirm ? MessageBoxIcon.Warning : MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                    return;
            }
            if (RemoteRunner.Prerequisite() != null)
            {
                MessageBox.Show(Owner, Localization.T("ComputerGrid.MissingTools", "PsExec or LiteDeploy.Worker.exe is missing next to LiteDeploy.exe."),
                    Owner.Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            host.SetStatus(Localization.F("ComputerActions.RunningQuick", "Running \"{0}\" on {1} computer(s)...", QuickCommandText(command), targets.Count));
            RunWithProgress(QuickCommandText(command), targets, c => QuickOutput(c, command), outputs => String.Join("\n\n", outputs),
                () => host.SetStatus(null));
        }

        // Runs on a reader thread: what the command wrote, and its exit code
        static String QuickOutput(String computer, QuickCommand command)
        {
            UpdateRunResult r = UpdateRunner.Quick(computer, command.Id);
            return FormatQuickOutput(computer, r.Messages, r.Failure);
        }

        /// <summary>The text shown for one computer: its lines, then the exit code (or why the command could not run).</summary>
        public static String FormatQuickOutput(String computer, IList<WorkerMessage> messages, String failure)
        {
            StringBuilder sb = new StringBuilder("== " + computer + " ==\n");
            foreach (WorkerMessage m in messages.Where(m => m.Type == QuickCommands.TypeOutput))
                sb.Append(m["text"]).Append('\n');
            WorkerMessage result = messages.FirstOrDefault(m => m.Type == QuickCommands.TypeResult);
            WorkerMessage error = messages.FirstOrDefault(m => m.Type == WorkerProtocol.TypeError);
            if (result != null)
                sb.Append(result["code"] == "0"
                    ? Localization.T("ComputerActions.QuickDone", "Done.")
                    : Localization.F("ComputerActions.QuickExitCode", "Exit code {0}.", result["code"]));
            else
                sb.Append(Localization.F("ComputerGrid.CardFailed", "Not read: {0}", error != null ? error["message"] : failure ?? "?"));
            return sb.ToString();
        }

        // ---- SIW report -------------------------------------------------------------------

        /// <summary>
        /// SIW report of the computers (SiwReports, a few at a time, progress window with Cancel). One computer:
        /// its report opens at the end. Several: a question first, then one report per computer and a summary of
        /// the chosen fields (SiwSummaryFields) saved as an Excel workbook or CSV.
        /// </summary>
        public void RunSiwReports(List<String> targets)
        {
            if (targets.Count == 0) return;
            UserSettings settings = UserSettings.Current;
            SiwRequest request = SiwReports.RequestFrom(settings);
            String folder = (settings.SiwFolder ?? "").Trim();
            Owner.Cursor = Cursors.WaitCursor;
            String problem;
            try { problem = SiwReports.CheckFolder(folder, request.Publisher); }
            finally { Owner.Cursor = Cursors.Default; }
            if (problem != null)
            {
                MessageBox.Show(Owner, problem, Owner.Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (targets.Count > 1)
            {
                String names = String.Join(", ", targets.Take(10)) + (targets.Count > 10 ? ", ..." : "");
                if (MessageBox.Show(Owner, Localization.F("ComputerActions.ConfirmSiw",
                        "Run SIW as SYSTEM on {0} computer(s) and get a report from each?\n\n{1}", targets.Count, names),
                        Owner.Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                    return;
            }
            if (RemoteRunner.Prerequisite() != null)
            {
                MessageBox.Show(Owner, Localization.T("ComputerGrid.MissingTools", "PsExec or LiteDeploy.Worker.exe is missing next to LiteDeploy.exe."),
                    Owner.Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            host.SetStatus(Localization.F("ComputerActions.RunningSiw", "SIW report of {0} computer(s)...", targets.Count));
            SiwRunResult[] results = new SiwRunResult[targets.Count];
            String title = targets.Count == 1
                ? Localization.F("ComputerActions.SiwTitle", "SIW report - {0}", targets[0])
                : Localization.F("ComputerActions.SiwTitleMany", "SIW reports - {0} computer(s)", targets.Count);
            RunWithProgress(title, targets, c =>
            {
                SiwRunResult r = SiwReports.Run(c, folder, request);
                results[targets.IndexOf(c)] = r;
                return FormatSiwResult(r);
            }, lines =>
            {
                String summary = Localization.F("ComputerActions.SiwSummary", "Reports: {0} computer(s), failed: {1}. Folder: {2}",
                    results.Count(r => r != null && r.Ok), results.Count(r => r == null || !r.Ok), SiwReports.Root);
                return summary + "\n\n" + String.Join("\n", lines);
            }, () =>
            {
                host.SetStatus(null);
                List<SiwRunResult> done = results.Where(r => r != null && r.Ok).ToList();
                if (done.Count == 0) return;
                if (targets.Count == 1) OpenFile(done[0].HtmlPath ?? done[0].ViewPath);
                else SaveSiwSummary(targets, results);
            });
        }

        /// <summary>The line shown for one computer: the report and its time, or why there is none.</summary>
        public static String FormatSiwResult(SiwRunResult r)
        {
            if (r.Ok) return Localization.F("ComputerActions.SiwDone", "{0}: report in {1} s - {2}", r.Computer, r.Seconds, r.XmlPath);
            return Localization.F("ComputerActions.SiwFailed", "{0}: no report: {1}", r.Computer, String.IsNullOrEmpty(r.Failure) ? "?" : r.Failure);
        }

        void SaveSiwSummary(List<String> targets, SiwRunResult[] results)
        {
            List<KeyValuePair<String, SiwReport>> reports = new List<KeyValuePair<String, SiwReport>>();
            Dictionary<String, String> failures = new Dictionary<String, String>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < targets.Count; i++)
            {
                SiwRunResult r = results[i];
                reports.Add(new KeyValuePair<String, SiwReport>(targets[i], r != null && r.Ok ? r.Report : null));
                if (r == null || !r.Ok)
                    failures[targets[i]] = r == null ? Localization.T("RemoteRunForm.NotRun", "Not run (cancelled).") : r.Failure ?? "?";
            }
            InventoryTable table = SiwReport.Summary(SiwReport.ParseFields(UserSettings.Current.SiwSummaryFields), reports, failures);
            using (SaveFileDialog dialog = new SaveFileDialog())
            {
                dialog.Title = Localization.T("ComputerActions.SiwSummaryTitle", "Save the summary of the SIW reports");
                dialog.Filter = Localization.T("ComputerGrid.ExportInventoryFilter", "Excel workbook (*.xlsx)|*.xlsx|CSV file, separator ; (*.csv)|*.csv");
                dialog.FileName = "LiteDeploy-SIW-" + DateTime.Now.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
                dialog.InitialDirectory = SiwReports.Root;
                dialog.OverwritePrompt = true;
                if (dialog.ShowDialog(Owner) != DialogResult.OK) return;
                try
                {
                    if (dialog.FilterIndex == 2 || dialog.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)) table.SaveCsv(dialog.FileName);
                    else table.SaveXlsx(dialog.FileName, "SIW");
                    host.SetStatus(Localization.F("ComputerActions.SiwSummarySaved", "Summary of {0} computer(s) saved: {1}", table.Rows.Count, dialog.FileName));
                    OpenFile(dialog.FileName);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(Owner, ex.Message, dialog.Title, MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        // ---- Uninstall another program (uninstall mode) ---------------------------------------------

        /// <summary>
        /// Uninstall mode: reads the programs of the target computers (worker "programs"), lets the administrator choose
        /// one (ProgramPickerForm), asks for a confirmation naming it and the computers (the number typed above
        /// UninstallPolicy.TypedConfirmationAbove), then removes it where it is installed (worker "uninstall-program",
        /// a few computers at a time). <paramref name="finished"/> runs at the end of an uninstall (UI thread).
        /// </summary>
        public void UninstallOtherProgram(List<String> targets, Action finished)
        {
            if (targets.Count == 0) return;
            if (RemoteRunner.Prerequisite() != null)
            {
                MessageBox.Show(Owner, Localization.T("ComputerGrid.MissingTools", "PsExec or LiteDeploy.Worker.exe is missing next to LiteDeploy.exe."),
                    Owner.Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            Dictionary<String, IList<WorkerMessage>> lists = new Dictionary<String, IList<WorkerMessage>>(StringComparer.OrdinalIgnoreCase);
            host.SetStatus(Localization.F("ComputerActions.ReadingPrograms", "Reading the programs of {0} computer(s)...", targets.Count));
            RunWithProgress(Localization.F("ComputerActions.ReadingProgramsTitle", "Installed programs - {0} computer(s)", targets.Count), targets, c =>
            {
                UpdateRunResult r = UpdateRunner.Programs(c);
                int count = r.Messages.Count(m => m.Type == ProgramPolicy.TypeProgram);
                if (r.Messages.Any(m => m.Type == WorkerProtocol.TypeDone))
                {
                    lock (lists) lists[c] = r.Messages;
                    return Localization.F("ComputerActions.ProgramsRead", "{0}: {1} program(s)", c, count);
                }
                WorkerMessage error = r.Messages.FirstOrDefault(m => m.Type == WorkerProtocol.TypeError);
                return Localization.F("ComputerGrid.CardFailed", "Not read: {0}", c + ": " + (error != null ? error["message"] : r.Failure ?? "?"));
            }, lines => String.Join("\n", lines), () =>
            {
                host.SetStatus(null);
                List<ProgramRow> rows;
                lock (lists) rows = ProgramInventory.Merge(lists.Select(e => new KeyValuePair<String, IList<WorkerMessage>>(e.Key, e.Value)));
                if (rows.Count == 0) return;
                ProgramRow chosen = ProgramPickerForm.Choose(Owner, rows, lists.Count);
                if (chosen == null || !chosen.Removable) return;
                ConfirmAndUninstall(chosen, finished);
            });
        }

        void ConfirmAndUninstall(ProgramRow program, Action finished)
        {
            List<String> computers = program.Computers.OrderBy(c => c, StringComparer.OrdinalIgnoreCase).ToList();
            String names = String.Join(", ", computers.Take(10)) + (computers.Count > 10 ? ", ..." : "");
            String label = program.Name + (program.Versions.Count > 0 ? " " + String.Join(", ", program.Versions) : "");
            if (MessageBox.Show(Owner, Localization.F("ComputerActions.ConfirmUninstallProgram",
                    "UNINSTALL {0} from {1} computer(s)?\n\nRemoval: {2}. Applications in use can be closed by the uninstaller without warning: {3}",
                    label, computers.Count, ProgramInventory.KindText(program), names),
                    Owner.Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;
            if (computers.Count > UninstallPolicy.TypedConfirmationAbove)
            {
                String typed = Prompt.Show(Owner, Owner.Text,
                    Localization.F("ComputerGrid.ConfirmUninstallCount", "To confirm, type the number of computers {0} will be removed from:", program.Name), "");
                if (!UninstallPolicy.IsTypedConfirmation(typed, computers.Count))
                {
                    if (typed != null)
                        MessageBox.Show(Owner, Localization.T("ComputerGrid.UninstallCancelled", "Not the number of computers: nothing was uninstalled."), Owner.Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
            }
            UserSettings s = UserSettings.Current;
            String args = ProgramPolicy.UninstallArguments(program.View, program.Key, program.Name, s.IncludeServers, s.IncludeServers && s.AllowDomainControllers);
            AppConsole.WriteLine("Uninstall mode: " + program.Name + " (" + program.View + "-bit view, " + program.Key + ") on " + computers.Count + " computer(s): " + String.Join(", ", computers));
            int removed = 0, failed = 0;
            host.SetStatus(Localization.F("ComputerActions.UninstallingProgram", "Uninstalling {0} from {1} computer(s)...", program.Name, computers.Count));
            RunWithProgress(Localization.F("ComputerActions.UninstallProgramTitle", "Uninstall {0} - {1} computer(s)", program.Name, computers.Count), computers, c =>
            {
                UpdateRunResult r = UpdateRunner.UninstallProgram(c, args);
                WorkerMessage line = r.Messages.FirstOrDefault(m => m.Type == ProgramPolicy.TypeResult);
                String text;
                if (line != null && line["result"] == "ok")
                {
                    Interlocked.Increment(ref removed);
                    text = Localization.F("ComputerActions.ProgramRemoved", "{0}: {1}", c, line["message"]);
                }
                else
                {
                    Interlocked.Increment(ref failed);
                    WorkerMessage error = r.Messages.FirstOrDefault(m => m.Type == WorkerProtocol.TypeError);
                    text = Localization.F("ComputerActions.ProgramNotRemoved", "{0}: FAILED: {1}", c,
                        line != null ? line["message"] : error != null ? error["message"] : r.Failure ?? "?");
                }
                AppConsole.WriteLine("Uninstall mode: " + text);
                return text;
            }, lines => Localization.F("ComputerActions.UninstallProgramSummary", "{0}: removed from {1} computer(s), failed on {2}.", program.Name, removed, failed)
                        + "\n\n" + String.Join("\n", lines),
            () =>
            {
                host.SetStatus(null);
                if (finished != null) finished();
            });
        }

        // ---- Portable report tools ------------------------------------------------------------

        /// <summary>Fills a "Reports" item (once) with one entry per tool of the fixed table; <paramref name="targets"/> is asked at click time.</summary>
        public void FillToolReports(ToolStripMenuItem parent, Func<List<String>> targets)
        {
            if (parent.DropDownItems.Count > 0) return;
            foreach (PortableTool tool in PortableToolPolicy.All)
            {
                PortableTool t = tool;
                ToolStripMenuItem item = new ToolStripMenuItem(PortableToolReports.Text(t)) { ToolTipText = t.Executable + " " + t.ArgumentsTemplate };
                item.Click += (s, e) => RunToolReports(t, targets());
                parent.DropDownItems.Add(item);
            }
            Theme.Apply(parent.DropDown);
        }

        /// <summary>
        /// Report of a portable tool on the computers (PortableToolReports, a few at a time, progress window with
        /// Cancel). One computer: its report opens at the end. Several: a question first, then one table of
        /// every row with the computer in the first column, saved as an Excel workbook or CSV.
        /// </summary>
        public void RunToolReports(PortableTool tool, List<String> targets)
        {
            if (targets.Count == 0) return;
            UserSettings settings = UserSettings.Current;
            PortableToolRequest request = PortableToolReports.RequestFrom(settings, tool);
            String folder = (settings.ToolsFolder ?? "").Trim();
            Owner.Cursor = Cursors.WaitCursor;
            String problem;
            try { problem = PortableToolReports.Check(folder, tool); }
            finally { Owner.Cursor = Cursors.Default; }
            if (problem != null)
            {
                MessageBox.Show(Owner, problem, Owner.Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (targets.Count > 1)
            {
                String names = String.Join(", ", targets.Take(10)) + (targets.Count > 10 ? ", ..." : "");
                if (MessageBox.Show(Owner, Localization.F("ComputerActions.ConfirmTool",
                        "Run {0} as SYSTEM on {1} computer(s) and get a report from each?\n\n{2}", tool.Product, targets.Count, names),
                        Owner.Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                    return;
            }
            if (RemoteRunner.Prerequisite() != null)
            {
                MessageBox.Show(Owner, Localization.T("ComputerGrid.MissingTools", "PsExec or LiteDeploy.Worker.exe is missing next to LiteDeploy.exe."),
                    Owner.Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            host.SetStatus(Localization.F("ComputerActions.RunningTool", "{0} on {1} computer(s)...", tool.Product, targets.Count));
            ToolRunResult[] results = new ToolRunResult[targets.Count];
            String title = Localization.F("ComputerActions.ToolTitle", "{0} - {1} computer(s)", tool.Product, targets.Count);
            RunWithProgress(title, targets, c =>
            {
                ToolRunResult r = PortableToolReports.Run(c, folder, request);
                results[targets.IndexOf(c)] = r;
                return r.Ok
                    ? Localization.F("ComputerActions.ToolDone", "{0}: {1} row(s) in {2} s - {3}", r.Computer, r.Report.Rows.Count, r.Seconds, r.ReportPath)
                    : Localization.F("ComputerActions.SiwFailed", "{0}: no report: {1}", r.Computer, String.IsNullOrEmpty(r.Failure) ? "?" : r.Failure);
            }, lines =>
            {
                String summary = Localization.F("ComputerActions.SiwSummary", "Reports: {0} computer(s), failed: {1}. Folder: {2}",
                    results.Count(r => r != null && r.Ok), results.Count(r => r == null || !r.Ok), PortableToolReports.Root(tool));
                return summary + "\n\n" + String.Join("\n", lines);
            }, () =>
            {
                host.SetStatus(null);
                List<ToolRunResult> done = results.Where(r => r != null && r.Ok).ToList();
                if (done.Count == 0) return;
                if (targets.Count == 1) OpenFile(done[0].ViewPath);
                else SaveToolTable(tool, targets, results);
            });
        }

        void SaveToolTable(PortableTool tool, List<String> targets, ToolRunResult[] results)
        {
            List<KeyValuePair<String, CsvReport>> reports = new List<KeyValuePair<String, CsvReport>>();
            Dictionary<String, String> failures = new Dictionary<String, String>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < targets.Count; i++)
            {
                ToolRunResult r = results[i];
                reports.Add(new KeyValuePair<String, CsvReport>(targets[i], r != null && r.Ok ? r.Report : null));
                if (r == null || !r.Ok)
                    failures[targets[i]] = r == null ? Localization.T("RemoteRunForm.NotRun", "Not run (cancelled).") : r.Failure ?? "?";
            }
            SaveTable(CsvReport.Combine(reports, failures), tool.Product, "LiteDeploy-" + tool.Product + "-", PortableToolReports.Root(tool));
        }

        // Save dialog (Excel workbook or CSV, like Export the inventory), then the file opens
        void SaveTable(InventoryTable table, String sheet, String filePrefix, String folder)
        {
            using (SaveFileDialog dialog = new SaveFileDialog())
            {
                dialog.Title = Localization.F("ComputerActions.SaveTableTitle", "Save the table of the {0} reports", sheet);
                dialog.Filter = Localization.T("ComputerGrid.ExportInventoryFilter", "Excel workbook (*.xlsx)|*.xlsx|CSV file, separator ; (*.csv)|*.csv");
                dialog.FileName = filePrefix + DateTime.Now.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
                if (System.IO.Directory.Exists(folder)) dialog.InitialDirectory = folder;
                dialog.OverwritePrompt = true;
                if (dialog.ShowDialog(Owner) != DialogResult.OK) return;
                try
                {
                    if (dialog.FilterIndex == 2 || dialog.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)) table.SaveCsv(dialog.FileName);
                    else table.SaveXlsx(dialog.FileName, sheet);
                    host.SetStatus(Localization.F("ComputerActions.TableSaved", "{0} row(s) saved: {1}", table.Rows.Count, dialog.FileName));
                    OpenFile(dialog.FileName);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(Owner, ex.Message, dialog.Title, MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        static void OpenFile(String path)
        {
            if (String.IsNullOrEmpty(path)) return;
            try { using (Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })) { } } catch (Exception) { }
        }

        // ---- Wake-on-LAN, logs --------------------------------------------------------------

        /// <summary>True when a MAC address is known for one of the computers.</summary>
        public bool CanWake(List<String> targets)
        {
            bool any = false;
            host.WithStore(store => any = targets.Any(c => { ComputerUpdateState s = store.Find(c); return s != null && !String.IsNullOrEmpty(s.Macs); }), false);
            return any;
        }

        public void Wake(List<String> targets)
        {
            int computersWoken = 0;
            host.WithStore(store =>
            {
                foreach (String computer in targets)
                {
                    ComputerUpdateState c = store.Find(computer);
                    if (c == null || String.IsNullOrEmpty(c.Macs)) continue;
                    if (WakeOnLan.Wake(c.Macs, c.Ips) > 0) computersWoken++;
                }
            }, false);
            host.SetStatus(Localization.F("ComputerGrid.WakeSent", "Wake-on-LAN sent to {0} computer(s). They can take a few minutes to start.", computersWoken));
        }

        public void FetchLogs(List<String> targets)
        {
            if (targets.Count == 0) return;
            host.SetStatus(targets.Count == 1
                ? Localization.F("ComputerGrid.FetchingLogs", "Getting the logs of {0}...", targets[0])
                : Localization.F("ComputerGrid.FetchingLogsMany", "Getting the logs of {0} computer(s)...", targets.Count));
            Owner.Cursor = Cursors.WaitCursor;
            new Thread(() =>
            {
                String folder = null;
                int total = 0, fetched = 0;
                List<String> errors = new List<String>();
                foreach (String computer in targets)
                {
                    try
                    {
                        int count;
                        folder = RemoteLogs.Fetch(computer, out count);
                        total += count;
                        fetched++;
                    }
                    catch (Exception ex) { errors.Add(computer + ": " + ex.Message); }
                }
                if (Owner.IsDisposed) return;
                try
                {
                    Owner.BeginInvoke(new MethodInvoker(() =>
                    {
                        Owner.Cursor = Cursors.Default;
                        if (fetched == 0)
                        {
                            host.SetStatus(null);
                            if (targets.Count == 1) MessageBox.Show(Owner, errors.FirstOrDefault() ?? "?", targets[0], MessageBoxButtons.OK, MessageBoxIcon.Warning);
                            else TextViewForm.ShowText(Owner, Owner.Text, String.Join("\n", errors));
                            return;
                        }
                        // One computer: its folder; several: the folder of every computer
                        String open = targets.Count == 1 ? folder : ScheduleRunner.LogDir;
                        host.SetStatus(targets.Count == 1
                            ? Localization.F("ComputerGrid.LogsFetched", "{0} log(s) of {1} copied to {2}.", total, targets[0], folder)
                            : Localization.F("ComputerGrid.LogsFetchedMany", "Logs of {0} of {1} computer(s) copied to {2}.", fetched, targets.Count, open));
                        if (errors.Count > 0) TextViewForm.ShowText(Owner, Owner.Text, String.Join("\n", errors));
                        try { using (Process.Start(new ProcessStartInfo(open) { UseShellExecute = true })) { } } catch (Exception) { }
                    }));
                }
                catch (InvalidOperationException) { }
            }) { IsBackground = true }.Start();
        }

        // ---- Pause, outside business hours only ---------------------------------------------

        public bool IsPaused(String computer)
        {
            bool paused = false;
            host.WithStore(store => { ComputerUpdateState c = store.Find(computer); paused = c != null && c.Paused; }, false);
            return paused;
        }

        public bool IsNightOnly(String computer)
        {
            bool night = false;
            host.WithStore(store => { ComputerUpdateState c = store.Find(computer); night = c != null && c.NightOnly; }, false);
            return night;
        }

        public void Pause(List<String> targets)
        {
            if (targets.Count == 0) return;
            String reason = Prompt.Show(Owner, Owner.Text,
                Localization.F("ComputerGrid.PauseReason", "Reason of the pause ({0} computer(s)):", targets.Count), "");
            if (reason == null) return;
            Change(targets, c => { c.Paused = true; c.PauseReason = reason.Length > 0 ? reason : null; });
        }

        public void Resume(List<String> targets)
        {
            Change(targets, c => { c.Paused = false; c.PauseReason = null; });
        }

        public void SetNightOnly(List<String> targets, bool nightOnly)
        {
            Change(targets, c => c.NightOnly = nightOnly);
        }

        /// <summary>Changes the saved state of the computers and writes updates.xml.</summary>
        public void Change(List<String> targets, Action<ComputerUpdateState> change)
        {
            if (targets.Count == 0) return;
            try
            {
                host.WithStore(store =>
                {
                    foreach (String computer in targets) change(store.GetOrAdd(computer));
                }, true);
            }
            catch (Exception ex) { MessageBox.Show(Owner, ex.Message, Owner.Text, MessageBoxButtons.OK, MessageBoxIcon.Error); }
            host.StoreChanged();
        }

        // ---- Export to Royal TS ---------------------------------------------------------------

        /// <summary>Remote Desktop connections of the computers for Royal TS: CSV or RoyalJSON, chosen in the save dialog.</summary>
        public void ExportRoyalTs(List<String> computers)
        {
            if (computers.Count == 0) return;
            using (SaveFileDialog dialog = new SaveFileDialog())
            {
                dialog.Title = Localization.T("ComputerActions.RoyalTsTitle", "Export to Royal TS");
                dialog.Filter = Localization.T("ComputerActions.RoyalTsFilter", "CSV file for the Royal TS import (*.csv)|*.csv|RoyalJSON document (*.rjson)|*.rjson");
                dialog.FileName = "LiteDeploy-RoyalTS" + RoyalTsExport.CsvExtension;
                dialog.OverwritePrompt = true;
                if (dialog.ShowDialog(Owner) != DialogResult.OK) return;
                String path = dialog.FileName;
                // The extension follows the chosen format when the name has none of the two
                String ext = System.IO.Path.GetExtension(path);
                if (!ext.Equals(RoyalTsExport.CsvExtension, StringComparison.OrdinalIgnoreCase) && !ext.Equals(RoyalTsExport.JsonExtension, StringComparison.OrdinalIgnoreCase))
                    path += dialog.FilterIndex == 2 ? RoyalTsExport.JsonExtension : RoyalTsExport.CsvExtension;
                try
                {
                    RoyalTsExport.Save(path, computers.Select(c => RoyalTsExport.FromRow(c, host.Row(c))));
                    host.SetStatus(Localization.F("ComputerActions.RoyalTsSaved", "{0} computer(s) exported to {1}.", computers.Count, path));
                }
                catch (Exception ex)
                {
                    MessageBox.Show(Owner, ex.Message, dialog.Title, MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }
    }
}
