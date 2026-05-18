using MonitoringSystem.Maui.ViewModels;
using MonitoringSystem.Maui.Services;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Windows.Input;
using Microsoft.AspNetCore.SignalR.Client;
using MonitoringSystem.Maui.Services;

namespace MonitoringSystem.Maui.ViewModels;

/// <summary>
/// ViewModel агента моніторингу.
/// Запускає фоновий цикл збору даних (процеси, мережа, браузер),
/// підключається до SignalR, показує сповіщення при порушеннях.
/// </summary>
public class AgentViewModel : BaseViewModel
{
    private readonly IApiService _api;

    // ── Властивості UI ───────────────────────────────────────────────────
    private string _statusText       = "Ініціалізація...";
    private string _userInfo         = "";
    private string _connectionStatus = "● Підключення...";
    private string _statusColor      = "#374151";
    private int    _logsSentCount;
    private int    _violationsCount;
    private int    _cycleCount;
    private string _lastViolationText = "";
    private string _lastViolationTime = "";
    private bool   _hasLastViolation;
    private string _nextCycleText     = "";

    // Повідомлення від адміна
    private string _lastAdminMessage      = "";
    private string _lastAdminMessageTitle = "";
    private bool   _hasAdminMessage;

    public string StatusText          { get => _statusText;          set => SetProperty(ref _statusText, value); }
    public string UserInfo            { get => _userInfo;            set => SetProperty(ref _userInfo, value); }
    public string ConnectionStatus    { get => _connectionStatus;    set => SetProperty(ref _connectionStatus, value); }
    public string StatusColor         { get => _statusColor;         set => SetProperty(ref _statusColor, value); }
    public int    LogsSentCount       { get => _logsSentCount;       set => SetProperty(ref _logsSentCount, value); }
    public int    ViolationsCount     { get => _violationsCount;     set => SetProperty(ref _violationsCount, value); }
    public int    CycleCount          { get => _cycleCount;          set => SetProperty(ref _cycleCount, value); }
    public string LastViolationText   { get => _lastViolationText;   set => SetProperty(ref _lastViolationText, value); }
    public string LastViolationTime   { get => _lastViolationTime;   set => SetProperty(ref _lastViolationTime, value); }
    public bool   HasLastViolation    { get => _hasLastViolation;    set => SetProperty(ref _hasLastViolation, value); }
    public string NextCycleText       { get => _nextCycleText;       set => SetProperty(ref _nextCycleText, value); }
    public string LastAdminMessage      { get => _lastAdminMessage;      set => SetProperty(ref _lastAdminMessage, value); }

    // ── Банер порушення (замість блокуючого DisplayAlert) ─────────────────
    private bool   _showViolationBanner;
    private string _bannerTitle   = "";
    private string _bannerDetail  = "";
    private string _bannerColor   = "#7f1d1d";

    public bool   ShowViolationBanner { get => _showViolationBanner; set => SetProperty(ref _showViolationBanner, value); }
    public string BannerTitle         { get => _bannerTitle;         set => SetProperty(ref _bannerTitle, value); }
    public string BannerDetail        { get => _bannerDetail;        set => SetProperty(ref _bannerDetail, value); }
    public string BannerColor         { get => _bannerColor;         set => SetProperty(ref _bannerColor, value); }
    public ICommand DismissBannerCommand { get; }
    public string LastAdminMessageTitle { get => _lastAdminMessageTitle; set => SetProperty(ref _lastAdminMessageTitle, value); }
    public bool   HasAdminMessage       { get => _hasAdminMessage;       set => SetProperty(ref _hasAdminMessage, value); }

    // Лог останніх подій
    public ObservableCollection<EventLogEntry>  RecentEvents      { get; } = new();
    // Live дані для сторінки деталей
    public ObservableCollection<string>         ActiveTabs        { get; } = new();
    public ObservableCollection<string>         ActiveProcesses   { get; } = new();
    public ObservableCollection<string>         ActiveConnections { get; } = new();

    public ICommand LogoutCommand              { get; }
    public ICommand DismissAdminMessageCommand { get; }
    public ICommand OpenDetailsCommand         { get; }

