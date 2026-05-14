using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR.Client;

/// <summary>
/// MonitoringSystem Console Agent — Linux / Windows
///
/// Запускається у терміналі або як systemd сервіс на Linux.
/// Збирає: процеси, мережеві з'єднання, Chrome History.
/// При порушенні: виводить в термінал + SignalR сповіщення.
/// </summary>

Console.OutputEncoding = System.Text.Encoding.UTF8;
Console.Title = "Monitoring System Agent";

// ── Читання конфігурації ──────────────────────────────────────────────
var apiUrl = Environment.GetEnvironmentVariable("MONITORING_API_URL")
    ?? ReadConfig("ApiUrl")
    ?? "http://localhost:5000";

Console.ForegroundColor = ConsoleColor.Cyan;
Console.WriteLine("╔══════════════════════════════════════╗");
Console.WriteLine("║     🛡️  Система моніторингу          ║");
Console.WriteLine("║     Корпоративний агент v1.0         ║");
Console.WriteLine($"║     API: {apiUrl,-27}║");
Console.WriteLine("╚══════════════════════════════════════╝");
Console.ResetColor();
Console.WriteLine();

// ── Логін ─────────────────────────────────────────────────────────────
string? token = null;
string? username = null;

while (token == null)
{
    Console.Write("Логін: ");
    username = Console.ReadLine()?.Trim();

    Console.Write("Пароль: ");
    var password = ReadPassword();
    Console.WriteLine();

    token = await LoginAsync(apiUrl, username!, password);
    if (token == null)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine("✗ Невірний логін або пароль. Спробуй ще раз.");
        Console.ResetColor();
    }
}

Console.ForegroundColor = ConsoleColor.Green;
Console.WriteLine($"✓ Авторизовано як: {username}");
Console.ResetColor();

// ── HTTP клієнт ───────────────────────────────────────────────────────
using var http = new HttpClient { BaseAddress = new Uri(apiUrl + "/") };
http.DefaultRequestHeaders.Authorization =
    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

// ── Реєстрація пристрою ───────────────────────────────────────────────
var hostname = Dns.GetHostName();
var platform = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "Windows" : "Linux";
var osVer    = Environment.OSVersion.VersionString;
var hwId     = GetOrCreateHardwareId();

await RegisterDeviceAsync(http, hwId, hostname, platform, osVer);
Console.WriteLine($"✓ Пристрій зареєстровано: {hostname} ({platform})");

// ── Відновлення сесії (IsOnline = true) ───────────────────────────────
await http.PostAsJsonAsync("api/auth/restore-session", new { DeviceId = hwId });

// ── SignalR ───────────────────────────────────────────────────────────
var hub = new HubConnectionBuilder()
    .WithUrl($"{apiUrl}/hubs/activity", opts =>
        opts.AccessTokenProvider = () => Task.FromResult<string?>(token))
    .WithAutomaticReconnect()
    .Build();

hub.On<string, string>("AdminAlert", (title, msg) =>
{
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine();
    Console.WriteLine("╔═══════════════════════════════════════╗");
    Console.WriteLine($"║  📢 {title,-35}║");
    Console.WriteLine($"║  {msg,-38}║");
    Console.WriteLine("╚═══════════════════════════════════════╝");
    Console.ResetColor();

    // Звуковий сигнал (термінал)
    Console.Beep();
});

hub.On("ForceLogout", () =>
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine("\n⛔ Пристрій видалено адміністратором. Завершення роботи...");
    Console.ResetColor();
    Environment.Exit(0);
});

hub.Reconnected   += _ => { Log("🟢 SignalR підключено"); return Task.CompletedTask; };
hub.Reconnecting  += _ => { Log("🟡 SignalR перепідключення..."); return Task.CompletedTask; };

try { await hub.StartAsync(); Log("🟢 SignalR підключено"); }
catch { Log("🔴 SignalR недоступний (сповіщення від адміна не надходитимуть)"); }

