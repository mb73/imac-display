using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace ImacDisplay
{
    /* A newer version: as a zip on this laptop, or (Zip null) still on GitHub */
    internal sealed class UpdateOffer
    {
        public readonly string Version, Zip;

        public UpdateOffer(string version, string zip)
        {
            Version = version;
            Zip = zip;
        }
    }

    /*
     Updates this installation from a release zip (GitHub "Code -> Download ZIP"). The window asks
     GitHub for the current VERSION and watches the Downloads folder for a newer zip; it fetches the
     zip itself (or lets the browser do it when that fails) and calls Install, which copies the files
     over the installation. A running exe cannot be overwritten but can be renamed, so it becomes
     imac-display.exe.old and keeps running until the new version has started.
     Settings and the pairing code (%APPDATA%) and ffmpeg (tools\) stay untouched.
     */
    internal static class Updater
    {
        public const string DownloadUrl = "https://github.com/mb73/imac-display/archive/refs/heads/main.zip";
        public const string VersionUrl = "https://raw.githubusercontent.com/mb73/imac-display/main/VERSION";
        /* set for the process started after an update: "display" if it takes over the Mac display, else "1" */
        public const string RestartedVariable = "IMAC_DISPLAY_UPDATED";

        /* mirrored exactly: a source file dropped from the release must not linger (the Mac would compile it) */
        static readonly string[] MirroredFolders = { @"mac\Sources", @"windows\src", @"windows\dev" };

        /* zips seen in the Downloads folder (length, write time, version): each is opened once, again only when it changes */
        static readonly Dictionary<string, Tuple<long, DateTime, string>> seen = new Dictionary<string, Tuple<long, DateTime, string>>(StringComparer.OrdinalIgnoreCase);

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

        /* The version on GitHub, or null if GitHub cannot be reached; error says why */
        public static string LatestVersion(out string error)
        {
            error = null;
            try
            {
                string latest = Sanitize(Web.Text(VersionUrl));
                if (latest == null) error = "unerwartete Antwort";
                return latest;
            }
            catch (WebException ex) { error = ex.Message; }
            catch (IOException ex) { error = ex.Message; }
            return null;
        }

        /* A newer zip in the Downloads folder or, with online, a newer version on GitHub; null if there is none */
        public static UpdateOffer FindOffer(bool online, out string error)
        {
            error = null;
            string newer;
            string zip = FindNewerDownload(DownloadsFolder(), out newer);
            UpdateOffer offer = zip != null ? new UpdateOffer(newer, zip) : null;
            if (!online) return offer;
            string latest = LatestVersion(out error);
            if (latest != null && Compare(latest, Version) > 0 && (offer == null || Compare(latest, offer.Version) > 0))
                offer = new UpdateOffer(latest, null);
            return offer;
        }

        /* Fetches the current release into %TEMP%: its path and the version inside, or null and the reason */
        public static string DownloadRelease(Action<long, long> progress, out string version, out string error)
        {
            version = null;
            error = null;
            string path = Path.Combine(Path.GetTempPath(), "imac-display-update.zip");
            try
            {
                Web.File(DownloadUrl, path, progress, null);
                /* opened here rather than by ZipVersion: a virus scanner that refuses the file says so in its error */
                using (ZipArchive zip = ZipFile.OpenRead(path))
                {
                    string root;
                    version = ZipVersion(zip, out root);
                }
            }
            catch (WebException ex) { error = ex.Message; }
            catch (IOException ex) { error = ex.Message; }
            catch (InvalidDataException) { }  // no zip at all: the message below
            catch (UnauthorizedAccessException ex) { error = ex.Message; }
            if (version != null) return path;
            if (error == null) error = "Die heruntergeladene Datei ist keine Zip-Datei von imac-display.";
            DeleteQuietly(path);
            return null;
        }

        public static void DeleteQuietly(string path)
        {
            try { File.Delete(path); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        /* Newest imac-display*.zip in the folder that is newer than the installed version, or null */
        public static string FindNewerDownload(string folder, out string newest)
        {
            newest = null;
            string best = null;
            foreach (string zip in Zips(folder))
            {
                string candidate = CachedZipVersion(zip);
                if (candidate == null || Compare(candidate, Version) <= 0) continue;
                if (newest != null && Compare(candidate, newest) <= 0) continue;
                newest = candidate;
                best = zip;
            }
            return best;
        }

        static string CachedZipVersion(string path)
        {
            try
            {
                var file = new FileInfo(path);
                long length = file.Length;
                DateTime written = file.LastWriteTimeUtc;
                Tuple<long, DateTime, string> known;
                lock (seen)
                {
                    if (seen.TryGetValue(path, out known) && known.Item1 == length && known.Item2 == written) return known.Item3;
                }
                /* e.g. the empty placeholder a browser creates while the download is still running */
                string found = ZipVersion(path);
                lock (seen) seen[path] = Tuple.Create(length, written, found);
                return found;
            }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
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
        public static string ZipVersion(string path)
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

        /* Copies the release in the zip over this installation (unpacked next to it first); null if that worked, else the reason */
        public static string Install(string zipPath)
        {
            string target = Program.BaseDirectory;
            string staging = Path.Combine(target, ".update");
            bool exeRestored = false;
            try
            {
                string newVersion;
                var files = new List<string>();
                using (ZipArchive zip = ZipFile.OpenRead(zipPath))
                {
                    string root;
                    newVersion = ZipVersion(zip, out root);
                    if (newVersion == null) throw new InvalidDataException(Path.GetFileName(zipPath) + " ist keine Zip-Datei von imac-display.");
                    Program.Log("Installiere iMac-Display " + newVersion + " aus " + Path.GetFileName(zipPath) + " …");
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
                }
                /* the exe first: if a scanner grabs it, nothing else changed yet and the running version comes back from its .old */
                /* VERSION last: whatever breaks off before it, the next start still has the old number and offers the update again */
                files.Sort((a, b) => InstallOrder(a).CompareTo(InstallOrder(b)));
                string exe = Path.Combine(target, "imac-display.exe");
                string exeAside = null;
                try
                {
                    foreach (string relative in files)
                    {
                        string destination = Path.Combine(target, relative);
                        /* noted before the new exe moves in: that move is what a scanner holding or removing it makes fail */
                        if (string.Equals(destination, exe, StringComparison.OrdinalIgnoreCase) && File.Exists(exe)) exeAside = MoveAside(exe);
                        Replace(Path.Combine(staging, relative), destination);
                    }
                }
                catch
                {
                    exeRestored = RestoreExe(exe, exeAside);
                    throw;
                }
                /* the update is in place: what cannot be tidied up from here on is logged, it does not make the update fail */
                RemoveStale(target, files);
                try { Directory.Delete(staging, true); }
                catch (IOException ex) { Program.Log("Der Ordner .update ließ sich nicht löschen: " + ex.Message); }
                catch (UnauthorizedAccessException ex) { Program.Log("Der Ordner .update ließ sich nicht löschen: " + ex.Message); }
                Program.Log("iMac-Display ist jetzt auf Version " + newVersion + ". LaptopScreen auf dem Mac fragt beim nächsten Verbinden, ob es nachziehen soll.");
                return null;
            }
            catch (IOException ex) { return Failed(ex, exeRestored); }
            catch (InvalidDataException ex) { return Failed(ex, exeRestored); }
            catch (UnauthorizedAccessException ex) { return Failed(ex, exeRestored); }
        }

        /* The advice after a failed update, and after one whose restart found no exe */
        public const string MissingExe = "imac-display.exe fehlt jetzt, vermutlich hat ein Virenscanner sie entfernt. Ein Doppelklick auf exe-missing.cmd im Programmordner baut sie neu.";

        static string Failed(Exception ex, bool exeRestored)
        {
            Program.Log("Die Aktualisierung ist fehlgeschlagen: " + ex.Message);
            string hint;
            if (exeRestored) hint = "Die neue imac-display.exe ließ sich nicht an ihren Platz bringen, vielleicht wegen eines Virenscanners. Die bisherige Version läuft weiter.";
            else if (File.Exists(Path.Combine(Program.BaseDirectory, "imac-display.exe"))) hint = "Notfalls die Zip-Datei von Hand entpacken und den Inhalt über den Programmordner kopieren (tools\\ bleibt).";
            else hint = MissingExe;
            return ex.Message + "\r\n\r\n" + hint;
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

        /* 0 for imac-display.exe, 2 for VERSION, 1 for the rest: the order List.Sort installs them in */
        static int InstallOrder(string relative)
        {
            if (string.Equals(relative, "imac-display.exe", StringComparison.OrdinalIgnoreCase)) return 0;
            return string.Equals(relative, "VERSION", StringComparison.OrdinalIgnoreCase) ? 2 : 1;
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

        /* A running exe cannot be overwritten or deleted, but renamed; the running instance keeps working. Returns the new path. */
        static string MoveAside(string exe)
        {
            string old = exe + ".old";
            try { if (File.Exists(old)) File.Delete(old); }
            catch (IOException) { old = exe + "." + DateTime.Now.Ticks.ToString(CultureInfo.InvariantCulture) + ".old"; }
            catch (UnauthorizedAccessException) { old = exe + "." + DateTime.Now.Ticks.ToString(CultureInfo.InvariantCulture) + ".old"; }
            File.Move(exe, old);
            return old;
        }

        /*
         After a failed update that left no exe behind (a virus scanner grabbing the fresh one is the likely
         cause), moves the running version back from its .old. That .old is this very process's image, so it is
         the exact version that was just working; nothing is built or fetched. Does nothing if the exe is there
         after all or its .old is gone too. True if it moved the exe back.
        */
        static bool RestoreExe(string exe, string aside)
        {
            try
            {
                if (aside == null || File.Exists(exe) || !File.Exists(aside)) return false;
                File.Move(aside, exe);
                Program.Log("Die Aktualisierung wurde abgebrochen; die laufende Version bleibt erhalten.");
                return true;
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }

        /* Deletes what the mirrored folders hold beyond the release; a file that will not go is logged and stays */
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
                    if (wanted.Contains(relative)) continue;
                    try
                    {
                        File.SetAttributes(file, FileAttributes.Normal);
                        File.Delete(file);
                    }
                    catch (IOException ex) { Program.Log("Die veraltete Datei " + relative + " ließ sich nicht löschen: " + ex.Message); }
                    catch (UnauthorizedAccessException ex) { Program.Log("Die veraltete Datei " + relative + " ließ sich nicht löschen: " + ex.Message); }
                }
            }
        }

        /* The changelog sections newer than the installed version, as plain text for the update dialog */
        public static string Changes(string zipPath)
        {
            try
            {
                using (ZipArchive zip = ZipFile.OpenRead(zipPath))
                {
                    string root;
                    if (ZipVersion(zip, out root) == null) return "";
                    ZipArchiveEntry entry = zip.Entries.FirstOrDefault(e => e.FullName.Replace('\\', '/') == root + "CHANGELOG.md");
                    if (entry == null) return "";
                    var changes = new StringBuilder();
                    using (var reader = new StreamReader(entry.Open(), Encoding.UTF8))
                    {
                        bool inside = false;
                        string line;
                        while ((line = reader.ReadLine()) != null)
                        {
                            if (line.StartsWith("## [", StringComparison.Ordinal))
                            {
                                int end = line.IndexOf(']');
                                if (end < 5) continue;
                                if (Compare(line.Substring(4, end - 4), Version) <= 0) break;
                                inside = true;
                                line = "Version " + line.Substring(4, end - 4) + line.Substring(end + 1);
                            }
                            else if (line.StartsWith("### ", StringComparison.Ordinal)) line = line.Substring(4);
                            else if (line.StartsWith("- ", StringComparison.Ordinal)) line = "• " + line.Substring(2);
                            else if (line.StartsWith("  - ", StringComparison.Ordinal)) line = "    – " + line.Substring(4);
                            if (inside) changes.AppendLine(line.Replace("**", "").Replace("`", ""));
                        }
                    }
                    return changes.ToString().Trim();
                }
            }
            catch (IOException) { return ""; }
            catch (InvalidDataException) { return ""; }
            catch (UnauthorizedAccessException) { return ""; }
        }

        /* Starts the freshly installed exe; the caller exits right after. handOverDisplay: the Mac display stays on for it */
        public static void Restart(IEnumerable<string> args, bool handOverDisplay)
        {
            var info = new ProcessStartInfo(Path.Combine(Program.BaseDirectory, "imac-display.exe"), JoinArguments(args));
            info.UseShellExecute = false;
            info.WorkingDirectory = Program.BaseDirectory;
            info.EnvironmentVariables[RestartedVariable] = handOverDisplay ? "display" : "1";
            Process.Start(info);
        }

        static string JoinArguments(IEnumerable<string> args)
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
