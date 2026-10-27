using System.Globalization;

namespace FastSwitchDNS
{
    /// <summary>UI strings: Russian when the system UI language is Russian, English otherwise.</summary>
    static class L
    {
        /// <summary>Overrides the UI culture (tests); null means the system UI language.</summary>
        public static CultureInfo Culture;

        static bool Ru => (Culture ?? CultureInfo.CurrentUICulture).TwoLetterISOLanguageName == "ru";

        static string T(string ru, string en) => Ru ? ru : en;

        public static string Auto => T("Автоматически (DHCP)", "Automatic (DHCP)");
        public static string AutoShort => T("Авто", "Auto");
        public static string Custom => T("Другой", "Other");
        public static string AddCustom => T("Добавить свой DNS…", "Add custom DNS…");
        public static string RemoveCustom => T("Удалить свой DNS", "Remove custom DNS");
        public static string Adapter => T("Адаптер", "Adapter");
        public static string AllActive => T("Основные (Wi-Fi / Ethernet)", "Main (Wi-Fi / Ethernet)");
        public static string NoAdapters => T("Нет активных подключений", "No active connections");
        public static string FlushCache => T("Очистить кэш DNS", "Flush DNS cache");
        public static string CacheFlushed => T("Кэш DNS очищен", "DNS cache flushed");
        public static string Autostart => T("Запускать при входе в Windows", "Run at Windows logon");
        public static string Exit => T("Выход", "Exit");
        public static string Applying => T("Применяю…", "Applying…");
        public static string Doh => "DoH";
        public static string DohToggle => T("Шифрование DNS (DoH)", "Encrypted DNS (DoH)");
        public static string DohFallback => T("Без шифрования, если DoH не отвечает", "Allow unencrypted if DoH fails");
        public static string Error => T("Ошибка", "Error");

        public static string DialogTitle => T("Свой DNS", "Custom DNS");
        public static string NameLabel => T("Название:", "Name:");
        public static string PrimaryLabel => T("Основной DNS:", "Primary DNS:");
        public static string SecondaryLabel => T("Дополнительный:", "Secondary:");
        public static string ExtraLabel => T("Ещё (IPv6 и др., через запятую):", "More (IPv6 etc., comma separated):");
        public static string DohLabel => T("Шаблон DoH:", "DoH template:");
        public static string DohHint => T("https://… (необязательно)", "https://… (optional)");
        public static string BadTemplate(string s) => T(
            $"Неверный шаблон DoH: {s}\nНужен адрес вида https://dns.example.com/dns-query",
            $"Invalid DoH template: {s}\nUse an address like https://dns.example.com/dns-query");
        public static string SaveAndApply => T("Сохранить и применить", "Save and apply");
        public static string SaveOnly => T("Сохранить", "Save");
        public static string Cancel => T("Отмена", "Cancel");
        public static string BadAddress(string s) => T($"Неверный IP-адрес: {s}", $"Invalid IP address: {s}");
        public static string NeedPrimary => T("Укажите основной DNS.", "Enter the primary DNS.");
        public static string NeedName => T("Укажите название.", "Enter a name.");
        public static string NameTaken => T("Такое название уже есть во встроенном списке.", "This name is already used by a built-in preset.");
    }
}
