using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Xunit;

namespace FastSwitchDNS.Tests
{
    public class TrayAppTests : EnvTest
    {
        const string CloudflareDoh = "https://cloudflare-dns.com/dns-query";

        readonly AdapterInfo wifi = Wifi();

        public TrayAppTests()
        {
            AdapterList.Add(wifi);
            // netsh writes the servers to the registry; emulate that so detection sees the switch
            Runner.Handler = (file, args) =>
            {
                if (file == "netsh.exe" && args.StartsWith("interface ipv4 set dnsservers"))
                {
                    var address = args.Split(' ').FirstOrDefault(p => p.StartsWith("address="));
                    SetStatic(wifi, address?.Substring(8) ?? "");
                }
                else if (file == "netsh.exe" && args.StartsWith("interface ipv4 add dnsservers"))
                {
                    var address = args.Split(' ').First(p => p.StartsWith("address=")).Substring(8);
                    SetStatic(wifi, string.Join(",", DnsManager.StaticServers(wifi, false).Concat(new[] { address })));
                }
                return (0, "");
            };
        }

        TrayApp NewApp()
        {
            var app = new TrayApp(live: false);
            app.BuildMenu();
            return app;
        }

        static ToolStripMenuItem Item(TrayApp app, string text) =>
            app.Menu.Items.OfType<ToolStripMenuItem>().Single(i => i.Text == text);

        static string[] Texts(TrayApp app) =>
            app.Menu.Items.Cast<ToolStripItem>().Select(i => i is ToolStripSeparator ? "---" : i.Text).ToArray();

        [Fact]
        public void StatusText_Variants() => Ui(() =>
        {
            Assert.Equal("FastSwitchDNS\nNo active connections", TrayApp.StatusText(null, null));
            Assert.Equal("DNS: Auto\nWi-Fi", TrayApp.StatusText(wifi, null));
            Assert.Equal("DNS: Google\nWi-Fi", TrayApp.StatusText(wifi, Presets.BuiltIn[1]));
            Assert.Equal("DNS: Google · DoH\nWi-Fi", TrayApp.StatusText(wifi, Presets.BuiltIn[1], doh: true));

            var longName = new AdapterInfo { Name = new string('x', 100) };
            Assert.Equal(63, TrayApp.StatusText(longName, null).Length);
        });

        [Fact]
        public void Startup_ShowsCurrentState() => Ui(() =>
        {
            SetStatic(wifi, "1.1.1.1,1.0.0.1");
            using var app = NewApp();
            Assert.Equal("DNS: Cloudflare\nWi-Fi", app.TrayText);
            Assert.NotNull(app.TrayIcon);
        });

        [Fact]
        public void Menu_Layout() => Ui(() =>
        {
            using var app = NewApp();
            Assert.Equal(new[]
            {
                "Wi-Fi: Auto", "---",
                "Automatic (DHCP)", "Cloudflare", "Google", "Quad9", "AdGuard", "OpenDNS", "---",
                "Add custom DNS…", "---",
                "Adapter", "Encrypted DNS (DoH)", "Allow unencrypted if DoH fails",
                "Flush DNS cache", "Run at Windows logon", "---", "Exit",
            }, Texts(app));
            Assert.False(app.Menu.Items[0].Enabled);
            Assert.True(Item(app, "Automatic (DHCP)").Checked);
            Assert.Equal("1.1.1.1 · DoH", Item(app, "Cloudflare").ShortcutKeyDisplayString);
            Assert.Equal(CloudflareDoh, Item(app, "Cloudflare").ToolTipText);
            Assert.True(Item(app, "Encrypted DNS (DoH)").Checked);
            Assert.False(Item(app, "Allow unencrypted if DoH fails").Checked);
            Assert.True(Item(app, "Allow unencrypted if DoH fails").Enabled);
        });

        [Fact]
        public void Menu_Windows10_HasNoDohItemsOrMarkers() => Ui(() =>
        {
            SetWindowsBuild(Windows10);
            using var app = NewApp();
            Assert.Equal(new[]
            {
                "Wi-Fi: Auto", "---",
                "Automatic (DHCP)", "Cloudflare", "Google", "Quad9", "AdGuard", "OpenDNS", "---",
                "Add custom DNS…", "---",
                "Adapter", "Flush DNS cache", "Run at Windows logon", "---", "Exit",
            }, Texts(app));
            Assert.Equal("1.1.1.1", Item(app, "Cloudflare").ShortcutKeyDisplayString);
            Assert.Null(Item(app, "Cloudflare").ToolTipText);
        });

