// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    WakeOnLan.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-12
// Purpose: Wake-on-LAN magic packets, from the MAJ-Logiciels panel: the MAC
//          addresses come from the computers themselves (worker), kept in the
//          state file while they are off. Sent to the local broadcast and to
//          the /24 broadcast of the last known address of the computer (routers
//          must forward directed broadcasts for another subnet), UDP 7 and 9.
// -----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Sockets;

namespace LiteDeploy
{
    static public class WakeOnLan
    {
        /// <summary>6 x 0xFF, then the MAC address 16 times. Null for an invalid MAC.</summary>
        static public byte[] MagicPacket(String mac)
        {
            byte[] address = ParseMac(mac);
            if (address == null) return null;
            byte[] packet = new byte[6 + 16 * 6];
            for (int i = 0; i < 6; i++) packet[i] = 0xFF;
            for (int i = 0; i < 16; i++) Buffer.BlockCopy(address, 0, packet, 6 + i * 6, 6);
            return packet;
        }

        static public byte[] ParseMac(String mac)
        {
            if (String.IsNullOrWhiteSpace(mac)) return null;
            String hex = new String(mac.Where(Uri.IsHexDigit).ToArray());
            if (hex.Length != 12) return null;
            byte[] b = new byte[6];
            for (int i = 0; i < 6; i++) b[i] = Byte.Parse(hex.Substring(i * 2, 2), NumberStyles.HexNumber);
            return b;
        }

        /// <summary>Broadcast addresses: 255.255.255.255 and the x.y.z.255 of each known IPv4 address.</summary>
        static public List<IPAddress> Targets(String ips)
        {
            List<IPAddress> targets = new List<IPAddress> { IPAddress.Broadcast };
            foreach (String ip in (ips ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                IPAddress a;
                if (IPAddress.TryParse(ip, out a) && a.AddressFamily == AddressFamily.InterNetwork)
                {
                    byte[] b = a.GetAddressBytes();
                    b[3] = 255;
                    IPAddress directed = new IPAddress(b);
                    if (!targets.Contains(directed)) targets.Add(directed);
                }
            }
            return targets;
        }

        /// <summary>Sends the packets for every MAC address of a computer. Returns the number of packets sent.</summary>
        static public int Wake(String macs, String ips)
        {
            int sent = 0;
            using (UdpClient udp = new UdpClient())
            {
                udp.EnableBroadcast = true;
                foreach (String mac in (macs ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    byte[] packet = MagicPacket(mac);
                    if (packet == null) continue;
                    foreach (IPAddress target in Targets(ips))
                    {
                        foreach (int port in new[] { 9, 7 })
                        {
                            try { udp.Send(packet, packet.Length, new IPEndPoint(target, port)); sent++; }
                            catch (SocketException) { }
                        }
                    }
                }
            }
            return sent;
        }
    }
}
