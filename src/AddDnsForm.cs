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

        void Submit(bool applyNow)
        {
            var title = name.Text.Trim().Replace("=", "");
            if (title.Length == 0) { Warn(L.NeedName, name); return; }
            if (Presets.BuiltIn.Any(p => p.Name.Equals(title, StringComparison.OrdinalIgnoreCase)))
            {
                Warn(L.NameTaken, name);
                return;
            }
            if (primary.Text.Trim().Length == 0) { Warn(L.NeedPrimary, primary); return; }

            var all = string.Join(",", primary.Text, secondary.Text, extra.Text);
            var servers = Presets.ParseServers(all, out var bad);
            if (bad != null) { Warn(L.BadAddress(bad), primary); return; }

            Result = new DnsPreset { Name = title, Servers = servers };
            ApplyNow = applyNow;
            DialogResult = DialogResult.OK;
        }

        void Warn(string message, Control focus)
        {
            MessageBox.Show(this, message, L.DialogTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            focus.Focus();
        }

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

        static void SetCue(TextBox box, string hint) =>
            box.HandleCreated += (s, e) => SendMessage(box.Handle, 0x1501 /* EM_SETCUEBANNER */, (IntPtr)1, hint);
    }
}