        [Fact]
        public void Switch_Windows10_PlainDnsAndNoError() => Ui(async () =>
        {
            SetWindowsBuild(Windows10);
            var plain = Runner.Handler;
            Runner.Handler = (f, a) => a.StartsWith("dns ") ? (1, "The following command was not found") : plain(f, a);
            using var app = NewApp();

            await app.Switch(Presets.BuiltIn[0]);

            Assert.Equal(("FastSwitchDNS", "DNS: Cloudflare\nWi-Fi", false), app.LastNotice);
            Assert.Equal("DNS: Cloudflare\nWi-Fi", app.TrayText);
            Assert.DoesNotContain(Runner.Args("netsh.exe"), a => a.StartsWith("dns "));
            app.BuildMenu();
            Assert.Equal("Wi-Fi: Cloudflare", app.Menu.Items[0].Text);
            Assert.True(Item(app, "Cloudflare").Checked);
        });

        [Fact]
        public void Menu_NoAdapters() => Ui(() =>
        {
            AdapterList.Clear();
            using var app = NewApp();
            Assert.Equal("No active connections", app.Menu.Items[0].Text);
            Assert.False(Item(app, "Automatic (DHCP)").Checked);
        });

        [Fact]
        public void Menu_UnknownServersShowsThem() => Ui(() =>
        {
            SetStatic(wifi, "10.0.0.1,10.0.0.2");
            using var app = NewApp();
            Assert.Equal("Wi-Fi: Other", app.Menu.Items[0].Text);
            Assert.Equal("10.0.0.1, 10.0.0.2", app.Menu.Items[1].Text);
        });

        [Fact]
        public void Menu_CustomPresetsGetSeparatorAndRemoveMenu() => Ui(() =>
        {
            Presets.AddCustom(new DnsPreset { Name = "Home", Servers = new[] { "192.168.1.1" } });
            using var app = NewApp();
            var texts = Texts(app);
            int home = Array.IndexOf(texts, "Home");
            Assert.Equal("---", texts[home - 1]);
            Assert.Equal("192.168.1.1", Item(app, "Home").ShortcutKeyDisplayString);
            Assert.Null(Item(app, "Home").ToolTipText);

            var remove = Item(app, "Remove custom DNS");
            remove.DropDownItems[0].PerformClick();
            Assert.Empty(Presets.LoadCustom());
        });

        [Fact]
        public void Menu_DohDisabled_HidesMarkerAndDisablesFallback() => Ui(() =>
        {
            Settings.DohEnabled = false;
            using var app = NewApp();
            Assert.Equal("1.1.1.1", Item(app, "Cloudflare").ShortcutKeyDisplayString);
            Assert.False(Item(app, "Encrypted DNS (DoH)").Checked);
            Assert.False(Item(app, "Allow unencrypted if DoH fails").Enabled);
        });

        [Fact]
        public void Switch_ToBuiltIn_SetsServersAndDoh() => Ui(async () =>
        {
            using var app = NewApp();
            await app.Switch(Presets.BuiltIn[0]);

            var netsh = Runner.Args("netsh.exe");
            Assert.Contains("interface ipv4 set dnsservers name=\"Wi-Fi\" source=static address=1.1.1.1 register=primary validate=no", netsh);
            Assert.Contains($"dns add encryption server=1.1.1.1 dohtemplate={CloudflareDoh} autoupgrade=yes udpfallback=no", netsh);
            Assert.Contains($"dns add encryption server=2606:4700:4700::1001 dohtemplate={CloudflareDoh} autoupgrade=yes udpfallback=no", netsh);
            Assert.Equal(1L, DohFlags(wifi, "1.1.1.1"));
            Assert.Contains(("ipconfig.exe", "/flushdns"), Runner.Calls);

            Assert.Equal(("FastSwitchDNS", "DNS: Cloudflare · DoH\nWi-Fi", false), app.LastNotice);
            Assert.Equal("DNS: Cloudflare · DoH\nWi-Fi", app.TrayText);

            app.BuildMenu();
            Assert.Equal("Wi-Fi: Cloudflare · DoH", app.Menu.Items[0].Text);
            Assert.True(Item(app, "Cloudflare").Checked);
        });

        [Fact]
        public void Switch_DohDisabled_PlainDnsOnly() => Ui(async () =>
        {
            Settings.DohEnabled = false;
            using var app = NewApp();
            await app.Switch(Presets.BuiltIn[0]);
            Assert.DoesNotContain(Runner.Args("netsh.exe"), a => a.StartsWith("dns "));
            Assert.Null(DohFlags(wifi, "1.1.1.1"));
            Assert.Equal("DNS: Cloudflare\nWi-Fi", app.LastNotice.text);
        });

