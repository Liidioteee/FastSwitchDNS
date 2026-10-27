using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Xml.Linq;
using Microsoft.Win32;
using Xunit;

namespace FastSwitchDNS.Tests
{
    public class AutostartTests : EnvTest
    {
        [Fact]
        public void IsEnabled_QueriesTask()
        {
            Assert.True(Autostart.IsEnabled());
            Assert.Equal(("schtasks.exe", "/query /tn \"FastSwitchDNS\""), Runner.Calls.Single());

            Runner.Handler = (f, a) => (1, "not found");
            Assert.False(Autostart.IsEnabled());
        }

        [Fact]
        public void Disable_DeletesTask()
        {
            Autostart.Set(false);
            Assert.Equal(("schtasks.exe", "/delete /tn \"FastSwitchDNS\" /f"), Runner.Calls.Single());
        }

        [Fact]
        public void Enable_CreatesTaskFromXmlFile_AndRemovesFile()
        {
            string file = null, xml = null;
            Runner.Handler = (f, a) =>
            {
                file = a.Split('"')[3];
                xml = File.ReadAllText(file, Encoding.Unicode);
                return (0, "");
            };

            Autostart.Set(true);

            Assert.StartsWith("/create /tn \"FastSwitchDNS\" /xml \"", Runner.Calls.Single().args);
            Assert.EndsWith("\" /f", Runner.Calls.Single().args);
            Assert.Contains(@"<Command>C:\Apps\FastSwitchDNS.exe</Command>", xml);
            Assert.False(File.Exists(file));
        }

        [Fact]
        public void Enable_Failure_ThrowsAndRemovesFile()
        {
            string file = null;
            Runner.Handler = (f, a) => { file = a.Split('"')[3]; return (1, " Access is denied. "); };
            var ex = Assert.Throws<Exception>(() => Autostart.Set(true));
            Assert.Equal("Access is denied.", ex.Message);
            Assert.False(File.Exists(file));
        }

        [Fact]
        public void TaskXml_IsValidAndEscaped()
        {
            var xml = Autostart.TaskXml(@"PC\Tom & Jerry", @"C:\A<B>\app.exe");
            var doc = XDocument.Parse(xml);
            XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
            Assert.Equal(@"PC\Tom & Jerry", doc.Descendants(ns + "LogonTrigger").Single().Element(ns + "UserId").Value);
            Assert.Equal(@"PC\Tom & Jerry", doc.Descendants(ns + "Principal").Single().Element(ns + "UserId").Value);
            Assert.Equal("HighestAvailable", doc.Descendants(ns + "RunLevel").Single().Value);
            Assert.Equal("false", doc.Descendants(ns + "DisallowStartIfOnBatteries").Single().Value);
            Assert.Equal(@"C:\A<B>\app.exe", doc.Descendants(ns + "Command").Single().Value);
        }
    }

    public class SettingsTests : EnvTest
    {
        [Fact]
        public void Defaults()
        {
            Assert.Equal("", Settings.AdapterId);
            Assert.True(Settings.DohEnabled);
            Assert.False(Settings.DohFallback);
        }

        [Fact]
        public void RoundTrip()
        {
            Settings.AdapterId = "{X}";
            Settings.DohEnabled = false;
            Settings.DohFallback = true;
            Assert.Equal("{X}", Settings.AdapterId);
            Assert.False(Settings.DohEnabled);
            Assert.True(Settings.DohFallback);

            Settings.AdapterId = null;
            Assert.Equal("", Settings.AdapterId);
        }

        [Fact]
        public void StoredInCurrentUserKey()
        {
            Settings.DohEnabled = false;
            Settings.AdapterId = "{X}";
            Assert.Equal(0, Registry.GetValue(RegistryHive.CurrentUser, @"Software\FastSwitchDNS", "Doh"));
            Assert.Equal("{X}", Registry.GetValue(RegistryHive.CurrentUser, @"Software\FastSwitchDNS", "Adapter"));
        }
    }

    public class StringsTests : IDisposable
    {
        public void Dispose() => L.Culture = null;

        static string[] All()
        {
            var props = typeof(L).GetProperties(BindingFlags.Public | BindingFlags.Static)
                .Where(p => p.PropertyType == typeof(string))
                .Select(p => (string)p.GetValue(null));
            var methods = new[] { L.BadAddress("x"), L.BadTemplate("y") };
            return props.Concat(methods).ToArray();
        }

        [Fact]
        public void EveryStringExistsInBothLanguages_AndDiffers()
        {
            L.Culture = new CultureInfo("ru-RU");
            var ru = All();
            L.Culture = new CultureInfo("en-US");
            var en = All();

            Assert.Equal(ru.Length, en.Length);
            Assert.All(ru.Concat(en), s => Assert.False(string.IsNullOrWhiteSpace(s)));
            int same = ru.Zip(en, (a, b) => a == b).Count(x => x);
            Assert.Equal(1, same); // only "DoH" is the same in both languages
        }

