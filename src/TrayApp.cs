using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace FastSwitchDNS
{
    class TrayApp : ApplicationContext
    {
        const string SettingsKey = @"Software\FastSwitchDNS";

        readonly NotifyIcon tray = new NotifyIcon();
        readonly ContextMenuStrip menu = new ContextMenuStrip();
        readonly Timer refreshTimer = new Timer { Interval = 15000 };
        Icon currentIcon;
        string currentIconKey;
        bool busy;

        public TrayApp()
        {
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

            NetworkChange.NetworkAddressChanged += (s, e) => menu.BeginInvoke((Action)RefreshStatus);
            refreshTimer.Tick += (s, e) => RefreshStatus();
            refreshTimer.Start();

            menu.CreateControl(); // create a handle so BeginInvoke works before the first opening
            RefreshStatus();
            tray.Visible = true;
        }

        static string SelectedAdapterId
        {
            get
            {
                using var key = Registry.CurrentUser.OpenSubKey(SettingsKey);
                return key?.GetValue("Adapter") as string ?? "";
            }
            set
            {
                using var key = Registry.CurrentUser.CreateSubKey(SettingsKey);
                key.SetValue("Adapter", value ?? "");
            }
        }

        /// <summary>Returns the primary target adapter and the DNS preset currently set on it.</summary>
        (NetworkInterface adapter, DnsPreset preset) CurrentState(List<DnsPreset> presets)
        {
            var target = DnsManager.Targets(SelectedAdapterId).FirstOrDefault();
            return (target, target == null ? null : DnsManager.Detect(target, presets));
        }

        void RefreshStatus()
        {
            if (busy) return;
            try
            {
                var (adapter, preset) = CurrentState(Presets.All());
                SetIcon(preset);
                var text = adapter == null
                    ? $"FastSwitchDNS\n{L.NoAdapters}"
                    : $"DNS: {preset?.Name ?? L.AutoShort}\n{adapter.Name}";
                tray.Text = text.Length > 63 ? text.Substring(0, 63) : text; // NotifyIcon.Text limit
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

        void BuildMenu()
        {
            menu.Items.Clear();
            var presets = Presets.All();
            var (adapter, active) = CurrentState(presets);

            var header = new ToolStripMenuItem(adapter == null
                ? L.NoAdapters
                : $"{adapter.Name}: {active?.Name ?? L.AutoShort}") { Enabled = false };
            menu.Items.Add(header);
            if (active != null && !presets.Contains(active))
                menu.Items.Add(new ToolStripMenuItem(string.Join(", ", active.Servers)) { Enabled = false });
            menu.Items.Add(new ToolStripSeparator());

            var auto = new ToolStripMenuItem(L.Auto) { Checked = adapter != null && active == null };
            auto.Click += (s, e) => Switch(null);
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
                    ShortcutKeyDisplayString = p.V4.FirstOrDefault() ?? p.Servers.FirstOrDefault(),
                    Image = Swatch(IconFactory.ColorFor(p)),
                };
                item.Click += (s, e) => Switch(preset);
                menu.Items.Add(item);
            }

            menu.Items.Add(new ToolStripSeparator());

            var add = new ToolStripMenuItem(L.AddCustom);
            add.Click += (s, e) => AddCustom();
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

            var flush = new ToolStripMenuItem(L.FlushCache);
            flush.Click += (s, e) =>
            {
                DnsManager.FlushCache();
                tray.ShowBalloonTip(1500, "FastSwitchDNS", L.CacheFlushed, ToolTipIcon.Info);
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
            var selected = SelectedAdapterId;
            var adapters = DnsManager.Adapters();
            bool selectedPresent = adapters.Any(a => a.Id == selected);

            var all = new ToolStripMenuItem(L.AllActive) { Checked = !selectedPresent };
            all.Click += (s, e) => { SelectedAdapterId = ""; RefreshStatus(); };
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
                item.Click += (s, e) => { SelectedAdapterId = id; RefreshStatus(); };
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

        void AddCustom()
        {
            using var form = new AddDnsForm();
            if (form.ShowDialog() != DialogResult.OK) return;
            Presets.AddCustom(form.Result);
            if (form.ApplyNow) Switch(form.Result);
            else RefreshStatus();
        }

        async void Switch(DnsPreset preset)
        {
            if (busy) return;
            busy = true;
            tray.Text = L.Applying;
            string error = null;
            var targets = DnsManager.Targets(SelectedAdapterId);

            await Task.Run(() =>
            {
                var errors = new List<string>();
                foreach (var t in targets)
                {
                    try { DnsManager.Apply(t, preset?.Servers); }
                    catch (Exception ex) { errors.Add(ex.Message); }
                }
                DnsManager.FlushCache();
                if (errors.Count > 0) error = string.Join("\n", errors);
            });

            busy = false;
            currentIconKey = null; // force redraw
            RefreshStatus();

            if (error != null)
                ShowError(error);
            else
                tray.ShowBalloonTip(1500, "FastSwitchDNS",
                    $"DNS: {preset?.Name ?? L.Auto}\n{string.Join(", ", targets.Select(t => t.Name))}",
                    ToolTipIcon.Info);
        }

        void ShowError(string message) =>
            tray.ShowBalloonTip(5000, L.Error, message.Length > 250 ? message.Substring(0, 250) : message, ToolTipIcon.Error);

        protected override void ExitThreadCore()
        {
            refreshTimer.Stop();
            tray.Visible = false;
            tray.Dispose();
            currentIcon?.Dispose();
            base.ExitThreadCore();
        }
    }
}