        [Fact]
        public void Switch_FallbackSetting_PassedToNetsh() => Ui(async () =>
        {
            Settings.DohFallback = true;
            using var app = NewApp();
            await app.Switch(Presets.BuiltIn[0]);
            Assert.Contains(Runner.Args("netsh.exe"), a => a.StartsWith("dns add encryption server=1.1.1.1") && a.EndsWith("udpfallback=yes"));
        });

        [Fact]
        public void Switch_ToDhcp() => Ui(async () =>
        {
            using var app = NewApp();
            await app.Switch(Presets.BuiltIn[0]);
            await app.Switch(null);
            Assert.Contains("interface ipv4 set dnsservers name=\"Wi-Fi\" source=dhcp", Runner.Args("netsh.exe"));
            Assert.Null(DohFlags(wifi, "1.1.1.1"));
            Assert.Equal("DNS: Automatic (DHCP)\nWi-Fi", app.LastNotice.text);
        });

        [Fact]
        public void Switch_Error_ShowsErrorBalloon() => Ui(async () =>
        {
            Runner.Handler = (f, a) => a.StartsWith("interface ipv4") ? (1, "Access denied") : (0, "");
            using var app = NewApp();
            await app.Switch(Presets.BuiltIn[1]);
            Assert.Equal(("Error", "Wi-Fi: Access denied", true), app.LastNotice);
        });

        [Fact]
        public void Switch_LongError_IsCut() => Ui(async () =>
        {
            Runner.Handler = (f, a) => f == "netsh.exe" ? (1, new string('e', 1000)) : (0, "");
            using var app = NewApp();
            await app.Switch(Presets.BuiltIn[1]);
            Assert.Equal(250, app.LastNotice.text.Length);
        });

        [Fact]
        public void Switch_UsesSelectedAdapter() => Ui(async () =>
        {
            var eth = Ethernet();
            AdapterList.Add(eth);
            Settings.AdapterId = eth.Id;
            using var app = NewApp();
            await app.Switch(Presets.BuiltIn[1]);
            Assert.All(Runner.Args("netsh.exe").Where(a => a.StartsWith("interface")), a => Assert.Contains("name=\"Ethernet\"", a));
        });

        [Fact]
        public void ApplyToAll_CollectsErrorsFromEveryAdapter() => Ui(() =>
        {
            Runner.Handler = (f, a) => f == "netsh.exe" ? (1, "fail") : (0, "");
            var error = TrayApp.ApplyToAll(new[] { Wifi(), Ethernet() }, Presets.BuiltIn[0], true, false);
            Assert.Equal("Wi-Fi: fail fail\nEthernet: fail fail", error);
            Assert.Contains(("ipconfig.exe", "/flushdns"), Runner.Calls);
        });

        [Fact]
        public void ApplyToAll_Success_ReturnsNull() =>
            Assert.Null(TrayApp.ApplyToAll(new[] { Wifi() }, null, true, false));

        [Fact]
        public void ToggleDoh_ReappliesActivePresetWithoutEncryption() => Ui(async () =>
        {
            using var app = NewApp();
            await app.Switch(Presets.BuiltIn[0]);
            Assert.Equal(1L, DohFlags(wifi, "1.1.1.1"));

            app.BuildMenu();
            Item(app, "Encrypted DNS (DoH)").PerformClick();
            await WaitUntil(() => app.TrayText == "DNS: Cloudflare\nWi-Fi");

            Assert.False(Settings.DohEnabled);
            Assert.Null(DohFlags(wifi, "1.1.1.1"));
        });

        [Fact]
        public void ToggleFallback_Reapplies() => Ui(async () =>
        {
            using var app = NewApp();
            await app.Switch(Presets.BuiltIn[0]);
            Runner.Calls.Clear();

            app.BuildMenu();
            Item(app, "Allow unencrypted if DoH fails").PerformClick();
            // Switch shows "Applying…" synchronously, so the final text means the re-apply has finished
            await WaitUntil(() => app.TrayText == "DNS: Cloudflare · DoH\nWi-Fi" &&
                                  Runner.Args("netsh.exe").Any(a => a.EndsWith("udpfallback=yes")));
            Assert.True(Settings.DohFallback);
        });

        [Fact]
        public void Reapply_SkipsWhenNothingToChange() => Ui(async () =>
        {
            using var app = new TrayApp(live: false);
            var presets = Presets.All();
            await app.Reapply(null, presets);
            await app.Reapply(new DnsPreset { Name = "Other", Servers = new[] { "10.0.0.1" } }, presets);
            Presets.AddCustom(new DnsPreset { Name = "Plain", Servers = new[] { "10.0.0.1" } });
            presets = Presets.All();
            await app.Reapply(presets.Last(), presets);
            Assert.DoesNotContain(Runner.Calls, c => c.file == "netsh.exe");
        });

