using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace ImacDisplay
{
    internal static class Crypto
    {
        /* Key material: upper-cased pairing code without separators (must match the Mac app) */
        public static string Normalize(string code)
        {
            var builder = new StringBuilder();
            foreach (char c in code.ToUpperInvariant())
                if (char.IsLetterOrDigit(c)) builder.Append(c);
            return builder.ToString();
        }

        public static string Hmac(string code, string message)
        {
            using (var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(Normalize(code))))
                return Hex(hmac.ComputeHash(Encoding.UTF8.GetBytes(message)));
        }

        public static string RandomHex(int bytes)
        {
            var buffer = new byte[bytes];
            using (var random = new RNGCryptoServiceProvider()) random.GetBytes(buffer);
            return Hex(buffer);
        }

        public static string Hex(byte[] bytes)
        {
            var builder = new StringBuilder(bytes.Length * 2);
            foreach (byte b in bytes) builder.Append(b.ToString("x2"));
            return builder.ToString();
        }

        /* Constant-time comparison */
        public static bool Equal(string a, string b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            int difference = 0;
            for (int i = 0; i < a.Length; i++) difference |= a[i] ^ b[i];
            return difference == 0;
        }
    }

    /*
     Control connection to LaptopScreen on the Mac. Only outbound: the laptop's firewall blocks
     inbound connections for standard users. Handshake (lines end with "\n"):
       Mac   -> "LAPTOPSCREEN 1 <nonceMac>"
       Agent -> "HELLO <nonceAgent> <hmac(code, "agent|<nonceMac>|<nonceAgent>")>"
       Mac   -> "WELCOME <hmac(code, "mac|<nonceAgent>|<nonceMac>")> <videoPort>"   or "DENIED"
     */
    internal sealed class ControlClient : IDisposable
    {
        public const string Denied = "denied";

        readonly Socket socket;
        readonly string code;
        readonly byte[] buffer = new byte[1 << 16];
        int length;
        readonly object sendLock = new object();
        string nonceMac, nonceAgent;

        public IPAddress Address { get; private set; }
        public int VideoPort { get; private set; }

        ControlClient(Socket socket, IPAddress address, string code)
        {
            this.socket = socket;
            this.code = code;
            Address = address;
        }

        /* Proves to the Mac that an update comes from the paired laptop, bound to this very session */
        public string UpdateProof(string version, string sha256)
        {
            return Crypto.Hmac(code, "update|" + nonceMac + "|" + nonceAgent + "|" + version + "|" + sha256);
        }

        public static ControlClient Connect(MacEndpoint endpoint, string code, out string error)
        {
            error = null;
            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            socket.NoDelay = true;
            ControlClient client = null;
            try
            {
                IAsyncResult pending = socket.BeginConnect(endpoint.Address, endpoint.Port, null, null);
                if (!pending.AsyncWaitHandle.WaitOne(3000))
                {
                    error = "keine Antwort";
                    socket.Close();
                    return null;
                }
                socket.EndConnect(pending);
                client = new ControlClient(socket, endpoint.Address, code);

                string greeting = client.ReadLine(3000);
                string[] hello = greeting == null ? new string[0] : greeting.Split(' ');
                if (hello.Length != 3 || hello[0] != "LAPTOPSCREEN") throw new IOException("unerwartete Begrüßung");
                string nonceMac = hello[2];
                string nonceAgent = Crypto.RandomHex(16);
                client.nonceMac = nonceMac;
                client.nonceAgent = nonceAgent;
                client.Send("HELLO " + nonceAgent + " " + Crypto.Hmac(code, "agent|" + nonceMac + "|" + nonceAgent));

                string reply = client.ReadLine(3000);
                if (reply == "DENIED")
                {
                    error = Denied;
                    client.Dispose();
                    return null;
                }
                string[] welcome = reply == null ? new string[0] : reply.Split(' ');
                if (welcome.Length != 3 || welcome[0] != "WELCOME" ||
                    !Crypto.Equal(welcome[1], Crypto.Hmac(code, "mac|" + nonceAgent + "|" + nonceMac)))
                    throw new IOException("der Mac konnte den Kopplungscode nicht nachweisen");
                client.VideoPort = int.Parse(welcome[2]);
                return client;
            }
            catch (Exception ex)
            {
                if (error == null) error = ex.Message;
                if (client != null) client.Dispose();
                else socket.Close();
                return null;
            }
        }

        /* Next line, or null on timeout; throws IOException when the connection is gone */
        public string ReadLine(int timeoutMs)
        {
            while (true)
            {
                int newline = Array.IndexOf(buffer, (byte)'\n', 0, length);
                if (newline >= 0)
                {
                    string line = Encoding.ASCII.GetString(buffer, 0, newline).TrimEnd('\r');
                    Buffer.BlockCopy(buffer, newline + 1, buffer, 0, length - newline - 1);
                    length -= newline + 1;
                    return line;
                }
                if (length == buffer.Length) throw new IOException("Zeile zu lang");
                if (!socket.Poll(timeoutMs * 1000, SelectMode.SelectRead)) return null;
                int received = socket.Receive(buffer, length, buffer.Length - length, SocketFlags.None);
                if (received <= 0) throw new IOException("Verbindung vom Mac beendet");
                length += received;
            }
        }

        public void Send(string line)
        {
            byte[] data = Encoding.ASCII.GetBytes(line + "\n");
            lock (sendLock) socket.Send(data);
        }

        public void Dispose()
        {
            try { socket.Shutdown(SocketShutdown.Both); } catch (SocketException) { } catch (ObjectDisposedException) { }
            socket.Close();
        }
    }
}
