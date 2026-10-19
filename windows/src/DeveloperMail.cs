using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ImacDisplay
{
    /*
     "E-Mail an den Entwickler" in the log window: a mailto: link to a new mail with as many of the log's newest lines
     as fit. mailto: knows no attachments, and the whole link has to pass Windows and the mail program. Windows 11 hands
     a protocol handler 32,000 characters unchanged, and the new Outlook took a link of 31,959 characters completely,
     line breaks, umlauts and dashes included (both measured on 2026-10-19). Other mail programs are not measured;
     older sources give about 2000 characters for Windows and Outlook and about 4096 for Gmail, so they get 2000.
     */
    internal static class DeveloperMail
    {
        /* the whole link, percent-encoded */
        const int NewOutlookLength = 30000, OtherLength = 2000;
        /* the start of the mail; the log follows after an empty line */
        const string Greeting = "Hallo Marcus,\r\n\r\nich habe bei „imac-display“ folgendes Problem festgestellt: [bitte ausfüllen]\r\n\r\n";

        /* Put together only at run time: address harvesters search the public source and the exe for name@domain */
        public static string Address
        {
            get { return string.Join("@", "mb", "stormlight.de"); }
        }

        /* The link, and how many lines of the log it carries; shown: the log window's lines, in case the log file cannot be read */
        public static string Link(string[] shown, out int count)
        {
            string app = MailtoHandler(Native.ASSOCSTR_APPID);
            int maxLength = app != null && app.StartsWith("Microsoft.OutlookForWindows_", StringComparison.OrdinalIgnoreCase)
                ? NewOutlookLength : OtherLength;
            /* more than fits: every byte of the file takes at least one character in the link */
            List<string> lines = NewestLines(2 * maxLength) ?? new List<string>(shown);
            while (lines.Count > 0 && lines[lines.Count - 1].Length == 0) lines.RemoveAt(lines.Count - 1);

            string head = "mailto:" + Address + "?subject=" + Escape("iMac-Display " + Updater.Version + " – Log") + "&body=" + Escape(Greeting);
            /* the heading names the count: room for the highest it may name */
            int room = maxLength - head.Length - Escape(Heading(lines.Count) + "\r\n").Length;
            var picked = new List<string>();
            for (int i = lines.Count - 1; i >= 0; i--)
            {
                string line = Escape(lines[i] + "\r\n");
                if (line.Length > room) break;
                picked.Add(line);
                room -= line.Length;
            }
            count = picked.Count;
            if (count == 0) return head;
            picked.Reverse();
            return head + Escape(Heading(count) + "\r\n") + string.Concat(picked);
        }

        /* The name of the program Windows opens mailto: links with, or null */
        public static string MailProgram
        {
            get { return MailtoHandler(Native.ASSOCSTR_FRIENDLYAPPNAME); }
        }

        /* What Windows tells about the program for mailto: links (for an app package, its app ID), or null */
        static string MailtoHandler(int what)
        {
            int size = 512;
            var output = new StringBuilder(size);
            return Native.AssocQueryString(Native.ASSOCF_IS_PROTOCOL, what, "mailto", null, output, ref size) == 0 ? output.ToString() : null;
        }

        static string Heading(int count)
        {
            return (count == 1 ? "Die letzte Zeile" : "Die letzten " + count + " Zeilen") + " aus dem Log von iMac-Display "
                + Updater.Version + " unter Windows " + Environment.OSVersion.Version.ToString(3) + ":";
        }

        /* The lines at the end of the log file, earlier runs included; null if it cannot be read */
        static List<string> NewestLines(int bytes)
        {
            try
            {
                /* the agent may be writing or rotating the log right now */
                using (var file = new FileStream(Program.LogFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    long start = Math.Max(0, file.Length - bytes);
                    var buffer = new byte[file.Length - start];
                    file.Position = start;
                    int read = 0, n;
                    while (read < buffer.Length && (n = file.Read(buffer, read, buffer.Length - read)) > 0) read += n;
                    string text = Encoding.UTF8.GetString(buffer, 0, read).TrimStart((char)0xFEFF);  // the file starts with a BOM
                    var lines = new List<string>(text.Replace("\r", "").Split('\n'));
                    if (start > 0) lines.RemoveAt(0);  // cut off at its start
                    return lines;
                }
            }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }

        /* Percent-encoded UTF-8 as RFC 6068 asks for, only letters, digits and - . _ ~ as they are; Uri.EscapeDataString throws on a lone surrogate */
        static string Escape(string text)
        {
            var escaped = new StringBuilder();
            foreach (byte b in Encoding.UTF8.GetBytes(text))
            {
                if ((b >= 'a' && b <= 'z') || (b >= 'A' && b <= 'Z') || (b >= '0' && b <= '9') || b == '-' || b == '.' || b == '_' || b == '~')
                    escaped.Append((char)b);
                else escaped.Append('%').Append(b.ToString("X2"));
            }
            return escaped.ToString();
        }
    }
}