        [Fact]
        public void AddCustom_SaveAndApply() => Ui(async () =>
        {
            using var app = NewApp();
            var preset = new DnsPreset { Name = "Home", Servers = new[] { "192.168.1.1" }, DohTemplate = "https://dns.home/q" };
            app.AskCustom = () => (preset, true);
            await app.AddCustom();

            Assert.Equal("https://dns.home/q", Presets.LoadCustom().Single().DohTemplate);
            Assert.Contains("dns add encryption server=192.168.1.1 dohtemplate=https://dns.home/q autoupgrade=yes udpfallback=no",
                Runner.Args("netsh.exe"));
            Assert.Equal("DNS: Home · DoH\nWi-Fi", app.TrayText);
        });

        [Fact]
        public void AddCustom_SaveOnly_DoesNotSwitch() => Ui(async () =>
        {
            using var app = NewApp();
            app.AskCustom = () => (new DnsPreset { Name = "Home", Servers = new[] { "192.168.1.1" } }, false);
            await app.AddCustom();
            Assert.Single(Presets.LoadCustom());
            Assert.DoesNotContain(Runner.Calls, c => c.file == "netsh.exe");
        });

        [Fact]
        public void AddCustom_Cancel_DoesNothing() => Ui(async () =>
        {
            using var app = NewApp();
            app.AskCustom = () => null;
            await app.AddCustom();
            Assert.Empty(Presets.LoadCustom());
        });

        [Fact]
        public void AdapterMenu_SelectsAndResets() => Ui(() =>
        {
            var eth = Ethernet();
            AdapterList.Add(eth);
            using var app = NewApp();
            var adapters = Item(app, "Adapter");
            Assert.Equal(new[] { "Main (Wi-Fi / Ethernet)", "", "Wi-Fi", "Ethernet" },
                adapters.DropDownItems.Cast<ToolStripItem>().Select(i => i.Text));
            Assert.True(((ToolStripMenuItem)adapters.DropDownItems[0]).Checked);

            adapters.DropDownItems[3].PerformClick();
            Assert.Equal(eth.Id, Settings.AdapterId);

            app.BuildMenu();
            adapters = Item(app, "Adapter");
            Assert.True(((ToolStripMenuItem)adapters.DropDownItems[3]).Checked);
            Assert.Equal("Realtek PCIe GbE", adapters.DropDownItems[3].ToolTipText);

            adapters.DropDownItems[0].PerformClick();
            Assert.Equal("", Settings.AdapterId);
        });

        [Fact]
        public void FlushMenu_FlushesAndNotifies() => Ui(() =>
        {
            using var app = NewApp();
            Item(app, "Flush DNS cache").PerformClick();
            Assert.Contains(("ipconfig.exe", "/flushdns"), Runner.Calls);
            Assert.Equal("DNS cache flushed", app.LastNotice.text);
        });

        [Fact]
        public void AutostartMenu_TogglesTask() => Ui(() =>
        {
            Runner.Handler = (f, a) => (1, "not found");
            using var app = NewApp();
            var item = Item(app, "Run at Windows logon");
            Assert.False(item.Checked);

            Runner.Handler = (f, a) => (0, "");
            item.PerformClick();
            Assert.Contains(Runner.Calls, c => c.file == "schtasks.exe" && c.args.StartsWith("/create"));
        });

        [Fact]
        public void AutostartMenu_ErrorIsShown() => Ui(() =>
        {
            Runner.Handler = (f, a) => (1, "Access denied");
            using var app = NewApp();
            Item(app, "Run at Windows logon").PerformClick();
            Assert.Equal(("Error", "Access denied", true), app.LastNotice);
        });

        [Fact]
        public void Exit_ClosesApp() => Ui(() =>
        {
            var app = NewApp();
            bool exited = false;
            app.ThreadExit += (s, e) => exited = true;
            Item(app, "Exit").PerformClick();
            Assert.True(exited);
        });

        [Fact]
        public void BrokenAdapterSource_DoesNotCrashStatus() => Ui(() =>
        {
            Env.AdapterSource = () => throw new InvalidOperationException("gone");
            using var app = new TrayApp(live: false);
            app.RefreshStatus();
        });

        static async Task WaitUntil(Func<bool> condition)
        {
            for (int i = 0; i < 200 && !condition(); i++) await Task.Delay(10);
            Assert.True(condition());
        }
    }
}
