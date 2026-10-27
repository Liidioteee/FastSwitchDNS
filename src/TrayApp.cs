using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace FastSwitchDNS
{
    class TrayApp : ApplicationContext
    {
        readonly NotifyIcon tray = new NotifyIcon();
        readonly ContextMenuStrip menu = new ContextMenuStrip();
        readonly Timer refreshTimer = new Timer { Interval = 15000 };
        readonly bool live;
        Icon currentIcon;
        string currentIconKey;
        bool busy;

        /// <summary>Opens the "add custom DNS" dialog; tests replace it.</summary>
        internal Func<(DnsPreset preset, bool applyNow)?> AskCustom = () =>
        {
            using var form = new AddDnsForm();
            return form.ShowDialog() == DialogResult.OK ? (form.Result, form.ApplyNow) : ((DnsPreset, bool)?)null;
        };

        /// <summary>Last balloon shown: (title, text, isError). For tests and diagnostics.</summary>
        internal (string title, string text, bool error) LastNotice { get; private set; }

        internal ContextMenuStrip Menu => menu;
        internal string TrayText => tray.Text;
        internal Icon TrayIcon => tray.Icon;

        /// <param name="live">false creates the app without showing the icon or listening for network changes (tests).</param>
        public TrayApp(bool live = true)
        {
            this.live = live;
            menu.Opening += (s, e) => { BuildMenu(); e.Cancel = false; };
            menu.Items.Add("…"); // ContextMenuStrip won't open while empty

            tray.ContextMenuStrip = menu;
            tray.MouseUp += (s, e) =>
            {
                // Left click opens the same menu as right click
                if (e.Button == MouseButtons.Left)
                    typeof(NotifyIcon).GetMethod("ShowContextMenu", BindingFlags.Instance | BindingFlags.NonPublic)
                        ?.Invoke(tray, null);
            };

            if (live)
            {
                NetworkChange.NetworkAddressChanged += OnNetworkChanged;
                refreshTimer.Tick += (s, e) => RefreshStatus();
                refreshTimer.Start();
                menu.CreateControl(); // create a handle so BeginInvoke works before the first opening
            }

            RefreshStatus();
            tray.Visible = live;
        }

        void OnNetworkChanged(object sender, EventArgs e) => menu.BeginInvoke((Action)RefreshStatus);

        /// <summary>Returns the primary target adapter and the DNS preset currently set on it.</summary>
        static (AdapterInfo adapter, DnsPreset preset) CurrentState(List<DnsPreset> presets)
        {
            var target = DnsManager.Targets(Settings.AdapterId).FirstOrDefault();
            return (target, target == null ? null : DnsManager.Detect(target, presets));
        }

        /// <summary>Tray tooltip, cut to the 63-character NotifyIcon limit.</summary>
        internal static string StatusText(AdapterInfo adapter, DnsPreset preset, bool doh = false)
        {
            var text = adapter == null
                ? $"FastSwitchDNS\n{L.NoAdapters}"
                : $"DNS: {preset?.Name ?? L.AutoShort}{(doh ? " · " + L.Doh : "")}\n{adapter.Name}";
            return text.Length > 63 ? text.Substring(0, 63) : text;
        }

        internal void RefreshStatus()
        {
            if (busy) return;
            try
            {
                var (adapter, preset) = CurrentState(Presets.All());
                SetIcon(preset);
                tray.Text = StatusText(adapter, preset, adapter != null && preset != null && DnsManager.IsDohActive(adapter));
            }
            catch
            {
                // Adapters can disappear mid-query; the next tick will catch up
            }
        }

        void SetIcon(DnsPreset preset)
        {
            var key = preset?.Name ?? "";
            if (key == currentIconKey && currentIcon != null) return;
            var old = currentIcon;
            currentIcon = IconFactory.Create(preset);
            currentIconKey = key;
            tray.Icon = currentIcon;
            old?.Dispose();
        }

        internal void BuildMenu()
        {
            menu.Items.Clear();
            var presets = Presets.All();
            var (adapter, active) = CurrentState(presets);
            bool dohSupported = DnsManager.DohSupported();
            bool dohOn = dohSupported && Settings.DohEnabled;

            bool dohActive = adapter != null && active != null && DnsManager.IsDohActive(adapter);
            var header = new ToolStripMenuItem(adapter == null
                ? L.NoAdapters
                : $"{adapter.Name}: {active?.Name ?? L.AutoShort}{(dohActive ? " · " + L.Doh : "")}") { Enabled = false };
            menu.Items.Add(header);
            if (active != null && !presets.Contains(active))
                menu.Items.Add(new ToolStripMenuItem(string.Join(", ", active.Servers)) { Enabled = false });
            menu.Items.Add(new ToolStripSeparator());

            var auto = new ToolStripMenuItem(L.Auto) { Checked = adapter != null && active == null };
            auto.Click += (s, e) => _ = Switch(null);
            menu.Items.Add(auto);

            bool customStarted = false;
            foreach (var p in presets)
            {
                if (!p.BuiltIn && !customStarted)
                {
                    menu.Items.Add(new ToolStripSeparator());
                    customStarted = true;
                }
                var preset = p;
                var item = new ToolStripMenuItem(p.Name)
                {
                    Checked = active == p,
                    ShortcutKeyDisplayString = (p.V4.FirstOrDefault() ?? p.Servers.FirstOrDefault()) +
                                               (p.HasDoh && dohOn ? " · " + L.Doh : ""),
                    ToolTipText = p.HasDoh && dohSupported ? p.DohTemplate : null,
                    Image = Swatch(IconFactory.ColorFor(p)),
                };
                item.Click += (s, e) => _ = Switch(preset);
                menu.Items.Add(item);
            }

            menu.Items.Add(new ToolStripSeparator());

            var add = new ToolStripMenuItem(L.AddCustom);
            add.Click += (s, e) => _ = AddCustom();
            menu.Items.Add(add);

            var customs = presets.Where(p => !p.BuiltIn).ToList();
            if (customs.Count > 0)
            {
                var remove = new ToolStripMenuItem(L.RemoveCustom);
                foreach (var p in customs)
                {
                    var name = p.Name;
                    remove.DropDownItems.Add(name, null, (s, e) => { Presets.RemoveCustom(name); RefreshStatus(); });
                }
                menu.Items.Add(remove);
            }

            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(BuildAdapterMenu());

            if (dohSupported)
            {
                var doh = new ToolStripMenuItem(L.DohToggle) { Checked = Settings.DohEnabled };
                doh.Click += (s, e) => { Settings.DohEnabled = !Settings.DohEnabled; _ = Reapply(active, presets); };
                menu.Items.Add(doh);

                var fallback = new ToolStripMenuItem(L.DohFallback)
                {
                    Checked = Settings.DohFallback,
                    Enabled = Settings.DohEnabled,
                };
                fallback.Click += (s, e) => { Settings.DohFallback = !Settings.DohFallback; _ = Reapply(active, presets); };
                menu.Items.Add(fallback);
            }

            var flush = new ToolStripMenuItem(L.FlushCache);
            flush.Click += (s, e) =>
            {
                DnsManager.FlushCache();
                Notify("FastSwitchDNS", L.CacheFlushed, false);
            };
            menu.Items.Add(flush);

            var startup = new ToolStripMenuItem(L.Autostart) { Checked = Autostart.IsEnabled() };
            startup.Click += (s, e) =>
            {
                try { Autostart.Set(!startup.Checked); }
                catch (Exception ex) { ShowError(ex.Message); }
            };
            menu.Items.Add(startup);

            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(L.Exit, null, (s, e) => ExitThread());
        }

        ToolStripMenuItem BuildAdapterMenu()
        {
            var root = new ToolStripMenuItem(L.Adapter);
            var selected = Settings.AdapterId;
            var adapters = DnsManager.Adapters();
            bool selectedPresent = adapters.Any(a => a.Id == selected);

            var all = new ToolStripMenuItem(L.AllActive) { Checked = !selectedPresent };
            all.Click += (s, e) => { Settings.AdapterId = ""; RefreshStatus(); };
            root.DropDownItems.Add(all);
            root.DropDownItems.Add(new ToolStripSeparator());

            foreach (var a in adapters)
            {
                var id = a.Id;
                var item = new ToolStripMenuItem(a.Name)
                {
                    Checked = id == selected,
                    ToolTipText = a.Description,
                };
                item.Click += (s, e) => { Settings.AdapterId = id; RefreshStatus(); };
                root.DropDownItems.Add(item);
            }
            return root;
        }

        static Bitmap Swatch(Color color)
        {
            var bmp = new Bitmap(16, 16);
            using var g = Graphics.FromImage(bmp);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var brush = new SolidBrush(color);
            g.FillEllipse(brush, 4, 4, 8, 8);
            return bmp;
        }

        /// <summary>After a DoH setting changes, re-applies the active preset so the change takes effect now.</summary>
        internal Task Reapply(DnsPreset active, List<DnsPreset> presets) =>
            active != null && presets.Contains(active) && active.HasDoh ? Switch(active) : Task.CompletedTask;

        internal async Task AddCustom()
        {
            var answer = AskCustom();
            if (answer == null) return;
            Presets.AddCustom(answer.Value.preset);
            if (answer.Value.applyNow) await Switch(answer.Value.preset);
            else RefreshStatus();
        }

        /// <summary>Applies <paramref name="preset"/> (null = DHCP) to every target adapter; returns the errors, or null.</summary>
        internal static string ApplyToAll(IEnumerable<AdapterInfo> targets, DnsPreset preset, bool doh, bool allowFallback)
        {
            var template = doh && preset != null && preset.HasDoh ? preset.DohTemplate : null;
            var errors = new List<string>();
            foreach (var t in targets)
            {
                try { DnsManager.Apply(t, preset?.Servers, template, allowFallback); }
                catch (Exception ex) { errors.Add(ex.Message); }
            }
            DnsManager.FlushCache();
            return errors.Count > 0 ? string.Join("\n", errors) : null;
        }

        internal async Task Switch(DnsPreset preset)
        {
            if (busy) return;
            busy = true;
            tray.Text = L.Applying;
            var targets = DnsManager.Targets(Settings.AdapterId);
            bool doh = Settings.DohEnabled && DnsManager.DohSupported(), fallback = Settings.DohFallback;

            var error = await Task.Run(() => ApplyToAll(targets, preset, doh, fallback));

            busy = false;
            currentIconKey = null; // force redraw
            RefreshStatus();

            if (error != null)
                ShowError(error);
            else
                Notify("FastSwitchDNS",
                    $"DNS: {preset?.Name ?? L.Auto}{(doh && preset?.HasDoh == true ? " · " + L.Doh : "")}\n{string.Join(", ", targets.Select(t => t.Name))}", false);
        }

        void Notify(string title, string text, bool error)
        {
            LastNotice = (title, text, error);
            if (live)
                tray.ShowBalloonTip(error ? 5000 : 1500, title, text, error ? ToolTipIcon.Error : ToolTipIcon.Info);
        }

        void ShowError(string message) =>
            Notify(L.Error, message.Length > 250 ? message.Substring(0, 250) : message, true);

        protected override void ExitThreadCore()
        {
            if (live) NetworkChange.NetworkAddressChanged -= OnNetworkChanged;
            refreshTimer.Stop();
            refreshTimer.Dispose();
            tray.Visible = false;
            tray.Dispose();
            menu.Dispose();
            currentIcon?.Dispose();
            base.ExitThreadCore();
        }
    }
}
