using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace FastSwitchDNS
{
    class DnsPreset
    {
        public string Name;
        public string[] Servers;
        public bool BuiltIn;

        /// <summary>DNS-over-HTTPS template, e.g. https://cloudflare-dns.com/dns-query. Null = no encryption.</summary>
        public string DohTemplate;

        public bool HasDoh => !string.IsNullOrEmpty(DohTemplate);

        public IEnumerable<string> V4 => Servers.Where(s => Family(s) == AddressFamily.InterNetwork);
        public IEnumerable<string> V6 => Servers.Where(s => Family(s) == AddressFamily.InterNetworkV6);

        static AddressFamily Family(string s) =>
            IPAddress.TryParse(s, out var ip) ? ip.AddressFamily : AddressFamily.Unknown;
    }

    static class Presets
    {
        public static readonly DnsPreset[] BuiltIn =
        {
            Make("Cloudflare", "https://cloudflare-dns.com/dns-query",
                "1.1.1.1", "1.0.0.1", "2606:4700:4700::1111", "2606:4700:4700::1001"),
            Make("Google", "https://dns.google/dns-query",
                "8.8.8.8", "8.8.4.4", "2001:4860:4860::8888", "2001:4860:4860::8844"),
            Make("Quad9", "https://dns.quad9.net/dns-query",
                "9.9.9.9", "149.112.112.112", "2620:fe::fe", "2620:fe::9"),
            Make("AdGuard", "https://dns.adguard-dns.com/dns-query",
                "94.140.14.14", "94.140.15.15", "2a10:50c0::ad1:ff", "2a10:50c0::ad2:ff"),
            Make("OpenDNS", "https://doh.opendns.com/dns-query",
                "208.67.222.222", "208.67.220.220", "2620:119:35::35", "2620:119:53::53"),
        };

        static DnsPreset Make(string name, string doh, params string[] servers) =>
            new DnsPreset { Name = name, Servers = servers, BuiltIn = true, DohTemplate = doh };

        static string Dir => Env.DataDir;

        /// <summary>User presets, one per line: "Name = server1, server2, ... [, https://doh-template]"</summary>
        static string CustomFile => Path.Combine(Dir, "custom-dns.txt");

        public static List<DnsPreset> LoadCustom()
        {
            var list = new List<DnsPreset>();
            if (!File.Exists(CustomFile)) return list;

            foreach (var raw in File.ReadAllLines(CustomFile, Encoding.UTF8))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;

                var name = line.Substring(0, eq).Trim();
                var servers = ParseEntry(line.Substring(eq + 1), out var doh, out _);
                if (name.Length > 0 && servers.Length > 0)
                    list.Add(new DnsPreset { Name = name, Servers = servers, DohTemplate = doh });
            }
            return list;
        }

        public static void SaveCustom(List<DnsPreset> list)
        {
            Directory.CreateDirectory(Dir);
            var lines = new List<string> { "# FastSwitchDNS custom servers: Name = server1, server2, ... [, https://doh-template]" };
            lines.AddRange(list.Select(p =>
                $"{p.Name} = {string.Join(", ", p.HasDoh ? p.Servers.Concat(new[] { p.DohTemplate }) : p.Servers)}"));
            File.WriteAllLines(CustomFile, lines, new UTF8Encoding(false));
        }

        /// <summary>Adds a preset, replacing an existing custom one with the same name.</summary>
        public static void AddCustom(DnsPreset preset)
        {
            var list = LoadCustom();
            list.RemoveAll(p => p.Name.Equals(preset.Name, StringComparison.OrdinalIgnoreCase));
            list.Add(preset);
            SaveCustom(list);
        }

        public static void RemoveCustom(string name)
        {
            var list = LoadCustom();
            list.RemoveAll(p => p.Name == name);
            SaveCustom(list);
        }

        public static List<DnsPreset> All() => BuiltIn.Concat(LoadCustom()).ToList();

        /// <summary>Splits a comma/space separated list of addresses; returns the first bad token in <paramref name="bad"/>.</summary>
        public static string[] ParseServers(string text, out string bad)
        {
            bad = null;
            var result = new List<string>();
            foreach (var token in text.Split(new[] { ',', ';', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
            {
                // IPAddress.TryParse accepts shorthand like "1" (= 0.0.0.1), so require a full dotted quad for IPv4
                bool ok = IPAddress.TryParse(token, out var ip) &&
                    (ip.AddressFamily == AddressFamily.InterNetworkV6 ||
                     ip.AddressFamily == AddressFamily.InterNetwork && token.Count(c => c == '.') == 3);
                if (ok)
                {
                    var normalized = ip.ToString();
                    if (!result.Contains(normalized)) result.Add(normalized);
                }
                else if (bad == null)
                {
                    bad = token;
                }
            }
            return result.ToArray();
        }

        /// <summary>
        /// Like <see cref="ParseServers"/>, but also accepts one https:// DoH template among the addresses.
        /// </summary>
        public static string[] ParseEntry(string text, out string dohTemplate, out string bad)
        {
            dohTemplate = null;
            bad = null;
            var addresses = new List<string>();
            foreach (var token in text.Split(new[] { ',', ';', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (token.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                    token.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                {
                    if (dohTemplate == null && IsValidTemplate(token)) dohTemplate = token;
                    else bad ??= token;
                }
                else
                {
                    addresses.Add(token);
                }
            }
            var servers = ParseServers(string.Join(",", addresses), out var badAddress);
            bad ??= badAddress;
            return servers;
        }

        /// <summary>A DoH template must be an absolute https URL with a host and no spaces or list separators.</summary>
        public static bool IsValidTemplate(string template)
        {
            if (string.IsNullOrWhiteSpace(template)) return false;
            if (template.IndexOfAny(new[] { ' ', '\t', ',', ';', '"' }) >= 0) return false;
            return Uri.TryCreate(template, UriKind.Absolute, out var uri) &&
                   uri.Scheme == Uri.UriSchemeHttps &&
                   uri.Host.Length > 0 &&
                   !uri.Host.StartsWith(".") &&
                   string.IsNullOrEmpty(uri.UserInfo);
        }
    }
}
