using System.IO;
using System.Linq;
using System.Text;
using Xunit;

namespace FastSwitchDNS.Tests
{
    public class PresetsTests : EnvTest
    {
        [Fact]
        public void BuiltIn_HasFiveProvidersWithDohTemplates()
        {
            Assert.Equal(new[] { "Cloudflare", "Google", "Quad9", "AdGuard", "OpenDNS" },
                Presets.BuiltIn.Select(p => p.Name));
            foreach (var p in Presets.BuiltIn)
            {
                Assert.True(p.BuiltIn);
                Assert.True(p.HasDoh, p.Name);
                Assert.True(Presets.IsValidTemplate(p.DohTemplate), p.DohTemplate);
                Assert.Equal(2, p.V4.Count());
                Assert.Equal(2, p.V6.Count());
            }
        }

        [Theory]
        [InlineData("Cloudflare", "https://cloudflare-dns.com/dns-query")]
        [InlineData("Google", "https://dns.google/dns-query")]
        [InlineData("Quad9", "https://dns.quad9.net/dns-query")]
        [InlineData("AdGuard", "https://dns.adguard-dns.com/dns-query")]
        [InlineData("OpenDNS", "https://doh.opendns.com/dns-query")]
        public void BuiltIn_Templates(string name, string template) =>
            Assert.Equal(template, Presets.BuiltIn.Single(p => p.Name == name).DohTemplate);

        [Fact]
        public void Preset_SplitsFamilies_AndIgnoresGarbage()
        {
            var p = new DnsPreset { Servers = new[] { "1.1.1.1", "2606:4700::1", "junk" } };
            Assert.Equal(new[] { "1.1.1.1" }, p.V4);
            Assert.Equal(new[] { "2606:4700::1" }, p.V6);
            Assert.False(p.HasDoh);
            p.DohTemplate = "";
            Assert.False(p.HasDoh);
        }

        [Theory]
        [InlineData("1.1.1.1, 8.8.8.8", new[] { "1.1.1.1", "8.8.8.8" })]
        [InlineData("1.1.1.1;8.8.8.8 9.9.9.9\t1.0.0.1", new[] { "1.1.1.1", "8.8.8.8", "9.9.9.9", "1.0.0.1" })]
        [InlineData("1.1.1.1, 1.1.1.1", new[] { "1.1.1.1" })]
        [InlineData("2001:DB8::1", new[] { "2001:db8::1" })]
        [InlineData("", new string[0])]
        public void ParseServers_Valid(string text, string[] expected)
        {
            Assert.Equal(expected, Presets.ParseServers(text, out var bad));
            Assert.Null(bad);
        }

        [Theory]
        [InlineData("1", "1")]
        [InlineData("1.1.1", "1.1.1")]
        [InlineData("1.1.1.1, abc, def", "abc")]
        [InlineData("300.1.1.1", "300.1.1.1")]
        public void ParseServers_ReportsFirstBadToken(string text, string bad)
        {
            Presets.ParseServers(text, out var actual);
            Assert.Equal(bad, actual);
        }

        [Fact]
        public void ParseEntry_PicksTemplate()
        {
            var servers = Presets.ParseEntry("1.2.3.4, 5.6.7.8, https://dns.example.com/dns-query", out var doh, out var bad);
            Assert.Equal(new[] { "1.2.3.4", "5.6.7.8" }, servers);
            Assert.Equal("https://dns.example.com/dns-query", doh);
            Assert.Null(bad);
        }

        [Fact]
        public void ParseEntry_WithoutTemplate()
        {
            var servers = Presets.ParseEntry("1.2.3.4", out var doh, out var bad);
            Assert.Equal(new[] { "1.2.3.4" }, servers);
            Assert.Null(doh);
            Assert.Null(bad);
        }

        [Theory]
        [InlineData("1.2.3.4, http://dns.example.com/q", "http://dns.example.com/q")]
        [InlineData("1.2.3.4, https://a.com/q, https://b.com/q", "https://b.com/q")]
        [InlineData("1.2.3.4, https://", "https://")]
        [InlineData("x, https://a.com/q", "x")]
        public void ParseEntry_ReportsBadTokens(string text, string bad)
        {
            Presets.ParseEntry(text, out _, out var actual);
            Assert.Equal(bad, actual);
        }