// ── Завантаження правил ───────────────────────────────────────────────
var blockedProcesses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
await LoadBlockedProcesses(http, blockedProcesses);
Log($"📋 Правил процесів: {blockedProcesses.Count}");

// ── Основний цикл ─────────────────────────────────────────────────────
Console.ForegroundColor = ConsoleColor.Green;
Console.WriteLine("\n✓ Моніторинг активний. Натисни Ctrl+C для зупинки.\n");
Console.ResetColor();

var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

var seenConnections = new HashSet<string>();
int logsSent = 0, violations = 0, cycle = 0;

while (!cts.IsCancellationRequested)
{
    cycle++;
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Цикл #{cycle} — збір даних...");

    // Процеси
    var procs = Process.GetProcesses()
        .Select(p => { try { return p.ProcessName.ToLower(); } catch { return ""; } })
        .Where(n => n.Length > 0).ToHashSet();

    foreach (var blocked in blockedProcesses)
    {
        if (procs.Any(p => p.Contains(blocked, StringComparison.OrdinalIgnoreCase)))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"  ⚠️  Заблокований процес: {blocked}");
            Console.ResetColor();
            var r = await LogActivity(http, "ProcessStarted", $"Процес: {blocked}", null, hwId);
            logsSent++;
            if (r) { violations++; ShowViolation($"Процес заблоковано: {blocked}"); }
        }
    }

    // Мережа
    try
    {
        var conns = IPGlobalProperties.GetIPGlobalProperties()
            .GetActiveTcpConnections()
            .Where(c => c.State == TcpState.Established
                     && !IPAddress.IsLoopback(c.RemoteEndPoint.Address)
                     && c.RemoteEndPoint.Port is 80 or 443)
            .Select(c => c.RemoteEndPoint.Address.ToString())
            .Distinct().ToList();

        foreach (var ip in conns.Except(seenConnections).Take(5))
        {
            seenConnections.Add(ip);
            Console.WriteLine($"  🌐 З'єднання: {ip}");
            await LogActivity(http, "NetworkConnection", $"IP: {ip}", null, hwId);
            logsSent++;
        }
    }
    catch { }

    // Chrome History (Linux)
    var (extraLogs, extraViols) = await CollectBrowserUrls(http, hwId);
    logsSent += extraLogs;
    violations += extraViols;

    Console.ForegroundColor = ConsoleColor.DarkGray;
    Console.WriteLine($"  📊 Надіслано: {logsSent} | Порушень: {violations}");
    Console.ResetColor();

    await Task.Delay(TimeSpan.FromSeconds(30), cts.Token)
              .ContinueWith(_ => { });
}

// Logout
Console.WriteLine("\nЗавершення роботи...");
await http.PostAsync("api/auth/logout", null);
if (hub.State == HubConnectionState.Connected) await hub.StopAsync();
Console.WriteLine("✓ Вихід виконано.");

// ── Функції ───────────────────────────────────────────────────────────

static void Log(string msg) =>
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {msg}");

static void ShowViolation(string msg)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine("┌─────────────────────────────────────┐");
    Console.WriteLine($"│ ⚠️  ПОРУШЕННЯ: {msg,-23}│");
    Console.WriteLine("└─────────────────────────────────────┘");
    Console.ResetColor();
    try { Console.Beep(); } catch { }
}

static string ReadPassword()
{
    var pwd = "";
    ConsoleKeyInfo key;
    while ((key = Console.ReadKey(true)).Key != ConsoleKey.Enter)
    {
        if (key.Key == ConsoleKey.Backspace && pwd.Length > 0)
        { pwd = pwd[..^1]; Console.Write("\b \b"); }
        else if (key.Key != ConsoleKey.Backspace)
        { pwd += key.KeyChar; Console.Write("*"); }
    }
    return pwd;
}

