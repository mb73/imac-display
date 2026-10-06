using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace ImacDisplay
{
    /* The log of this run, live. "Diagnose" adds the read-only checks of --test, "Logdatei öffnen" shows earlier runs too. */
    internal sealed class LogWindow : ThemedForm
    {
        const int MaxLength = 400000;

        readonly Options options;
        readonly TextBox text = new TextBox();
        readonly Button diagnose = new Button(), file = new Button(), close = new Button();
        readonly Action<string> listener;

        public LogWindow(Options options)
        {
            this.options = options;
            Text = "iMac-Display – Log";
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = false;
            ClientSize = new Size(760, 440);
            MinimumSize = new Size(440, 260);

            text.Multiline = true;
            text.ReadOnly = true;
            text.WordWrap = true;
            text.ScrollBars = ScrollBars.Vertical;
            text.BorderStyle = BorderStyle.None;
            text.Dock = DockStyle.Fill;
            var field = new Panel();
            field.Dock = DockStyle.Fill;
            field.Margin = Padding.Empty;
            field.Padding = new Padding(12, 10, 2, 10);
            field.Controls.Add(text);

            diagnose.Text = "Diagnose";
            diagnose.Click += delegate { RunDiagnose(); };
            file.Text = "Logdatei öffnen";
            file.Click += delegate { OpenLogFile(); };
            close.Text = "Schließen";
            close.Click += delegate { Close(); };
            CancelButton = close;
            FlowLayoutPanel bar = ButtonBar(close, file, diagnose);

            var root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 1;
            root.RowCount = 2;
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.Margin = Padding.Empty;
            root.Controls.Add(field, 0, 0);
            root.Controls.Add(bar, 0, 1);
            Controls.Add(root);
            listener = delegate (string line) { Post(delegate { Append(line); }); };
            EndLayout();
        }

        /* Buttons right-aligned in the bar at the bottom, the first one rightmost */
        public static FlowLayoutPanel ButtonBar(params Button[] buttons)
        {
            var bar = new FlowLayoutPanel();
            bar.Tag = "bar";
            bar.Dock = DockStyle.Fill;
            bar.AutoSize = true;
            bar.FlowDirection = FlowDirection.RightToLeft;
            bar.WrapContents = false;
            bar.Margin = Padding.Empty;
            bar.Padding = new Padding(12, 10, 12, 10);
            foreach (Button button in buttons)
            {
                button.AutoSize = true;
                button.MinimumSize = new Size(96, 28);
                button.Margin = new Padding(8, 0, 0, 0);
                bar.Controls.Add(button);
            }
            return bar;
        }

        /* not disposing the previous font: a control keeps its old font when the new one is equal */
        protected override void UpdateFonts()
        {
            text.Font = new Font("Consolas", Font.Size, FontStyle.Regular, Font.Unit);
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            string[] lines = Program.ListenToLog(listener);
            text.Text = lines.Length == 0 ? "" : string.Join("\r\n", lines) + "\r\n";
            text.SelectionStart = text.TextLength;
            text.ScrollToCaret();
            ActiveControl = close;
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            Program.StopListening(listener);
            base.OnFormClosed(e);
        }

        void Append(string line)
        {
            if (IsDisposed) return;
            if (text.TextLength > MaxLength)
            {
                /* keep the newer half, from a line start */
                string kept = text.Text.Substring(text.TextLength - MaxLength / 2);
                int start = kept.IndexOf('\n');
                text.Text = start >= 0 ? kept.Substring(start + 1) : kept;
            }
            text.AppendText(line + "\r\n");
        }

        void RunDiagnose()
        {
            diagnose.Enabled = false;
            ThreadPool.QueueUserWorkItem(delegate
            {
                try { Program.Diagnose(options); }
                catch (Exception ex) { Program.Log("Die Diagnose brach ab: " + ex.Message); }  // a worker thread must not take the program down
                Post(delegate { diagnose.Enabled = true; });
            });
        }

        void OpenLogFile()
        {
            string path = Program.LogFile;
            try { Process.Start(path); }
            catch (Win32Exception)
            {
                /* no program for .log files */
                try { Process.Start("notepad.exe", "\"" + path + "\""); }
                catch (Win32Exception ex) { MessageBox.Show(this, ex.Message + "\r\n\r\n" + path, "iMac-Display", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            }
        }
    }

    /* Asks for the pairing code that LaptopScreen shows on the Mac */
    internal sealed class PairingDialog : ThemedForm
    {
        const int Width96 = 360;

        readonly TextBox code = new TextBox();
        readonly Label error = new Label();

        public PairingDialog(bool denied)
        {
            Text = "Kopplungscode";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;

            var hint = new Label();
            hint.AutoSize = true;
            hint.MaximumSize = new Size(Width96, 0);
            hint.Margin = new Padding(0, 0, 0, 12);
            hint.Text = denied
                ? "Der Mac hat den Kopplungscode abgelehnt. Gib bitte den Code ein, der jetzt im LaptopScreen-Fenster auf dem Mac steht."
                : "Gib den Kopplungscode ein, der im LaptopScreen-Fenster auf dem Mac steht, z. B. ABCD-EFGH-JKLM.";
            code.CharacterCasing = CharacterCasing.Upper;
            code.Width = Width96;
            code.Margin = Padding.Empty;
            error.AutoSize = true;
            error.MaximumSize = new Size(Width96, 0);
            error.Margin = new Padding(0, 6, 0, 0);
            error.Text = "Das sieht nicht wie ein Kopplungscode aus (z. B. ABCD-EFGH-JKLM).";
            error.Visible = false;
            var content = new FlowLayoutPanel();
            content.FlowDirection = FlowDirection.TopDown;
            content.WrapContents = false;
            content.AutoSize = true;
            content.Margin = new Padding(20, 18, 20, 18);
            content.Controls.AddRange(new Control[] { hint, code, error });

            var ok = new Button();
            ok.Text = "OK";
            ok.Click += delegate
            {
                if (PairingStore.LooksValid(code.Text))
                {
                    DialogResult = DialogResult.OK;
                    return;
                }
                error.ForeColor = Badges.ColorOf(Light.Problem, Theme.Current.Dark);
                error.Visible = true;
                code.Focus();
                code.SelectAll();
            };
            var cancel = new Button();
            cancel.Text = "Abbrechen";
            cancel.DialogResult = DialogResult.Cancel;
            AcceptButton = ok;
            CancelButton = cancel;

            var root = new TableLayoutPanel();
            root.ColumnCount = 1;
            root.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            root.AutoSize = true;
            root.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            root.Margin = Padding.Empty;
            root.Controls.Add(content, 0, 0);
            root.Controls.Add(LogWindow.ButtonBar(cancel, ok), 0, 1);
            Controls.Add(root);
            EndLayout();
        }

        public string Code
        {
            get { return code.Text.Trim(); }
        }

        protected override void UpdateFonts()
        {
            code.Font = new Font(Font.FontFamily, Font.Size * 4F / 3F, FontStyle.Regular, Font.Unit);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            code.Focus();
        }
    }

    /* Text in a scrollable box: what an update brings (with the question whether to install), or the diagnostics of --test */
    internal sealed class TextDialog : ThemedForm
    {
        readonly TextBox text = new TextBox();
        readonly Button accept;

        TextDialog(string title, string intro, string body, string acceptText, string cancelText, bool owned)
        {
            Text = title;
            StartPosition = owned ? FormStartPosition.CenterParent : FormStartPosition.CenterScreen;
            ShowInTaskbar = !owned;
            MinimizeBox = false;
            ClientSize = new Size(600, 420);
            MinimumSize = new Size(420, 300);

            var root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 1;
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            root.Margin = Padding.Empty;
            if (intro != null)
            {
                var label = new Label();
                label.AutoSize = true;
                label.MaximumSize = new Size(560, 0);
                label.Margin = new Padding(20, 16, 20, 12);
                label.Text = intro;
                root.Controls.Add(label);
                root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            }
            text.Multiline = true;
            text.ReadOnly = true;
            text.WordWrap = true;
            text.ScrollBars = ScrollBars.Vertical;
            text.BorderStyle = BorderStyle.None;
            text.Dock = DockStyle.Fill;
            text.Text = body.Replace("\r\n", "\n").Replace("\n", "\r\n");
            var field = new Panel();
            field.Dock = DockStyle.Fill;
            field.Margin = Padding.Empty;
            field.Padding = new Padding(16, 10, 2, 10);
            field.Controls.Add(text);
            root.Controls.Add(field);
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            var cancel = new Button();
            cancel.Text = cancelText;
            cancel.DialogResult = DialogResult.Cancel;
            CancelButton = cancel;
            if (acceptText != null)
            {
                accept = new Button();
                accept.Text = acceptText;
                accept.DialogResult = DialogResult.OK;
                AcceptButton = accept;
                root.Controls.Add(LogWindow.ButtonBar(cancel, accept));
            }
            else root.Controls.Add(LogWindow.ButtonBar(cancel));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(root);
            ActiveControl = accept ?? cancel;
            EndLayout();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            text.SelectionStart = 0;
            text.SelectionLength = 0;
        }

        public static bool Confirm(IWin32Window owner, string title, string intro, string body, string acceptText, string cancelText)
        {
            using (var dialog = new TextDialog(title, intro, body, acceptText, cancelText, true))
                return dialog.ShowDialog(owner) == DialogResult.OK;
        }

        public static void ShowText(string title, string body)
        {
            using (var dialog = new TextDialog(title, null, body, null, "Schließen", false))
                dialog.ShowDialog();
        }
    }
}