    // ── Внутрішній стан ───────────────────────────────────────────────────
    private HubConnection?             _hub;
    private CancellationTokenSource?   _cts;
    private bool                       _monitoring;
    private readonly HashSet<string>   _seenProcesses   = new();
    private readonly HashSet<string>   _seenConnections  = new();
    // Список заблокованих імен процесів (завантажується з правил)
    private readonly HashSet<string>   _blockedProcessNames = new();
    // Кеш нещодавно відрепорчених подій (не дублюємо)
    private readonly HashSet<string>   _recentlyReported    = new();
    private readonly Dictionary<string, DateTime> _urlCacheExpiry = new();
    // Домени що були активні в попередньому циклі (для виявлення нових/закритих)
    private HashSet<string> _previousActiveDomains = new(StringComparer.OrdinalIgnoreCase);
    private System.Threading.Timer?    _countdownTimer;
    private int                        _secondsToNext = 30;

    public AgentViewModel(IApiService api)
    {
        _api = api;
        LogoutCommand              = new Command(async () => await LogoutAsync());
        OpenDetailsCommand         = new Command(async () =>
            await Shell.Current.GoToAsync("DetailsPage"));
        DismissBannerCommand = new Command(() =>
        {
            ShowViolationBanner = false;
            BannerTitle  = "";
            BannerDetail = "";
        });

        DismissAdminMessageCommand = new Command(() =>
        {
            HasAdminMessage = false;
            LastAdminMessage = "";
            LastAdminMessageTitle = "";
        });
    }

    /// <summary>
    /// Запуск моніторингу — викликається з AgentPage.OnAppearing.
    /// Ідемпотентний: повторний виклик нічого не робить.
    /// </summary>
    public async Task StartMonitoringAsync()
    {
        if (_monitoring) return;
        _monitoring = true;

        // Беремо токен зі статичного кешу (щойно після логіну) або з файлу
        var token    = LoginViewModel.LastToken
                    ?? await StorageHelper.GetAsync("auth_token") ?? "";
        var username = LoginViewModel.LastUsername
                    ?? await StorageHelper.GetAsync("username")   ?? "User";
        var role     = LoginViewModel.LastRole
                    ?? await StorageHelper.GetAsync("role")       ?? "";

        if (string.IsNullOrEmpty(token))
        {
            // Немає токену — повертаємо на логін
            await MainThread.InvokeOnMainThreadAsync(async () =>
                await Shell.Current.GoToAsync("//LoginPage", animate: false));
            _monitoring = false;
            return;
        }

        _api.SetToken(token);
        UserInfo   = $"{username}  •  {GetHostName()}";
        StatusText = "Моніторинг активний";

        // Завантажуємо список заблокованих процесів з правил
        await LoadBlockedProcessesAsync();

        // Логуємо відновлення сесії в активності
        await _api.LogActivityAsync("AgentHeartbeat", "Агент запущено");

        // SignalR підключення
        await ConnectSignalRAsync(token);

        // Таймер зворотнього відліку (оновлює UI кожну секунду)
        _countdownTimer = new System.Threading.Timer(_ =>
        {
            _secondsToNext--;
            if (_secondsToNext <= 0) _secondsToNext = 30;
            MainThread.BeginInvokeOnMainThread(() =>
                NextCycleText = $"Наступний збір через {_secondsToNext} с");
        }, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));

        // ── Android: налаштовуємо VPN моніторинг трафіку ─────────────────
        // Підписуємось на callbacks від VPN сервісу.
        // Коли VPN перехоплює IP пакет — він викликає ці дії.
        // Ми зберігаємо IP в _androidDetectedIPs для обробки в наступному циклі.
#if ANDROID
        MonitoringSystem.Maui.Platforms.Android.Services.MonitoringVpnService.OnIpDetected = ip =>
        {
            lock (_androidDetectedIPs)
                if (!_androidDetectedIPs.Contains(ip))
                    _androidDetectedIPs.Add(ip);
        };