static async Task<string?> LoginAsync(string apiUrl, string username, string password)
{
    try
    {
        using var http = new HttpClient();
        var resp = await http.PostAsJsonAsync($"{apiUrl}/api/auth/login",
            new { username, password });
        if (!resp.IsSuccessStatusCode) return null;

        var options = new System.Text.Json.JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true  // "token" і "Token" — однаково
        };
        var data = await resp.Content
            .ReadFromJsonAsync<System.Text.Json.JsonElement>(options);

        // API повертає поле "Token" (PascalCase)
        if (data.TryGetProperty("Token", out var t)) return t.GetString();
        if (data.TryGetProperty("token", out var t2)) return t2.GetString();
        return null;
    }
    catch { return null; }
}

static async Task RegisterDeviceAsync(HttpClient http, string hwId,
    string name, string platform, string osVer)
{
    try
    {
        await http.PostAsJsonAsync("api/devices/register",
            new { hardwareId = hwId, suggestedName = name,
                  platform = platform, osVersion = osVer });
    }
    catch { }
}

static async Task<bool> LogActivity(HttpClient http, string action,
    string details, string? url, string deviceId)
{
    try
    {
        var resp = await http.PostAsJsonAsync("api/activity/log",
            new { action, details, url, deviceId });
        if (resp.StatusCode == System.Net.HttpStatusCode.Forbidden) return true;
        return false;
    }
    catch { return false; }
}

static async Task LoadBlockedProcesses(HttpClient http, HashSet<string> set)
{
    try
    {
        var rules = await http.GetFromJsonAsync<JsonElement[]>("api/rules");
        if (rules == null) return;
        foreach (var r in rules)
        {
            if (r.GetProperty("ruleType").GetString() == "BlockedProcess"
             && r.GetProperty("isActive").GetBoolean())
                set.Add(r.GetProperty("value").GetString() ?? "");
        }
    }
    catch { }
}

static async Task<(int logs, int viols)> CollectBrowserUrls(HttpClient http, string hwId)
{
    int logsSent = 0, violations = 0;
    // Linux Chrome history
    var histPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".config", "google-chrome", "Default", "History");

    if (!File.Exists(histPath)) return (0, 0);

    try
    {
        var tmp = Path.GetTempFileName();
        File.Copy(histPath, tmp, true);
        var text = await File.ReadAllTextAsync(tmp);
        File.Delete(tmp);

        var urls = System.Text.RegularExpressions.Regex
            .Matches(text, @"https?://[a-zA-Z0-9\-._~:/?#\[\]@!$&()*+,;=%]{8,200}")
            .Select(m => m.Value)
            .Where(u => !u.Contains("google.com/complete")
                     && !u.Contains("accounts.google.com")
                     && !u.Contains("localhost"))
            .Select(u => {
                try { return new Uri(u).Host.Replace("www.", ""); } catch { return ""; }
            })
            .Where(d => d.Length > 3)
            .Distinct().Take(10).ToList();

        foreach (var domain in urls)
        {
            var blocked = await LogActivity(http, "WebNavigation",
                $"Активна вкладка: {domain}", $"https://{domain}", hwId);
            logsSent++;
            if (blocked)
            {
                violations++;
                ShowViolation($"Заблокований сайт: {domain}");
            }
        }
    }
    catch { }
    return (logsSent, violations);
}

static string GetOrCreateHardwareId()
{
    var dir  = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "MonitoringSystem");
    var file = Path.Combine(dir, "device.id");
    Directory.CreateDirectory(dir);
    if (File.Exists(file)) return File.ReadAllText(file).Trim();
    var id = Guid.NewGuid().ToString();
    File.WriteAllText(file, id);
    return id;
}

static string? ReadConfig(string key)
{
    try
    {
        var cfg = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        if (!File.Exists(cfg)) return null;
        var json = File.ReadAllText(cfg);
        var m = System.Text.RegularExpressions.Regex
            .Match(json, $"\"{key}\"\\s*:\\s*\"([^\"]+)\"");
        return m.Success ? m.Groups[1].Value : null;
    }
    catch { return null; }
}
