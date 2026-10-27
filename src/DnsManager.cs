using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using Microsoft.Win32;

namespace FastSwitchDNS
{
    static class DnsManager
    {
        /// <summary>Connected physical/virtual adapters that can carry DNS settings.</summary>
        public static List<AdapterInfo> Adapters() =>
            Env.AdapterSource()
                .Where(n => n.IsUp &&
                            n.Type != NetworkInterfaceType.Loopback &&
                            n.Type != NetworkInterfaceType.Tunnel &&
                            n.SupportsIPv4)
                .ToList();

        static readonly string[] VirtualMarkers =
        {
            "virtual", "vpn", "tap", "tun", "wireguard", "wintun", "hyper-v", "vmware", "virtualbox",
            "radmin", "hamachi", "zerotier", "tailscale", "openvpn", "npcap", "bluetooth",
        };

        /// <summary>VPN / VM / tunnel adapters: they manage their own DNS, so leave them alone by default.</summary>
        public static bool IsVirtual(AdapterInfo n)
        {
            if (n.Type != NetworkInterfaceType.Ethernet &&
                n.Type != NetworkInterfaceType.Wireless80211 &&
                n.Type != NetworkInterfaceType.GigabitEthernet)
                return true;
            var text = (n.Name + " " + n.Description).ToLowerInvariant();
            return VirtualMarkers.Any(text.Contains);
        }

        /// <summary>
        /// Adapters to change. Empty <paramref name="adapterId"/> means all real internet connections
        /// (physical Ethernet / Wi-Fi with a gateway), skipping VPN and VM adapters.
        /// </summary>
        public static List<AdapterInfo> Targets(string adapterId)
        {
            var all = Adapters();
            if (!string.IsNullOrEmpty(adapterId))
            {
                var one = all.Where(n => n.Id == adapterId).ToList();
                if (one.Count > 0) return one;
            }
            var withGateway = all.Where(n => n.HasGateway).ToList();
            var physical = withGateway.Where(n => !IsVirtual(n)).ToList();
            if (physical.Count > 0) return physical;
            return withGateway.Count > 0 ? withGateway : all;
        }

        /// <summary>Statically configured DNS servers (empty = obtained automatically).</summary>
        public static string[] StaticServers(AdapterInfo n, bool v6)
        {
            var path = $@"SYSTEM\CurrentControlSet\Services\{(v6 ? "Tcpip6" : "Tcpip")}\Parameters\Interfaces\{n.Id}";
            var value = Env.Registry.GetValue(RegistryHive.LocalMachine, path, "NameServer") as string;
            return string.IsNullOrWhiteSpace(value)
                ? new string[0]
                : value.Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries);
        }

        /// <summary>Returns the matching preset, null for DHCP, or a synthetic "Other" preset for unknown servers.</summary>
        public static DnsPreset Detect(AdapterInfo n, IEnumerable<DnsPreset> presets)
        {
            var v4 = StaticServers(n, false);
            var v6 = StaticServers(n, true);
            if (v4.Length == 0 && v6.Length == 0) return null;

            foreach (var p in presets)
            {
                bool match = v4.Length > 0
                    ? SameSet(v4, p.V4)
                    : SameSet(v6, p.V6);
                if (match) return p;
            }
            return new DnsPreset { Name = L.Custom, Servers = v4.Concat(v6).ToArray() };
        }

        static bool SameSet(IEnumerable<string> a, IEnumerable<string> b) =>
            new HashSet<string>(a, StringComparer.OrdinalIgnoreCase).SetEquals(b);

        /// <summary>
        /// Sets DNS servers on an adapter; null or empty <paramref name="servers"/> restores DHCP.
        /// With <paramref name="dohTemplate"/> the servers are also switched to DNS over HTTPS;
        /// without it any DoH settings on the adapter are removed.
        /// </summary>
        public static void Apply(AdapterInfo n, string[] servers, string dohTemplate = null, bool allowFallback = false)
        {
            servers ??= new string[0];
            var preset = new DnsPreset { Servers = servers };
            var v4 = preset.V4.ToArray();
            var v6 = n.SupportsIPv6 ? preset.V6.ToArray() : new string[0];
            var errors = new List<string>();

            SetFamily(n.Name, "ipv4", v4, errors);
            if (n.SupportsIPv6)
                SetFamily(n.Name, "ipv6", v6, errors);

            if (errors.Count == 0)
                ConfigureDoh(n, v4, v6, dohTemplate, allowFallback, errors);

            if (errors.Count > 0)
                throw new Exception($"{n.Name}: {string.Join(" ", errors)}");
        }

