// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    UserMessageTests.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-18
// Purpose: Message to the logged-on users: base64 UTF-8 round trip, cleaning
//          and length limit of the text, worker arguments and their parsing by
//          the worker, sessions that get the message, result of each computer,
//          ready texts (English and French) and targets of the action.
// -----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using LiteDeploy.Worker;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LiteDeploy.Tests
{
    [TestClass]
    public class UserMessageTests
    {
        [TestCleanup]
        public void Cleanup()
        {
            Localization.Initialize(Localization.English);
        }

        // ---- Text -----------------------------------------------------------------------------

        [TestMethod]
        public void Base64_RoundTrip()
        {
            foreach (String text in new[] { "Hello", "Redémarrez votre poste, merci !", "Line 1\nLine 2", "\"quotes\" & | % ^ < > ' ` $(x) ; --seconds 0", "☃ \U0001F600", "" })
                Assert.AreEqual(text, UserMessage.Decode(UserMessage.Encode(text)), text);
        }

        [TestMethod]
        public void Decode_InvalidValues_Null()
        {
            Assert.IsNull(UserMessage.Decode(null));
            Assert.IsNull(UserMessage.Decode("not base64!"));
            Assert.IsNull(UserMessage.Decode(Convert.ToBase64String(new byte[] { 0xC3, 0x28 })), "invalid UTF-8");
        }

        [TestMethod]
        public void Clean_LineBreaksAndControlCharacters()
        {
            Assert.AreEqual("a\nb\nc d", UserMessage.Clean("  a\r\nb\rc\td \u0007 "));
            Assert.AreEqual("", UserMessage.Clean(null));
            Assert.AreEqual("", UserMessage.Clean(" \r\n "));
        }

        [TestMethod]
        public void Validate_EmptyAndLengthLimit()
        {
            Assert.IsNotNull(UserMessage.Validate(""));
            Assert.IsNotNull(UserMessage.Validate("  \n "));
            Assert.IsNull(UserMessage.Validate(new String('x', UserMessage.MaxLength)));
            Assert.IsNotNull(UserMessage.Validate(new String('x', UserMessage.MaxLength + 1)));
            // Counted once cleaned: spaces around do not count
            Assert.IsNull(UserMessage.Validate("   " + new String('x', UserMessage.MaxLength) + "   "));
        }

        // ---- Worker arguments -------------------------------------------------------------------

        [TestMethod]
        public void WorkerArguments_OnlyBase64OnTheCommandLine()
        {
            String text = "Please restart \"now\" & save; 100% | done\nThanks!";
            String args = UserMessage.WorkerArguments(text, 300, "Message from your administrator");
            String[] parts = args.Split(' ');
            Assert.AreEqual(7, parts.Length, args);
            Assert.AreEqual("message", parts[0]);
            Assert.AreEqual("--text", parts[1]);
            Assert.IsTrue(parts[2].All(c => Char.IsLetterOrDigit(c) || c == '+' || c == '/' || c == '='), parts[2]);
            Assert.AreEqual("--seconds", parts[3]);
            Assert.AreEqual("300", parts[4]);
            Assert.AreEqual("--title", parts[5]);
            Assert.IsFalse(args.Contains("\""));
            Assert.IsFalse(args.Contains("&"));
        }

        [TestMethod]
        public void WorkerArguments_ParsedByTheWorker()
        {
            String text = "Bonjour,\nveuillez redémarrer : merci !";
            String[] args = UserMessage.WorkerArguments(text, 0, "Titre").Split(' ');
            String parsedText, title;
            int seconds;
            Assert.IsNull(UserMessage.ParseArguments(args, out parsedText, out seconds, out title));
            Assert.AreEqual(text, parsedText);
            Assert.AreEqual(0, seconds);
            Assert.AreEqual("Titre", title);
        }

        [TestMethod]
        public void WorkerArguments_InvalidValues_Throw()
        {
            AssertThrows<ArgumentException>(() => UserMessage.WorkerArguments("  ", 300, null));
            AssertThrows<ArgumentException>(() => UserMessage.WorkerArguments(new String('x', UserMessage.MaxLength + 1), 300, null));
            AssertThrows<ArgumentOutOfRangeException>(() => UserMessage.WorkerArguments("Hi", -1, null));
            AssertThrows<ArgumentOutOfRangeException>(() => UserMessage.WorkerArguments("Hi", UserMessage.MaxSeconds + 1, null));
            Assert.AreEqual("message --text " + UserMessage.Encode("Hi") + " --seconds 60", UserMessage.WorkerArguments(" Hi ", 60, "  "));
        }

        [TestMethod]
        public void ParseArguments_DefaultsAndErrors()
        {
            String text, title;
            int seconds;
            String hi = UserMessage.Encode("Hi");
            Assert.IsNull(UserMessage.ParseArguments(new[] { "MESSAGE", "--text", hi }, out text, out seconds, out title));
            Assert.AreEqual("Hi", text);
            Assert.AreEqual(UserMessage.DefaultSeconds, seconds);
            Assert.IsNull(title);

            Assert.IsNotNull(UserMessage.ParseArguments(new[] { "message" }, out text, out seconds, out title), "no text");
            Assert.IsNotNull(UserMessage.ParseArguments(new[] { "message", "--text" }, out text, out seconds, out title), "no value");
            Assert.IsNotNull(UserMessage.ParseArguments(new[] { "message", "--text", "Hi there" }, out text, out seconds, out title), "raw text");
            Assert.IsNotNull(UserMessage.ParseArguments(new[] { "message", "--text", hi, "--seconds", "-5" }, out text, out seconds, out title));
            Assert.IsNotNull(UserMessage.ParseArguments(new[] { "message", "--text", hi, "--seconds", "999999" }, out text, out seconds, out title));
            Assert.IsNotNull(UserMessage.ParseArguments(new[] { "message", "--text", hi, "--run", "cmd" }, out text, out seconds, out title), "unknown option");
            Assert.IsNotNull(UserMessage.ParseArguments(new[] { "message", "--text", UserMessage.Encode(new String('x', UserMessage.MaxLength + 1)) }, out text, out seconds, out title), "too long");
            Assert.IsNotNull(UserMessage.ParseArguments(new[] { "quick", "gpupdate" }, out text, out seconds, out title));
        }

        [TestMethod]
        public void Title_OneLineAndLimited()
        {
            Assert.AreEqual("a b", UserMessage.CleanTitle("a\r\nb"));
            Assert.AreEqual(UserMessage.MaxTitleLength, UserMessage.CleanTitle(new String('t', 200)).Length);
        }

        // ---- Sessions and results ------------------------------------------------------------------

        [TestMethod]
        public void Sessions_ActiveWithAUserOnly()
        {
            List<SessionEntry> targets = SessionMessenger.Targets(new[]
            {
                new SessionEntry { Id = 0, Active = true, User = "SYSTEM" },
                new SessionEntry { Id = 3, Active = true, User = "EXAMPLE user2" },
                new SessionEntry { Id = 1, Active = true, User = "user1" },
                new SessionEntry { Id = 2, Active = false, User = "user3" },
                new SessionEntry { Id = 4, Active = true, User = "" },
                new SessionEntry { Id = 1, Active = true, User = "user1" },
                null
            });
            CollectionAssert.AreEqual(new[] { 1, 3 }, targets.Select(s => s.Id).ToArray());
            Assert.AreEqual(0, SessionMessenger.Targets(null).Count);
        }

        static List<WorkerMessage> Line(params String[] keyValues)
        {
            return new List<WorkerMessage> { WorkerProtocol.Parse(WorkerProtocol.Format(new[] { "type", UserMessage.TypeResult }.Concat(keyValues).ToArray())) };
        }

        [TestMethod]
        public void Result_SentNoUserFailed()
        {
            MessageResult sent = MessageResult.From(Line("sessions", "2", "users", "user1, user2", "failed", "0", "error", ""), null);
            Assert.AreEqual(MessageResultKind.Sent, sent.Kind);
            Assert.AreEqual(2, sent.Sessions);
            Assert.AreEqual("PC01: sent to 2 session(s) (user1, user2)", ComputerActions.FormatMessageResult("PC01", sent));

            MessageResult nobody = MessageResult.From(Line("sessions", "0", "users", "", "failed", "0"), null);
            Assert.AreEqual(MessageResultKind.NoUser, nobody.Kind);
            Assert.AreEqual("PC01: no user logged on", ComputerActions.FormatMessageResult("PC01", nobody));

            MessageResult refused = MessageResult.From(Line("sessions", "0", "failed", "1", "error", "Session 1: Access is denied."), null);
            Assert.AreEqual(MessageResultKind.Failed, refused.Kind);
            Assert.AreEqual("PC01: failed: Session 1: Access is denied.", ComputerActions.FormatMessageResult("PC01", refused));

            MessageResult unreachable = MessageResult.From(new List<WorkerMessage>(), "PsExec exit code 53: The network path was not found.");
            Assert.AreEqual(MessageResultKind.Failed, unreachable.Kind);
            StringAssert.Contains(ComputerActions.FormatMessageResult("PC02", unreachable), "network path");

            MessageResult usage = MessageResult.From(new List<WorkerMessage> { WorkerProtocol.Parse(WorkerProtocol.Format("type", WorkerProtocol.TypeError, "message", "--text is missing")) }, "exit 100");
            Assert.AreEqual("--text is missing", usage.Error);
        }

        [TestMethod]
        public void Result_LineOfTheWorker_RoundTrip()
        {
            MessageOutcome outcome = new MessageOutcome { Sent = 1 };
            outcome.Users.Add("user1");
            outcome.Errors.Add("Session 2: The RPC server is unavailable.");
            MessageResult r = MessageResult.From(new List<WorkerMessage> { WorkerProtocol.Parse(LiteDeploy.Worker.Program.MessageResultLine(outcome)) }, null);
            Assert.AreEqual(MessageResultKind.Sent, r.Kind);
            Assert.AreEqual(1, r.Sessions);
            Assert.AreEqual("user1", r.Users);
            Assert.AreEqual("Session 2: The RPC server is unavailable.", r.Error);
        }

        // ---- Ready texts, targets --------------------------------------------------------------------

        [TestMethod]
        public void Presets_FitTheLimit_InEnglishAndFrench()
        {
            foreach (String language in new[] { Localization.English, Localization.French })
            {
                Localization.Initialize(language);
                List<String> presets = ComputerActions.MessagePresets();
                Assert.AreEqual(3, presets.Count);
                foreach (String p in presets) Assert.IsNull(UserMessage.Validate(p), language + ": " + p);
            }
            Assert.AreEqual("Veuillez redémarrer votre ordinateur dès que possible. Merci !", ComputerActions.MessagePresets()[0]);
        }

        [TestMethod]
        public void Targets_CheckedElseSelected()
        {
            CollectionAssert.AreEqual(new[] { "PC01", "PC02" }, ComputerActions.Targets(new[] { "PC02", "PC01" }, new[] { "PC03" }));
            CollectionAssert.AreEqual(new[] { "PC03" }, ComputerActions.Targets(new String[0], new[] { "PC03" }));
            Assert.AreEqual(0, ComputerActions.Targets(null, null).Count);
        }

        static void AssertThrows<T>(Action action) where T : Exception
        {
            try { action(); }
            catch (T) { return; }
            Assert.Fail("Expected " + typeof(T).Name);
        }
    }
}
