using Microsoft.Win32;

namespace FastSwitchDNS
{
    /// <summary>Per-user settings in HKCU\Software\FastSwitchDNS.</summary>
    static class Settings
    {
        const string Key = @"Software\FastSwitchDNS";

        /// <summary>Selected adapter id; empty means "all main connections".</summary>
        public static string AdapterId
        {
            get => Env.Registry.GetValue(RegistryHive.CurrentUser, Key, "Adapter") as string ?? "";
            set => Env.Registry.SetValue(RegistryHive.CurrentUser, Key, "Adapter", value ?? "", RegistryValueKind.String);
        }

        /// <summary>Use DNS over HTTPS for presets that have a template (on by default).</summary>
        public static bool DohEnabled
        {
            get => GetFlag("Doh", true);
            set => SetFlag("Doh", value);
        }

        /// <summary>Allow plain DNS when the DoH server does not answer (off by default: encrypted only).</summary>
        public static bool DohFallback
        {
            get => GetFlag("DohFallback", false);
            set => SetFlag("DohFallback", value);
        }

        static bool GetFlag(string name, bool fallback) =>
            Env.Registry.GetValue(RegistryHive.CurrentUser, Key, name) is int v ? v != 0 : fallback;

        static void SetFlag(string name, bool value) =>
            Env.Registry.SetValue(RegistryHive.CurrentUser, Key, name, value ? 1 : 0, RegistryValueKind.DWord);
    }
}
