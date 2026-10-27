using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;
using Xunit;

// Env is static and shared, so tests must not run in parallel
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace FastSwitchDNS.Tests
{
    class FakeRunner : ICommandRunner
    {
        public readonly List<(string file, string args)> Calls = new List<(string, string)>();

        /// <summary>Decides the result for each command; default is success with no output.</summary>
        public Func<string, string, (int, string)> Handler = (f, a) => (0, "");

        public (int code, string output) Run(string file, string args)
        {
            Calls.Add((file, args));
            return Handler(file, args);
        }

        public List<string> Args(string file) =>
            Calls.Where(c => c.file == file).Select(c => c.args).ToList();
    }

    class FakeRegistry : IRegistry
    {
        readonly Dictionary<string, Dictionary<string, object>> keys =
            new Dictionary<string, Dictionary<string, object>>(StringComparer.OrdinalIgnoreCase);

        public readonly List<string> Deleted = new List<string>();
        public Exception FailOnDelete;
        public Exception FailOnSet;

        static string Full(RegistryHive hive, string path) => hive + "\\" + path.Trim('\\');

        public bool KeyExists(RegistryHive hive, string path) => keys.ContainsKey(Full(hive, path));

        public object GetValue(RegistryHive hive, string path, string name) =>
            keys.TryGetValue(Full(hive, path), out var values) && values.TryGetValue(name, out var v) ? v : null;

        public void SetValue(RegistryHive hive, string path, string name, object value, RegistryValueKind kind)
        {
            if (FailOnSet != null) throw FailOnSet;
            var full = Full(hive, path);
            if (!keys.TryGetValue(full, out var values))
                keys[full] = values = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            // Store what the real registry would hand back
            values[name] = kind == RegistryValueKind.QWord ? Convert.ToInt64(value)
                : kind == RegistryValueKind.DWord ? (object)Convert.ToInt32(value)
                : value;
        }

        public string[] GetSubKeyNames(RegistryHive hive, string path)
        {
            var prefix = Full(hive, path) + "\\";
            return keys.Keys.Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .Select(k => k.Substring(prefix.Length).Split('\\')[0])
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        public void DeleteKeyTree(RegistryHive hive, string path)
        {
            if (FailOnDelete != null) throw FailOnDelete;
            var full = Full(hive, path);
            Deleted.Add(full);
            foreach (var k in keys.Keys.Where(k =>
                         k.Equals(full, StringComparison.OrdinalIgnoreCase) ||
                         k.StartsWith(full + "\\", StringComparison.OrdinalIgnoreCase)).ToList())
                keys.Remove(k);
        }
    }

    /// <summary>Base for tests: swaps every system dependency for a fake and restores it afterwards.</summary>
    public abstract class EnvTest : IDisposable
    {
        internal readonly FakeRunner Runner = new FakeRunner();
        internal readonly FakeRegistry Registry = new FakeRegistry();
        internal readonly List<AdapterInfo> AdapterList = new List<AdapterInfo>();
        protected readonly string DataDir;

        protected EnvTest()
        {
            DataDir = Path.Combine(Path.GetTempPath(), "FastSwitchDNS.Tests", Guid.NewGuid().ToString("N"));
            Env.Runner = Runner;
            Env.Registry = Registry;
            Env.AdapterSource = () => AdapterList;
            Env.DataDir = DataDir;
            Env.CurrentUser = () => @"PC\User";
            Env.ExecutablePath = () => @"C:\Apps\FastSwitchDNS.exe";
            L.Culture = CultureInfo.InvariantCulture; // English strings
            SetWindowsBuild("22631"); // Windows 11 23H2 unless a test says otherwise
        }

        public void Dispose()
        {
            Env.Reset();
            L.Culture = null;
            if (Directory.Exists(DataDir)) Directory.Delete(DataDir, true);
        }

        internal static AdapterInfo Wifi(string id = "{WIFI}", string name = "Wi-Fi", bool gateway = true) => new AdapterInfo
        {
            Id = id, Name = name, Description = "Intel Wireless", Type = NetworkInterfaceType.Wireless80211,
            IsUp = true, SupportsIPv4 = true, SupportsIPv6 = true, HasGateway = gateway,
        };

        internal static AdapterInfo Ethernet(string id = "{ETH}", string name = "Ethernet", bool gateway = true) => new AdapterInfo
        {
            Id = id, Name = name, Description = "Realtek PCIe GbE", Type = NetworkInterfaceType.Ethernet,
            IsUp = true, SupportsIPv4 = true, SupportsIPv6 = true, HasGateway = gateway,
        };

        internal static AdapterInfo Vpn(string id = "{VPN}") => new AdapterInfo
        {
            Id = id, Name = "WireGuard Tunnel", Description = "WireGuard Tunnel", Type = NetworkInterfaceType.Ethernet,
            IsUp = true, SupportsIPv4 = true, SupportsIPv6 = true, HasGateway = true,
        };

        /// <summary>Pretends to run on the given Windows build (null = value missing).</summary>
        internal void SetWindowsBuild(string build) =>
            Registry.SetValue(RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion",
                "CurrentBuildNumber", build, RegistryValueKind.String);

        internal const string Windows10 = "19045";

        /// <summary>Puts static DNS servers into the fake registry, as netsh would.</summary>
        internal void SetStatic(AdapterInfo a, string v4, string v6 = null)
        {
            if (v4 != null)
                Registry.SetValue(RegistryHive.LocalMachine,
                    $@"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\{a.Id}", "NameServer", v4, RegistryValueKind.String);
            if (v6 != null)
                Registry.SetValue(RegistryHive.LocalMachine,
                    $@"SYSTEM\CurrentControlSet\Services\Tcpip6\Parameters\Interfaces\{a.Id}", "NameServer", v6, RegistryValueKind.String);
        }

        internal long? DohFlags(AdapterInfo a, string ip, bool v6 = false) =>
            Registry.GetValue(RegistryHive.LocalMachine, $@"{DnsManager.DohKey(a, v6)}\{ip}", "DohFlags") as long?;

        protected string CustomFile => Path.Combine(DataDir, "custom-dns.txt");

        /// <summary>
        /// Runs WinForms code on its own STA thread with a message loop, like the real app.
        /// Creating a control on a test thread would leave a WinForms sync context there,
        /// and a later await on that thread would wait forever for a loop that never runs.
        /// </summary>
        protected static void Ui(Func<Task> body)
        {
            Exception error = null;
            var thread = new Thread(() =>
            {
                SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
                Task task;
                try { task = body(); }
                catch (Exception ex) { error = ex; return; }
                task.ContinueWith(_ => Application.ExitThread(), TaskScheduler.FromCurrentSynchronizationContext());
                Application.Run();
                try { task.GetAwaiter().GetResult(); }
                catch (Exception ex) { error = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();
            if (!thread.Join(TimeSpan.FromSeconds(20)))
                throw new TimeoutException("UI test did not finish in 20 s");
            if (error != null) ExceptionDispatchInfo.Capture(error).Throw();
        }

        protected static void Ui(Action body) => Ui(() => { body(); return Task.CompletedTask; });
    }
}
