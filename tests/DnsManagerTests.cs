using System;
using System.Linq;
using System.Net.NetworkInformation;
using Microsoft.Win32;
using Xunit;

namespace FastSwitchDNS.Tests
{
    public class DnsManagerTests : EnvTest
    {
        const string Template = "https://dns.example.com/dns-query";

        [Fact]
        public void Adapters_KeepsOnlyUpIPv4NonLoopbackNonTunnel()
        {
            var down = Wifi("{DOWN}"); down.IsUp = false;
            var loop = Ethernet("{LO}"); loop.Type = NetworkInterfaceType.Loopback;
            var tunnel = Ethernet("{TUN}"); tunnel.Type = NetworkInterfaceType.Tunnel;
            var v6only = Ethernet("{V6}"); v6only.SupportsIPv4 = false;
            var wifi = Wifi();
            AdapterList.AddRange(new[] { down, loop, tunnel, v6only, wifi });

            Assert.Equal(new[] { "{WIFI}" }, DnsManager.Adapters().Select(a => a.Id));
        }

        [Theory]
        [InlineData(NetworkInterfaceType.Ethernet, "Ethernet", "Realtek", false)]
        [InlineData(NetworkInterfaceType.Wireless80211, "Wi-Fi", "Intel", false)]
        [InlineData(NetworkInterfaceType.GigabitEthernet, "LAN", "Intel", false)]
        [InlineData(NetworkInterfaceType.Ppp, "Dial-up", "Modem", true)]
        [InlineData(NetworkInterfaceType.Ethernet, "vEthernet (WSL)", "Hyper-V Virtual Ethernet Adapter", true)]
        [InlineData(NetworkInterfaceType.Ethernet, "OpenVPN", "TAP-Windows Adapter V9", true)]
        [InlineData(NetworkInterfaceType.Ethernet, "Tailscale", "Tailscale Tunnel", true)]
        [InlineData(NetworkInterfaceType.Ethernet, "Ethernet 3", "VirtualBox Host-Only", true)]
        [InlineData(NetworkInterfaceType.Ethernet, "Bluetooth Network", "Bluetooth Device (PAN)", true)]
        public void IsVirtual(NetworkInterfaceType type, string name, string description, bool expected)
        {
            var a = new AdapterInfo { Type = type, Name = name, Description = description };
            Assert.Equal(expected, DnsManager.IsVirtual(a));
        }

        [Fact]
        public void Targets_PrefersPhysicalWithGateway()
        {
            AdapterList.AddRange(new[] { Vpn(), Wifi(), Ethernet(gateway: false) });
            Assert.Equal(new[] { "{WIFI}" }, DnsManager.Targets("").Select(a => a.Id));
        }

        [Fact]
        public void Targets_SelectedAdapterWins()
        {
            AdapterList.AddRange(new[] { Vpn(), Wifi() });
            Assert.Equal(new[] { "{VPN}" }, DnsManager.Targets("{VPN}").Select(a => a.Id));
        }

        [Fact]
        public void Targets_MissingSelectedAdapter_FallsBackToDefault()
        {
            AdapterList.AddRange(new[] { Vpn(), Wifi() });
            Assert.Equal(new[] { "{WIFI}" }, DnsManager.Targets("{GONE}").Select(a => a.Id));
        }

        [Fact]
        public void Targets_OnlyVirtualWithGateway_UsesThem()
        {
            AdapterList.AddRange(new[] { Vpn(), Ethernet(gateway: false) });
            Assert.Equal(new[] { "{VPN}" }, DnsManager.Targets(null).Select(a => a.Id));
        }

        [Fact]
        public void Targets_NoGateways_UsesAll()
        {
            AdapterList.AddRange(new[] { Wifi(gateway: false), Ethernet(gateway: false) });
            Assert.Equal(2, DnsManager.Targets("").Count);
        }

        [Fact]
        public void Targets_NoAdapters_Empty() => Assert.Empty(DnsManager.Targets(""));

        [Fact]
        public void StaticServers_ReadsAndSplitsRegistry()
        {
            var wifi = Wifi();
            SetStatic(wifi, "1.1.1.1,1.0.0.1", "2606:4700:4700::1111 2606:4700:4700::1001");
            Assert.Equal(new[] { "1.1.1.1", "1.0.0.1" }, DnsManager.StaticServers(wifi, false));
            Assert.Equal(new[] { "2606:4700:4700::1111", "2606:4700:4700::1001" }, DnsManager.StaticServers(wifi, true));
        }

        [Fact]
        public void StaticServers_EmptyOrMissing()
        {
            var wifi = Wifi();
            Assert.Empty(DnsManager.StaticServers(wifi, false));
            SetStatic(wifi, "  ");
            Assert.Empty(DnsManager.StaticServers(wifi, false));
        }

