using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace ImacDisplay
{
    /* What the window shows for the agent: light, headline, detail and progress (ThinBar.None, ThinBar.Unknown or 0..100) */
    internal sealed class AgentStatus
    {
        public readonly Light Light;
        public readonly string Headline, Detail;
        public readonly int Progress;

        public AgentStatus(Light light, string headline, string detail, int progress)
        {
            Light = light;
            Headline = headline;
            Detail = detail;
            Progress = progress;
        }
    }

    /*
     The program window: what the agent is doing, "Trennen und beenden" (or "Verbinden" after the agent
     paused itself), the clipboard switch, links to the guide and the log, and the offer of a newer
     version when there is one. The taskbar button shows the state as a badge and downloads and display
     switches as progress. The agent runs on its own thread; ShowStatus, Ask, AskCode, Warn and
     CheckForUpdatesSoon may be called from there.
     */
    internal sealed class MainWindow : ThemedForm
    {
        const int TextWidth = 380;
        /* the Downloads folder every few seconds, GitHub at the start and then every few hours */
        const int CheckInterval = 5000;
        static readonly TimeSpan OnlineInterval = TimeSpan.FromHours(6);

        readonly Options options;
        readonly bool displayHandedOver;
        readonly StatusLight light = new StatusLight();
        readonly Label headline = new Label(), detail = new Label();
        readonly ThinBar progress = new ThinBar();
        readonly TableLayoutPanel banner = new TableLayoutPanel();
        readonly Label bannerText = new Label();
        readonly Button bannerButton = new Button();
        readonly ThinBar bannerProgress = new ThinBar();
        readonly ThemedCheckBox clipboard = new ThemedCheckBox();
        readonly Button connect = new Button();
        readonly LinkLabel guide = new LinkLabel(), log = new LinkLabel();
        readonly Label version = new Label();
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();

        AgentStatus status = new AgentStatus(Light.Busy, "Starte …", "", ThinBar.None);
        Taskbar taskbar;
        LogWindow logWindow;
        /* a newer version, and the progress of fetching or installing it (shown on the taskbar before the agent's) */
        UpdateOffer offer;
        int updateProgress = ThinBar.None;
        bool checking, updating, browserFallback, updateRequested, restarting;
        volatile bool closing;
        DateTime nextOnlineCheck = DateTime.MinValue;

        public MainWindow(Options options, bool displayHandedOver)
        {
            this.options = options;
            this.displayHandedOver = displayHandedOver;
            Text = "iMac-Display";
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;

            /* status: light, headline, detail, progress */
            light.Size = new Size(12, 12);
            light.Margin = new Padding(0, 8, 10, 0);
            headline.AutoSize = true;
            headline.MaximumSize = new Size(TextWidth, 0);
            headline.Margin = new Padding(0, 0, 0, 4);
            detail.AutoSize = true;
            detail.Tag = "muted";
            /* two lines' room, so the window keeps its height when the text changes */
            detail.MinimumSize = new Size(TextWidth, 34);
            detail.MaximumSize = new Size(TextWidth, 0);
            detail.Margin = Padding.Empty;
            progress.Size = new Size(TextWidth, 4);
            progress.Margin = new Padding(0, 8, 0, 0);
            var texts = new FlowLayoutPanel();
            texts.FlowDirection = FlowDirection.TopDown;
            texts.WrapContents = false;
            texts.AutoSize = true;
            texts.Margin = Padding.Empty;
            texts.Controls.AddRange(new Control[] { headline, detail, progress });
            var top = new TableLayoutPanel();
            top.ColumnCount = 2;
            top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            top.AutoSize = true;
            top.Margin = new Padding(20, 18, 20, 8);
            top.Controls.Add(light, 0, 0);
            top.Controls.Add(texts, 1, 0);

            /* the offer of a newer version */
            banner.Tag = "banner";
            banner.Visible = false;
            banner.AutoSize = true;
            banner.Dock = DockStyle.Fill;
            banner.ColumnCount = 2;
            banner.RowCount = 2;
            banner.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            banner.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            banner.Padding = new Padding(12, 8, 8, 8);
            banner.Margin = new Padding(20, 0, 20, 10);
            bannerText.AutoSize = true;
            bannerText.Anchor = AnchorStyles.Left;
            bannerText.MaximumSize = new Size(270, 0);
            bannerText.Margin = new Padding(0, 0, 10, 0);
            bannerButton.AutoSize = true;
            bannerButton.Anchor = AnchorStyles.Right;
            bannerButton.Margin = Padding.Empty;
            bannerButton.Click += delegate { OnUpdateClicked(); };
            bannerProgress.Size = new Size(270, 4);
            bannerProgress.Anchor = AnchorStyles.Left;
            bannerProgress.Margin = new Padding(0, 8, 10, 2);
            bannerProgress.Visible = false;
            banner.Controls.Add(bannerText, 0, 0);
            banner.Controls.Add(bannerButton, 1, 0);
            banner.Controls.Add(bannerProgress, 0, 1);
            banner.SetRowSpan(bannerButton, 2);

            clipboard.Text = "Zwischenablage mit dem Mac teilen";
            clipboard.AutoSize = true;
            clipboard.Checked = Program.ShareClipboard;
            clipboard.Margin = new Padding(42, 2, 20, 16);
            clipboard.CheckedChanged += delegate { Program.ShareClipboard = clipboard.Checked; };

            /* button bar */
            connect.Text = "Trennen und beenden";
            connect.AutoSize = true;
            connect.MinimumSize = new Size(150, 30);
            connect.Anchor = AnchorStyles.Left;
            connect.Margin = Padding.Empty;
            connect.Click += delegate { OnConnectClicked(); };
            guide.Text = "Anleitung";
            guide.AutoSize = true;
            guide.Margin = new Padding(0, 0, 14, 0);
            guide.LinkClicked += delegate { OpenInBrowser(Program.GuideUrl); };
            log.Text = "Log";
            log.AutoSize = true;
            log.Margin = new Padding(0, 0, 14, 0);
            log.LinkClicked += delegate { ShowLog(); };
            version.Text = Updater.Version;
            version.Tag = "muted";
            version.AutoSize = true;
            version.Margin = Padding.Empty;
            var links = new FlowLayoutPanel();
            links.AutoSize = true;
            links.WrapContents = false;
            links.Anchor = AnchorStyles.Right;
            links.Margin = Padding.Empty;
            links.Controls.AddRange(new Control[] { guide, log, version });
            var bar = new TableLayoutPanel();
            bar.Tag = "bar";
            bar.Dock = DockStyle.Fill;
            bar.AutoSize = true;
            bar.ColumnCount = 2;
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            bar.Margin = Padding.Empty;
            bar.Padding = new Padding(20, 12, 20, 12);
            bar.Controls.Add(connect, 0, 0);
            bar.Controls.Add(links, 1, 0);

            var root = new TableLayoutPanel();
            root.ColumnCount = 1;
            root.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            root.AutoSize = true;
            root.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            root.Margin = Padding.Empty;
            root.Controls.Add(top, 0, 0);
            root.Controls.Add(banner, 0, 1);
            root.Controls.Add(clipboard, 0, 2);
            root.Controls.Add(bar, 0, 3);
            Controls.Add(root);
            ShowStatus(status);
            EndLayout();
        }

        /* not disposing the previous font: a control keeps its old font when the new one is equal */
        protected override void UpdateFonts()
        {
            headline.Font = Semibold(Font, 12F / 9F);
        }

        /* Segoe UI Semibold where it exists, bold otherwise */
        static Font Semibold(Font basis, float factor)
        {
            var font = new Font("Segoe UI Semibold", basis.Size * factor, FontStyle.Regular, basis.Unit);
            if (font.Name == "Segoe UI Semibold") return font;
            font.Dispose();
            return new Font(basis.FontFamily, basis.Size * factor, FontStyle.Bold, basis.Unit);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Program.StartAgent(options, displayHandedOver);
            timer.Interval = CheckInterval;
            timer.Tick += delegate { CheckForUpdates(false); };
            timer.Start();
            if (options.UpdateZip != null) OfferZip(options.UpdateZip);
            else
            {
                updateRequested = options.Update;
                CheckForUpdates(true);
            }
        }

        /* Alt+F4, "Fenster schließen" on the taskbar or "Trennen und beenden" (the X only minimizes, see WndProc); taskkill sends a bare WM_CLOSE */
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            if (e.Cancel || restarting) return;
            bool byUser = e.CloseReason == CloseReason.UserClosing || e.CloseReason == CloseReason.TaskManagerClosing;
            if (byUser && !LidConfirmed("Trotzdem beenden?", "Beenden"))
            {
                e.Cancel = true;
                return;
            }
            closing = true;
            timer.Stop();
            Hide();
            Program.Shutdown(false);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_SYSCOMMAND && ((long)m.WParam & 0xFFF0) == Native.SC_CLOSE && OnCloseButton(m.LParam))
            {
                /* the X puts the window into the taskbar; the connection stays */
                WindowState = FormWindowState.Minimized;
                return;
            }
            if (m.Msg == Taskbar.ButtonCreatedMessage && m.Msg != 0)
            {
                if (taskbar == null) taskbar = Taskbar.For(Handle);
                UpdateTaskbar();
            }
            else if (m.Msg == Native.WM_SETTINGCHANGE || m.Msg == Native.WM_SYSCOLORCHANGE)
            {
                /* light/dark mode or high contrast switched */
                if (Theme.Refresh() || m.Msg == Native.WM_SYSCOLORCHANGE)
                {
                    foreach (Form form in Application.OpenForms)
                    {
                        var themed = form as ThemedForm;
                        if (themed != null) themed.ApplyTheme();
                    }
                }
            }
            base.WndProc(ref m);
        }

        /*
         A click on the X sends SC_CLOSE with the cursor position. Alt+F4, the window menu and "Fenster schließen"
         on the taskbar do not point at the X: they end the program.
         */
        bool OnCloseButton(IntPtr cursor)
        {
            return cursor != IntPtr.Zero && Native.SendMessage(Handle, Native.WM_NCHITTEST, IntPtr.Zero, cursor) == (IntPtr)Native.HTCLOSE;
        }

        /* ---- for the agent (any thread) ---- */

        public void ShowStatus(AgentStatus next)
        {
            if (IsHandleCreated && InvokeRequired)
            {
                Post(delegate { ShowStatus(next); });
                return;
            }
            status = next;
            light.Light = next.Light;
            light.AccessibleName = next.Headline;
            headline.Text = next.Headline;
            detail.Text = next.Detail;
            progress.Value = next.Progress;
            connect.Text = Program.Paused ? "Verbinden" : "Trennen und beenden";
            UpdateTaskbar();
        }

        /* A question with accept or "Abbrechen"; false if cancelled or the window is closing */
        public bool Ask(string question, string accept)
        {
            return OnWindow<bool>(delegate
            {
                ComeForward();
                return MessageDialog.Confirm(this, question, MessageKind.Information, accept, "Abbrechen", true);
            }, false);
        }

        public void Warn(string message)
        {
            OnWindow<bool>(delegate
            {
                ComeForward();
                MessageDialog.Show(this, message, MessageKind.Warning);
                return true;
            }, false);
        }

        /* The pairing code from the Mac, or null if the user cancels */
        public string AskCode(bool denied)
        {
            return OnWindow<string>(delegate
            {
                ComeForward();
                using (var dialog = new PairingDialog(denied))
                    return dialog.ShowDialog(this) == DialogResult.OK ? dialog.Code : null;
            }, null);
        }

        /* LaptopScreen is newer than this program: look on GitHub right now */
        public void CheckForUpdatesSoon()
        {
            Post(delegate
            {
                nextOnlineCheck = DateTime.MinValue;
                CheckForUpdates(true);
            });
        }

        /* Runs question on the window's thread and waits for the answer; fallback if the window closes meanwhile */
        T OnWindow<T>(Func<T> question, T fallback)
        {
            Func<T> guarded = delegate { return closing ? fallback : question(); };
            IAsyncResult pending;
            try { pending = BeginInvoke(guarded); }
            catch (InvalidOperationException) { return fallback; }
            while (!pending.AsyncWaitHandle.WaitOne(200))
                if (closing) return fallback;
            try { return (T)EndInvoke(pending); }
            catch (InvalidOperationException) { return fallback; }
        }

        void ComeForward()
        {
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Activate();
        }

        /* ---- buttons and links ---- */

        /* "Trennen und beenden" closes the window like Alt+F4, so OnFormClosing asks first if the lid is closed */
        void OnConnectClicked()
        {
            if (!Program.Paused)
            {
                Close();
                return;
            }
            Program.Resume();
            ShowStatus(new AgentStatus(Light.Busy, "Suche den Mac …", "", ThinBar.None));
        }

        /* With the lid closed the Mac is the only screen: without it the laptop shows nothing until the lid opens */
        bool LidConfirmed(string question, string action)
        {
            if (!Program.InSession || !Program.LidClosed) return true;
            return MessageDialog.Confirm(this, "Der Deckel ist zu, der Mac ist der einzige Bildschirm des Laptops. "
                + "Ohne ihn siehst du den Laptop erst wieder, wenn du ihn aufklappst.\r\n\r\n" + question,
                MessageKind.Warning, action, "Abbrechen", false);
        }

        void OpenInBrowser(string url)
        {
            try { Process.Start(url); }
            catch (Win32Exception) { MessageDialog.Show(this, "Bitte im Browser öffnen:\r\n" + url, MessageKind.Information); }
        }

        void ShowLog()
        {
            if (logWindow == null || logWindow.IsDisposed)
            {
                logWindow = new LogWindow(options);
                logWindow.Show(this);
                return;
            }
            if (logWindow.WindowState == FormWindowState.Minimized) logWindow.WindowState = FormWindowState.Normal;
            logWindow.Activate();
        }

        void UpdateTaskbar()
        {
            if (taskbar == null) return;
            try
            {
                taskbar.SetBadge(Badges.TaskbarIcon(status.Light), status.Headline);
                int value = updateProgress != ThinBar.None ? updateProgress : status.Progress;
                if (value == ThinBar.None) taskbar.SetProgress(Taskbar.NoProgress, 0);
                else if (value == ThinBar.Unknown) taskbar.SetProgress(Taskbar.Indeterminate, 0);
                else taskbar.SetProgress(Taskbar.Normal, value);
            }
            catch (COMException) { }  // Explorer is restarting; TaskbarButtonCreated follows
        }

        /* ---- updates ---- */

        void CheckForUpdates(bool online)
        {
            if (checking || updating || closing) return;
            bool requested = updateRequested;
            updateRequested = false;
            if (Updater.IsGitCheckout(Program.BaseDirectory))
            {
                if (requested)
                    MessageDialog.Show(this, "Dieser Ordner ist ein Git-Arbeitsverzeichnis: bitte mit git pull aktualisieren.", MessageKind.Information);
                return;
            }
            online = online || DateTime.UtcNow >= nextOnlineCheck;
            if (online) nextOnlineCheck = DateTime.UtcNow + OnlineInterval;
            checking = true;
            ThreadPool.QueueUserWorkItem(delegate
            {
                string error = null;
                UpdateOffer found = null;
                try { found = Updater.FindOffer(online, out error); }
                catch (Exception ex) { error = ex.Message; }  // a worker thread must not take the program down
                if (online && error != null) Program.Log("Die Suche nach einer neuen Version hat GitHub nicht erreicht: " + error);
                Post(delegate
                {
                    checking = false;
                    if (found != null) Offer(found);
                    if (!requested) return;
                    if (offer != null) OnUpdateClicked();
                    else ReportUpToDate(error);
                });
            });
        }

        /* Shows a newer version unless the same or a newer one is offered already; a zip on the laptop beats GitHub */
        void Offer(UpdateOffer found)
        {
            if (updating) return;
            if (offer != null)
            {
                int order = Updater.Compare(found.Version, offer.Version);
                if (order < 0 || (order == 0 && (offer.Zip != null || found.Zip == null))) return;
            }
            offer = found;
            ShowOffer();
        }

        void ShowOffer()
        {
            browserFallback = false;
            if (offer == null) banner.Visible = false;
            else ShowBanner("iMac-Display " + offer.Version + " ist da.", "Aktualisieren …", ThinBar.None);
            updateProgress = ThinBar.None;
            UpdateTaskbar();
        }

        void ShowBanner(string text, string button, int value)
        {
            bannerText.Text = text;
            bannerButton.Visible = button != null;
            if (button != null) bannerButton.Text = button;
            bannerProgress.Value = value;
            bannerProgress.Visible = value != ThinBar.None;
            banner.Visible = true;
            updateProgress = value;
            UpdateTaskbar();
        }

        void ReportUpToDate(string error)
        {
            if (error == null)
            {
                MessageDialog.Show(this, "iMac-Display ist auf dem neuesten Stand (" + Updater.Version + ").", MessageKind.Information);
                return;
            }
            if (MessageDialog.Confirm(this, "GitHub ist gerade nicht erreichbar (" + error + ").\r\n\r\nDie aktuelle Version im Browser herunterladen?",
                MessageKind.Information, "Im Browser laden", "Abbrechen", true))
                WaitForBrowser();
        }

        void OnUpdateClicked()
        {
            if (offer == null || updating) return;
            if (browserFallback)
            {
                WaitForBrowser();
                return;
            }
            if (offer.Zip != null)
            {
                ConfirmAndInstall(offer.Zip, offer.Version, false);
                return;
            }
            /* still on GitHub: fetch it first */
            updating = true;
            string wanted = offer.Version;
            ShowBanner("Lade iMac-Display " + wanted + " herunter …", null, ThinBar.Unknown);
            ThreadPool.QueueUserWorkItem(delegate
            {
                string error;
                string zip = Updater.DownloadRelease(delegate (long done, long total)
                {
                    int percent = total > 0 ? (int)(done * 100 / total) : ThinBar.Unknown;
                    Post(delegate { if (updating) ShowBanner("Lade iMac-Display " + wanted + " herunter …", null, percent); });
                }, out error);
                Post(delegate
                {
                    updating = false;
                    if (zip == null)
                    {
                        Program.Log("Die neue Version ließ sich nicht herunterladen: " + error);
                        ShowBanner("Der Download hat nicht geklappt: " + error, "Im Browser laden", ThinBar.None);
                        browserFallback = true;
                        return;
                    }
                    string downloaded = Updater.ZipVersion(zip);
                    offer = new UpdateOffer(downloaded, zip);
                    ConfirmAndInstall(zip, downloaded, true);
                });
            });
        }

        /* The browser fetches the zip; the regular look into the Downloads folder finds it there */
        void WaitForBrowser()
        {
            OpenInBrowser(Updater.DownloadUrl);
            ShowBanner("Sobald die Zip-Datei im Ordner „Downloads“ liegt, geht es hier weiter.", null, ThinBar.None);
        }

        /* --update with a zip, e.g. dropped onto update.cmd */
        void OfferZip(string zip)
        {
            if (Updater.IsGitCheckout(Program.BaseDirectory))
            {
                MessageDialog.Show(this, "Dieser Ordner ist ein Git-Arbeitsverzeichnis: bitte mit git pull aktualisieren.", MessageKind.Information);
                return;
            }
            string zipVersion = Updater.ZipVersion(zip);
            if (zipVersion == null)
            {
                MessageDialog.Show(this, Path.GetFileName(zip) + " ist keine Zip-Datei von imac-display.", MessageKind.Warning);
                return;
            }
            offer = new UpdateOffer(zipVersion, zip);
            ConfirmAndInstall(zip, zipVersion, false);
        }

        /* Shows what is new and installs on request; temporary: the zip was downloaded into %TEMP% */
        void ConfirmAndInstall(string zip, string newVersion, bool temporary)
        {
            string intro = Updater.Compare(newVersion, Updater.Version) > 0
                ? "Installiert ist " + Updater.Version + "."
                : "Die Zip-Datei enthält Version " + newVersion + ", installiert ist " + Updater.Version + ".";
            intro += Program.InSession
                ? " iMac-Display startet danach neu; die Verbindung zum Mac ist dabei ein paar Sekunden unterbrochen."
                : " iMac-Display startet danach neu.";
            string changes = Updater.Changes(zip);
            if (!TextDialog.Confirm(this, "iMac-Display " + newVersion + " installieren?", intro,
                changes.Length > 0 ? changes : "(Die Zip-Datei sagt nicht, was sich geändert hat.)", "Installieren", "Später"))
            {
                ShowOffer();
                return;
            }
            updating = true;
            ShowBanner("Installiere iMac-Display " + newVersion + " …", null, ThinBar.Unknown);
            ThreadPool.QueueUserWorkItem(delegate
            {
                string error = Updater.Install(zip);
                if (temporary) Updater.DeleteQuietly(zip);
                Post(delegate
                {
                    if (error == null)
                    {
                        RestartNow();
                        return;
                    }
                    updating = false;
                    if (temporary) offer = new UpdateOffer(newVersion, null);
                    ShowOffer();
                    MessageDialog.Show(this, "Die Aktualisierung ist fehlgeschlagen:\r\n\r\n" + error, MessageKind.Warning);
                });
            });
        }

        void RestartNow()
        {
            ShowBanner("Starte die neue Version …", null, ThinBar.Unknown);
            Update();  // paint this before the wait for the agent
            restarting = true;
            closing = true;
            timer.Stop();
            if (!Program.RestartAfterUpdate(options))
                MessageDialog.Show(this, "Die neue Version ist installiert, ließ sich aber nicht starten. Bitte starte iMac-Display noch einmal.",
                    MessageKind.Warning);
            Close();
        }
    }
}
