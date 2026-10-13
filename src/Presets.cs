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

        public IEnumerable<string> V4 => Servers.Where(s => Family(s) == AddressFamily.InterNetwork);
        public IEnumerable<string> V6 => Servers.Where(s => Family(s) == AddressFamily.InterNetworkV6);

        static AddressFamily Family(string s) =>
            IPAddress.TryParse(s, out var ip) ? ip.AddressFamily : AddressFamily.Unknown;
    }

    static class Presets
    {
        public static readonly DnsPreset[] BuiltIn =
        {
            Make("Cloudflare", "1.1.1.1", "1.0.0.1", "2606:4700:4700::1111", "2606:4700:4700::1001"),
            Make("Google", "8.8.8.8", "8.8.4.4", "2001:4860:4860::8888", "2001:4860:4860::8844"),
            Make("Quad9", "9.9.9.9", "149.112.112.112", "2620:fe::fe", "2620:fe::9"),
            Make("AdGuard", "94.140.14.14", "94.140.15.15", "2a10:50c0::ad1:ff", "2a10:50c0::ad2:ff"),
            Make("OpenDNS", "208.67.222.222", "208.67.220.220", "2620:119:35::35", "2620:119:53::53"),
        };

        static DnsPreset Make(string name, params string[] servers) =>
            new DnsPreset { Name = name, Servers = servers, BuiltIn = true };

        static string Dir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FastSwitchDNS");

        /// <summary>User presets, one per line: "Name = server1, server2, ..."</summary>
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
                var servers = ParseServers(line.Substring(eq + 1), out _);
                if (name.Length > 0 && servers.Length > 0)
                    list.Add(new DnsPreset { Name = name, Servers = servers });
            }
            return list;
        }

        public static void SaveCustom(List<DnsPreset> list)
        {
            Directory.CreateDirectory(Dir);
            var lines = new List<string> { "# FastSwitchDNS custom servers: Name = server1, server2, ..." };
            lines.AddRange(list.Select(p => $"{p.Name} = {string.Join(", ", p.Servers)}"));
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
    }
}
