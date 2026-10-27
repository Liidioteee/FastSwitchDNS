using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace FastSwitchDNS
{
    /// <summary>Dialog for entering a custom DNS server set.</summary>
    class AddDnsForm : Form
    {
        readonly TextBox name = new TextBox();
        readonly TextBox primary = new TextBox();
        readonly TextBox secondary = new TextBox();
        readonly TextBox extra = new TextBox();
        readonly TextBox doh = new TextBox();

        public DnsPreset Result { get; private set; }
        public bool ApplyNow { get; private set; }

        public AddDnsForm()
        {
            Text = L.DialogTitle;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = true;
            StartPosition = FormStartPosition.CenterScreen;
            TopMost = true;
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = SystemFonts.MessageBoxFont;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Padding = new Padding(12);

            var table = new TableLayoutPanel
            {
                ColumnCount = 2,
                AutoSize = true,
                Dock = DockStyle.Fill,
            };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            void Row(string label, TextBox box, string hint)
            {
                box.Width = 260;
                SetCue(box, hint);
                table.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 8, 6) });
                table.Controls.Add(box);
            }

            Row(L.NameLabel, name, "My DNS");
            Row(L.PrimaryLabel, primary, "1.2.3.4");
            Row(L.SecondaryLabel, secondary, "5.6.7.8");

            var extraLabel = new Label { Text = L.ExtraLabel, AutoSize = true, Margin = new Padding(0, 10, 0, 4) };
            table.Controls.Add(extraLabel);
            table.SetColumnSpan(extraLabel, 2);
            extra.Width = 260;
            SetCue(extra, "2001:db8::1, 2001:db8::2");
            table.Controls.Add(new Label());
            table.Controls.Add(extra);

            // Windows 10 can't use a DoH template, so don't ask for one
            if (DnsManager.DohSupported())
                Row(L.DohLabel, doh, L.DohHint);

            var buttons = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.RightToLeft,
                AutoSize = true,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 12, 0, 0),
            };
            var cancel = new Button { Text = L.Cancel, AutoSize = true, DialogResult = DialogResult.Cancel };
            var save = new Button { Text = L.SaveOnly, AutoSize = true };
            var apply = new Button { Text = L.SaveAndApply, AutoSize = true };
            save.Click += (s, e) => Submit(false);
            apply.Click += (s, e) => Submit(true);
            buttons.Controls.AddRange(new Control[] { cancel, save, apply });
            table.Controls.Add(buttons);
            table.SetColumnSpan(buttons, 2);

            Controls.Add(table);
            AcceptButton = apply;
            CancelButton = cancel;
        }

        internal TextBox NameBox => name;
        internal TextBox PrimaryBox => primary;
        internal TextBox SecondaryBox => secondary;
        internal TextBox ExtraBox => extra;
        internal TextBox DohBox => doh;

        /// <summary>Shows a validation warning; tests replace it to avoid a modal message box.</summary>
        internal Action<string> ShowWarning;

        internal enum Field { Name, Primary, Doh }

        /// <summary>Checks the entered values. Returns null and the preset, or an error message and the field to fix.</summary>
        internal static string Validate(string nameText, string primaryText, string secondaryText, string extraText,
            string dohText, out DnsPreset preset, out Field field)
        {
            preset = null;
            field = Field.Name;
            var title = (nameText ?? "").Trim().Replace("=", "");
            if (title.Length == 0) return L.NeedName;
            if (Presets.BuiltIn.Any(p => p.Name.Equals(title, StringComparison.OrdinalIgnoreCase)))
                return L.NameTaken;

            field = Field.Primary;
            if ((primaryText ?? "").Trim().Length == 0) return L.NeedPrimary;

            var all = string.Join(",", primaryText, secondaryText, extraText);
            var servers = Presets.ParseServers(all, out var bad);
            if (bad != null) return L.BadAddress(bad);

            field = Field.Doh;
            var template = (dohText ?? "").Trim();
            if (template.Length > 0 && !Presets.IsValidTemplate(template)) return L.BadTemplate(template);

            preset = new DnsPreset { Name = title, Servers = servers, DohTemplate = template.Length > 0 ? template : null };
            return null;
        }

        internal void Submit(bool applyNow)
        {
            var error = Validate(name.Text, primary.Text, secondary.Text, extra.Text, doh.Text, out var preset, out var field);
            if (error != null)
            {
                Warn(error, field == Field.Name ? name : field == Field.Doh ? doh : primary);
                return;
            }
            Result = preset;
            ApplyNow = applyNow;
            DialogResult = DialogResult.OK;
        }

        void Warn(string message, Control focus)
        {
            if (ShowWarning != null) ShowWarning(message);
            else MessageBox.Show(this, message, L.DialogTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            focus.Focus();
        }

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

        static void SetCue(TextBox box, string hint) =>
            box.HandleCreated += (s, e) => SendMessage(box.Handle, 0x1501 /* EM_SETCUEBANNER */, (IntPtr)1, hint);
    }
}