        [Fact]
        public void NonRussianCultureFallsBackToEnglish()
        {
            L.Culture = new CultureInfo("de-DE");
            Assert.Equal("Exit", L.Exit);
            L.Culture = new CultureInfo("ru-RU");
            Assert.Equal("Выход", L.Exit);
        }

        [Fact]
        public void MessagesIncludeTheBadValue()
        {
            L.Culture = CultureInfo.InvariantCulture;
            Assert.Contains("999.1.1.1", L.BadAddress("999.1.1.1"));
            Assert.Contains("http://x", L.BadTemplate("http://x"));
        }
    }

    public class IconFactoryTests
    {
        [Fact]
        public void BuiltInColors_AreDistinct()
        {
            var colors = Presets.BuiltIn.Select(IconFactory.ColorFor).Concat(new[] { IconFactory.ColorFor(null) }).ToList();
            Assert.Equal(colors.Count, colors.Distinct().Count());
        }

        [Fact]
        public void CustomColor_IsStablePerName()
        {
            var a = IconFactory.ColorFor(new DnsPreset { Name = "Home" });
            var b = IconFactory.ColorFor(new DnsPreset { Name = "Home" });
            Assert.Equal(a, b);
        }

        [Fact]
        public void CustomColor_HandlesLongAndUnicodeNames()
        {
            IconFactory.ColorFor(new DnsPreset { Name = new string('я', 500) });
            IconFactory.ColorFor(new DnsPreset { Name = "" });
        }

        [Theory]
        [InlineData(null)]
        [InlineData("Cloudflare")]
        [InlineData("мой")]
        public void Create_ReturnsSmallIcon(string name)
        {
            using var icon = IconFactory.Create(name == null ? null : new DnsPreset { Name = name });
            Assert.Equal(System.Windows.Forms.SystemInformation.SmallIconSize, icon.Size);
            using var bmp = icon.ToBitmap();
            var center = bmp.GetPixel(1, bmp.Height / 2);
            Assert.NotEqual(0, center.A); // badge is drawn
        }
    }

    public class EnvTests : IDisposable
    {
        const string TestKey = @"Software\FastSwitchDNS.Tests";

        public void Dispose()
        {
            new WindowsRegistry().DeleteKeyTree(RegistryHive.CurrentUser, TestKey);
            Env.Reset();
        }

        [Fact]
        public void Reset_UsesRealImplementations()
        {
            Env.Reset();
            Assert.IsType<ProcessRunner>(Env.Runner);
            Assert.IsType<WindowsRegistry>(Env.Registry);
            Assert.EndsWith("FastSwitchDNS", Env.DataDir);
            Assert.False(string.IsNullOrEmpty(Env.CurrentUser()));
        }

        [Fact]
        public void ProcessRunner_ReturnsCodeAndOutput()
        {
            var (code, output) = new ProcessRunner().Run("cmd.exe", "/c echo hello& echo oops 1>&2& exit /b 3");
            Assert.Equal(3, code);
            Assert.Contains("hello", output);
            Assert.Contains("oops", output);
        }

        [Fact]
        public void WindowsRegistry_RoundTripInCurrentUser()
        {
            var reg = new WindowsRegistry();
            Assert.Null(reg.GetValue(RegistryHive.CurrentUser, TestKey, "x"));
            Assert.Empty(reg.GetSubKeyNames(RegistryHive.CurrentUser, TestKey));

            reg.SetValue(RegistryHive.CurrentUser, TestKey + @"\1.2.3.4", "DohFlags", 1L, RegistryValueKind.QWord);
            reg.SetValue(RegistryHive.CurrentUser, TestKey, "s", "v", RegistryValueKind.String);

            Assert.Equal(1L, reg.GetValue(RegistryHive.CurrentUser, TestKey + @"\1.2.3.4", "DohFlags"));
            Assert.Equal("v", reg.GetValue(RegistryHive.CurrentUser, TestKey, "s"));
            Assert.Equal(new[] { "1.2.3.4" }, reg.GetSubKeyNames(RegistryHive.CurrentUser, TestKey));

            reg.DeleteKeyTree(RegistryHive.CurrentUser, TestKey);
            Assert.Null(reg.GetValue(RegistryHive.CurrentUser, TestKey, "s"));
            reg.DeleteKeyTree(RegistryHive.CurrentUser, TestKey); // missing key is fine
        }

        [Fact]
        public void WindowsBuild_IsReadableFromRealRegistry()
        {
            var build = new WindowsRegistry().GetValue(RegistryHive.LocalMachine,
                @"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "CurrentBuildNumber") as string;
            Assert.True(int.TryParse(build, out var number) && number >= 10240, build); // 10240 = first Windows 10
            Assert.Equal(number >= DnsManager.DohMinBuild, DnsManager.DohSupported());
        }

        [Fact]
        public void AdapterInfo_FromSystem_ListsAdapters()
        {
            var list = AdapterInfo.FromSystem().ToList();
            Assert.NotEmpty(list); // there is always at least loopback
            Assert.All(list, a => Assert.False(string.IsNullOrEmpty(a.Id)));
        }
    }
}
