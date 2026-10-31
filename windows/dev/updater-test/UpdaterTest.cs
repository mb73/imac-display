using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace ImacDisplay
{
    /*
     Test harness for Updater.cs, compiled together with windows\src\*.cs (/main:ImacDisplay.UpdaterTest) by
     windows\dev\updater-test.ps1, which also runs it.
       harness.exe driver <work>
         builds the test zips and one installation of a release "1.11.1" per case: a copy of this very exe as
         imac-display.exe, so Program.BaseDirectory is the case folder and the running image is the one the update
         renames. Runs that copy, then checks the folder. Exit code 0 if every check passed.
       imac-display.exe case <log> install <zip> <fault> [restart]   one update, with an injected fault
       imac-display.exe case <log> unit <work>                       the functions without side effects
     Program.LogPath is bent to <log>. Nothing here touches displays, settings, the real log or the Downloads
     folder. The zips carry Probe.cs as their new exe, never a real imac-display.exe: a virus scanner that
     flags one would report it as a finding on this computer.
     */
    internal static class UpdaterTest
    {
        static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);
        static readonly string[] RestartArgs = { "--host", "mein mac", "--size", "3840x2160", "--mac-primary", "a\"b", "" };
        const string Changelog = "# Changelog\n\nAlle nennenswerten Änderungen.\n\n## [1.11.2] – 2026-11-01\n\n### Behoben\n\n- Neu.\n\n## [1.11.1] – 2026-10-30\n\n- Alt.\n";

        /* the installed release: the update must replace, keep or remove each of these */
        static readonly string[] OldFiles =
        {
            "VERSION", "CHANGELOG.md", "README.md", "update.cmd", "exe-missing.cmd", "imac-display.exe.config", "notes.txt",
            "mac/build.sh", "mac/Sources/Main.swift", "mac/Sources/Stale.swift",
            "windows/src/A.cs", "windows/src/Stale.cs", "windows/dev/tool.ps1", "windows/dev/sub/stale.txt",
            "tools/ffmpeg.txt", ".update/junk.txt"
        };
        static readonly string[] NewTextFiles =
        {
            "newfile.txt", "mac/build.sh", "mac/Sources/Main.swift", "mac/Sources/New.swift",
            "windows/src/A.cs", "windows/src/B.cs", "windows/dev/tool.ps1"
        };

        static int passed, failed, counter;
        static volatile bool installReturned;
        static readonly StringBuilder notes = new StringBuilder();

        static int Main(string[] args)
        {
            var output = new StreamWriter(Console.OpenStandardOutput(), Utf8);
            output.AutoFlush = true;
            Console.SetOut(output);
            try
            {
                if (args.Length == 2 && args[0] == "driver") return Driver(args[1]);
                if (args.Length >= 4 && args[0] == "case") return Case(args);
                Console.WriteLine("Aufruf: driver <work> | case <log> unit <work> | case <log> install <zip> <fault> [restart]");
                return 2;
            }
            catch (Exception ex)
            {
                Console.WriteLine("HÜLLE-AUSNAHME " + ex);
                return 3;
            }
        }

        /* ---- inside one test installation ---- */

        static int Case(string[] args)
        {
            FieldInfo logPath = typeof(Program).GetField("LogPath", BindingFlags.NonPublic | BindingFlags.Static);
            if (logPath == null) throw new MissingFieldException("Program.LogPath: renamed? The test must not write to the real log.");
            logPath.SetValue(null, args[1]);
            if (args[2] == "unit")
            {
                Unit(args[3]);
                Console.WriteLine("SUMMARY " + passed + " " + failed);
                return 0;
            }
            return InstallCase(args[3], args[4], args.Length > 5 && args[5] == "restart");
        }

        static int InstallCase(string zip, string fault, bool restart)
        {
            string dir = Program.BaseDirectory;
            string exe = Path.Combine(dir, "imac-display.exe");
            string staged = Path.Combine(dir, @".update\imac-display.exe");
            /* in the zip right after the exe: once it is being unpacked, the exe in .update is complete */
            string marker = Path.Combine(dir, @".update\zz-big.bin");
            var release = new ManualResetEvent(false);
            FileStream held = null;
            Thread scanner = null;
            if (fault == "rename-blocked") held = new FileStream(exe, FileMode.Open, FileAccess.Read, FileShare.Read);
            else if (fault == "later-locked") held = new FileStream(Path.Combine(dir, "README.md"), FileMode.Open, FileAccess.Read, FileShare.Read);
            else if (fault == "stale-locked") held = new FileStream(Path.Combine(dir, @"mac\Sources\Stale.swift"), FileMode.Open, FileAccess.Read, FileShare.Read);
            else if (fault == "exe-deleted" || fault == "exe-locked")
                scanner = Background(delegate
                {
                    if (!Until(delegate { return File.Exists(marker) || installReturned; }, 30000) || installReturned)
                    {
                        Note("Störer kam nicht zum Zug");
                        return;
                    }
                    if (!File.Exists(staged))
                    {
                        Note("Störer zu spät, die neue exe war schon fort aus .update");
                        return;
                    }
                    if (fault == "exe-deleted")
                    {
                        bool gone = Until(delegate { return TryDelete(staged); }, 10000);
                        Note(gone ? "neue exe in .update gelöscht, bevor sie an ihren Platz kam" : "Löschen der neuen exe in .update klappte nicht");
                        return;
                    }
                    FileStream grip = null;
                    Until(delegate
                    {
                        try
                        {
                            grip = new FileStream(staged, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                            return true;
                        }
                        catch (IOException) { return false; }
                    }, 10000);
                    Note(grip != null ? "neue exe in .update ohne Freigabe zum Löschen geöffnet" : "Öffnen der neuen exe in .update klappte nicht");
                    release.WaitOne();
                    if (grip != null) grip.Dispose();
                });
            else if (fault == "exe-vanishes")
                scanner = Background(delegate
                {
                    if (!Until(delegate { return File.Exists(exe + ".old") || installReturned; }, 30000))
                    {
                        Note("Störer kam nicht zum Zug");
                        return;
                    }
                    bool gone = Until(delegate { return File.Exists(exe) && TryDelete(exe); }, 10000);
                    Note(gone ? "neue exe an ihrem Platz gelöscht" : "neue exe nie gelöscht");
                });
            string result = Updater.Install(zip);
            installReturned = true;
            release.Set();
            if (scanner != null) scanner.Join(30000);
            if (held != null) held.Dispose();
            Console.WriteLine("RESULT " + (result == null ? "<null>" : result.Replace("\r\n", " | ")));
            lock (notes) Console.WriteLine("NOTES " + notes);
            if (restart && result == null) RestartProbe();
            return 0;
        }

        static void RestartProbe()
        {
            string answer = Path.Combine(Program.BaseDirectory, "probe-result.txt");
            foreach (bool handOver in new[] { true, false })
            {
                /* a fresh file is often opened for a moment by a virus scanner or the indexer */
                Until(delegate { return !File.Exists(answer) || TryDelete(answer); }, 5000);
                Updater.Restart(RestartArgs, handOver);
                string text = null;
                Until(delegate
                {
                    try
                    {
                        text = File.ReadAllText(answer, Utf8);
                        return true;
                    }
                    catch (IOException) { return false; }
                }, 15000);
                Console.WriteLine("PROBE " + handOver + " " + (text != null ? text.Replace("\r\n", " | ") : "<keine Antwort>"));
            }
        }

        static void Note(string text)
        {
            lock (notes) notes.Append(text).Append("; ");
        }

        static Thread Background(ThreadStart body)
        {
            var thread = new Thread(body);
            thread.IsBackground = true;
            thread.Start();
            return thread;
        }

        static bool Until(Func<bool> condition, int milliseconds)
        {
            Stopwatch clock = Stopwatch.StartNew();
            while (clock.ElapsedMilliseconds < milliseconds)
            {
                if (condition()) return true;
                Thread.Yield();
            }
            return condition();
        }

        static bool TryDelete(string path)
        {
            try
            {
                File.Delete(path);
                return !File.Exists(path);
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }

        /* ---- functions without side effects ---- */

        static void Unit(string work)
        {
            Wipe(work);
            Directory.CreateDirectory(work);

            Section("Versionen");
            Same("Updater.Version liest VERSION neben der exe", "1.11.1", Updater.Version);
            Order("1.11.1", "1.11.0", 1);
            Order("1.11.0", "1.11.1", -1);
            Order("1.11.1", "1.11.1", 0);
            Order("1.11", "1.11.0", 0);
            Order("1.10.0", "1.9.9", 1);
            Order("1.11.10", "1.11.9", 1);
            Order("2.0.0", "1.99.99", 1);
            Order("0.0.0", "0.0.1", -1);
            Order("1.11.1-test", "1.11.1", 0);
            VersionFile(work, "LF", Utf8.GetBytes("1.11.2\n"), "1.11.2");
            VersionFile(work, "CRLF", Utf8.GetBytes("1.11.2\r\n"), "1.11.2");
            VersionFile(work, "mit BOM", Bom("1.11.2\n"), "1.11.2");
            VersionFile(work, "Leerraum außen", Utf8.GetBytes(" \t1.11.2 \n\n"), "1.11.2");
            VersionFile(work, "Leerzeichen innen", Utf8.GetBytes("1.11 .2"), null);
            VersionFile(work, "leer", new byte[0], null);
            VersionFile(work, "32 Zeichen", Utf8.GetBytes(new string('1', 32)), new string('1', 32));
            VersionFile(work, "33 Zeichen", Utf8.GetBytes(new string('1', 33)), null);
            VersionFile(work, "Umlaut", Utf8.GetBytes("1.11.ä"), null);
            VersionFile(work, "Fehlerseite statt VERSION", Utf8.GetBytes("<html><body>Proxy</body></html>"), null);
            Same("ReadVersion: Ordner ohne VERSION", null, Updater.ReadVersion(Dir(work, "ohne-version")));

            Section("Git-Arbeitsverzeichnis");
            string gitFolder = Dir(work, "git-ordner");
            Directory.CreateDirectory(Path.Combine(gitFolder, ".git"));
            string gitFile = Dir(work, "git-datei");
            File.WriteAllText(Path.Combine(gitFile, ".git"), "gitdir: anderswo");
            Same("IsGitCheckout: .git-Ordner", true, Updater.IsGitCheckout(gitFolder));
            Same("IsGitCheckout: .git-Datei (Worktree)", true, Updater.IsGitCheckout(gitFile));
            Same("IsGitCheckout: ohne .git", false, Updater.IsGitCheckout(Dir(work, "kein-git")));

            Section("ZipVersion");
            string z = Dir(work, "zips");
            Same("GitHub-Zip mit Wurzelordner", "1.11.2", Updater.ZipVersion(Zip(z, "github.zip", "imac-display-main/", "", "imac-display-main/VERSION", "1.11.2\n", "imac-display-main/imac-display.exe", "exe")));
            Same("Zip ohne Wurzelordner", "1.11.2", Updater.ZipVersion(Zip(z, "flat.zip", "VERSION", "1.11.2\n", "imac-display.exe", "exe")));
            Same("ohne imac-display.exe", null, Updater.ZipVersion(Zip(z, "noexe.zip", "r/VERSION", "1.11.2\n", "r/README.md", "x")));
            Same("ungültige VERSION", null, Updater.ZipVersion(Zip(z, "bad.zip", "r/VERSION", "1.11.2 beta", "r/imac-display.exe", "x")));
            Same("VERSION mit BOM und CRLF", "1.11.2", Updater.ZipVersion(Zip(z, "bom.zip", "r/VERSION", (char)0xFEFF + "1.11.2\r\n", "r/imac-display.exe", "x")));
            Same("Namen mit Backslash", "1.11.2", Updater.ZipVersion(Zip(z, "backslash.zip", "r\\VERSION", "1.11.2\n", "r\\imac-display.exe", "x")));
            Same("tiefere Kandidatin zuerst: die flachere gewinnt", "1.11.2", Updater.ZipVersion(Zip(z, "deeper-first.zip", "a/b/VERSION", "9.9.9\n", "a/b/imac-display.exe", "x", "a/VERSION", "1.11.2\n", "a/imac-display.exe", "x")));
            Same("flache VERSION ohne exe: die tiefere mit exe", "1.11.2", Updater.ZipVersion(Zip(z, "shallow-noexe.zip", "a/VERSION", "1.0.0\n", "a/b/VERSION", "1.11.2\n", "a/b/imac-display.exe", "x")));
            Same("exe-Name in anderer Schreibweise", "1.11.2", Updater.ZipVersion(Zip(z, "case.zip", "r/VERSION", "1.11.2\n", "r/IMAC-DISPLAY.EXE", "x")));
            Info("„version“ klein geschrieben ergibt " + Show(Updater.ZipVersion(Zip(z, "lower.zip", "r/version", "1.11.2\n", "r/imac-display.exe", "x"))));
            File.WriteAllBytes(Path.Combine(z, "empty.zip"), new byte[0]);
            Same("leere Datei (Platzhalter des Browsers)", null, Updater.ZipVersion(Path.Combine(z, "empty.zip")));
            File.WriteAllText(Path.Combine(z, "text.zip"), "keine Zip");
            Same("Textdatei", null, Updater.ZipVersion(Path.Combine(z, "text.zip")));
            byte[] whole = File.ReadAllBytes(Path.Combine(z, "github.zip"));
            File.WriteAllBytes(Path.Combine(z, "cut.zip"), whole.Take(whole.Length / 2).ToArray());
            Same("halb geladene Zip", null, Updater.ZipVersion(Path.Combine(z, "cut.zip")));
            Same("fehlende Datei", null, Updater.ZipVersion(Path.Combine(z, "gibt-es-nicht.zip")));

            Section("Changes (Text für den Update-Dialog)");
            string changelog = "# Changelog\n\nAlle nennenswerten Änderungen.\n\n"
                + "## [1.11.3] – 2026-11-02\n\n### Behoben\n\n- **Fett** und `Code`.\n  - Unterpunkt\n\n"
                + "## [1.11.2] – 2026-11-01\n\n### Hinzugefügt\n\n- Zwei.\n\n"
                + "## [1.11.1] – 2026-10-30\n\n- Installiert, darf nicht erscheinen.\n\n"
                + "## [1.11.0] – 2026-10-28\n\n- Alt.\n";
            string expected = "Version 1.11.3 – 2026-11-02\r\n\r\nBehoben\r\n\r\n• Fett und Code.\r\n    – Unterpunkt\r\n\r\n"
                + "Version 1.11.2 – 2026-11-01\r\n\r\nHinzugefügt\r\n\r\n• Zwei.";
            Same("nur Abschnitte über der installierten Version, aufbereitet", expected,
                Updater.Changes(Zip(z, "changes.zip", "r/VERSION", "1.11.3\n", "r/imac-display.exe", "x", "r/CHANGELOG.md", changelog)));
            Same("Zip ohne CHANGELOG.md", "", Updater.Changes(Path.Combine(z, "github.zip")));
            Same("keine Zip", "", Updater.Changes(Path.Combine(z, "text.zip")));
            Info("„## [Unreleased]“ ganz oben ergibt " + Show(Updater.Changes(Zip(z, "unreleased.zip", "r/VERSION", "1.11.2\n", "r/imac-display.exe", "x",
                "r/CHANGELOG.md", "## [Unreleased]\n\n- Bald.\n\n## [1.11.2] – 2026-11-01\n\n- Zwei.\n"))));

            Section("FindNewerDownload (Blick in „Downloads“)");
            string dl = Dir(work, "downloads");
            ReleaseZip(dl, "imac-display-main.zip", "1.11.0");
            ReleaseZip(dl, "imac-display-main (1).zip", "1.11.2");
            ReleaseZip(dl, "imac-display-main (2).zip", "1.11.3");
            Zip(dl, "imac-display-sonstwas.zip", "r/README.md", "kein Release");
            ReleaseZip(dl, "other.zip", "1.12.0");
            File.WriteAllBytes(Path.Combine(dl, "imac-display-main (3).zip"), new byte[0]);
            Newest(dl, "neueste passende; ältere, fremde, leere, anders benannte übergangen", "imac-display-main (2).zip 1.11.3");
            ReleaseZip(dl, "imac-display-main (3).zip", "1.11.4");
            Newest(dl, "Platzhalter fertig geladen: neu gelesen", "imac-display-main (3).zip 1.11.4");
            using (Exclusive(Path.Combine(dl, "imac-display-main (3).zip")))
                Newest(dl, "bekannte Zip aus dem Zwischenspeicher, auch gesperrt", "imac-display-main (3).zip 1.11.4");
            string fresh = ReleaseZip(dl, "imac-display-main (4).zip", "1.11.5");
            using (Exclusive(fresh))
                Newest(dl, "neue Zip, beim ersten Blick gesperrt: noch nicht", "imac-display-main (3).zip 1.11.4");
            string afterLock;
            string foundAfterLock = Updater.FindNewerDownload(dl, out afterLock);
            Info("dieselbe Zip nach dem Entsperren: " + (foundAfterLock == null ? "null" : Path.GetFileName(foundAfterLock)) + " " + afterLock
                + " (gesperrt beim ersten Blick zählt bis zur nächsten Änderung als keine Release-Zip)");
            string none;
            Same("FindNewerDownload: Ordner fehlt", null, Updater.FindNewerDownload(Path.Combine(work, "gibt-es-nicht"), out none));
            Same("FindNewerDownload: Ordner null", null, Updater.FindNewerDownload(null, out none));

            Section("Installable (was eine Zip anfassen darf)");
            MethodInfo installable = Private("Installable");
            foreach (string allowed in new[] { "README.md", @"mac\Sources\x.swift", @"toolsx\y", @"x\tools\y", @"a\.gitignore" })
                Same("erlaubt: " + allowed, true, installable.Invoke(null, new object[] { allowed }));
            foreach (string refused in new[] { "", @"..\a", @"a\..\b", @"a\.\b", @"\a", @"a\\b", @"C:\a", "C:a", @"tools\ffmpeg.exe", @"TOOLS\x", @".git\config", @".GIT\x", @".update\x", "a<b", "a|b", "a\"b" })
                Same("verboten: " + Show(refused), false, installable.Invoke(null, new object[] { refused }));

            Section("JoinArguments (Befehlszeile für den Neustart)");
            Same("Leerzeichen, Anführungszeichen, leeres Argument", "--host \"mein mac\" --size 3840x2160 --mac-primary \"a\\\"b\" \"\"",
                Private("JoinArguments").Invoke(null, new object[] { RestartArgs }));

            Section("RestoreExe (Rückfall auf die laufende Version)");
            MethodInfo restore = Private("RestoreExe");
            string rd = Dir(work, "restore");
            string exe = Path.Combine(rd, "imac-display.exe"), aside = exe + ".old";
            File.WriteAllText(aside, "alt");
            restore.Invoke(null, new object[] { exe, aside });
            Same("exe fehlt, .old da: zurückbenannt", "alt", ReadOrNull(exe));
            Check("… und .old ist weg", !File.Exists(aside), "liegt noch da");
            File.WriteAllText(aside, "älter");
            restore.Invoke(null, new object[] { exe, aside });
            Same("exe da: bleibt, wie sie ist", "alt", ReadOrNull(exe));
            Check("… und .old bleibt", File.Exists(aside), "fehlt");
            File.Delete(exe);
            restore.Invoke(null, new object[] { exe, null });
            Check("ohne .old-Pfad: nichts", !File.Exists(exe), "angelegt");
            File.Delete(aside);
            restore.Invoke(null, new object[] { exe, aside });
            Check(".old auch fort: nichts, keine Ausnahme", !File.Exists(exe), "angelegt");

            Section("MacUpdate (Paket für LaptopScreen)");
            string mu = Dir(work, "macupdate");
            Put(mu, "VERSION", Utf8.GetBytes("1.11.2\n"));
            Put(mu, "CHANGELOG.md", Utf8.GetBytes("Änderungen"));
            Put(mu, "README.md", Utf8.GetBytes("gehört nicht hinein"));
            Put(mu, "mac/build.sh", Utf8.GetBytes("#!/bin/sh"));
            Put(mu, "mac/Info.plist", Utf8.GetBytes("<plist/>"));
            Put(mu, "mac/Sources/main.swift", Utf8.GetBytes("print(1)"));
            var icon = new byte[50000];
            new Random(2).NextBytes(icon);
            Put(mu, "mac/AppIcon.icns", icon);
            Put(mu, "mac/.DS_Store", Utf8.GetBytes("x"));
            Put(mu, "mac/Sources/.hidden.swift", Utf8.GetBytes("x"));
            Put(mu, "mac/LaptopScreen.app/Contents/MacOS/LaptopScreen", Utf8.GetBytes("x"));
            string version;
            byte[] archive = MacUpdate.Build(mu, out version);
            Same("Version", "1.11.2", version);
            var names = new List<string>();
            byte[] packedIcon = null;
            using (var zip = new ZipArchive(new MemoryStream(archive)))
                foreach (ZipArchiveEntry entry in zip.Entries)
                {
                    names.Add(entry.FullName);
                    if (entry.FullName != "mac/AppIcon.icns") continue;
                    using (Stream stream = entry.Open())
                    using (var copy = new MemoryStream())
                    {
                        stream.CopyTo(copy);
                        packedIcon = copy.ToArray();
                    }
                }
            names.Sort(StringComparer.Ordinal);
            Same("Inhalt: VERSION, CHANGELOG.md, mac\\ ohne .app und versteckte Dateien", "CHANGELOG.md, VERSION, mac/AppIcon.icns, mac/Info.plist, mac/Sources/main.swift, mac/build.sh", string.Join(", ", names));
            Check("Binärdatei unverändert", packedIcon != null && packedIcon.SequenceEqual(icon), null);
            string sha;
            using (SHA256 hash = SHA256.Create()) sha = Crypto.Hex(hash.ComputeHash(archive));
            List<string> frame = MacUpdate.Frame(archive, version, delegate (string v, string s) { return "PROOF-" + v + "-" + s; });
            Same("Kopfzeile UPDATE <version> <bytes> <sha256> <proof>", "UPDATE 1.11.2 " + archive.Length.ToString(CultureInfo.InvariantCulture) + " " + sha + " PROOF-1.11.2-" + sha, frame[0]);
            Check("SHA-256 in Kleinbuchstaben", sha.Length == 64 && sha == sha.ToLowerInvariant(), sha);
            Same("letzte Zeile E", "E", frame[frame.Count - 1]);
            Same("Anzahl D-Zeilen", (archive.Length + 16383) / 16384, frame.Count - 2);
            var joined = new List<byte>();
            bool sizes = true;
            for (int i = 1; i < frame.Count - 1; i++)
            {
                byte[] chunk = Convert.FromBase64String(frame[i].Substring(2));
                if (!frame[i].StartsWith("D ", StringComparison.Ordinal) || chunk.Length > 16384 || (i < frame.Count - 2 && chunk.Length != 16384)) sizes = false;
                joined.AddRange(chunk);
            }
            Check("D-Zeilen: je 16 KiB, zusammen genau das Archiv", sizes && joined.SequenceEqual(archive), null);
            Check("alle Zeilen ASCII", frame.All(line => line.All(c => c < 128)), null);
            string noVersion = Dir(work, "mu-ohne-version");
            Put(noVersion, "mac/build.sh", Utf8.GetBytes("#!/bin/sh"));
            Same("ohne VERSION: kein Paket", null, MacUpdate.Build(noVersion, out version));
            string noBuild = Dir(work, "mu-ohne-build");
            Put(noBuild, "VERSION", Utf8.GetBytes("1.11.2\n"));
            Put(noBuild, "mac/Sources/main.swift", Utf8.GetBytes("print(1)"));
            Same("ohne mac/build.sh: kein Paket", null, MacUpdate.Build(noBuild, out version));

            Section("Netz und Downloads-Ordner (nur lesend)");
            string error;
            string latest = Updater.LatestVersion(out error);
            /* offline is no failure of the updater; an answer it cannot read is */
            if (latest == null && error != "unerwartete Antwort") Info("LatestVersion übersprungen, GitHub nicht erreichbar: " + error);
            else
            {
                Check("LatestVersion: GitHub antwortet mit einer Version", latest != null, error);
                Info("GitHub nennt " + Show(latest) + ", in dieser Testinstallation ist " + Updater.Version);
            }
            string downloads = Updater.DownloadsFolder();
            Check("DownloadsFolder: ein vorhandener Ordner", downloads != null && Directory.Exists(downloads), downloads);
        }

        static void Order(string a, string b, int expected)
        {
            Same("Compare(" + a + ", " + b + ")", expected, Math.Sign(Updater.Compare(a, b)));
        }

        static void VersionFile(string work, string label, byte[] content, string expected)
        {
            string dir = Dir(work, "version-" + (++counter).ToString(CultureInfo.InvariantCulture));
            File.WriteAllBytes(Path.Combine(dir, "VERSION"), content);
            Same("ReadVersion: " + label, expected, Updater.ReadVersion(dir));
        }

        static void Newest(string folder, string label, string expected)
        {
            string version;
            string found = Updater.FindNewerDownload(folder, out version);
            Same("FindNewerDownload: " + label, expected, (found == null ? "null" : Path.GetFileName(found)) + " " + (version ?? "null"));
        }

        static byte[] Bom(string text)
        {
            return new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Utf8.GetBytes(text)).ToArray();
        }

        static string ReleaseZip(string folder, string name, string version)
        {
            return Zip(folder, name, "imac-display-main/", "", "imac-display-main/VERSION", version + "\n", "imac-display-main/imac-display.exe", "exe");
        }

        /* entry names and texts in pairs, in this order */
        static string Zip(string folder, string name, params string[] pairs)
        {
            string path = Path.Combine(folder, name);
            using (var stream = new FileStream(path, FileMode.Create))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
                for (int i = 0; i < pairs.Length; i += 2)
                {
                    byte[] content = Utf8.GetBytes(pairs[i + 1]);
                    using (Stream entry = zip.CreateEntry(pairs[i]).Open()) entry.Write(content, 0, content.Length);
                }
            return path;
        }

        static MethodInfo Private(string name)
        {
            MethodInfo method = typeof(Updater).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static);
            if (method == null) throw new MissingMethodException("Updater." + name + " fehlt: umbenannt? Dann hier nachziehen.");
            return method;
        }

        static FileStream Exclusive(string path)
        {
            return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
        }

        /* ---- the driver: installations and checks ---- */

        sealed class CaseResult
        {
            public string Dir, Output, Result, Notes, Log;
        }

        static int Driver(string work)
        {
            string bin = AppDomain.CurrentDomain.BaseDirectory;
            byte[] harness = File.ReadAllBytes(Path.Combine(bin, "harness.exe"));
            byte[] probe = File.ReadAllBytes(Path.Combine(bin, "probe.exe"));
            foreach (string name in new[] { "cases", "zips", "logs", "unit" }) Wipe(Path.Combine(work, name));
            Directory.CreateDirectory(Path.Combine(work, "zips"));
            Directory.CreateDirectory(Path.Combine(work, "logs"));
            string release = Path.Combine(work, @"zips\imac-display-main.zip");
            Release(release, probe, false);
            string big = Path.Combine(work, @"zips\imac-display-main-big.zip");
            Release(big, probe, true);
            string flat = Path.Combine(work, @"zips\flat.zip");
            Flat(flat, probe);

            Section("U   Funktionen ohne Nebenwirkungen (in einer Testinstallation von 1.11.1)");
            string unit = NewInstallation(work, "U-unit", harness);
            string text = RunCopy(unit, LogFile(work, "U-unit"), "unit " + Quote(Path.Combine(work, "unit")));
            foreach (string line in text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (!line.StartsWith("SUMMARY ", StringComparison.Ordinal))
                {
                    Console.WriteLine(line.StartsWith("  ", StringComparison.Ordinal) ? line : "\r\n" + line);
                    continue;
                }
                string[] counts = line.Split(' ');
                passed += int.Parse(counts[1], CultureInfo.InvariantCulture);
                failed += int.Parse(counts[2], CultureInfo.InvariantCulture);
            }

            Section("N1  Update aus einer GitHub-Zip, danach Neustart");
            CaseResult r = Install(work, "N1-normal", harness, probe, release, "none", true);
            Same("Install meldet Erfolg", "<null>", r.Result);
            ExpectNew(r, harness, probe);
            ExpectRestart(r);

            Section("N2  Update aus einer Zip ohne Wurzelordner und ohne mac\\");
            r = Install(work, "N2-flat", harness, probe, flat, "none", false);
            Same("Install meldet Erfolg", "<null>", r.Result);
            Same("imac-display.exe ist die neue", "neue exe (Probe)", Describe(r.Dir + "imac-display.exe", harness, probe));
            Same("VERSION neu", "1.11.2\n", ReadOrNull(Rel(r.Dir, "VERSION")));
            Same("windows/src/A.cs neu", NewText("windows/src/A.cs"), ReadOrNull(Rel(r.Dir, "windows/src/A.cs")));
            Check("windows/src/Stale.cs entfernt", !File.Exists(Rel(r.Dir, "windows/src/Stale.cs")), "liegt noch da");
            Check("mac/Sources/Stale.swift bleibt, denn mac\\Sources fehlt in der Zip", File.Exists(Rel(r.Dir, "mac/Sources/Stale.swift")), "entfernt");
            Same("mac/Sources/Main.swift unangetastet", OldText("mac/Sources/Main.swift"), ReadOrNull(Rel(r.Dir, "mac/Sources/Main.swift")));

            Section("F1  Virenscanner löscht die neue exe in .update, bevor sie an ihren Platz kommt");
            r = Install(work, "F1-exe-deleted", harness, probe, big, "exe-deleted", false);
            ExpectRolledBack(r, harness, probe, "bevor sie an ihren Platz kam");

            Section("F2  Virenscanner hält die neue exe in .update offen, ohne Freigabe zum Löschen");
            r = Install(work, "F2-exe-locked", harness, probe, big, "exe-locked", false);
            ExpectRolledBack(r, harness, probe, "ohne Freigabe zum Löschen geöffnet");

            Section("F3  Die laufende exe lässt sich nicht umbenennen");
            r = Install(work, "F3-rename-blocked", harness, probe, release, "rename-blocked", false);
            Check("Install meldet einen Fehler", r.Result != "<null>", r.Result);
            ExpectOld(r, harness, probe);

            Section("F4  Eine spätere Datei (README.md) ist gesperrt, die neue exe ist schon an ihrem Platz");
            r = Install(work, "F4-later-locked", harness, probe, release, "later-locked", false);
            Check("Install meldet einen Fehler", r.Result != "<null>", r.Result);
            Same("imac-display.exe ist die neue", "neue exe (Probe)", Describe(r.Dir + "imac-display.exe", harness, probe));
            Same("imac-display.exe.old ist die alte", "alte exe (Hülle)", Describe(r.Dir + "imac-display.exe.old", harness, probe));
            Same("README.md alt (gesperrt)", OldText("README.md"), ReadOrNull(Rel(r.Dir, "README.md")));
            Same("VERSION noch alt, damit der nächste Start das Update erneut anbietet", "1.11.1\n", ReadOrNull(Rel(r.Dir, "VERSION")));
            Check("Hinweis „Notfalls … von Hand“, denn die exe ist da", r.Result.Contains("Notfalls"), r.Result);
            Info("Stand danach: " + Mixed(r.Dir));

            Section("F5  Virenscanner entfernt die neue exe erst an ihrem Platz, sonst klappt alles");
            r = Install(work, "F5-exe-vanishes", harness, probe, release, "exe-vanishes", false);
            Info("imac-display.exe danach: " + Describe(r.Dir + "imac-display.exe", harness, probe)
                + ", imac-display.exe.old: " + Describe(r.Dir + "imac-display.exe.old", harness, probe) + ", " + Mixed(r.Dir)
                + " (das merkt erst der Neustart, seine Meldung nennt exe-missing.cmd)");

            Section("F6  imac-display.exe.old läuft noch (die vorige Version hat sich nicht beendet)");
            r = Install(work, "F6-old-running", harness, probe, release, "old-running", false);
            Same("Install meldet Erfolg", "<null>", r.Result);
            Same("imac-display.exe ist die neue", "neue exe (Probe)", Describe(r.Dir + "imac-display.exe", harness, probe));
            Same("das laufende imac-display.exe.old bleibt unberührt", "neue exe (Probe)", Describe(r.Dir + "imac-display.exe.old", harness, probe));
            string[] olds = Directory.GetFiles(r.Dir, "*.old");
            Check("die alte exe liegt unter eigenem Namen imac-display.exe.<Ticks>.old", olds.Any(f => Path.GetFileName(f) != "imac-display.exe.old" && SameBytes(f, harness)),
                string.Join(", ", olds.Select(Path.GetFileName)));

            Section("F7  Veraltete Datei in mac\\Sources ist schreibgeschützt");
            r = Install(work, "F7-stale-readonly", harness, probe, release, "stale-readonly", false);
            Same("Install meldet Erfolg (das Update ist drin)", "<null>", r.Result);
            Same("imac-display.exe ist die neue", "neue exe (Probe)", Describe(r.Dir + "imac-display.exe", harness, probe));
            Same("VERSION neu", "1.11.2\n", ReadOrNull(Rel(r.Dir, "VERSION")));
            Check("schreibgeschützte Stale.swift entfernt", !File.Exists(Rel(r.Dir, "mac/Sources/Stale.swift")), "liegt noch da");
            Check(".update aufgeräumt", !Directory.Exists(Rel(r.Dir, ".update")), "liegt noch da");

            Section("F8  Veraltete Datei in mac\\Sources ist gesperrt");
            r = Install(work, "F8-stale-locked", harness, probe, release, "stale-locked", false);
            Same("Install meldet Erfolg (das Update ist drin)", "<null>", r.Result);
            Same("imac-display.exe ist die neue", "neue exe (Probe)", Describe(r.Dir + "imac-display.exe", harness, probe));
            Check("das Log nennt die gebliebene Datei", r.Log.Contains("Stale.swift"), null);
            Check(".update aufgeräumt", !Directory.Exists(Rel(r.Dir, ".update")), "liegt noch da");

            Console.WriteLine();
            Console.WriteLine("ERGEBNIS: " + passed + " ok, " + failed + " Fehler");
            return failed == 0 ? 0 : 1;
        }

        static void ExpectNew(CaseResult r, byte[] harness, byte[] probe)
        {
            string d = r.Dir;
            Same("imac-display.exe ist die neue", "neue exe (Probe)", Describe(d + "imac-display.exe", harness, probe));
            Same("imac-display.exe.old ist die alte, laufende", "alte exe (Hülle)", Describe(d + "imac-display.exe.old", harness, probe));
            Same("VERSION neu", "1.11.2\n", ReadOrNull(Rel(d, "VERSION")));
            Same("CHANGELOG.md neu", Changelog, ReadOrNull(Rel(d, "CHANGELOG.md")));
            foreach (string f in new[] { "README.md", "update.cmd", "exe-missing.cmd" }.Concat(NewTextFiles))
                Same(f + " neu", NewText(f), ReadOrNull(Rel(d, f)));
            Check("README.md nicht mehr schreibgeschützt", (File.GetAttributes(Rel(d, "README.md")) & FileAttributes.ReadOnly) == 0, null);
            foreach (string f in new[] { "mac/Sources/Stale.swift", "windows/src/Stale.cs", "windows/dev/sub/stale.txt" })
                Check(f + " entfernt (gespiegelter Ordner)", !File.Exists(Rel(d, f)), "liegt noch da");
            foreach (string f in new[] { "imac-display.exe.config", "notes.txt", "tools/ffmpeg.txt" })
                Same(f + " unangetastet", OldText(f), ReadOrNull(Rel(d, f)));
            Same("tiefere VERSION (Lockvogel) nur als gewöhnliche Datei", "9.9.9\n", ReadOrNull(Rel(d, "windows/dev/decoy/VERSION")));
            Check("tools/evil.txt nicht installiert", !File.Exists(Rel(d, "tools/evil.txt")), "installiert");
            Check(".git nicht angelegt", !Directory.Exists(Rel(d, ".git")), "angelegt");
            Check(".update aufgeräumt", !Directory.Exists(Rel(d, ".update")), "liegt noch da");
            Check("../escape.txt nirgends", !File.Exists(Rel(d, "../escape.txt")) && !File.Exists(Rel(d, "escape.txt")), "geschrieben");
            Check("sub/../../escape2.txt nirgends", !File.Exists(Rel(d, "../escape2.txt")) && !File.Exists(Rel(d, "../../escape2.txt")), "geschrieben");
            Check("C:/abs.txt nicht angelegt", !File.Exists(@"C:\abs.txt") && !File.Exists(Rel(d, "C/abs.txt")), "angelegt");
            Check("Eintrag außerhalb des Wurzelordners übergangen", !File.Exists(Rel(d, "other/file.txt")) && !File.Exists(Rel(d, "file.txt")), "installiert");
            Check("Log: „Installiere iMac-Display 1.11.2 aus …“", r.Log.Contains("Installiere iMac-Display 1.11.2 aus imac-display-main.zip"), null);
            Check("Log: „… ist jetzt auf Version 1.11.2.“", r.Log.Contains("iMac-Display ist jetzt auf Version 1.11.2."), null);
        }

        static void ExpectRestart(CaseResult r)
        {
            string withDisplay = Field(r.Output, "PROBE True "), plain = Field(r.Output, "PROBE False ");
            string args = "argc=" + RestartArgs.Length + string.Concat(RestartArgs.Select((a, i) => " | arg" + i + "=[" + a + "]"));
            Check("Neustart startet die neue exe im Programmordner", Has(withDisplay, "exe=" + r.Dir + "imac-display.exe |"), withDisplay);
            Check("Neustart: Arbeitsordner ist der Programmordner", Has(withDisplay, "cwd=" + r.Dir.TrimEnd('\\') + " |"), withDisplay);
            Check("Neustart mit Übergabe: IMAC_DISPLAY_UPDATED=display", Has(withDisplay, "env=display |"), withDisplay);
            Check("Neustart: Argumente kommen unverändert an", Has(withDisplay, args + " |"), withDisplay);
            Check("Neustart ohne Übergabe: IMAC_DISPLAY_UPDATED=1", Has(plain, "env=1 |"), plain);
        }

        static void ExpectRolledBack(CaseResult r, byte[] harness, byte[] probe, string injected)
        {
            Check("Störer hat vor dem Hineinschieben zugeschlagen", r.Notes.Contains(injected), r.Notes);
            Check("Install meldet einen Fehler", r.Result != "<null>", r.Result);
            ExpectOld(r, harness, probe);
            Check("Log: „Die Aktualisierung wurde abgebrochen; die laufende Version bleibt erhalten.“",
                r.Log.Contains("Die Aktualisierung wurde abgebrochen; die laufende Version bleibt erhalten."), null);
            Check("Hinweis nennt die neue exe und dass die bisherige weiterläuft", r.Result.Contains("ließ sich nicht an ihren Platz bringen"), r.Result);
        }

        static void ExpectOld(CaseResult r, byte[] harness, byte[] probe)
        {
            string d = r.Dir;
            Same("imac-display.exe ist die alte, laufende", "alte exe (Hülle)", Describe(d + "imac-display.exe", harness, probe));
            Same("kein imac-display.exe.old übrig", "fehlt", Describe(d + "imac-display.exe.old", harness, probe));
            Same("VERSION alt", "1.11.1\n", ReadOrNull(Rel(d, "VERSION")));
            foreach (string f in new[] { "CHANGELOG.md", "README.md", "update.cmd", "mac/Sources/Main.swift", "windows/src/A.cs" })
                Same(f + " alt", OldText(f), ReadOrNull(Rel(d, f)));
            Check("newfile.txt nicht angelegt", !File.Exists(Rel(d, "newfile.txt")), "angelegt");
            Check("mac/Sources/Stale.swift noch da", File.Exists(Rel(d, "mac/Sources/Stale.swift")), "entfernt");
        }

        /* which of the files a release replaces are new afterwards */
        static string Mixed(string dir)
        {
            var parts = new List<string>();
            foreach (string f in new[] { "VERSION", "CHANGELOG.md", "update.cmd", "newfile.txt", "mac/Sources/Main.swift", "windows/src/A.cs" })
            {
                string text = ReadOrNull(Rel(dir, f));
                string state = text == null ? "fehlt" : text == NewText(f) || (f == "CHANGELOG.md" && text == Changelog) ? "neu" : text == OldText(f) ? "alt" : "?";
                parts.Add(f + " " + state);
            }
            return string.Join(", ", parts);
        }

        static CaseResult Install(string work, string name, byte[] harness, byte[] probe, string zip, string fault, bool restart)
        {
            var r = new CaseResult();
            r.Dir = NewInstallation(work, name, harness);
            string log = LogFile(work, name);
            Process sleeper = null;
            if (fault == "stale-readonly") File.SetAttributes(Rel(r.Dir, "mac/Sources/Stale.swift"), FileAttributes.ReadOnly);
            if (fault == "old-running")
            {
                File.WriteAllBytes(r.Dir + "imac-display.exe.old", probe);
                var info = new ProcessStartInfo(r.Dir + "imac-display.exe.old");
                info.UseShellExecute = false;
                info.CreateNoWindow = true;
                info.EnvironmentVariables["PROBE_SLEEP"] = "60000";
                sleeper = Process.Start(info);
                Thread.Sleep(300);
            }
            try
            {
                r.Output = RunCopy(r.Dir, log, "install " + Quote(zip) + " " + fault + (restart ? " restart" : ""));
            }
            finally
            {
                if (sleeper != null)
                {
                    try { sleeper.Kill(); }
                    catch (InvalidOperationException) { }
                    catch (Win32Exception) { }
                    sleeper.WaitForExit(5000);
                    sleeper.Dispose();
                }
            }
            r.Result = Field(r.Output, "RESULT ");
            r.Notes = Field(r.Output, "NOTES ");
            r.Log = File.Exists(log) ? File.ReadAllText(log, Utf8) : "";
            Console.WriteLine("  Install gibt zurück: " + r.Result);
            if (r.Notes.Length > 0) Console.WriteLine("  Störer: " + r.Notes);
            return r;
        }

        static string NewInstallation(string work, string name, byte[] harness)
        {
            string dir = Path.Combine(work, "cases", name) + "\\";
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(dir + "imac-display.exe", harness);
            foreach (string f in OldFiles) Put(dir, f, Utf8.GetBytes(OldText(f)));
            File.SetAttributes(Rel(dir, "README.md"), FileAttributes.ReadOnly);
            return dir;
        }

        static string RunCopy(string dir, string log, string arguments)
        {
            var info = new ProcessStartInfo(dir + "imac-display.exe", "case " + Quote(log) + " " + arguments);
            info.UseShellExecute = false;
            info.CreateNoWindow = true;
            info.RedirectStandardOutput = true;
            info.StandardOutputEncoding = Utf8;
            info.WorkingDirectory = dir;
            using (Process process = Process.Start(info))
            {
                string text = process.StandardOutput.ReadToEnd();
                process.WaitForExit();
                if (text.Contains("HÜLLE-AUSNAHME")) Console.WriteLine(text);
                return text;
            }
        }

        static void Release(string path, byte[] probe, bool big)
        {
            var entries = new List<KeyValuePair<string, byte[]>>();
            foreach (string f in new[] { "VERSION", "README.md", "update.cmd", "exe-missing.cmd" }) entries.Add(Pair(f, NewText(f)));
            entries.Add(Pair("CHANGELOG.md", Changelog));
            entries.Add(new KeyValuePair<string, byte[]>("imac-display.exe", probe));
            if (big)
            {
                /* gives a scanner thread time while it is unpacked, right after the exe */
                var noise = new byte[64 << 20];
                new Random(1).NextBytes(noise);
                entries.Add(new KeyValuePair<string, byte[]>("zz-big.bin", noise));
            }
            foreach (string f in NewTextFiles) entries.Add(Pair(f, NewText(f)));
            /* a deeper VERSION next to an exe must not become the root */
            entries.Add(Pair("windows/dev/decoy/VERSION", "9.9.9\n"));
            entries.Add(Pair("windows/dev/decoy/imac-display.exe", "Lockvogel"));
            /* never installed */
            foreach (string f in new[] { "tools/evil.txt", ".git/config", ".update/x.txt", "../escape.txt", "sub/../../escape2.txt", "C:/abs.txt" })
                entries.Add(Pair(f, "darf nicht installiert werden"));
            WriteZip(path, "imac-display-main/", entries, "other/file.txt");
        }

        static void Flat(string path, byte[] probe)
        {
            var entries = new List<KeyValuePair<string, byte[]>>();
            entries.Add(Pair("VERSION", NewText("VERSION")));
            entries.Add(new KeyValuePair<string, byte[]>("imac-display.exe", probe));
            foreach (string f in new[] { "README.md", "windows/src/A.cs", "windows/src/B.cs" }) entries.Add(Pair(f, NewText(f)));
            WriteZip(path, "", entries, null);
        }

        /* like GitHub's archive: every folder as its own entry before its files */
        static void WriteZip(string path, string root, List<KeyValuePair<string, byte[]>> entries, string outside)
        {
            using (var stream = new FileStream(path, FileMode.Create))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                var folders = new HashSet<string>();
                if (root.Length > 0 && folders.Add(root)) zip.CreateEntry(root);
                foreach (KeyValuePair<string, byte[]> e in entries)
                {
                    string name = root + e.Key;
                    for (int slash = name.IndexOf('/', root.Length); slash >= 0; slash = name.IndexOf('/', slash + 1))
                    {
                        string folder = name.Substring(0, slash + 1);
                        if (folders.Add(folder)) zip.CreateEntry(folder);
                    }
                    CompressionLevel level = e.Key.EndsWith(".bin", StringComparison.Ordinal) ? CompressionLevel.NoCompression : CompressionLevel.Optimal;
                    using (Stream s = zip.CreateEntry(name, level).Open()) s.Write(e.Value, 0, e.Value.Length);
                }
                if (outside != null)
                    using (Stream s = zip.CreateEntry(outside).Open()) s.WriteByte(0x78);
            }
        }

        /* ---- helpers ---- */

        static string OldText(string relative)
        {
            if (relative == "VERSION") return "1.11.1\n";
            /* Windows reads it before the exe even starts: anything but XML fails as "side-by-side configuration is incorrect" */
            if (relative == "imac-display.exe.config") return "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<configuration>\n</configuration>\n";
            return "alt: " + relative + "\n";
        }

        static string NewText(string relative)
        {
            return relative == "VERSION" ? "1.11.2\n" : "neu: " + relative + "\n";
        }

        static KeyValuePair<string, byte[]> Pair(string name, string text)
        {
            return new KeyValuePair<string, byte[]>(name, Utf8.GetBytes(text));
        }

        static string Rel(string dir, string relative)
        {
            return Path.Combine(dir, relative.Replace('/', '\\'));
        }

        static string Dir(string work, string name)
        {
            string path = Path.Combine(work, name);
            Directory.CreateDirectory(path);
            return path;
        }

        static void Put(string dir, string relative, byte[] content)
        {
            string path = Rel(dir, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, content);
        }

        static void Wipe(string path)
        {
            if (!Directory.Exists(path)) return;
            foreach (string file in Directory.GetFiles(path, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(path, true);
        }

        static string ReadOrNull(string path)
        {
            return File.Exists(path) ? File.ReadAllText(path, Utf8) : null;
        }

        static bool SameBytes(string path, byte[] expected)
        {
            return File.Exists(path) && File.ReadAllBytes(path).SequenceEqual(expected);
        }

        static string Describe(string path, byte[] harness, byte[] probe)
        {
            if (!File.Exists(path)) return "fehlt";
            byte[] content = File.ReadAllBytes(path);
            if (content.SequenceEqual(harness)) return "alte exe (Hülle)";
            if (content.SequenceEqual(probe)) return "neue exe (Probe)";
            return content.Length + " Bytes, unbekannt";
        }

        static string LogFile(string work, string name)
        {
            return Path.Combine(work, "logs", name + ".log");
        }

        static string Quote(string text)
        {
            return "\"" + text + "\"";
        }

        static string Field(string output, string prefix)
        {
            foreach (string line in (output ?? "").Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
                if (line.StartsWith(prefix, StringComparison.Ordinal)) return line.Substring(prefix.Length);
            return "";
        }

        static bool Has(string text, string part)
        {
            return text.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static void Section(string title)
        {
            Console.WriteLine();
            Console.WriteLine(title);
        }

        static void Info(string text)
        {
            Console.WriteLine("  info    " + text);
        }

        static void Check(string what, bool ok, string detail)
        {
            if (ok) passed++;
            else failed++;
            Console.WriteLine((ok ? "  ok      " : "  FEHLER  ") + what + (ok || detail == null ? "" : "\r\n            -> " + detail));
        }

        static void Same(string what, object expected, object actual)
        {
            Check(what, Equals(expected, actual), "erwartet " + Show(expected) + ", ist " + Show(actual));
        }

        static string Show(object value)
        {
            if (value == null) return "null";
            string text = value.ToString();
            return text.Length == 0 ? "(leer)" : "„" + text.Replace("\r", "\\r").Replace("\n", "\\n") + "“";
        }
    }
}
