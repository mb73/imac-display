using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace ImacDisplay
{
    /*
     Shares plain text with the Mac, following the user's focus: Windows clipboard changes go to the Mac
     only while LaptopScreen is in front there (or was until a moment ago, when the copy raced the switch
     to a Mac app); the Mac reports that with "FOCUS 1|0". Text from the Mac goes onto the Windows
     clipboard. Lines in both directions:
       "CLIP+ <base64>" for leading chunks, "CLIP <base64>" for the last one (UTF-8, \n line breaks)
     Chunks keep every line well below the 64 KB line buffer on both sides. Content that password
     managers keep out of clipboard history and viewers is never sent.
     */
    internal sealed class ClipboardSync
    {
        const int ChunkBytes = 32 * 1024, MaxBytes = 4 << 20;
        static readonly string[] PrivateFormats = { "ExcludeClipboardContentFromMonitorProcessing", "Clipboard Viewer Ignore" };

        uint seenSequence = Native.GetClipboardSequenceNumber();
        string lastText;
        readonly MemoryStream incoming = new MemoryStream();
        bool overflow;
        bool macFocused;
        bool enabled = true;
        DateTime focusLost = DateTime.MinValue;

        /* The window's switch, during a session too; what was copied while it was off stays on the laptop */
        public bool Enabled
        {
            get { return enabled; }
            set
            {
                if (value && !enabled) seenSequence = Native.GetClipboardSequenceNumber();
                enabled = value;
            }
        }

        /* "FOCUS 1" or "FOCUS 0" from the Mac */
        public void SetFocus(bool focused)
        {
            if (macFocused && !focused) focusLost = DateTime.UtcNow;
            macFocused = focused;
        }

        /* Clipboard text that changed since the last call, framed for the Mac; null if there is nothing to send */
        public List<string> Poll()
        {
            if (!enabled) return null;
            uint sequence = Native.GetClipboardSequenceNumber();
            if (sequence == seenSequence) return null;
            seenSequence = sequence;
            /* copied while the user works elsewhere: stays on the laptop */
            if (!macFocused && (DateTime.UtcNow - focusLost).TotalSeconds > 3) return null;
            string text = ReadText();
            if (text == null || text == lastText) return null;
            lastText = text;
            byte[] data = Encoding.UTF8.GetBytes(text.Replace("\r\n", "\n"));
            if (data.Length > MaxBytes) return null;
            var lines = new List<string>();
            for (int offset = 0; offset < data.Length; offset += ChunkBytes)
            {
                int count = Math.Min(ChunkBytes, data.Length - offset);
                lines.Add((offset + count < data.Length ? "CLIP+ " : "CLIP ") + Convert.ToBase64String(data, offset, count));
            }
            return lines;
        }

        /* "CLIP+ …" or "CLIP …" from the Mac; false for any other line */
        public bool Handle(string line)
        {
            bool more = line.StartsWith("CLIP+ ", StringComparison.Ordinal);
            if (!more && !line.StartsWith("CLIP ", StringComparison.Ordinal)) return false;
            try
            {
                byte[] chunk = Convert.FromBase64String(line.Substring(more ? 6 : 5));
                if (incoming.Length + chunk.Length > MaxBytes) overflow = true;
                else incoming.Write(chunk, 0, chunk.Length);
            }
            catch (FormatException) { overflow = true; }
            if (more) return true;
            if (!overflow && enabled) Apply(Encoding.UTF8.GetString(incoming.ToArray()));
            incoming.SetLength(0);
            overflow = false;
            return true;
        }

        void Apply(string text)
        {
            text = text.Replace("\r\n", "\n").Replace("\n", "\r\n");
            if (text.Length == 0) return;
            OnStaThread(delegate { Clipboard.SetText(text, TextDataFormat.UnicodeText); });
            lastText = text;
            /* our own change must not travel back to the Mac */
            seenSequence = Native.GetClipboardSequenceNumber();
        }

        static string ReadText()
        {
            string text = null;
            OnStaThread(delegate
            {
                IDataObject data = Clipboard.GetDataObject();
                if (data == null) return;
                foreach (string format in PrivateFormats) if (data.GetDataPresent(format)) return;
                if (data.GetDataPresent(DataFormats.UnicodeText)) text = data.GetData(DataFormats.UnicodeText) as string;
            });
            return string.IsNullOrEmpty(text) ? null : text;
        }

        /* The clipboard API needs an STA thread; SetText flushes its data, so the text outlives the thread */
        static void OnStaThread(ThreadStart action)
        {
            var thread = new Thread(delegate ()
            {
                try { action(); }
                catch (ExternalException) { }  // another program holds the clipboard right now
                catch (ThreadStateException) { }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();
            thread.Join(3000);
        }
    }
}
