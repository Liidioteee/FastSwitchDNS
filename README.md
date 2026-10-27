# FastSwitchDNS

Маленькая утилита для трея Windows: переключение DNS в два клика.

- Встроенные DNS: **Cloudflare**, **Google**, **Quad9**, **AdGuard**, **OpenDNS** (IPv4 + IPv6)
- **Шифрование DNS (DoH, Windows 11)**: у встроенных DNS уже есть шаблоны DNS over HTTPS, запросы уходят зашифрованными. Включается и выключается в меню, там же можно разрешить незашифрованный запрос, если DoH-сервер не отвечает (по умолчанию выключено — только шифрование)
- **Свой DNS**: «Добавить свой DNS…» → название, основной/дополнительный адрес (и IPv6 при желании), шаблон DoH (необязательно). Сохраняется и появляется в меню, удалить можно там же
- **Автоматически (DHCP)** — вернуть DNS от роутера/провайдера
- Иконка в трее показывает текущий DNS (буква и цвет), подсказка — адаптер и «DoH», если шифрование включено
- По умолчанию меняются только основные подключения (Wi-Fi / Ethernet) — VPN и виртуальные адаптеры не трогаются. Конкретный адаптер можно выбрать в меню
- Очистка кэша DNS (делается и автоматически после смены)
- Автозапуск при входе в Windows (через Планировщик заданий, без запроса UAC)
- ~60 КБ, без установки, работает на встроенном .NET Framework 4.8 (Windows 10/11)

## Установка

Скачайте `FastSwitchDNS.exe` со страницы [Releases](../../releases/latest) и запустите. Программа просит права администратора — без них Windows не даёт менять DNS.

Клик по иконке (левой или правой кнопкой) открывает меню.

## Где хранятся настройки

- Свои DNS: `%APPDATA%\FastSwitchDNS\custom-dns.txt` (формат `Название = 1.2.3.4, 5.6.7.8, https://dns.example.com/dns-query` — шаблон DoH необязателен)
- Выбранный адаптер и настройки DoH: `HKCU\Software\FastSwitchDNS`
- Шаблоны DoH регистрируются в Windows (`netsh dns show encryption`), шифрование на адаптере — в `HKLM\SYSTEM\CurrentControlSet\Services\Dnscache\InterfaceSpecificParameters\{адаптер}\DohInterfaceSettings` (то же, что пишут «Параметры Windows»)
- Автозапуск: задача `FastSwitchDNS` в Планировщике заданий

## Шифрование DNS (DoH)

Работает в Windows 11 и Windows Server 2022 и новее. В Windows 10 системного DNS over HTTPS нет, поэтому там пункты DoH в меню и поле шаблона не показываются, а DNS переключается как обычно, без шифрования. Проверить, что шифрование работает: «Параметры» → «Сеть и Интернет» → адаптер → «Назначение DNS-сервера» — должно быть «Зашифровано (DNS по протоколу HTTPS)».

## Сборка

```
dotnet build src -c Release -o dist
```

Нужен .NET SDK 6+ (целевая платформа — .NET Framework 4.8, референсные сборки подтягиваются из NuGet).

## Тесты

```
dotnet test tests
```

Тесты не трогают настройки сети: команды, реестр и список адаптеров подменяются. Только один тест проверяет работу с настоящим реестром — во временном ключе `HKCU\Software\FastSwitchDNS.Tests`, который сразу удаляется.

---

**English:** a tiny Windows tray app to switch DNS servers in two clicks. Built-in Cloudflare, Google, Quad9, AdGuard and OpenDNS presets with DNS over HTTPS (Windows 11), your own custom servers, DHCP reset, DNS cache flush and autostart. Download the exe from Releases; it needs admin rights to change adapter settings.

## License

MIT