        [Fact]
        public void Detect_DhcpIsNull()
        {
            Assert.Null(DnsManager.Detect(Wifi(), Presets.BuiltIn));
        }

        [Fact]
        public void Detect_MatchesByIPv4SetInAnyOrder()
        {
            var wifi = Wifi();
            SetStatic(wifi, "8.8.4.4,8.8.8.8");
            Assert.Equal("Google", DnsManager.Detect(wifi, Presets.BuiltIn).Name);
        }

        [Fact]
        public void Detect_MatchesByIPv6WhenNoIPv4()
        {
            var wifi = Wifi();
            SetStatic(wifi, null, "2620:fe::fe,2620:fe::9");
            Assert.Equal("Quad9", DnsManager.Detect(wifi, Presets.BuiltIn).Name);
        }

        [Fact]
        public void Detect_UnknownServers_GiveOtherPreset()
        {
            var wifi = Wifi();
            SetStatic(wifi, "10.0.0.1", "fd00::1");
            var p = DnsManager.Detect(wifi, Presets.BuiltIn);
            Assert.Equal(L.Custom, p.Name);
            Assert.Equal(new[] { "10.0.0.1", "fd00::1" }, p.Servers);
            Assert.DoesNotContain(p, Presets.BuiltIn);
        }

        [Fact]
        public void Apply_StaticServers_RunsNetshForBothFamilies()
        {
            var wifi = Wifi();
            DnsManager.Apply(wifi, Presets.BuiltIn[0].Servers);

            Assert.Equal(new[]
            {
                "interface ipv4 set dnsservers name=\"Wi-Fi\" source=static address=1.1.1.1 register=primary validate=no",
                "interface ipv4 add dnsservers name=\"Wi-Fi\" address=1.0.0.1 index=2 validate=no",
                "interface ipv6 set dnsservers name=\"Wi-Fi\" source=static address=2606:4700:4700::1111 register=primary validate=no",
                "interface ipv6 add dnsservers name=\"Wi-Fi\" address=2606:4700:4700::1001 index=2 validate=no",
            }, Runner.Args("netsh.exe"));
        }

        [Fact]
        public void Apply_NullServers_RestoresDhcp()
        {
            DnsManager.Apply(Wifi(), null);
            Assert.Equal(new[]
            {
                "interface ipv4 set dnsservers name=\"Wi-Fi\" source=dhcp",
                "interface ipv6 set dnsservers name=\"Wi-Fi\" source=dhcp",
            }, Runner.Args("netsh.exe"));
        }

        [Fact]
        public void Apply_IPv4OnlyAdapter_SkipsIPv6()
        {
            var wifi = Wifi(); wifi.SupportsIPv6 = false;
            DnsManager.Apply(wifi, new[] { "1.1.1.1", "2606:4700:4700::1111" });
            Assert.All(Runner.Args("netsh.exe"), a => Assert.StartsWith("interface ipv4", a));
        }

        [Fact]
        public void Apply_IPv6WithoutAddresses_SetsDhcpForIPv6()
        {
            DnsManager.Apply(Wifi(), new[] { "10.0.0.1" });
            Assert.Contains("interface ipv6 set dnsservers name=\"Wi-Fi\" source=dhcp", Runner.Args("netsh.exe"));
        }

        [Fact]
        public void Apply_NetshFailure_ThrowsWithAdapterNameAndStopsFamily()
        {
            Runner.Handler = (f, a) => a.StartsWith("interface ipv4 set") ? (1, " Access denied \r\n") : (0, "");
            var ex = Assert.Throws<Exception>(() => DnsManager.Apply(Wifi(), new[] { "1.1.1.1", "1.0.0.1" }));
            Assert.Equal("Wi-Fi: Access denied", ex.Message);
            Assert.DoesNotContain(Runner.Args("netsh.exe"), a => a.StartsWith("interface ipv4 add"));
            Assert.DoesNotContain(Runner.Args("netsh.exe"), a => a.StartsWith("dns "));
        }

        [Fact]
        public void Apply_WithTemplate_RegistersEveryServerAndSetsFlags()
        {
            var wifi = Wifi();
            DnsManager.Apply(wifi, new[] { "1.2.3.4", "5.6.7.8", "2001:db8::1" }, Template);

            var dns = Runner.Args("netsh.exe").Where(a => a.StartsWith("dns ")).ToList();
            Assert.Equal(new[]
            {
                $"dns add encryption server=1.2.3.4 dohtemplate={Template} autoupgrade=yes udpfallback=no",
                $"dns add encryption server=5.6.7.8 dohtemplate={Template} autoupgrade=yes udpfallback=no",
                $"dns add encryption server=2001:db8::1 dohtemplate={Template} autoupgrade=yes udpfallback=no",
            }, dns);
            Assert.Equal(1L, DohFlags(wifi, "1.2.3.4"));
            Assert.Equal(1L, DohFlags(wifi, "5.6.7.8"));
            Assert.Equal(1L, DohFlags(wifi, "2001:db8::1", v6: true));
            Assert.Null(DohFlags(wifi, "2001:db8::1", v6: false));
        }

