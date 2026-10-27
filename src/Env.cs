using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Security.Principal;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace FastSwitchDNS
{
    /// <summary>Runs a console program and returns its exit code and combined output.</summary>
    interface ICommandRunner
    {
        (int code, string output) Run(string file, string args);
    }

    /// <summary>Minimal registry access, so the logic can be tested without touching the real registry.</summary>
    interface IRegistry
    {
        object GetValue(RegistryHive hive, string path, string name);
        void SetValue(RegistryHive hive, string path, string name, object value, RegistryValueKind kind);
        string[] GetSubKeyNames(RegistryHive hive, string path);
        void DeleteKeyTree(RegistryHive hive, string path);
    }

    /// <summary>A network adapter snapshot: only the fields the app needs.</summary>
    class AdapterInfo
    {
        public string Id;
        public string Name;
        public string Description;
        public NetworkInterfaceType Type;
        public bool IsUp;
        public bool SupportsIPv4;
        public bool SupportsIPv6;
        public bool HasGateway;

        public static IEnumerable<AdapterInfo> FromSystem() =>
            NetworkInterface.GetAllNetworkInterfaces().Select(n => new AdapterInfo
            {
                Id = n.Id,
                Name = n.Name,
                Description = n.Description,
                Type = n.NetworkInterfaceType,
                IsUp = n.OperationalStatus == OperationalStatus.Up,
                SupportsIPv4 = n.Supports(NetworkInterfaceComponent.IPv4),
                SupportsIPv6 = n.Supports(NetworkInterfaceComponent.IPv6),
                HasGateway = SafeHasGateway(n),
            });

        static bool SafeHasGateway(NetworkInterface n)
        {
            try
            {
                return n.GetIPProperties().GatewayAddresses
                    .Any(g => !g.Address.Equals(System.Net.IPAddress.Any));
            }
            catch (NetworkInformationException)
            {
                return false;
            }
        }
    }

    class ProcessRunner : ICommandRunner
    {
        public (int code, string output) Run(string file, string args)
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

    class WindowsRegistry : IRegistry
    {
        static RegistryKey Base(RegistryHive hive) => RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);

        public object GetValue(RegistryHive hive, string path, string name)
        {
            using var root = Base(hive);
            using var key = root.OpenSubKey(path);
            return key?.GetValue(name);
        }

        public void SetValue(RegistryHive hive, string path, string name, object value, RegistryValueKind kind)
        {
            using var root = Base(hive);
            using var key = root.CreateSubKey(path);
            key.SetValue(name, value, kind);
        }

        public string[] GetSubKeyNames(RegistryHive hive, string path)
        {
            using var root = Base(hive);
            using var key = root.OpenSubKey(path);
            return key?.GetSubKeyNames() ?? new string[0];
        }

        public void DeleteKeyTree(RegistryHive hive, string path)
        {
            using var root = Base(hive);
            root.DeleteSubKeyTree(path, false);
        }
    }

    /// <summary>Everything that talks to Windows. Tests swap these for fakes.</summary>
    static class Env
    {
        public static ICommandRunner Runner;
        public static IRegistry Registry;
        public static Func<IEnumerable<AdapterInfo>> AdapterSource;
        public static string DataDir;
        public static Func<string> CurrentUser;
        public static Func<string> ExecutablePath;

        static Env() => Reset();

        public static void Reset()
        {
            Runner = new ProcessRunner();
            Registry = new WindowsRegistry();
            AdapterSource = AdapterInfo.FromSystem;
            DataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FastSwitchDNS");
            CurrentUser = () => WindowsIdentity.GetCurrent().Name;
            ExecutablePath = () => Application.ExecutablePath;
        }
    }
}