        [Theory]
        [InlineData("https://cloudflare-dns.com/dns-query", true)]
        [InlineData("https://dns.example.com/dns-query{?dns}", true)]
        [InlineData("https://dns.example.com:8443/q", true)]
        [InlineData("HTTPS://DNS.EXAMPLE.COM/q", true)]
        [InlineData("http://dns.example.com/dns-query", false)]
        [InlineData("ftp://dns.example.com", false)]
        [InlineData("dns.example.com/dns-query", false)]
        [InlineData("https://", false)]
        [InlineData("https://user:pass@dns.example.com/q", false)]
        [InlineData("https://dns.example.com/a b", false)]
        [InlineData("https://dns.example.com/a,b", false)]
        [InlineData("https://dns.example.com/a;b", false)]
        [InlineData("https://dns.example.com/\"x", false)]
        [InlineData("", false)]
        [InlineData("   ", false)]
        [InlineData(null, false)]
        public void IsValidTemplate(string template, bool valid) =>
            Assert.Equal(valid, Presets.IsValidTemplate(template));

        [Fact]
        public void LoadCustom_NoFile_ReturnsEmpty() => Assert.Empty(Presets.LoadCustom());

        [Fact]
        public void SaveAndLoad_RoundTrip_KeepsTemplate()
        {
            Presets.SaveCustom(new[]
            {
                new DnsPreset { Name = "Home", Servers = new[] { "192.168.1.1" } },
                new DnsPreset { Name = "Secure", Servers = new[] { "1.2.3.4", "2001:db8::1" }, DohTemplate = "https://dns.example.com/dns-query" },
            }.ToList());

            var text = File.ReadAllText(CustomFile);
            Assert.Contains("Home = 192.168.1.1", text);
            Assert.Contains("Secure = 1.2.3.4, 2001:db8::1, https://dns.example.com/dns-query", text);
            Assert.False(File.ReadAllBytes(CustomFile).Take(3).SequenceEqual(new byte[] { 0xEF, 0xBB, 0xBF }), "no BOM");

            var loaded = Presets.LoadCustom();
            Assert.Equal(2, loaded.Count);
            Assert.Null(loaded[0].DohTemplate);
            Assert.False(loaded[0].BuiltIn);
            Assert.Equal(new[] { "1.2.3.4", "2001:db8::1" }, loaded[1].Servers);
            Assert.Equal("https://dns.example.com/dns-query", loaded[1].DohTemplate);
        }

        [Fact]
        public void LoadCustom_SkipsCommentsBlankAndBrokenLines_AndReadsOldFormat()
        {
            Directory.CreateDirectory(DataDir);
            File.WriteAllLines(CustomFile, new[]
            {
                "# comment",
                "",
                "no equals sign",
                "= 1.2.3.4",
                "Empty = ",
                "Bad = nonsense",
                "  Old style = 10.0.0.1, 10.0.0.2  ",
                "Мой DNS = 77.88.8.8, https://common.dot.dns.yandex.net/dns-query",
            }, Encoding.UTF8);

            var loaded = Presets.LoadCustom();
            Assert.Equal(new[] { "Old style", "Мой DNS" }, loaded.Select(p => p.Name));
            Assert.Equal(new[] { "10.0.0.1", "10.0.0.2" }, loaded[0].Servers);
            Assert.Equal("https://common.dot.dns.yandex.net/dns-query", loaded[1].DohTemplate);
        }

        [Fact]
        public void AddCustom_ReplacesSameNameIgnoringCase()
        {
            Presets.AddCustom(new DnsPreset { Name = "Home", Servers = new[] { "1.1.1.1" } });
            Presets.AddCustom(new DnsPreset { Name = "Work", Servers = new[] { "2.2.2.2" } });
            Presets.AddCustom(new DnsPreset { Name = "HOME", Servers = new[] { "3.3.3.3" }, DohTemplate = "https://h.example/q" });

            var loaded = Presets.LoadCustom();
            Assert.Equal(new[] { "Work", "HOME" }, loaded.Select(p => p.Name));
            Assert.Equal("https://h.example/q", loaded[1].DohTemplate);
        }

        [Fact]
        public void RemoveCustom_RemovesExactName()
        {
            Presets.AddCustom(new DnsPreset { Name = "Home", Servers = new[] { "1.1.1.1" } });
            Presets.AddCustom(new DnsPreset { Name = "Work", Servers = new[] { "2.2.2.2" } });
            Presets.RemoveCustom("Home");
            Assert.Equal(new[] { "Work" }, Presets.LoadCustom().Select(p => p.Name));
        }

        [Fact]
        public void All_IsBuiltInThenCustom()
        {
            Presets.AddCustom(new DnsPreset { Name = "Home", Servers = new[] { "1.1.1.1" } });
            var all = Presets.All();
            Assert.Equal(6, all.Count);
            Assert.Equal("Cloudflare", all[0].Name);
            Assert.Equal("Home", all[5].Name);
        }
    }
}