        [Fact]
        public void Apply_WithTemplate_AllowFallback()
        {
            DnsManager.Apply(Wifi(), new[] { "1.2.3.4" }, Template, allowFallback: true);
            Assert.Contains($"dns add encryption server=1.2.3.4 dohtemplate={Template} autoupgrade=yes udpfallback=yes",
                Runner.Args("netsh.exe"));
        }

        [Fact]
        public void Apply_DohKeysAreTheOnesWindowsSettingsUses()
        {
            var wifi = Wifi();
            Assert.Equal(@"SYSTEM\CurrentControlSet\Services\Dnscache\InterfaceSpecificParameters\{WIFI}\DohInterfaceSettings\Doh",
                DnsManager.DohKey(wifi, false));
            Assert.Equal(@"SYSTEM\CurrentControlSet\Services\Dnscache\InterfaceSpecificParameters\{WIFI}\DohInterfaceSettings\Doh6",
                DnsManager.DohKey(wifi, true));
            Assert.Equal(1L, DnsManager.DohFlagAutoTemplate);
        }

        [Fact]
        public void Apply_WithoutTemplate_ClearsOldDohSettings()
        {
            var wifi = Wifi();
            DnsManager.Apply(wifi, new[] { "1.2.3.4" }, Template);
            Assert.Equal(1L, DohFlags(wifi, "1.2.3.4"));

            DnsManager.Apply(wifi, new[] { "1.2.3.4" });
            Assert.Null(DohFlags(wifi, "1.2.3.4"));
            Assert.Contains(@"LocalMachine\" + DnsManager.DohKey(wifi, false), Registry.Deleted);
            Assert.Contains(@"LocalMachine\" + DnsManager.DohKey(wifi, true), Registry.Deleted);
        }

        [Fact]
        public void Apply_Dhcp_ClearsDohAndRegistersNothing()
        {
            var wifi = Wifi();
            DnsManager.Apply(wifi, new[] { "1.2.3.4" }, Template);
            Runner.Calls.Clear();

            DnsManager.Apply(wifi, null, Template);
            Assert.Null(DohFlags(wifi, "1.2.3.4"));
            Assert.DoesNotContain(Runner.Args("netsh.exe"), a => a.StartsWith("dns "));
        }

        [Fact]
        public void Apply_SwitchingServers_DropsFlagsOfOldOnes()
        {
            var wifi = Wifi();
            DnsManager.Apply(wifi, new[] { "1.2.3.4" }, Template);
            DnsManager.Apply(wifi, new[] { "5.6.7.8" }, Template);
            Assert.Null(DohFlags(wifi, "1.2.3.4"));
            Assert.Equal(1L, DohFlags(wifi, "5.6.7.8"));
        }

        [Fact]
        public void Apply_IPv4OnlyAdapter_NoIPv6DohFlags()
        {
            var wifi = Wifi(); wifi.SupportsIPv6 = false;
            DnsManager.Apply(wifi, new[] { "1.2.3.4", "2001:db8::1" }, Template);
            Assert.Null(DohFlags(wifi, "2001:db8::1", v6: true));
            Assert.DoesNotContain(Runner.Args("netsh.exe"), a => a.Contains("server=2001:db8::1"));
        }

        [Fact]
        public void RegisterTemplate_FallsBackToSetWhenAddFails()
        {
            Runner.Handler = (f, a) => a.StartsWith("dns add") ? (1, "The object already exists.") : (0, "");
            Assert.Null(DnsManager.RegisterTemplate("1.1.1.1", Template, false));
            Assert.Equal(new[]
            {
                $"dns add encryption server=1.1.1.1 dohtemplate={Template} autoupgrade=yes udpfallback=no",
                $"dns set encryption server=1.1.1.1 dohtemplate={Template} autoupgrade=yes udpfallback=no",
            }, Runner.Args("netsh.exe"));
        }

        [Fact]
        public void RegisterTemplate_BothFail_ReturnsError()
        {
            Runner.Handler = (f, a) => (1, " The parameter is incorrect. ");
            Assert.Equal("DoH 1.1.1.1: The parameter is incorrect.", DnsManager.RegisterTemplate("1.1.1.1", Template, true));
        }

        [Fact]
        public void Apply_RegistrationFailure_ThrowsButFlagsOtherServers()
        {
            Runner.Handler = (f, a) => a.Contains("server=1.2.3.4") ? (1, "nope") : (0, "");
            var wifi = Wifi();
            var ex = Assert.Throws<Exception>(() => DnsManager.Apply(wifi, new[] { "1.2.3.4", "5.6.7.8" }, Template));
            Assert.Equal("Wi-Fi: DoH 1.2.3.4: nope", ex.Message);
            Assert.Null(DohFlags(wifi, "1.2.3.4"));
            Assert.Equal(1L, DohFlags(wifi, "5.6.7.8"));
        }

        [Fact]
        public void Apply_RegistryDeleteFailure_Throws()
        {
            Registry.FailOnDelete = new UnauthorizedAccessException("denied");
            var ex = Assert.Throws<Exception>(() => DnsManager.Apply(Wifi(), new[] { "1.2.3.4" }, Template));
            Assert.Equal("Wi-Fi: denied", ex.Message);
            Assert.DoesNotContain(Runner.Args("netsh.exe"), a => a.StartsWith("dns "));
        }

        [Fact]
        public void Apply_RegistryWriteFailure_Throws()
        {
            var wifi = Wifi();
            Registry.FailOnSet = new UnauthorizedAccessException("denied");
            var ex = Assert.Throws<Exception>(() => DnsManager.Apply(wifi, new[] { "1.2.3.4" }, Template));
            Assert.Equal("Wi-Fi: denied", ex.Message);
        }

        [Fact]
        public void IsDohActive_ReflectsFlagsOfCurrentServers()
        {
            var wifi = Wifi();
            Assert.False(DnsManager.IsDohActive(wifi));

            SetStatic(wifi, "1.2.3.4");
            Assert.False(DnsManager.IsDohActive(wifi));

            DnsManager.Apply(wifi, new[] { "1.2.3.4" }, Template);
            Assert.True(DnsManager.IsDohActive(wifi));

            Registry.SetValue(RegistryHive.LocalMachine, $@"{DnsManager.DohKey(wifi, false)}\1.2.3.4", "DohFlags", 0L, RegistryValueKind.QWord);
            Assert.False(DnsManager.IsDohActive(wifi));
        }

        [Fact]
        public void IsDohActive_IPv6Only()
        {
            var wifi = Wifi();
            SetStatic(wifi, null, "2001:db8::1");
            Registry.SetValue(RegistryHive.LocalMachine, $@"{DnsManager.DohKey(wifi, true)}\2001:db8::1", "DohFlags", 1L, RegistryValueKind.QWord);
            Assert.True(DnsManager.IsDohActive(wifi));
        }

        [Fact]
        public void IsDohActive_FlagsForOtherServersDoNotCount()
        {
            var wifi = Wifi();
            SetStatic(wifi, "9.9.9.9");
            Registry.SetValue(RegistryHive.LocalMachine, $@"{DnsManager.DohKey(wifi, false)}\1.2.3.4", "DohFlags", 1L, RegistryValueKind.QWord);
            Assert.False(DnsManager.IsDohActive(wifi));
        }

        [Theory]
        [InlineData("19045", false)] // Windows 10 22H2
        [InlineData("19041", false)] // Windows 10 2004
        [InlineData("20348", true)]  // Windows Server 2022
        [InlineData("22000", true)]  // Windows 11 21H2
        [InlineData("26100", true)]  // Windows 11 24H2
        [InlineData("", false)]
        [InlineData("abc", false)]
        [InlineData(null, false)]
        public void DohSupported_ByBuild(string build, bool supported)
        {
            SetWindowsBuild(build);
            Assert.Equal(supported, DnsManager.DohSupported());
        }

        [Fact]
        public void Apply_Windows10_SetsPlainDnsWithoutDohCommandsOrErrors()
        {
            SetWindowsBuild(Windows10);
            // netsh on Windows 10 has no "dns ... encryption" command
            Runner.Handler = (f, a) => a.StartsWith("dns ") ? (1, "The following command was not found") : (0, "");
            var wifi = Wifi();

            DnsManager.Apply(wifi, Presets.BuiltIn[0].Servers, Presets.BuiltIn[0].DohTemplate);

            Assert.Contains("interface ipv4 set dnsservers name=\"Wi-Fi\" source=static address=1.1.1.1 register=primary validate=no",
                Runner.Args("netsh.exe"));
            Assert.DoesNotContain(Runner.Args("netsh.exe"), a => a.StartsWith("dns "));
            Assert.Null(DohFlags(wifi, "1.1.1.1"));
        }

        [Fact]
        public void FlushCache_RunsIpconfig()
        {
            DnsManager.FlushCache();
            Assert.Equal(new[] { ("ipconfig.exe", "/flushdns") }, Runner.Calls);
        }

        [Fact]
        public void Run_GoesThroughEnvRunner()
        {
            Runner.Handler = (f, a) => (7, "out");
            Assert.Equal((7, "out"), DnsManager.Run("x.exe", "y"));
        }
    }
}