        MonitoringSystem.Maui.Platforms.Android.Services.MonitoringVpnService.OnDomainDetected = domain =>
        {
            // Домен розрезолвлений з IP — логуємо як WebNavigation
            MainThread.BeginInvokeOnMainThread(() =>
                AddEvent("🌐", $"Домен: {domain}"));
            _ = _api.LogActivityAsync("WebNavigation", $"Домен: {domain}", $"https://{domain}");
        };

        // Перевіряємо дозвіл на UsageStats (статистика додатків)
        if (!MonitoringSystem.Maui.Platforms.Android.Services
            .UsageStatsHelper.HasUsagePermission(Android.App.Application.Context))
        {
            // Якщо дозволу немає — показуємо банер і відкриваємо налаштування
            MainThread.BeginInvokeOnMainThread(() =>
            {
                BannerColor = "#1e3a5f";
                BannerTitle = "📊 Потрібен дозвіл";
                BannerDetail = "Дозволь доступ до статистики додатків у налаштуваннях";
                ShowViolationBanner = true;
            });
            // Відкриваємо системний екран налаштувань через 1 секунду
            await Task.Delay(1000);
            MonitoringSystem.Maui.Platforms.Android.Services
                .UsageStatsHelper.OpenUsagePermissionSettings(Android.App.Application.Context);
        }

        // Запускаємо VPN сервіс через MainActivity.
        // MainActivity.RequestVpnPermission показує системний діалог якщо потрібно.
        // Коли юзер дасть дозвіл — OnVpnPermissionGranted викличе StartVpnService.

        // Підписуємось на callback від MainActivity
        MonitoringSystem.Maui.MainActivity.OnVpnPermissionGranted = StartVpnService;

        // Запитуємо дозвіл (або запускаємо одразу якщо дозвіл вже є)
        var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
        if (activity != null)
        {
            MonitoringSystem.Maui.MainActivity.RequestVpnPermission(activity);
        }
        else
        {
            // Activity недоступна — спробуємо запустити без діалогу
            StartVpnService();
        }
#endif

