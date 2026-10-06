using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace ImacDisplay
{
    internal sealed class MacEndpoint
    {
        public string Name;
        public IPAddress Address;
        public int Port;

        public override string ToString()
        {
            return string.Format("{0} ({1}:{2})", Name, Address, Port);
        }
    }

    /*
     Finds LaptopScreen via mDNS/DNS-SD (Bonjour). Uses legacy unicast queries from an ephemeral
     port, so responders answer via unicast and we do not compete with Windows for UDP 5353.
     */
    internal static class Discovery
    {
        const string ServiceType = "_laptopscreen._tcp.local";
        static readonly IPEndPoint Group = new IPEndPoint(IPAddress.Parse("224.0.0.251"), 5353);
        static readonly Random Ids = new Random();

        public static List<MacEndpoint> Find(int timeoutMs)
        {
            var instances = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var services = new Dictionary<string, KeyValuePair<string, int>>(StringComparer.OrdinalIgnoreCase);
            var hosts = new Dictionary<string, List<IPAddress>>(StringComparer.OrdinalIgnoreCase);
            var sockets = new List<UdpClient>();
            try
            {
                foreach (var local in LocalAddresses())
                {
                    try
                    {
                        var socket = new UdpClient(new IPEndPoint(local, 0));
                        socket.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, local.GetAddressBytes());
                        socket.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 255);
                        sockets.Add(socket);
                    }
                    catch (SocketException) { }
                }
                Query(sockets, ServiceType, 12);
                Collect(sockets, timeoutMs / 2, instances, services, hosts);
                /* ask explicitly for SRV and A records the responders did not volunteer */
                for (int round = 0; round < 2; round++)
                {
                    bool asked = false;
                    foreach (var instance in instances)
                    {
                        KeyValuePair<string, int> service;
                        if (!services.TryGetValue(instance, out service)) { Query(sockets, instance, 33); asked = true; }
                        else if (!hosts.ContainsKey(service.Key)) { Query(sockets, service.Key, 1); asked = true; }
                    }
                    if (!asked) break;
                    Collect(sockets, timeoutMs / 4, instances, services, hosts);
                }
            }
            finally
            {
                foreach (var socket in sockets) socket.Close();
            }

            var result = new List<MacEndpoint>();
            foreach (var entry in services)
            {
                List<IPAddress> addresses;
                if (!hosts.TryGetValue(entry.Value.Key, out addresses)) continue;
                foreach (var address in addresses)
                {
                    if (result.Any(e => e.Address.Equals(address) && e.Port == entry.Value.Value)) continue;
                    var endpoint = new MacEndpoint();
                    endpoint.Name = entry.Key.Split('.')[0];
                    endpoint.Address = address;
                    endpoint.Port = entry.Value.Value;
                    result.Add(endpoint);
                }
            }
            /* prefer the direct cable (link-local addresses) */
            return result.OrderBy(e => IsLinkLocal(e.Address) ? 0 : 1).ToList();
        }

        static bool IsLinkLocal(IPAddress address)
        {
            byte[] b = address.GetAddressBytes();
            return b.Length == 4 && b[0] == 169 && b[1] == 254;
        }

        static List<IPAddress> LocalAddresses()
        {
            var list = new List<IPAddress>();
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up || !nic.SupportsMulticast) continue;
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback || nic.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;
                foreach (var unicast in nic.GetIPProperties().UnicastAddresses)
                    if (unicast.Address.AddressFamily == AddressFamily.InterNetwork) list.Add(unicast.Address);
            }
            return list;
        }

        static void Query(List<UdpClient> sockets, string name, int type)
        {
            var packet = new List<byte>();
            int id;
            lock (Ids) id = Ids.Next(1, 65535);
            foreach (int value in new[] { id, 0, 1, 0, 0, 0 }) AddU16(packet, value);
            foreach (var label in name.TrimEnd('.').Split('.'))
            {
                byte[] bytes = Encoding.UTF8.GetBytes(label);
                packet.Add((byte)bytes.Length);
                packet.AddRange(bytes);
            }
            packet.Add(0);
            AddU16(packet, type);
            AddU16(packet, 1);
            byte[] data = packet.ToArray();
            foreach (var socket in sockets)
            {
                try { socket.Send(data, data.Length, Group); } catch (SocketException) { }
            }
        }

        static void Collect(List<UdpClient> sockets, int milliseconds, HashSet<string> instances,
            Dictionary<string, KeyValuePair<string, int>> services, Dictionary<string, List<IPAddress>> hosts)
        {
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(milliseconds);
            while (DateTime.UtcNow < deadline)
            {
                bool received = false;
                foreach (var socket in sockets)
                {
                    while (socket.Available > 0)
                    {
                        received = true;
                        IPEndPoint from = null;
                        byte[] data;
                        try { data = socket.Receive(ref from); }
                        catch (SocketException) { break; }
                        try { Parse(data, instances, services, hosts); }
                        catch (IndexOutOfRangeException) { }
                    }
                }
                if (!received) Thread.Sleep(15);
            }
        }

        static void Parse(byte[] d, HashSet<string> instances,
            Dictionary<string, KeyValuePair<string, int>> services, Dictionary<string, List<IPAddress>> hosts)
        {
            int questions = U16(d, 4);
            int records = U16(d, 6) + U16(d, 8) + U16(d, 10);
            int pos = 12;
            for (int i = 0; i < questions; i++)
            {
                ReadName(d, ref pos);
                pos += 4;
            }
            for (int i = 0; i < records; i++)
            {
                string name = ReadName(d, ref pos);
                int type = U16(d, pos);
                int length = U16(d, pos + 8);
                int start = pos + 10;
                if (type == 12 && name.Equals(ServiceType, StringComparison.OrdinalIgnoreCase))
                {
                    int p = start;
                    instances.Add(ReadName(d, ref p));
                }
                else if (type == 33 && name.EndsWith("." + ServiceType, StringComparison.OrdinalIgnoreCase))
                {
                    int p = start + 6;
                    string target = ReadName(d, ref p);
                    services[name] = new KeyValuePair<string, int>(target, U16(d, start + 4));
                    instances.Add(name);
                }
                else if (type == 1 && length == 4)
                {
                    List<IPAddress> list;
                    if (!hosts.TryGetValue(name, out list))
                    {
                        list = new List<IPAddress>();
                        hosts[name] = list;
                    }
                    var address = new IPAddress(new[] { d[start], d[start + 1], d[start + 2], d[start + 3] });
                    if (!list.Contains(address)) list.Add(address);
                }
                pos = start + length;
            }
        }

        static string ReadName(byte[] d, ref int pos)
        {
            var labels = new List<string>();
            int p = pos;
            bool jumped = false;
            for (int guard = 0; guard < 128; guard++)
            {
                int length = d[p];
                if (length == 0)
                {
                    p++;
                    break;
                }
                if ((length & 0xC0) == 0xC0)
                {
                    /* compression pointer: continue at the referenced offset */
                    if (!jumped)
                    {
                        pos = p + 2;
                        jumped = true;
                    }
                    p = ((length & 0x3F) << 8) | d[p + 1];
                    continue;
                }
                labels.Add(Encoding.UTF8.GetString(d, p + 1, length));
                p += 1 + length;
            }
            if (!jumped) pos = p;
            return string.Join(".", labels);
        }

        static int U16(byte[] d, int p)
        {
            return (d[p] << 8) | d[p + 1];
        }

        static void AddU16(List<byte> packet, int value)
        {
            packet.Add((byte)((value >> 8) & 0xFF));
            packet.Add((byte)(value & 0xFF));
        }
    }
}
