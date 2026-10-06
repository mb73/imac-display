using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace ImacDisplay
{
    /*
     Updates this installation from a release zip (GitHub "Code -> Download ZIP"). The browser does
     the download; this class finds the zip in the Downloads folder, checks its VERSION and copies the
     files over the installation. A running exe cannot be overwritten but can be renamed, so it
     becomes imac-display.exe.old and keeps running.
     Settings and the pairing code (%APPDATA%) and ffmpeg (tools\) stay untouched.
     */
    internal static class Updater
    {
        public const string DownloadUrl = "https://github.com/mb73/imac-display/archive/refs/heads/main.zip";
        /* set for the process started after an update, so it does not offer the same zip again */
        public const string RestartedVariable = "IMAC_DISPLAY_UPDATED";

        /* mirrored exactly: a source file dropped from the release must not linger (the Mac would compile it) */
        static readonly string[] MirroredFolders = { @"mac\Sources", @"windows\src", @"windows\dev" };

        static string version;

        /* Installed version: the VERSION file next to the exe, "0.0.0" if it is missing */
        public static string Version
        {
            get
            {
                if (version == null) version = ReadVersion(Program.BaseDirectory) ?? "0.0.0";
                return version;
            }
        }

        public static string ReadVersion(string folder)
        {
            try { return Sanitize(File.ReadAllText(Path.Combine(folder, "VERSION"))); }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }

        /* Versions travel in protocol lines: ASCII letters, digits, dots and dashes only */
        static string Sanitize(string text)
        {
            string trimmed = text.Trim();
            if (trimmed.Length == 0 || trimmed.Length > 32) return null;
            foreach (char c in trimmed)
                if (c > 127 || (!char.IsLetterOrDigit(c) && c != '.' && c != '-')) return null;
            return trimmed;
        }

        /* Compares dotted version numbers: negative if a is older than b, 0 if equal, positive if newer */
        public static int Compare(string a, string b)
        {
            int[] x = Numbers(a), y = Numbers(b);
            for (int i = 0; i < Math.Max(x.Length, y.Length); i++)
            {
                int p = i < x.Length ? x[i] : 0, q = i < y.Length ? y[i] : 0;
                if (p != q) return p < q ? -1 : 1;
            }
            return 0;
        }

        static int[] Numbers(string version)
        {
            return version.Split('.').Select(part =>
            {
                int digits = 0;
                while (digits < part.Length && part[digits] >= '0' && part[digits] <= '9') digits++;
                int value;
                return int.TryParse(part.Substring(0, digits), NumberStyles.None, CultureInfo.InvariantCulture, out value) ? value : 0;
            }).ToArray();
        }

        public static bool IsGitCheckout(string folder)
        {
            string git = Path.Combine(folder, ".git");
            return Directory.Exists(git) || File.Exists(git);
        }

        public static string DownloadsFolder()
        {
            Guid id = Native.FOLDERID_Downloads;
            IntPtr path;
            if (Native.SHGetKnownFolderPath(ref id, 0, IntPtr.Zero, out path) != 0) return null;
            try { return Marshal.PtrToStringUni(path); }
            finally { Marshal.FreeCoTaskMem(path); }
        }

        /* At start: offers a newer zip from the Downloads folder (e.g. just fetched from GitHub) */
        public static bool OfferDownloadedUpdate()
        {
            if (IsGitCheckout(Program.BaseDirectory)) return false;
            string newer;
            string zip = FindNewerDownload(DownloadsFolder(), out newer);
            if (zip == null) return false;
            Console.WriteLine("In deinen Downloads liegt iMac-Display " + newer + " (" + Path.GetFileName(zip) + "), installiert ist " + Version + ".");
            bool installed = Program.Ask("Jetzt installieren?") && Install(zip);
            Console.WriteLine();
            return installed;
        }

        /* --update [zip]: installs a newer version from the given zip, the Downloads folder or the browser */
        public static int Run(string zip)
        {
            int result = RunSteps(zip);
            Program.Pause();
            return result;
        }

        static int RunSteps(string zip)
        {
            if (IsGitCheckout(Program.BaseDirectory))
            {
                Console.WriteLine("Dieser Ordner ist ein Git-Arbeitsverzeichnis: bitte mit git pull aktualisieren.");
                return 1;
            }
            if (zip != null)
            {
                string offered = ZipVersion(zip);
                if (offered == null)
                {
                    Console.WriteLine(Path.GetFileName(zip) + " ist keine Zip-Datei von imac-display.");
                    return 1;
                }
                if (Compare(offered, Version) <= 0 &&
                    !Program.Ask("Die Zip-Datei enthält Version " + offered + ", installiert ist " + Version + ". Trotzdem installieren?"))
                    return 0;
            }
            else
            {
                string newer;
                zip = FindNewerDownload(DownloadsFolder(), out newer) ?? DownloadInBrowser();
                if (zip == null) return 1;
            }
            return Install(zip) ? 0 : 1;
        }

        /* Opens the download in the browser and waits for the zip to arrive in the Downloads folder */
        static string DownloadInBrowser()
        {
            string folder = DownloadsFolder();
            Console.WriteLine("Installiert ist Version " + Version + ". Ich öffne den Download der aktuellen Version im Browser.");
            Console.WriteLine("Speichert der Browser die Datei woanders als in " + folder + ", zieh sie einfach auf update.cmd.");
            Console.WriteLine();
            DateTime started = DateTime.Now.AddSeconds(-2);
            try { Process.Start(DownloadUrl); }
            catch (Win32Exception) { Console.WriteLine("Bitte im Browser öffnen: " + DownloadUrl); }
            Console.Write("Warte auf den Download …");
            for (int second = 0; second < 600 && folder != null; second++)
            {
                Thread.Sleep(1000);
                string newer;
                string zip = FindNewerDownload(folder, out newer);
                if (zip != null)
                {
                    Console.WriteLine(" da.");
                    return zip;
                }
                if (DownloadedInstalledVersion(folder, started))
                {
                    Console.WriteLine();
                    Console.WriteLine("Die heruntergeladene Version ist die installierte (" + Version + "): Es gibt nichts zu tun.");
                    return null;
                }
            }
            Console.WriteLine();
            Console.WriteLine("Es ist kein Download angekommen. Bitte später noch einmal versuchen.");
            return null;
        }

        /* Newest imac-display*.zip in the folder that is newer than the installed version, or null */
        public static string FindNewerDownload(string folder, out string newest)
        {
            newest = null;
            string best = null;
            foreach (string zip in Zips(folder))
            {
                string candidate = ZipVersion(zip);
                if (candidate == null || Compare(candidate, Version) <= 0) continue;
                if (newest != null && Compare(candidate, newest) <= 0) continue;
                newest = candidate;
                best = zip;
            }
            return best;
        }

        /* A zip of the installed version that arrived after we opened the browser: already up to date */
        static bool DownloadedInstalledVersion(string folder, DateTime since)
        {
            foreach (string zip in Zips(folder))
            {
                if (File.GetLastWriteTime(zip) < since && File.GetCreationTime(zip) < since) continue;
                string candidate = ZipVersion(zip);
                if (candidate != null && Compare(candidate, Version) == 0) return true;
            }
            return false;
        }

        static string[] Zips(string folder)
        {
            try
            {
                if (folder != null && Directory.Exists(folder)) return Directory.GetFiles(folder, "imac-display*.zip");
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return new string[0];
        }

        /* Version inside a release zip, or null (not ours, still being written, damaged) */
        static string ZipVersion(string path)
        {
            try
            {
                using (ZipArchive zip = ZipFile.OpenRead(path))
                {
                    string root;
                    return ZipVersion(zip, out root);
                }
            }
            catch (IOException) { return null; }
            catch (InvalidDataException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }

        /* root receives the folder inside the zip that holds VERSION and imac-display.exe ("" or "name/") */
        static string ZipVersion(ZipArchive zip, out string root)
        {
            root = null;
            var names = new HashSet<string>(zip.Entries.Select(e => e.FullName.Replace('\\', '/')), StringComparer.OrdinalIgnoreCase);
            string result = null;
            foreach (ZipArchiveEntry entry in zip.Entries)
            {
                string name = entry.FullName.Replace('\\', '/');
                int slash = name.LastIndexOf('/');
                if (name.Substring(slash + 1) != "VERSION") continue;
                string prefix = name.Substring(0, slash + 1);
                if ((root != null && prefix.Length >= root.Length) || !names.Contains(prefix + "imac-display.exe")) continue;
                string candidate;
                using (var reader = new StreamReader(entry.Open(), Encoding.UTF8)) candidate = Sanitize(reader.ReadToEnd());
                if (candidate == null) continue;
                root = prefix;
                result = candidate;
            }
            return result;
        }

        /* Copies the release in the zip over this installation; first unpacks everything next to it */
        public static bool Install(string zipPath)
        {
            string target = Program.BaseDirectory;
            string staging = Path.Combine(target, ".update");
            try
            {
                string newVersion;
                var files = new List<string>();
                using (ZipArchive zip = ZipFile.OpenRead(zipPath))
                {
                    string root;
                    newVersion = ZipVersion(zip, out root);
                    if (newVersion == null) throw new InvalidDataException(Path.GetFileName(zipPath) + " ist keine Zip-Datei von imac-display.");
                    Console.WriteLine("Installiere iMac-Display " + newVersion + " aus " + Path.GetFileName(zipPath) + " …");
                    if (Directory.Exists(staging)) Directory.Delete(staging, true);
                    foreach (ZipArchiveEntry entry in zip.Entries)
                    {
                        string name = entry.FullName.Replace('\\', '/');
                        if (!name.StartsWith(root, StringComparison.Ordinal) || name.EndsWith("/", StringComparison.Ordinal)) continue;
                        string relative = name.Substring(root.Length).Replace('/', '\\');
                        if (!Installable(relative)) continue;
                        string destination = Path.Combine(staging, relative);
                        Directory.CreateDirectory(Path.GetDirectoryName(destination));
                        entry.ExtractToFile(destination, true);
                        files.Add(relative);
                    }
                    PrintChanges(zip, root);
                }
                foreach (string relative in files) Replace(Path.Combine(staging, relative), Path.Combine(target, relative));
                RemoveStale(target, files);
                Directory.Delete(staging, true);
                Program.Log("iMac-Display ist jetzt auf Version " + newVersion + ".");
                Console.WriteLine("LaptopScreen auf dem Mac zieht beim nächsten Verbinden nach: Der Mac fragt kurz, ob er sich aktualisieren soll.");
                return true;
            }
            catch (IOException ex) { return Failed(ex); }
            catch (InvalidDataException ex) { return Failed(ex); }
            catch (UnauthorizedAccessException ex) { return Failed(ex); }
        }

        static bool Failed(Exception ex)
        {
            Console.WriteLine("Die Aktualisierung ist fehlgeschlagen: " + ex.Message);
            Console.WriteLine("Notfalls die Zip-Datei von Hand entpacken und den Inhalt über diesen Ordner kopieren (tools\\ bleibt).");
            return false;
        }

        /* No absolute paths or "..", and never ffmpeg (tools\), git data or our own staging folder */
        static bool Installable(string relative)
        {
            if (relative.Length == 0 || relative.IndexOfAny(Path.GetInvalidPathChars()) >= 0 || relative.IndexOf(':') >= 0) return false;
            string[] parts = relative.Split('\\');
            if (parts.Any(part => part.Length == 0 || part == "." || part == "..")) return false;
            string first = parts[0].ToLowerInvariant();
            return first != "tools" && first != ".git" && first != ".update";
        }

        static void Replace(string source, string destination)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination));
            if (File.Exists(destination))
            {
                if (destination.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) MoveAside(destination);
                else
                {
                    File.SetAttributes(destination, FileAttributes.Normal);
                    File.Delete(destination);
                }
            }
            File.Move(source, destination);
        }

        /* A running exe cannot be overwritten or deleted, but renamed; the running instance keeps working */
        static void MoveAside(string exe)
        {
            string old = exe + ".old";
            try { if (File.Exists(old)) File.Delete(old); }
            catch (IOException) { old = exe + "." + DateTime.Now.Ticks.ToString(CultureInfo.InvariantCulture) + ".old"; }
            catch (UnauthorizedAccessException) { old = exe + "." + DateTime.Now.Ticks.ToString(CultureInfo.InvariantCulture) + ".old"; }
            File.Move(exe, old);
        }

        static void RemoveStale(string target, List<string> files)
        {
            var wanted = new HashSet<string>(files, StringComparer.OrdinalIgnoreCase);
            foreach (string folder in MirroredFolders)
            {
                string full = Path.Combine(target, folder);
                string prefix = folder + "\\";
                if (!Directory.Exists(full) || !files.Any(f => f.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))) continue;
                foreach (string file in Directory.GetFiles(full, "*", SearchOption.AllDirectories))
                {
                    string relative = file.Substring(target.Length).TrimStart('\\');
                    if (!wanted.Contains(relative)) File.Delete(file);
                }
            }
        }

        /* Shows the changelog sections that are newer than the installed version */
        static void PrintChanges(ZipArchive zip, string root)
        {
            ZipArchiveEntry entry = zip.Entries.FirstOrDefault(e => e.FullName.Replace('\\', '/') == root + "CHANGELOG.md");
            if (entry == null) return;
            var changes = new List<string>();
            using (var reader = new StreamReader(entry.Open(), Encoding.UTF8))
            {
                bool inside = false;
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (line.StartsWith("## [", StringComparison.Ordinal))
                    {
                        int end = line.IndexOf(']');
                        if (end > 4 && Compare(line.Substring(4, end - 4), Version) <= 0) break;
                        inside = true;
                    }
                    if (inside) changes.Add(line.Replace("**", "").Replace("`", ""));
                }
            }
            if (changes.Count == 0) return;
            Console.WriteLine();
            Console.WriteLine("Neu seit Version " + Version + ":");
            foreach (string change in changes) Console.WriteLine("  " + change);
            Console.WriteLine();
        }

        /* Starts the freshly installed exe in this console window; the caller exits right after */
        public static void Restart(string[] args)
        {
            var info = new ProcessStartInfo(Path.Combine(Program.BaseDirectory, "imac-display.exe"), JoinArguments(args));
            info.UseShellExecute = false;
            info.EnvironmentVariables[RestartedVariable] = "1";
            Process.Start(info);
        }

        static string JoinArguments(string[] args)
        {
            return string.Join(" ", args.Select(a =>
                a.Length > 0 && a.IndexOfAny(new[] { ' ', '\t', '"' }) < 0 ? a : "\"" + a.Replace("\"", "\\\"") + "\""));
        }
    }

    /*
     The Mac sources this laptop carries, packed for LaptopScreen's self-update: a zip with VERSION,
     CHANGELOG.md and the mac\ folder. Sent on "GETUPDATE" over the control channel as
       UPDATE <version> <bytes> <sha256> <proof>   then "D <base64>" lines, then "E"
     where proof = hmac(code, "update|<nonceMac>|<nonceAgent>|<version>|<sha256>").
     */
    internal static class MacUpdate
    {
        const int ChunkBytes = 16 * 1024;

        public static byte[] Build(string folder, out string version)
        {
            version = Updater.ReadVersion(folder);
            string mac = Path.Combine(folder, "mac");
            if (version == null || !File.Exists(Path.Combine(mac, "build.sh"))) return null;
            using (var buffer = new MemoryStream())
            {
                using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, true))
                {
                    zip.CreateEntryFromFile(Path.Combine(folder, "VERSION"), "VERSION");
                    string changelog = Path.Combine(folder, "CHANGELOG.md");
                    if (File.Exists(changelog)) zip.CreateEntryFromFile(changelog, "CHANGELOG.md");
                    foreach (string file in Directory.GetFiles(mac, "*", SearchOption.AllDirectories))
                    {
                        string relative = file.Substring(mac.Length + 1).Replace('\\', '/');
                        /* neither a built app bundle nor hidden files such as .DS_Store */
                        if (relative.Split('/').Any(part => part.EndsWith(".app", StringComparison.OrdinalIgnoreCase) || part.StartsWith("."))) continue;
                        zip.CreateEntryFromFile(file, "mac/" + relative);
                    }
                }
                return buffer.ToArray();
            }
        }

        public static List<string> Frame(byte[] archive, string version, Func<string, string, string> proof)
        {
            string sha;
            using (var hash = SHA256.Create()) sha = Crypto.Hex(hash.ComputeHash(archive));
            var lines = new List<string>();
            lines.Add(string.Format(CultureInfo.InvariantCulture, "UPDATE {0} {1} {2} {3}", version, archive.Length, sha, proof(version, sha)));
            for (int offset = 0; offset < archive.Length; offset += ChunkBytes)
                lines.Add("D " + Convert.ToBase64String(archive, offset, Math.Min(ChunkBytes, archive.Length - offset)));
            lines.Add("E");
            return lines;
        }
    }
}