        // Фоновий цикл збору даних
        _cts = new CancellationTokenSource();
        _ = Task.Run(() => MonitoringLoopAsync(_cts.Token));
    }

    /// <summary>
    /// Основний цикл: кожні 30 секунд збирає процеси, мережу, браузер.
    /// </summary>
    private async Task MonitoringLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await CollectProcessesAsync();
                await CollectNetworkAsync();
                await CollectBrowserUrlsAsync();

                CycleCount++;
                _secondsToNext = 30;
            }
            catch (Exception ex)
            {
                AddEvent("❌", $"Помилка циклу: {ex.Message}");
            }

            await Task.Delay(TimeSpan.FromSeconds(30), ct)
                      .ContinueWith(_ => { });
        }
    }

    // ── Збір даних ────────────────────────────────────────────────────────

    private async Task CollectProcessesAsync()
    {
        try
        {
            var current = Process.GetProcesses()
                .Select(p => { try { return p.ProcessName.ToLower(); } catch { return ""; } })
                .Where(n => n.Length > 0)
                .Distinct()
                .ToHashSet();

            // Оновлюємо список відомих процесів
            _seenProcesses.UnionWith(current);

            // Оновлюємо публічну колекцію для UI (відсортовано A-Z, без дублів)
            MainThread.BeginInvokeOnMainThread(() =>
            {
                ActiveProcesses.Clear();
                foreach (var p in current.OrderBy(x => x))
                    ActiveProcesses.Add(p);
            });

            // Перевіряємо заблоковані процеси — сповіщаємо при КОЖНОМУ циклі поки процес живий
            foreach (var blocked in _blockedProcessNames)
            {
                var isRunning = current.Any(p =>
                    p.Contains(blocked, StringComparison.OrdinalIgnoreCase));

                if (isRunning)
                {
                    // Кожен цикл (30с) — нове порушення поки процес запущений
                    AddEvent("⚙️", $"Заблокований процес: {blocked}");

                    // Android: показуємо системне сповіщення в шторці.
                    // Це працює навіть якщо додаток згорнутий або екран заблокований.
#if ANDROID
                    MonitoringSystem.Maui.Platforms.Android.Services
                        .NotificationHelper.ShowBlockedAppNotification(
                            Android.App.Application.Context, blocked);
#endif

                    var r = await _api.LogActivityAsync("ProcessStarted",
                        $"Виявлено заблокований процес: {blocked}");
                    LogsSentCount++;
                    if (r.IsBlocked)
                        await ShowViolationAsync("Процес заблоковано", blocked, r.Message);
                }
            }
        }
        catch { }
    }

    private async Task CollectNetworkAsync()
    {
#if ANDROID
        // ── Android: збираємо IP адреси що були перехоплені VPN сервісом ──
        //
        // На Android IPGlobalProperties.GetActiveTcpConnections() не підтримується
        // (кидає NotSupportedException). Замість цього ми використовуємо MonitoringVpnService:
        // VPN сервіс перехоплює всі IP пакети і через статичний Action повідомляє нас.
        //
        // _androidDetectedIPs — список IP що зібрав VPN сервіс між циклами.
        // Ми беремо їх тут і очищуємо список для наступного циклу.

        List<string> androidConns;
        lock (_androidDetectedIPs)
        {
            androidConns = new List<string>(_androidDetectedIPs);
            _androidDetectedIPs.Clear();
        }

        // Додаємо також додатки з UsageStats (які додатки були активні)
        var recentApps = MonitoringSystem.Maui.Platforms.Android.Services
            .UsageStatsHelper.GetRecentApps(
                Android.App.Application.Context,
                seconds: 60); // додатки за останню хвилину

        // Оновлюємо UI список активних з'єднань
        MainThread.BeginInvokeOnMainThread(() =>
        {
            ActiveConnections.Clear();
            foreach (var ip in androidConns) ActiveConnections.Add(ip);
        });

        // Логуємо нові IP на сервер
        var newConns = androidConns.Except(_seenConnections).Take(5).ToList();
        foreach (var ip in newConns) _seenConnections.Add(ip);

        foreach (var ip in newConns)
        {
            AddEvent("🌐", $"З'єднання: {ip}");
            var r = await _api.LogActivityAsync("NetworkConnection", $"IP: {ip}");
            LogsSentCount++;
            if (r.IsBlocked) await ShowViolationAsync("З'єднання заблоковано", ip, r.Message);
        }

        // Логуємо активні додатки (UsageStats API)
        foreach (var app in recentApps.Except(_seenProcesses).Take(3))
        {
            _seenProcesses.Add(app);
            AddEvent("📱", $"Додаток: {app}");
            var r = await _api.LogActivityAsync("AppUsage", $"Активний додаток: {app}");
            LogsSentCount++;
            if (r.IsBlocked) await ShowViolationAsync("Додаток заблоковано", app, r.Message);
        }
#else
        // ── Windows / Linux: стандартний збір TCP з'єднань ─────────────────
        //
        // IPGlobalProperties.GetIPGlobalProperties() — клас з System.Net.NetworkInformation.
        // GetActiveTcpConnections() — повертає всі активні TCP з'єднання ОС.
        // Це ті самі дані що показує netstat -ano в командному рядку.
        try
        {
            var conns = IPGlobalProperties.GetIPGlobalProperties()
                .GetActiveTcpConnections()
                .Where(c => c.State == TcpState.Established &&
                            !IPAddress.IsLoopback(c.RemoteEndPoint.Address) &&
                            c.RemoteEndPoint.Port is 80 or 443)
                .Select(c => c.RemoteEndPoint.Address.ToString())
                .Distinct().ToList();

            MainThread.BeginInvokeOnMainThread(() =>
            {
                ActiveConnections.Clear();
                foreach (var ip in conns) ActiveConnections.Add(ip);
            });

            var newConns = conns.Except(_seenConnections).Take(5).ToList();
            foreach (var ip in newConns) _seenConnections.Add(ip);

            foreach (var ip in newConns)
            {
                AddEvent("🌐", $"З'єднання: {ip}");
                var r = await _api.LogActivityAsync("NetworkConnection", $"IP: {ip}");
                LogsSentCount++;
                if (r.IsBlocked) await ShowViolationAsync("З'єднання заблоковано", ip, r.Message);
            }
        }
        catch { }
#endif
    }

    // Список IP адрес що зібрав VPN сервіс (тільки Android).
    // lock(_androidDetectedIPs) — захист від гонки між VPN потоком і циклом моніторингу.
    private readonly List<string> _androidDetectedIPs = new();

    /// <summary>
    /// Отримує URL з Chrome History за останні N секунд.
    ///
    /// Підхід: Chrome History — SQLite база. Ми копіюємо файл (Chrome тримає lock),
    /// потім читаємо таблицю visits JOIN urls, фільтруємо по last_visit_time.
    ///
    /// Chrome зберігає час як мікросекунди від 1601-01-01 (Windows FILETIME).
    /// Формула: DateTimeOffset = new DateTime(1601,1,1) + TimeSpan.FromMicroseconds(t)
    ///
    /// Порівнюємо з _previousActiveDomains — показуємо тільки нові відкриті сайти.
    /// </summary>
    private async Task CollectBrowserUrlsAsync()
    {
        var historyPath = GetChromeHistoryPath();
        if (historyPath == null) return;

        var recentUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            // Копіюємо файл — Chrome тримає lock на оригіналі
            var tmp = Path.Combine(Path.GetTempPath(), $"ch_hist_{Guid.NewGuid():N}.db");
            File.Copy(historyPath, tmp, overwrite: true);

            try
            {
                // Читаємо SQLite через прямий парсинг сторінок (без залежностей)
                // Простіший підхід: шукаємо URL рядки в бінарному вмісті
                // але фільтруємо по часу через розмір файлу між циклами
                var bytes = await File.ReadAllBytesAsync(tmp);
                var text  = System.Text.Encoding.UTF8.GetString(bytes);

                // Витягуємо URL з History — обмежуємо 400 символами та беремо тільки домени
                var urlMatches = System.Text.RegularExpressions.Regex
                    .Matches(text, @"https?://[a-zA-Z0-9\-._~:/?#\[\]@!$&()*+,;=%]{8,200}");

                foreach (System.Text.RegularExpressions.Match m in urlMatches)
                {
                    var url = m.Value.TrimEnd();
                    if (IsValidActiveUrl(url))
                        recentUrls.Add(url);

                    // Обмеження: не більше 500 унікальних URL за читання
                    if (recentUrls.Count >= 500) break;
                }
            }
            finally
            {
                try { File.Delete(tmp); } catch { }
            }
        }
        catch { return; }

        // Витягуємо домени
        var currentDomains = recentUrls
            .Select(ExtractDomain)
            .Where(d => !string.IsNullOrEmpty(d))
            .Distinct()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Оновлюємо ActiveTabs для UI
        MainThread.BeginInvokeOnMainThread(() =>
        {
            ActiveTabs.Clear();
            foreach (var d in currentDomains.OrderBy(x => x))
                ActiveTabs.Add(d);
        });

        // Всі домени поточного циклу — перевіряємо кожен проти правил
        _previousActiveDomains = currentDomains;

        // Обмеження: перевіряємо максимум 20 доменів за цикл
        // History містить тисячі URL — не надсилаємо всі
        var domainsToCheck = currentDomains.Take(20).ToList();

        foreach (var domain in domainsToCheck)
        {
            var fullUrl = recentUrls.FirstOrDefault(u =>
                ExtractDomain(u).Equals(domain, StringComparison.OrdinalIgnoreCase)) ?? domain;

            // Логуємо тільки заблоковані (не всі підряд — це спамить лог)
            var r = await _api.LogActivityAsync("WebNavigation",
                $"Активна вкладка: {domain}", fullUrl);
            LogsSentCount++;

            if (r.IsBlocked)
            {
                AddEvent("⚠️", $"Заблокований: {domain}");

                // Android: сповіщення про заблокований сайт
#if ANDROID
                MonitoringSystem.Maui.Platforms.Android.Services
                    .NotificationHelper.ShowViolationNotification(
                        Android.App.Application.Context,
                        title: "🌐 Заблокований сайт",
                        message: $"Спроба відкрити заборонений сайт:\n{domain}");
#endif

                await ShowViolationAsync("Заблокований сайт відкрито",
                    domain, r.Message, "Warning");
            }
            else
            {
                // Дозволені домени — тільки лічильник, не додаємо в лог UI
                // щоб не спамити список подій
            }
        }
    }

    private static string? GetChromeHistoryPath()
    {
        // Chrome
        var paths = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Google", "Chrome", "User Data", "Default", "History"),
            // Chromium
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Chromium", "User Data", "Default", "History"),
            // Edge
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Microsoft", "Edge", "User Data", "Default", "History"),
            // Linux Chrome
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".config", "google-chrome", "Default", "History"),
        };
        return paths.FirstOrDefault(File.Exists);
    }

        /// <summary>
    /// Фільтрує URL — залишає тільки реальні сторінки, не внутрішні Chrome URL.
    /// </summary>
    private static bool IsValidActiveUrl(string url) =>
        !string.IsNullOrEmpty(url) &&
        url.Length > 10 &&
        !url.Contains("chrome://") &&
        !url.Contains("chrome-extension://") &&
        !url.Contains("devtools://") &&
        !url.Contains("edge://") &&
        !url.Contains("accounts.google.com") &&
        !url.Contains("google.com/complete") &&
        !url.Contains("google.com/search") &&
        !url.Contains("localhost") &&
        !url.Contains("127.0.0.1");

    private static string ExtractDomain(string url)
    {
        try
        {
            var uri = new Uri(url);
            return uri.Host.Replace("www.", "");
        }
        catch { return url.Length > 40 ? url[..40] : url; }
    }

    // ── Завантаження правил ───────────────────────────────────────────────

    /// <summary>
    /// Завантажує з API список правил типу BlockedProcess.
    /// Агент перевіряє ЛИШЕ ці процеси — не всі запущені програми.
    /// Це уникає spam сповіщень при кожному новому процесі.
    /// </summary>
    private async Task LoadBlockedProcessesAsync()
    {
        try
        {
            var rules = await _api.GetRulesAsync();
            _blockedProcessNames.Clear();
            foreach (var r in rules.Where(r => r.RuleType == "BlockedProcess" && r.IsActive))
                _blockedProcessNames.Add(r.Value.ToLowerInvariant());

            AddEvent("📋", $"Правил процесів: {_blockedProcessNames.Count}");
        }
        catch (Exception ex)
        {
            AddEvent("⚠️", $"Не вдалось завантажити правила: {ex.Message}");
        }
    }

    // ── SignalR ───────────────────────────────────────────────────────────

    /// <summary>
    /// Запускає VPN сервіс після отримання дозволу.
    /// Викликається або одразу (якщо дозвіл вже є) або після діалогу.
    /// </summary>
    private void StartVpnService()
    {
#if ANDROID
        try
        {
            var serviceIntent = new Android.Content.Intent(
                Android.App.Application.Context,
                typeof(MonitoringSystem.Maui.Platforms.Android.Services.MonitoringVpnService));
            serviceIntent.SetAction("START");
            Android.App.Application.Context.StartService(serviceIntent);
            MainThread.BeginInvokeOnMainThread(() =>
                AddEvent("🔒", "VPN моніторинг трафіку запущено"));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[VPN] Start error: {ex.Message}");
        }
#endif
    }

    private static string GetApiUrl()
    {
        // Той самий пріоритет що і ApiService — custom > env > appsettings > дефолт
        var custom = StorageHelper.GetSync("custom_api_url");
        if (!string.IsNullOrEmpty(custom)) return custom;

        var env = Environment.GetEnvironmentVariable("MONITORING_API_URL");
        if (!string.IsNullOrEmpty(env)) return env;

        try
        {
            var cfg = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
            if (File.Exists(cfg))
            {
                var json = File.ReadAllText(cfg);
                var m = System.Text.RegularExpressions.Regex
                    .Match(json, "\"ApiUrl\"\\s*:\\s*\"([^\"]+)\"");
                if (m.Success) return m.Groups[1].Value;
            }
        }
        catch { }

#if ANDROID
        return "http://10.0.2.2:5000";
#else
        return "http://localhost:5000";
#endif
    }

    private async Task ConnectSignalRAsync(string token)
    {
        try
        {
            // Читаємо URL з того ж місця що і ApiService (env → appsettings.json → localhost)
            var apiUrl = GetApiUrl();

            _hub = new HubConnectionBuilder()
                .WithUrl($"{apiUrl}/hubs/activity", opts =>
                    opts.AccessTokenProvider = () => Task.FromResult<string?>(token))
                .WithAutomaticReconnect(new[]
                {
                    TimeSpan.FromSeconds(5),
                    TimeSpan.FromSeconds(15),
                    TimeSpan.FromSeconds(30)
                })
                .Build();

            // Примусовий вихід (пристрій видалено адміном)
            _hub.On("ForceLogout", async () =>
            {
                await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    BannerColor         = "#450a0a";
                    BannerTitle         = "⛔ Пристрій видалено адміністратором";
                    BannerDetail        = "Для продовження роботи необхідно увійти знову.";
                    ShowViolationBanner = true;
                    BringWindowToFront();
                    await Task.Delay(3000);
                    await LogoutAsync();
                });
            });

            // Сповіщення від адміна (через SendAlertToUser або порушення правил)
            _hub.On<string, string>("AdminAlert", async (title, msg) =>
            {
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    LastAdminMessageTitle = title;
                    LastAdminMessage      = msg;
                    HasAdminMessage       = true;
                    AddEvent("📢", title);
                });

                // Android: показуємо системне сповіщення від адміна.
                // Це критично — якщо додаток згорнутий, юзер все одно побачить
                // сповіщення в шторці і зможе відреагувати.
