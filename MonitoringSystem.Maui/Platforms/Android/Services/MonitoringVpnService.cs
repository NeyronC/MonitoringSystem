#if ANDROID
using Android.App;
using Android.Content;
using Android.Net;
using Android.OS;
using Java.IO;
using Java.Nio;
using Java.Nio.Channels;

namespace MonitoringSystem.Maui.Platforms.Android.Services;

/// <summary>
/// VPN сервіс для перехоплення мережевого трафіку на Android.
///
/// Як це працює:
/// 1. Android VpnService API дозволяє створити "локальний VPN" — 
///    віртуальний мережевий інтерфейс (tun0) прямо на пристрої.
/// 2. Весь IP-трафік телефону замість того щоб іти напряму в інтернет,
///    спочатку проходить через наш додаток.
/// 3. Ми читаємо IP-пакети, витягуємо з них адреси призначення (IP + порт),
///    дізнаємось домени через зворотний DNS, і надсилаємо на сервер моніторингу.
/// 4. Після аналізу пакет пересилається далі через реальне з'єднання.
///
/// ВАЖЛИВО: Це НЕ шифрує і НЕ змінює трафік — тільки читає заголовки пакетів.
/// Вміст HTTPS пакетів (сам текст) НЕ доступний без MITM — тільки домен.
/// </summary>
[Service(
    Name = "monitoringsystem.maui.MonitoringVpnService",
    Permission = "android.permission.BIND_VPN_SERVICE",
    Exported = false)]
[IntentFilter(new[] { "android.net.VpnService" })]
public class MonitoringVpnService : VpnService
{
    // Канал для читання/запису IP пакетів через tun інтерфейс
    private ParcelFileDescriptor? _vpnInterface;
    private Thread? _monitorThread;
    private volatile bool _running;

    // Callback: коли виявлено новий домен — повідомляємо AgentViewModel
    public static Action<string>? OnDomainDetected;
    // Callback: коли з'явився новий IP
    public static Action<string>? OnIpDetected;

    public override IBinder? OnBind(Intent? intent) => null;

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        if (intent?.Action == "STOP")
        {
            StopVpn();
            return StartCommandResult.NotSticky;
        }

        StartVpn();
        return StartCommandResult.Sticky; // Android перезапустить сервіс якщо він упаде
    }

    private void StartVpn()
    {
        try
        {
            // ── Будуємо VPN інтерфейс ───────────────────────────────────
            // Builder налаштовує параметри віртуального мережевого інтерфейсу
            var builder = new Builder(this)
                // Локальна IP адреса нашого VPN тунелю
                .AddAddress("10.0.0.1", 32)
                // Маршрут: весь IPv4 трафік (0.0.0.0/0) іде через наш тунель
                .AddRoute("0.0.0.0", 0)
                // DNS сервер — використовуємо Google DNS
                .AddDnsServer("8.8.8.8")
                .SetSession("MonitoringSystem VPN")
                // MTU — максимальний розмір пакету (стандарт для Ethernet)
                .SetMtu(1500);

            // Виключаємо наш власний додаток з VPN — інакше запити до API
            // теж будуть перехоплені і виникне нескінченна петля
            try { builder.AddDisallowedApplication(PackageName!); } catch { }

            // Встановлюємо VPN — після цього весь трафік іде через нас
            _vpnInterface = builder.Establish();

            if (_vpnInterface == null)
            {
                // Null означає що юзер не дав дозвіл на VPN
                return;
            }

            // ── Запускаємо читання пакетів в окремому потоці ────────────
            _running = true;
            _monitorThread = new Thread(MonitorTraffic) { IsBackground = true };
            _monitorThread.Start();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[VPN] Start error: {ex.Message}");
        }
    }

    /// <summary>
    /// Головний цикл читання IP пакетів.
    ///
    /// IP пакет складається з:
    /// - Байт 0: версія IP (4 або 6) + довжина заголовку
    /// - Байт 9: протокол (6=TCP, 17=UDP, 1=ICMP)
    /// - Байти 12-15: IP адреса відправника (source)
    /// - Байти 16-19: IP адреса призначення (destination)
    /// - Байти 20+: заголовок TCP/UDP (порти) + дані
    ///
    /// Для TCP пакету:
    /// - Байти 20-21: порт відправника
    /// - Байти 22-23: порт призначення
    /// </summary>
    private void MonitorTraffic()
    {
        var fd = _vpnInterface!.FileDescriptor;

        // FileInputStream читає IP пакети з tun інтерфейсу
        using var inputStream = new FileInputStream(fd);
        // FileOutputStream записує пакети назад (пересилаємо їх далі)
        using var outputStream = new FileOutputStream(fd);

        // Буфер для одного IP пакету (макс розмір MTU = 1500 байт)
        var buffer = new byte[32767];
        var seenIPs = new HashSet<string>();

        while (_running)
        {
            try
            {
                // Читаємо один IP пакет з тунелю
                int length = inputStream.Read(buffer);
                if (length <= 0) continue;

                // ── Парсимо IP заголовок ─────────────────────────────────
                // Перший байт: старші 4 біти = версія IP
                int version = (buffer[0] >> 4) & 0xF;

                if (version == 4 && length >= 20)
                {
                    // IPv4: читаємо IP адресу призначення (байти 16-19)
                    string destIP = $"{buffer[16] & 0xFF}.{buffer[17] & 0xFF}.{buffer[18] & 0xFF}.{buffer[19] & 0xFF}";

                    // Протокол: байт 9 (6=TCP, 17=UDP)
                    int protocol = buffer[9] & 0xFF;

                    if (protocol == 6 && length >= 24) // TCP
                    {
                        // Порт призначення: байти 22-23 (big-endian)
                        int destPort = ((buffer[22] & 0xFF) << 8) | (buffer[23] & 0xFF);

                        // Моніторимо тільки HTTP (80) і HTTPS (443) трафік
                        if ((destPort == 80 || destPort == 443) &&
                            !destIP.StartsWith("10.") &&
                            !destIP.StartsWith("192.168.") &&
                            !destIP.StartsWith("127."))
                        {
                            if (seenIPs.Add(destIP)) // Add повертає true якщо елемент новий
                            {
                                // Повідомляємо AgentViewModel про новий IP
                                OnIpDetected?.Invoke(destIP);

                                // Асинхронно резолвимо домен (зворотний DNS lookup)
                                _ = Task.Run(async () =>
                                {
                                    try
                                    {
                                        var hostEntry = await System.Net.Dns.GetHostEntryAsync(destIP);
                                        if (!string.IsNullOrEmpty(hostEntry.HostName))
                                            OnDomainDetected?.Invoke(hostEntry.HostName);
                                    }
                                    catch { } // DNS може не резолвитись — ігноруємо
                                });
                            }
                        }
                    }
                }

                // Пересилаємо пакет далі — без цього інтернет перестане працювати
                outputStream.Write(buffer, 0, length);
            }
            catch (Exception ex) when (_running)
            {
                System.Diagnostics.Debug.WriteLine($"[VPN] Packet error: {ex.Message}");
                Thread.Sleep(10); // коротка пауза при помилці
            }
        }
    }

    private void StopVpn()
    {
        _running = false;
        try { _vpnInterface?.Close(); } catch { }
        _vpnInterface = null;
        StopSelf();
    }

    public override void OnDestroy()
    {
        StopVpn();
        base.OnDestroy();
    }
}
#endif
