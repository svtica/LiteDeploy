// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    ServerGuardTests.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-18
// Purpose: Unit tests of the server guard: the decision of the worker
//          (ServerPolicy), its options (--allow-servers,
//          --allow-domain-controllers), the arguments LiteDeploy builds, the
//          setting, the scheduled updates and the operating system kept in
//          the state file.
// -----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LiteDeploy.Tests
{
    [TestClass]
    public class ServerGuardTests
    {
        // ---- Decision ---------------------------------------------------------------

        [TestMethod]
        public void Decide_Workstation_AlwaysAllowed()
        {
            foreach (string command in new[] { "scan", "upgrade", "install" })
                Assert.AreEqual(ServerDecision.Allowed, ServerPolicy.Decide(command, ServerPolicy.ProductWorkstation, false, false), command);
        }

        [TestMethod]
        public void Decide_Scan_AllowedOnEveryType()
        {
            foreach (int type in new[] { ServerPolicy.ProductUnknown, ServerPolicy.ProductWorkstation, ServerPolicy.ProductDomainController, ServerPolicy.ProductServer })
            {
                Assert.AreEqual(ServerDecision.Allowed, ServerPolicy.Decide("scan", type, false, false), "type " + type);
                Assert.AreEqual(ServerDecision.Allowed, ServerPolicy.Decide("info", type, false, false), "type " + type);
                Assert.AreEqual(ServerDecision.Allowed, ServerPolicy.Decide("processes", type, false, false), "type " + type);
            }
        }

        [TestMethod]
        public void Decide_Server_NeedsAllowServers()
        {
            foreach (string command in new[] { "upgrade", "install", "UPGRADE" })
            {
                Assert.AreEqual(ServerDecision.RefusedServer, ServerPolicy.Decide(command, ServerPolicy.ProductServer, false, false), command);
                Assert.AreEqual(ServerDecision.RefusedServer, ServerPolicy.Decide(command, ServerPolicy.ProductServer, false, true), command + ": the DC option alone is not enough");
                Assert.AreEqual(ServerDecision.Allowed, ServerPolicy.Decide(command, ServerPolicy.ProductServer, true, false), command);
            }
        }

        [TestMethod]
        public void Decide_DomainController_NeedsBothOptions()
        {
            foreach (string command in new[] { "upgrade", "install" })
            {
                Assert.AreEqual(ServerDecision.RefusedDomainController, ServerPolicy.Decide(command, ServerPolicy.ProductDomainController, false, false), command);
                Assert.AreEqual(ServerDecision.RefusedDomainController, ServerPolicy.Decide(command, ServerPolicy.ProductDomainController, true, false), command);
                Assert.AreEqual(ServerDecision.RefusedDomainController, ServerPolicy.Decide(command, ServerPolicy.ProductDomainController, false, true), command);
                Assert.AreEqual(ServerDecision.Allowed, ServerPolicy.Decide(command, ServerPolicy.ProductDomainController, true, true), command);
            }
        }

        [TestMethod]
        public void Decide_UnknownType_TreatedAsDomainController()
        {
            Assert.AreEqual(ServerDecision.RefusedUnknown, ServerPolicy.Decide("upgrade", ServerPolicy.ProductUnknown, false, false));
            Assert.AreEqual(ServerDecision.RefusedUnknown, ServerPolicy.Decide("install", ServerPolicy.ProductUnknown, true, false));
            Assert.AreEqual(ServerDecision.RefusedUnknown, ServerPolicy.Decide("upgrade", 42, true, false));
            Assert.AreEqual(ServerDecision.Allowed, ServerPolicy.Decide("upgrade", ServerPolicy.ProductUnknown, true, true));
        }

        [TestMethod]
        public void RefusalMessage_SaysWhyAndHowToAllow()
        {
            string server = ServerPolicy.RefusalMessage(ServerDecision.RefusedServer, 3);
            StringAssert.Contains(server, "Server operating system (ProductType 3)");
            StringAssert.Contains(server, "Include servers");
            string dc = ServerPolicy.RefusalMessage(ServerDecision.RefusedDomainController, 2);
            StringAssert.Contains(dc, "Domain controller (ProductType 2)");
            StringAssert.Contains(dc, ServerPolicy.AllowDomainControllersOption);
            StringAssert.Contains(ServerPolicy.RefusalMessage(ServerDecision.RefusedUnknown, 0), "unknown");
            Assert.IsNull(ServerPolicy.RefusalMessage(ServerDecision.Allowed, 1));
        }

        [TestMethod]
        public void OsType_ProductTypeAndRegistryValues()
        {
            Assert.AreEqual(ServerPolicy.TypeWorkstation, ServerPolicy.OsType(1));
            Assert.AreEqual(ServerPolicy.TypeDomainController, ServerPolicy.OsType(2));
            Assert.AreEqual(ServerPolicy.TypeServer, ServerPolicy.OsType(3));
            Assert.IsNull(ServerPolicy.OsType(0));
            Assert.IsNull(ServerPolicy.OsType(7));
            for (int type = 1; type <= 3; type++)
                Assert.AreEqual(type, ServerPolicy.ProductTypeOf(ServerPolicy.OsType(type)));
            Assert.AreEqual(ServerPolicy.ProductUnknown, ServerPolicy.ProductTypeOf(null));
            Assert.AreEqual(ServerPolicy.ProductUnknown, ServerPolicy.ProductTypeOf("other"));

            Assert.AreEqual(ServerPolicy.ProductWorkstation, ServerPolicy.ProductTypeFromRegistry("WinNT"));
            Assert.AreEqual(ServerPolicy.ProductDomainController, ServerPolicy.ProductTypeFromRegistry("LanmanNT"));
            Assert.AreEqual(ServerPolicy.ProductServer, ServerPolicy.ProductTypeFromRegistry("ServerNT"));
            Assert.AreEqual(ServerPolicy.ProductUnknown, ServerPolicy.ProductTypeFromRegistry(null));
        }

        // ---- Worker arguments -----------------------------------------------------------

        [TestMethod]
        public void Worker_ParseArguments_ServerOptions()
        {
            string command, cache;
            List<UpdatePackage> packages;
            int timeout;
            bool unsigned, servers, dcs;

            Assert.IsNull(Worker.Program.ParseArguments(new[] { "upgrade", "--package", "Example.Product||" },
                out command, out packages, out timeout, out unsigned, out cache, out servers, out dcs));
            Assert.IsFalse(servers, "workstations only by default");
            Assert.IsFalse(dcs);

            Assert.IsNull(Worker.Program.ParseArguments(new[] { "install", "--package", "Example.Product||", "--allow-servers" },
                out command, out packages, out timeout, out unsigned, out cache, out servers, out dcs));
            Assert.IsTrue(servers);
            Assert.IsFalse(dcs);

            Assert.IsNull(Worker.Program.ParseArguments(new[] { "upgrade", "--allow-servers", "--package", "Example.Product||", "--allow-domain-controllers" },
                out command, out packages, out timeout, out unsigned, out cache, out servers, out dcs));
            Assert.IsTrue(servers);
            Assert.IsTrue(dcs);
            Assert.AreEqual(1, packages.Count);

            // Accepted by scan too (LiteDeploy passes the same options to every command)
            Assert.IsNull(Worker.Program.ParseArguments(new[] { "scan", "--package", "Example.Product||", "--allow-servers" },
                out command, out packages, out timeout, out unsigned, out cache, out servers, out dcs));

            // The former overloads still parse them
            Assert.IsNull(Worker.Program.ParseArguments(new[] { "upgrade", "--package", "Example.Product||", "--allow-servers" },
                out command, out packages, out timeout, out unsigned));

            Assert.IsNotNull(Worker.Program.ParseArguments(new[] { "upgrade", "--package", "Example.Product||", "--allow-server" },
                out command, out packages, out timeout, out unsigned, out cache, out servers, out dcs), "misspelled option");
        }

        [TestMethod]
        public void BuildWorkerArguments_ServerOptions()
        {
            List<UpdatePackage> list = new List<UpdatePackage> { UpdatePackage.Parse("Example.Product | example | Example") };

            Assert.IsFalse(UpdateRunner.BuildWorkerArguments("upgrade", list).Contains("--allow-servers"));
            Assert.IsFalse(UpdateRunner.BuildWorkerArguments("upgrade", list, false, null).Contains("--allow-servers"));
            Assert.IsFalse(UpdateRunner.BuildWorkerArguments("upgrade", list, false, null, false, true).Contains("--allow-"),
                "the domain controller option is never given without --allow-servers");

            string servers = UpdateRunner.BuildWorkerArguments("upgrade", list, false, null, true, false);
            StringAssert.EndsWith(servers, " --allow-servers");
            Assert.IsFalse(servers.Contains("--allow-domain-controllers"));

            string both = UpdateRunner.BuildWorkerArguments(InstallPolicy.Command, list, false, null, true, true);
            StringAssert.EndsWith(both, " --allow-servers --allow-domain-controllers");

            // What LiteDeploy builds is what the worker reads
            string command, cache;
            List<UpdatePackage> packages;
            int timeout;
            bool unsigned, allowServers, allowDcs;
            Assert.IsNull(Worker.Program.ParseArguments(SplitArguments(both), out command, out packages, out timeout, out unsigned, out cache, out allowServers, out allowDcs));
            Assert.IsTrue(allowServers);
            Assert.IsTrue(allowDcs);
            Assert.AreEqual("Example.Product", packages[0].Id);
        }

        // Minimal command-line split (quoted arguments without embedded quotes), enough for these arguments
        static string[] SplitArguments(string line)
        {
            List<string> args = new List<string>();
            System.Text.StringBuilder current = new System.Text.StringBuilder();
            bool quoted = false, any = false;
            foreach (char ch in line)
            {
                if (ch == '"') { quoted = !quoted; any = true; continue; }
                if (ch == ' ' && !quoted)
                {
                    if (any) args.Add(current.ToString());
                    current.Clear();
                    any = false;
                    continue;
                }
                current.Append(ch);
                any = true;
            }
            if (any) args.Add(current.ToString());
            return args.ToArray();
        }

        // ---- Setting ----------------------------------------------------------------------

        [TestMethod]
        public void UserSettings_IncludeServers_DefaultsFalseAndRoundTrips()
        {
            Assert.IsFalse(new UserSettings().IncludeServers);
            Assert.IsFalse(new UserSettings().AllowDomainControllers);

            XmlSerializer serializer = new XmlSerializer(typeof(UserSettings));
            StringWriter writer = new StringWriter();
            serializer.Serialize(writer, new UserSettings { IncludeServers = true });
            UserSettings read = (UserSettings)serializer.Deserialize(new StringReader(writer.ToString()));
            Assert.IsTrue(read.IncludeServers);
            Assert.IsFalse(read.AllowDomainControllers);

            UserSettings old = (UserSettings)serializer.Deserialize(new StringReader("<LiteDeploySettings><Language>en</Language></LiteDeploySettings>"));
            Assert.IsFalse(old.IncludeServers, "settings saved by an older version: servers not included");
        }

        // ---- Schedules --------------------------------------------------------------------

        [TestMethod]
        public void Schedule_SkipsKnownServersUnlessIncluded()
        {
            ComputerUpdateState server = new ComputerUpdateState { Name = "SRV01", OsType = ServerPolicy.TypeServer };
            ComputerUpdateState dc = new ComputerUpdateState { Name = "SRV02", OsType = ServerPolicy.TypeDomainController };
            ComputerUpdateState pc = new ComputerUpdateState { Name = "PC01", OsType = ServerPolicy.TypeWorkstation };
            ComputerUpdateState unknown = new ComputerUpdateState { Name = "PC02" };
            UserSettings defaults = new UserSettings();
            UserSettings servers = new UserSettings { IncludeServers = true };

            Assert.IsTrue(ScheduleRunner.SkipsServer(server, "upgrade", defaults));
            Assert.IsTrue(ScheduleRunner.SkipsServer(dc, "upgrade", defaults));
            Assert.IsFalse(ScheduleRunner.SkipsServer(server, "scan", defaults), "a check runs on servers too");
            Assert.IsFalse(ScheduleRunner.SkipsServer(pc, "upgrade", defaults));
            Assert.IsFalse(ScheduleRunner.SkipsServer(unknown, "upgrade", defaults), "type not known yet: the worker checks it");
            Assert.IsFalse(ScheduleRunner.SkipsServer(null, "upgrade", defaults));

            Assert.IsFalse(ScheduleRunner.SkipsServer(server, "upgrade", servers));
            Assert.IsTrue(ScheduleRunner.SkipsServer(dc, "upgrade", servers), "domain controllers need their own setting");
            Assert.IsFalse(ScheduleRunner.SkipsServer(dc, "upgrade", new UserSettings { IncludeServers = true, AllowDomainControllers = true }));
        }

        // ---- State file -------------------------------------------------------------------

        [TestMethod]
        public void State_KeepsOperatingSystemOfDoneAndInfo()
        {
            UpdateStateStore store = new UpdateStateStore();
            DateTime now = new DateTime(2026, 11, 18, 10, 0, 0);
            store.Apply("SRV01", new List<WorkerMessage>
            {
                WorkerProtocol.Parse(WorkerProtocol.Format("type", "start", "computer", "SRV01")),
                WorkerProtocol.Parse(WorkerProtocol.Format("type", WorkerProtocol.TypeError, "message", ServerPolicy.RefusalMessage(ServerDecision.RefusedServer, 3))),
                WorkerProtocol.Parse(WorkerProtocol.Format("type", WorkerProtocol.TypeDone, "failed", "1", "os", "Microsoft Windows Server Example 10.0.20348", "ostype", ServerPolicy.TypeServer))
            }, null, now);

            ComputerUpdateState c = store.Find("SRV01");
            Assert.AreEqual("Microsoft Windows Server Example 10.0.20348", c.OsCaption);
            Assert.AreEqual(ServerPolicy.TypeServer, c.OsType);
            Assert.IsTrue(c.IsServer);
            StringAssert.Contains(c.LastError, "ProductType 3", "the refusal is the computer's error");

            // A message of an older worker (no such field) keeps what is known
            store.Apply("SRV01", new List<WorkerMessage> { WorkerProtocol.Parse(WorkerProtocol.Format("type", WorkerProtocol.TypeDone, "failed", "0")) }, null, now);
            Assert.AreEqual(ServerPolicy.TypeServer, store.Find("SRV01").OsType);

            // The info card updates it too
            store.Apply("PC01", new List<WorkerMessage> { WorkerProtocol.Parse(WorkerProtocol.Format("type", "info", "os", "Microsoft Windows Example 10.0.26100", "ostype", ServerPolicy.TypeWorkstation)) }, null, now);
            Assert.AreEqual(ServerPolicy.TypeWorkstation, store.Find("PC01").OsType);
            Assert.IsFalse(store.Find("PC01").IsServer);
        }

        [TestMethod]
        public void State_OperatingSystem_RoundTripAndOlderFile()
        {
            UpdateStateStore store = new UpdateStateStore();
            ComputerUpdateState c = store.GetOrAdd("SRV01");
            c.OsCaption = "Microsoft Windows Server Example 10.0.20348";
            c.OsType = ServerPolicy.TypeDomainController;
            string xml = store.Serialize();
            StringAssert.Contains(xml, "OsType=\"domaincontroller\"");
            Assert.IsFalse(xml.Contains("IsServer"), "computed, not saved");

            string path = Path.GetTempFileName();
            try
            {
                File.WriteAllText(path, xml);
                ComputerUpdateState read = UpdateStateStore.Load(path).Find("SRV01");
                Assert.AreEqual(c.OsCaption, read.OsCaption);
                Assert.AreEqual(ServerPolicy.TypeDomainController, read.OsType);

                // File written by an older version: no such attributes
                File.WriteAllText(path, "<LiteDeployUpdates><Computer Name=\"PC01\" Paused=\"false\" NightOnly=\"false\"><Package Id=\"Example.Product\" Status=\"uptodate\" /></Computer></LiteDeployUpdates>");
                ComputerUpdateState old = UpdateStateStore.Load(path).Find("PC01");
                Assert.IsNotNull(old);
                Assert.IsNull(old.OsCaption);
                Assert.IsNull(old.OsType);
                Assert.IsFalse(old.IsServer);
                Assert.AreEqual(1, old.Packages.Count);
            }
            finally { File.Delete(path); }
        }

        [TestMethod]
        public void OsTypeText_EnglishAndFrenchNames()
        {
            try
            {
                Localization.Initialize(Localization.English);
                Assert.AreEqual("Workstation", ComputerUpdateState.OsTypeText(ServerPolicy.TypeWorkstation));
                Assert.AreEqual("Server", ComputerUpdateState.OsTypeText(ServerPolicy.TypeServer));
                Assert.AreEqual("Domain controller", ComputerUpdateState.OsTypeText(ServerPolicy.TypeDomainController));
                Assert.AreEqual("", ComputerUpdateState.OsTypeText(null));

                Localization.Initialize(Localization.French);
                Assert.AreEqual("Serveur", ComputerUpdateState.OsTypeText(ServerPolicy.TypeServer));
                Assert.AreEqual("Contrôleur de domaine", ComputerUpdateState.OsTypeText(ServerPolicy.TypeDomainController));
            }
            finally
            {
                Localization.Initialize(Localization.English);
            }
        }
    }
}