#if ANDROID
                MonitoringSystem.Maui.Platforms.Android.Services
                    .NotificationHelper.ShowAdminAlert(
                        Android.App.Application.Context, title, msg);
#endif

                // Показуємо банер адміна + розгортаємо вікно
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    BannerColor         = "#0f172a";
                    BannerTitle         = $"📢 {title}";
                    BannerDetail        = msg;
                    ShowViolationBanner = true;
                    BringWindowToFront();
                });
            });

            _hub.Reconnecting  += _ => { SetConnected(false); return Task.CompletedTask; };
            _hub.Reconnected   += _ => { SetConnected(true);  return Task.CompletedTask; };
            _hub.Closed        += _ => { SetConnected(false); return Task.CompletedTask; };

            await _hub.StartAsync();
            SetConnected(true);
        }
        catch (Exception ex)
        {
            SetConnected(false);
            AddEvent("❌", $"SignalR: {ex.Message}");
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    /// <summary>
    /// Показує порушення: розгортає вікно агента і показує червоний банер.
    ///
    /// Алгоритм:
    /// 1. Оновлюємо властивості банеру (колір, текст) — UI оновлюється через Binding
    /// 2. Розгортаємо/активуємо вікно агента через Win32 API (тільки Windows)
    /// 3. Банер автозакривається через 20 секунд
    /// </summary>
    private async Task ShowViolationAsync(string title, string subject,
        string? detail, string severity = "Warning")
    {
        ViolationsCount++;
        LastViolationText = $"{title}: {subject}";
        LastViolationTime = DateTime.Now.ToString("HH:mm:ss");
        HasLastViolation  = true;
        AddEvent("⚠️", $"{title}: {subject}");

        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            BannerColor         = severity == "Critical" ? "#450a0a" : "#422006";
            BannerTitle         = $"⚠️ {title}: {subject}";
            BannerDetail        = detail ?? "";
            ShowViolationBanner = true;

            // Розгортаємо вікно агента поверх інших вікон
            BringWindowToFront();
        });

        // Автозакриття банеру через 20 секунд
        var capturedSubject = subject;
        _ = Task.Delay(TimeSpan.FromSeconds(20)).ContinueWith(_ =>
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (ShowViolationBanner && BannerTitle.Contains(capturedSubject))
                    ShowViolationBanner = false;
            }));
    }

    /// <summary>
    /// Розгортає та активує вікно агента через Win32 API.
    ///
    /// Стратегія пошуку вікна (від надійнішого до запасного):
    /// 1. По заголовку "Monitoring System Agent" (встановлено в Title XAML)
    /// 2. По заголовку "Monitoring System"
    /// 3. По імені процесу поточного додатку
    ///
    /// SW_RESTORE (9) — розгортає якщо мінімізовано, нічого не робить якщо вже видимо.
    /// SetForegroundWindow — переводить на передній план навіть якщо інше вікно активне.
    /// </summary>
    private static void BringWindowToFront()
    {
        try
        {
#if WINDOWS
            // Спроба 1: знайти по точному заголовку XAML Title
            var hwnd = FindWindowByTitle("Monitoring System Agent");

            // Спроба 2: знайти по короткому заголовку
            if (hwnd == IntPtr.Zero)
                hwnd = FindWindowByTitle("Monitoring System");

            // Спроба 3: знайти по імені процесу
            if (hwnd == IntPtr.Zero)
            {
                var proc = System.Diagnostics.Process.GetCurrentProcess();
                hwnd = proc.MainWindowHandle;
            }

            if (hwnd != IntPtr.Zero)
            {
                // Якщо мінімізовано — відновити
                ShowWindow(hwnd, 9);  // SW_RESTORE
                // Перевести на передній план
                SetForegroundWindow(hwnd);
                // Додатково: мигнути в таскбарі якщо не вдалось активувати
                var flashInfo = new FLASHWINFO
                {
                    cbSize    = (uint)System.Runtime.InteropServices.Marshal.SizeOf<FLASHWINFO>(),
                    hwnd      = hwnd,
                    dwFlags   = 3,  // FLASHW_ALL = мигати і вікно і таскбар
                    uCount    = 3,
                    dwTimeout = 0
                };
                FlashWindowEx(ref flashInfo);
            }
#endif
        }
        catch { /* ігноруємо помилки Win32 */ }
    }

