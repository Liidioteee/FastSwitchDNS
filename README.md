# FastSwitchDNS

Маленькая утилита для трея Windows: переключение DNS в два клика.

- Встроенные DNS: **Cloudflare**, **Google**, **Quad9**, **AdGuard**, **OpenDNS** (IPv4 + IPv6)
- **Свой DNS**: «Добавить свой DNS…» → название, основной/дополнительный адрес (и IPv6 при желании). Сохраняется и появляется в меню, удалить можно там же
- **Автоматически (DHCP)** — вернуть DNS от роутера/провайдера
- Иконка в трее показывает текущий DNS (буква и цвет), подсказка — адаптер
- По умолчанию меняются только основные подключения (Wi-Fi / Ethernet) — VPN и виртуальные адаптеры не трогаются. Конкретный адаптер можно выбрать в меню
- Очистка кэша DNS (делается и автоматически после смены)
- Автозапуск при входе в Windows (через Планировщик заданий, без запроса UAC)
- ~50 КБ, без установки, работает на встроенном .NET Framework 4.8 (Windows 10/11)

## Установка

Скачайте `FastSwitchDNS.exe` со страницы [Releases](../../releases/latest) и запустите. Программа просит права администратора — без них Windows не даёт менять DNS.

Клик по иконке (левой или правой кнопкой) открывает меню.

## Где хранятся настройки

- Свои DNS: `%APPDATA%\FastSwitchDNS\custom-dns.txt` (формат `Название = 1.2.3.4, 5.6.7.8`)
- Выбранный адаптер: `HKCU\Software\FastSwitchDNS`
- Автозапуск: задача `FastSwitchDNS` в Планировщике заданий

## Сборка

```
dotnet build src -c Release -o dist
```

Нужен .NET SDK 6+ (целевая платформа — .NET Framework 4.8, референсные сборки подтягиваются из NuGet).

---

**English:** a tiny Windows tray app to switch DNS servers in two clicks. Built-in Cloudflare, Google, Quad9, AdGuard and OpenDNS presets, your own custom servers, DHCP reset, DNS cache flush and autostart. Download the exe from Releases; it needs admin rights to change adapter settings.

## License

MIT
