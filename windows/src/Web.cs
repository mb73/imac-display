using System;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace ImacDisplay
{
    /* HTTPS requests to GitHub: through the system proxy, with TLS 1.2 or newer */
    internal static class Web
    {
        static Web()
        {
            /* without "target 4.7 or newer" .NET Framework would still offer TLS 1.0, which GitHub refuses */
            if (ServicePointManager.SecurityProtocol != SecurityProtocolType.SystemDefault)
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            /* company proxies that want the Windows sign-in */
            IWebProxy proxy = WebRequest.DefaultWebProxy;
            if (proxy != null) proxy.Credentials = CredentialCache.DefaultCredentials;
        }

        static HttpWebRequest Request(string url)
        {
            var request = (HttpWebRequest)WebRequest.Create(url);
            request.UserAgent = "iMac-Display/" + Updater.Version;
            request.Timeout = 20000;
            request.ReadWriteTimeout = 30000;
            return request;
        }

        /* A small text file, e.g. VERSION; throws WebException or IOException */
        public static string Text(string url)
        {
            using (WebResponse response = Request(url).GetResponse())
            using (var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
                return reader.ReadToEnd();
        }

        /*
         Saves url to path. progress receives the bytes so far and the total (-1 if unknown) a few times
         a second; hash (optional) sees every byte. Throws WebException or IOException.
         */
        public static void File(string url, string path, Action<long, long> progress, HashAlgorithm hash)
        {
            using (WebResponse response = Request(url).GetResponse())
            using (Stream input = response.GetResponseStream())
            using (var output = new FileStream(path, FileMode.Create, FileAccess.Write))
            {
                long total = response.ContentLength, done = 0;
                var buffer = new byte[81920];
                DateTime reported = DateTime.MinValue;
                int read;
                while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    output.Write(buffer, 0, read);
                    if (hash != null) hash.TransformBlock(buffer, 0, read, null, 0);
                    done += read;
                    if (progress != null && (DateTime.UtcNow - reported).TotalMilliseconds >= 200)
                    {
                        reported = DateTime.UtcNow;
                        progress(done, total);
                    }
                }
                if (hash != null) hash.TransformFinalBlock(buffer, 0, 0);
                if (total >= 0 && done != total) throw new IOException("Die Verbindung brach nach " + done + " von " + total + " Bytes ab.");
                if (progress != null) progress(done, total);
            }
        }
    }
}