#if WINDOWS
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? lpClassName, string lpWindowName);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool FlashWindowEx(ref FLASHWINFO pwfi);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct FLASHWINFO
    {
        public uint  cbSize;
        public IntPtr hwnd;
        public uint  dwFlags;
        public uint  uCount;
        public uint  dwTimeout;
    }

    private static IntPtr FindWindowByTitle(string title)
        => FindWindow(null, title);
#endif

    private void AddEvent(string icon, string text)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            RecentEvents.Insert(0, new EventLogEntry(icon, text,
                DateTime.Now.ToString("HH:mm:ss")));
            // Обмежуємо список 50 записами
            while (RecentEvents.Count > 50)
                RecentEvents.RemoveAt(RecentEvents.Count - 1);
        });
    }

    private void SetConnected(bool connected)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            ConnectionStatus = connected ? "● Online" : "● Offline";
            StatusColor      = connected ? "#16a34a"  : "#dc2626";
        });
    }

    private static string GetHostName()
    {
        try { return System.Net.Dns.GetHostName(); }
        catch { return "Unknown"; }
    }

    private async Task LogoutAsync()
    {
        _cts?.Cancel();
        _countdownTimer?.Dispose();
        if (_hub != null) await _hub.DisposeAsync();
        _monitoring = false;
        _seenProcesses.Clear();
        _seenConnections.Clear();
        RecentEvents.Clear();

        await _api.LogoutAsync();
        await StorageHelper.ClearAsync("auth_token");
        await StorageHelper.ClearAsync("username");
        await StorageHelper.ClearAsync("role");

        await Shell.Current.GoToAsync("//LoginPage");
    }
}

/// <summary>Запис у логу подій для відображення в CollectionView.</summary>
public record EventLogEntry(string Icon, string Text, string Time);
