// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    ServerPolicy.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-18
// Purpose: Server guard of the software updates: the worker never upgrades or
//          installs anything on a server unless LiteDeploy allows it
//          (--allow-servers, "Include servers" in App > Options), and never on
//          a domain controller without a second explicit option
//          (--allow-domain-controllers). Win32_OperatingSystem.ProductType:
//          1 workstation, 2 domain controller, 3 server. "siw" (SIW report,
//          SiwPolicy) and "tool" (portable report tools, PortableToolPolicy:
//          third-party code run as SYSTEM) follow the same rule. "scan", "info" and "processes"
//          are not concerned (report only). Pure logic, compiled into both
//          programs (and the tests).
// -----------------------------------------------------------------------------
using System;

namespace LiteDeploy
{
    /// <summary>What the worker does with an upgrade or install run, given the operating system type.</summary>
    internal enum ServerDecision
    {
        /// <summary>Workstation, a command that changes nothing, or a server type explicitly allowed.</summary>
        Allowed,
        /// <summary>Server (ProductType 3) without --allow-servers.</summary>
        RefusedServer,
        /// <summary>Domain controller (ProductType 2) without both --allow-servers and --allow-domain-controllers.</summary>
        RefusedDomainController,
        /// <summary>Type unreadable: treated as a domain controller (both options needed).</summary>
        RefusedUnknown
    }

    internal static class ServerPolicy
    {
        /// <summary>Worker option: upgrades and installs allowed on servers (ProductType 3).</summary>
        public const string AllowServersOption = "--allow-servers";
        /// <summary>Worker option: with --allow-servers, also on domain controllers (ProductType 2).</summary>
        public const string AllowDomainControllersOption = "--allow-domain-controllers";

        // Win32_OperatingSystem.ProductType (0: could not be read)
        public const int ProductUnknown = 0;
        public const int ProductWorkstation = 1;
        public const int ProductDomainController = 2;
        public const int ProductServer = 3;

        // Values of the "ostype" field of the worker messages (info, done) and of updates.xml
        public const string TypeWorkstation = "workstation";
        public const string TypeDomainController = "domaincontroller";
        public const string TypeServer = "server";

        /// <summary>
        /// The commands under the guard: "upgrade", "install" and "uninstall" (they change the computer) and
        /// "siw" (a third-party tool that reads the hardware at a low level, run as SYSTEM).
        /// </summary>
        public static bool AppliesTo(string command)
        {
            return string.Equals(command, "upgrade", StringComparison.OrdinalIgnoreCase)
                || string.Equals(command, InstallPolicy.Command, StringComparison.OrdinalIgnoreCase)
                || string.Equals(command, UninstallPolicy.Command, StringComparison.OrdinalIgnoreCase)
                || string.Equals(command, SiwPolicy.Command, StringComparison.OrdinalIgnoreCase)
                || string.Equals(command, PortableToolPolicy.Command, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// The decision for one run. A workstation, or a command that changes nothing (scan, info,
        /// processes), is always allowed; a server needs <paramref name="allowServers"/>; a domain
        /// controller, or a computer whose type could not be read, needs both options.
        /// </summary>
        public static ServerDecision Decide(string command, int productType, bool allowServers, bool allowDomainControllers)
        {
            if (!AppliesTo(command) || productType == ProductWorkstation) return ServerDecision.Allowed;
            if (productType == ProductServer)
                return allowServers ? ServerDecision.Allowed : ServerDecision.RefusedServer;
            bool both = allowServers && allowDomainControllers;
            if (productType == ProductDomainController)
                return both ? ServerDecision.Allowed : ServerDecision.RefusedDomainController;
            return both ? ServerDecision.Allowed : ServerDecision.RefusedUnknown;
        }

        /// <summary>The error message of a refusal (null when allowed).</summary>
        public static string RefusalMessage(ServerDecision decision, int productType)
        {
            switch (decision)
            {
                case ServerDecision.RefusedServer:
                    return "Server operating system (ProductType " + productType + "): updates and installs refused; "
                         + "enable 'Include servers' in LiteDeploy (App > Options > Software updates) to allow them.";
                case ServerDecision.RefusedDomainController:
                    return "Domain controller (ProductType " + productType + "): updates and installs refused; "
                         + "they need both " + AllowServersOption + " ('Include servers' in LiteDeploy) and " + AllowDomainControllersOption + ".";
                case ServerDecision.RefusedUnknown:
                    return "Operating system type unknown (ProductType could not be read): updates and installs refused; "
                         + "they need both " + AllowServersOption + " and " + AllowDomainControllersOption + ".";
                default:
                    return null;
            }
        }

        /// <summary>The "ostype" value of a ProductType (null when unknown).</summary>
        public static string OsType(int productType)
        {
            switch (productType)
            {
                case ProductWorkstation: return TypeWorkstation;
                case ProductDomainController: return TypeDomainController;
                case ProductServer: return TypeServer;
                default: return null;
            }
        }

        /// <summary>The ProductType of an "ostype" value (ProductUnknown when empty or unknown).</summary>
        public static int ProductTypeOf(string osType)
        {
            if (string.Equals(osType, TypeWorkstation, StringComparison.OrdinalIgnoreCase)) return ProductWorkstation;
            if (string.Equals(osType, TypeDomainController, StringComparison.OrdinalIgnoreCase)) return ProductDomainController;
            if (string.Equals(osType, TypeServer, StringComparison.OrdinalIgnoreCase)) return ProductServer;
            return ProductUnknown;
        }

        /// <summary>
        /// Fallback when WMI cannot be read: the ProductType value of
        /// HKLM\SYSTEM\CurrentControlSet\Control\ProductOptions ("WinNT", "LanmanNT", "ServerNT").
        /// </summary>
        public static int ProductTypeFromRegistry(string value)
        {
            if (string.Equals(value, "WinNT", StringComparison.OrdinalIgnoreCase)) return ProductWorkstation;
            if (string.Equals(value, "LanmanNT", StringComparison.OrdinalIgnoreCase)) return ProductDomainController;
            if (string.Equals(value, "ServerNT", StringComparison.OrdinalIgnoreCase)) return ProductServer;
            return ProductUnknown;
        }
    }
}
