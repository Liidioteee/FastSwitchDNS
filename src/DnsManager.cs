using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using Microsoft.Win32;

namespace FastSwitchDNS
{
    static class DnsManager
    {
        /// <summary>Connected physical/virtual adapters that can carry DNS settings.</summary>
        public static List<NetworkInterface> Adapters() =>
            NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up &&
                            n.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                            n.NetworkInterfaceType != NetworkInterfaceType.Tunnel &&
                            n.Supports(NetworkInterfaceComponent.IPv4))
                .ToList();

        static bool HasGateway(NetworkInterface n) =>
            n.GetIPProperties().GatewayAddresses.Any(g => !g.Address.Equals(System.Net.IPAddress.Any));

        static readonly string[] VirtualMarkers =
        {
            "virtual", "vpn", "tap", "tun", "wireguard", "wintun", "hyper-v", "vmware", "virtualbox",
            "radmin", "hamachi", "zerotier", "tailscale", "openvpn", "npcap", "bluetooth",
        };

        /// <summary>VPN / VM / tunnel adapters: they manage their own DNS, so leave them alone by default.</summary>
        public static bool IsVirtual(NetworkInterface n)
        {
            if (n.NetworkInterfaceType != NetworkInterfaceType.Ethernet &&
                n.NetworkInterfaceType != NetworkInterfaceType.Wireless80211 &&
                n.NetworkInterfaceType != NetworkInterfaceType.GigabitEthernet)
                return true;
            var text = (n.Name + " " + n.Description).ToLowerInvariant();
            return VirtualMarkers.Any(text.Contains);
        }

        /// <summary>
        /// Adapters to change. Empty <paramref name="adapterId"/> means all real internet connections
        /// (physical Ethernet / Wi-Fi with a gateway), skipping VPN and VM adapters.
        /// </summary>
        public static List<NetworkInterface> Targets(string adapterId)
        {
            var all = Adapters();
            if (!string.IsNullOrEmpty(adapterId))
            {
                var one = all.Where(n => n.Id == adapterId).ToList();
                if (one.Count > 0) return one;
            }
            var withGateway = all.Where(HasGateway).ToList();
            var physical = withGateway.Where(n => !IsVirtual(n)).ToList();
            if (physical.Count > 0) return physical;
            return withGateway.Count > 0 ? withGateway : all;
        }

        /// <summary>Statically configured DNS servers (empty = obtained automatically).</summary>
        public static string[] StaticServers(NetworkInterface n, bool v6)
        {
            var path = $@"SYSTEM\CurrentControlSet\Services\{(v6 ? "Tcpip6" : "Tcpip")}\Parameters\Interfaces\{n.Id}";
            using var key = Registry.LocalMachine.OpenSubKey(path);
            var value = key?.GetValue("NameServer") as string;
            return string.IsNullOrWhiteSpace(value)
                ? new string[0]
                : value.Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries);
        }

        /// <summary>Returns the matching preset, null for DHCP, or a synthetic "Other" preset for unknown servers.</summary>
        public static DnsPreset Detect(NetworkInterface n, IEnumerable<DnsPreset> presets)
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

        /// <summary>Sets DNS servers on an adapter; null or empty <paramref name="servers"/> restores DHCP.</summary>
        public static void Apply(NetworkInterface n, string[] servers)
        {
            servers ??= new string[0];
            var preset = new DnsPreset { Servers = servers };
            var errors = new List<string>();

            SetFamily(n.Name, "ipv4", preset.V4.ToArray(), errors);
            if (n.Supports(NetworkInterfaceComponent.IPv6))
                SetFamily(n.Name, "ipv6", preset.V6.ToArray(), errors);

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

        public static void FlushCache() => Run("ipconfig.exe", "/flushdns");

        public static (int code, string output) Run(string file, string args)
        {
            // Console tools print in the OEM code page (cp866 on Russian Windows)
            var oem = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
            var psi = new ProcessStartInfo(file, args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = oem,
                StandardErrorEncoding = oem,
            };
            using var p = Process.Start(psi);
            var stderr = p.StandardError.ReadToEndAsync();
            var stdout = p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            return (p.ExitCode, stdout + stderr.Result);
        }
    }
}