        static void SetFamily(string name, string family, string[] servers, List<string> errors)
        {
            var ifc = $"interface {family}";
            var commands = new List<string>();
            if (servers.Length == 0)
            {
                commands.Add($"{ifc} set dnsservers name=\"{name}\" source=dhcp");
            }
            else
            {
                commands.Add($"{ifc} set dnsservers name=\"{name}\" source=static address={servers[0]} register=primary validate=no");
                for (int i = 1; i < servers.Length; i++)
                    commands.Add($"{ifc} add dnsservers name=\"{name}\" address={servers[i]} index={i + 1} validate=no");
            }

            foreach (var cmd in commands)
            {
                var (code, output) = Run("netsh.exe", cmd);
                if (code != 0)
                {
                    errors.Add(output.Trim());
                    return;
                }
            }
        }

        /// <summary>
        /// First Windows build whose system resolver can do DNS over HTTPS: Windows Server 2022 (20348).
        /// Windows 11 starts at 22000; Windows 10 (up to 19045) has no DoH.
        /// </summary>
        public const int DohMinBuild = 20348;

        /// <summary>True on Windows 11 / Server 2022 and later. Unknown build counts as unsupported.</summary>
        public static bool DohSupported()
        {
            var build = Env.Registry.GetValue(RegistryHive.LocalMachine,
                @"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "CurrentBuildNumber") as string;
            return int.TryParse(build, out var number) && number >= DohMinBuild;
        }

        const string DnscacheInterfaces = @"SYSTEM\CurrentControlSet\Services\Dnscache\InterfaceSpecificParameters";

        /// <summary>Per-adapter DoH settings key, the same one the Windows Settings app writes.</summary>
        public static string DohKey(AdapterInfo n, bool v6) =>
            $@"{DnscacheInterfaces}\{n.Id}\DohInterfaceSettings\{(v6 ? "Doh6" : "Doh")}";

        /// <summary>DohFlags = 1: encrypt using the template registered for this server address.</summary>
        public const long DohFlagAutoTemplate = 1;

        static void ConfigureDoh(AdapterInfo n, string[] v4, string[] v6, string template, bool allowFallback,
            List<string> errors)
        {
            try
            {
                Env.Registry.DeleteKeyTree(RegistryHive.LocalMachine, DohKey(n, false));
                Env.Registry.DeleteKeyTree(RegistryHive.LocalMachine, DohKey(n, true));
            }
            catch (Exception ex)
            {
                errors.Add(ex.Message);
                return;
            }
            // Windows 10 has no DoH: plain DNS only, without "unknown command" errors from netsh
            if (string.IsNullOrEmpty(template) || !DohSupported()) return;

            foreach (var (servers, v6Family) in new[] { (v4, false), (v6, true) })
            foreach (var ip in servers)
            {
                var error = RegisterTemplate(ip, template, allowFallback);
                if (error != null)
                {
                    errors.Add(error);
                    continue;
                }
                try
                {
                    Env.Registry.SetValue(RegistryHive.LocalMachine, $@"{DohKey(n, v6Family)}\{ip}", "DohFlags",
                        DohFlagAutoTemplate, RegistryValueKind.QWord);
                }
                catch (Exception ex)
                {
                    errors.Add(ex.Message);
                }
            }
        }

        /// <summary>Adds (or updates) the system-wide DoH template for a server. Returns the error text, or null.</summary>
        public static string RegisterTemplate(string ip, string template, bool allowFallback)
        {
            var options = $"server={ip} dohtemplate={template} autoupgrade=yes udpfallback={(allowFallback ? "yes" : "no")}";
            var (code, _) = Run("netsh.exe", $"dns add encryption {options}");
            if (code == 0) return null;
            // Windows already knows the big providers, so "add" fails for them: update instead
            var (setCode, setOutput) = Run("netsh.exe", $"dns set encryption {options}");
            return setCode == 0 ? null : $"DoH {ip}: {setOutput.Trim()}";
        }

        /// <summary>True when the adapter's current DNS servers are set to DNS over HTTPS.</summary>
        public static bool IsDohActive(AdapterInfo n)
        {
            foreach (var v6 in new[] { false, true })
            {
                var servers = StaticServers(n, v6);
                if (servers.Length == 0) continue;
                return servers.Any(ip =>
                    Env.Registry.GetValue(RegistryHive.LocalMachine, $@"{DohKey(n, v6)}\{ip}", "DohFlags") is long flags &&
                    flags != 0);
            }
            return false;
        }

        public static void FlushCache() => Run("ipconfig.exe", "/flushdns");

        public static (int code, string output) Run(string file, string args) => Env.Runner.Run(file, args);
    }
}
