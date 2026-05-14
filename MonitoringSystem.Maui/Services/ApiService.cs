using System.Net.Http.Json;
using System.Runtime.InteropServices;
using MonitoringSystem.Maui.Models;

namespace MonitoringSystem.Maui.Services;

public interface IApiService
{
    void SetToken(string token);
    Task<LoginResult> LoginAsync(string username, string password);
    Task LogoutAsync();
    Task<List<UserModel>> GetUsersAsync();
    Task<List<UserModel>> GetAllUsersFullAsync();
    Task<bool> CreateUserAsync(string username, string password,
        string role, string department);
    Task<bool> ChangeUserRoleAsync(string userId, string role);
    Task<bool> DeleteUserAsync(string userId);
    Task<List<DeviceModel>> GetMyDevicesAsync();
    Task<List<DeviceModel>> GetAllDevicesAsync();
    Task<bool> RenameDeviceAsync(string deviceId, string newName);
    Task<string?> RegisterDeviceAsync(string hardwareId = "", string suggestedName = "",
        string platform = "", string osVersion = "");
    Task<List<RuleModel>> GetRulesAsync();
    Task<bool> CreateRuleAsync(RuleModel rule);
    Task<bool> DeleteRuleAsync(string ruleId);
    Task<ActivityResult> LogActivityAsync(string action, string details,
        string? url = null);
    Task RestoreSessionAsync();
}

public class ApiService : IApiService
{
    private readonly HttpClient _http;

    // URL API залежить від платформи:
    //   Android emulator → 10.0.2.2 (localhost хост-машини)
    //   Windows / Linux  → localhost або env змінна MONITORING_API_URL
    private static string GetBaseUrl()
    {
        // Пріоритет: 1) збережений користувачем URL, 2) env, 3) appsettings.json, 4) дефолт
        var customUrl = StorageHelper.GetSync("custom_api_url");
        if (!string.IsNullOrEmpty(customUrl)) return customUrl;

        var envUrl = Environment.GetEnvironmentVariable("MONITORING_API_URL");
        if (!string.IsNullOrEmpty(envUrl)) return envUrl;

        try
        {
            var cfgFile = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
            if (File.Exists(cfgFile))
            {
                var json = File.ReadAllText(cfgFile);
                var match = System.Text.RegularExpressions.Regex
                    .Match(json, "\"ApiUrl\"\\s*:\\s*\"([^\"]+)\"");
                if (match.Success) return match.Groups[1].Value;
            }
        }
        catch { }

#if ANDROID
        return "http://10.0.2.2:5000";
#else
        return "http://localhost:5000";
#endif
    }

    public ApiService()
    {
        _http = new HttpClient
        {
            BaseAddress = new Uri(GetBaseUrl()),
            Timeout = TimeSpan.FromSeconds(15)
        };
    }

    public void SetToken(string token)
        => _http.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

    // ── Auth ──────────────────────────────────────────────────────────────

    public async Task<LoginResult> LoginAsync(string username, string password)
    {
        try
        {
            var response = await _http.PostAsJsonAsync("api/auth/login",
                new { username, password });

            if (!response.IsSuccessStatusCode)
            {
                var err = await response.Content
                    .ReadFromJsonAsync<Dictionary<string, string>>();
                return new LoginResult
                {
                    Success = false,
                    Message = err?.GetValueOrDefault("message") ?? "Помилка входу"
                };
            }

            var data     = await response.Content
                .ReadFromJsonAsync<Dictionary<string, object>>();
            var token    = data?["Token"]?.ToString() ?? "";
            var userId   = data?["UserId"]?.ToString() ?? "";
            var uname    = data?["Username"]?.ToString() ?? "";
            var role     = data?["Role"]?.ToString() ?? "";

            SetToken(token);
            await SaveToStorage("auth_token", token);
            await SaveToStorage("user_id",    userId);
            await SaveToStorage("username",   uname);
            await SaveToStorage("role",       role);

            // Автоматична реєстрація пристрою одразу після логіну
            var (hwId, name, platform, osVer) = GetDeviceInfo();
            var deviceId = await RegisterDeviceAsync(hwId, name, platform, osVer);
            if (!string.IsNullOrEmpty(deviceId))
                await SaveToStorage("device_id", deviceId);

            return new LoginResult
            {
                Success = true, Token = token,
                UserId  = userId, Username = uname, Role = role
            };
        }
        catch (Exception ex)
        {
            return new LoginResult
            {
                Success = false,
                Message = $"Немає зв'язку з сервером: {ex.Message}"
            };
        }
    }

    public async Task LogoutAsync()
    {
        try { await _http.PostAsync("api/auth/logout", null); }
        catch { /* ignore */ }
    }

    // ── Users ─────────────────────────────────────────────────────────────

    public async Task<List<UserModel>> GetUsersAsync()
    {
        try
        {
            return await _http.GetFromJsonAsync<List<UserModel>>(
                "api/activity/users") ?? new();
        }
        catch { return new(); }
    }

    public async Task<List<UserModel>> GetAllUsersFullAsync()
    {
        try
        {
            return await _http.GetFromJsonAsync<List<UserModel>>(
                "api/users") ?? new();
        }
        catch { return new(); }
    }

    public async Task<bool> CreateUserAsync(
        string username, string password, string role, string department)
    {
        try
        {
            var r = await _http.PostAsJsonAsync("api/users",
                new { username, password, role, department });
            return r.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public async Task<bool> ChangeUserRoleAsync(string userId, string role)
    {
        try
        {
            var r = await _http.PatchAsJsonAsync(
                $"api/users/{userId}/role", new { role });
            return r.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public async Task<bool> DeleteUserAsync(string userId)
    {
        try
        {
            var r = await _http.DeleteAsync($"api/users/{userId}");
            return r.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    // ── Devices ───────────────────────────────────────────────────────────

    public async Task<List<DeviceModel>> GetMyDevicesAsync()
    {
        try
        {
            return await _http.GetFromJsonAsync<List<DeviceModel>>(
                "api/devices") ?? new();
        }
        catch { return new(); }
    }

    public async Task<List<DeviceModel>> GetAllDevicesAsync()
    {
        try
        {
            return await _http.GetFromJsonAsync<List<DeviceModel>>(
                "api/devices/all") ?? new();
        }
        catch { return new(); }
    }

    public async Task<bool> RenameDeviceAsync(string deviceId, string newName)
    {
        try
        {
            var r = await _http.PatchAsJsonAsync(
                $"api/devices/{deviceId}/rename", new { Name = newName });
            return r.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public async Task<string?> RegisterDeviceAsync(
        string hardwareId = "", string suggestedName = "",
        string platform = "", string osVersion = "")
    {
        try
        {
            // Якщо параметри порожні — визначаємо автоматично через GetDeviceInfo()
            var (hwId, name, plat, osVer) = GetDeviceInfo();
            if (!string.IsNullOrEmpty(hardwareId)) hwId = hardwareId;
            if (!string.IsNullOrEmpty(suggestedName)) name = suggestedName;
            if (!string.IsNullOrEmpty(platform)) plat = platform;
            if (!string.IsNullOrEmpty(osVersion)) osVer = osVersion;

            var r = await _http.PostAsJsonAsync("api/devices/register",
                new { hardwareId = hwId, suggestedName = name,
                      platform = plat, osVersion = osVer });
            if (!r.IsSuccessStatusCode) return null;
            var data = await r.Content
                .ReadFromJsonAsync<Dictionary<string, string>>();
            return data?.GetValueOrDefault("deviceId");
        }
        catch { return null; }
    }

    // ── DeviceInfo — визначення платформи ─────────────────────────────────

    /// <summary>
    /// Повертає (hardwareId, displayName, platform, osVersion) для поточного пристрою.
    /// Підтримує Windows, Linux, Android. macOS/iOS — резервний варіант.
    /// </summary>
    private static (string hwId, string name, string platform, string osVer)
        GetDeviceInfo()
    {
        string platform, osVer, name;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            platform = "Linux";
            osVer    = GetLinuxOsVersion();
            name     = GetHostname();
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            platform = "Windows";
            osVer    = Environment.OSVersion.VersionString;
            name     = Environment.MachineName;
        }
        else
        {
            // Android / iOS — MAUI DeviceInfo
            platform = DeviceInfo.Platform.ToString();
            osVer    = DeviceInfo.VersionString;
            name     = DeviceInfo.Name;
        }

        return (GetOrCreateHardwareId(), name, platform, osVer);
    }

    private static string GetLinuxOsVersion()
    {
        try
        {
            if (File.Exists("/etc/os-release"))
            {
                var pretty = File.ReadAllLines("/etc/os-release")
                    .FirstOrDefault(l => l.StartsWith("PRETTY_NAME="));
                if (pretty != null)
                    return pretty.Split('=')[1].Trim('"');
            }
            return RuntimeInformation.OSDescription;
        }
        catch { return "Linux"; }
    }

    private static string GetHostname()
    {
        try { return System.Net.Dns.GetHostName(); }
        catch { return "Unknown Host"; }
    }

    /// <summary>
    /// Унікальний ID пристрою. Зберігається між запусками.
    /// Linux/Windows → файл у AppData. Android/iOS → SecureStorage.
    /// </summary>
    private static string GetOrCreateHardwareId()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ||
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var dir  = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "MonitoringSystem");
            var file = Path.Combine(dir, "device.id");

            if (File.Exists(file))
            {
                var existing = File.ReadAllText(file).Trim();
                if (!string.IsNullOrEmpty(existing)) return existing;
            }

            Directory.CreateDirectory(dir);
            var newId = Guid.NewGuid().ToString();
            File.WriteAllText(file, newId);
            return newId;
        }

        // Android / iOS
        var saved = SecureStorage.GetAsync("hardware_id").GetAwaiter().GetResult();
        if (!string.IsNullOrEmpty(saved)) return saved;

        var id = Guid.NewGuid().ToString();
        SecureStorage.SetAsync("hardware_id", id).GetAwaiter().GetResult();
        return id;
    }

    // ── Rules ─────────────────────────────────────────────────────────────

    public async Task<List<RuleModel>> GetRulesAsync()
    {
        try
        {
            return await _http.GetFromJsonAsync<List<RuleModel>>(
                "api/rules") ?? new();
        }
        catch { return new(); }
    }

    public async Task<bool> CreateRuleAsync(RuleModel rule)
    {
        try
        {
            var r = await _http.PostAsJsonAsync("api/rules", new
            {
                rule.Name, rule.RuleType, rule.Value,
                rule.Severity, rule.Action, rule.IsActive, rule.Description
            });
            return r.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public async Task<bool> DeleteRuleAsync(string ruleId)
    {
        try
        {
            var r = await _http.DeleteAsync($"api/rules/{ruleId}");
            return r.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    // ── Activity ──────────────────────────────────────────────────────────

    /// <summary>
    /// Відновлює онлайн-сесію без повторного логіну.
    /// Викликається при автологіні щоб сервер знав що юзер онлайн.
    /// </summary>
    public async Task RestoreSessionAsync()
    {
        try
        {
            var deviceId = await GetSavedDeviceId();
            var request  = new HttpRequestMessage(HttpMethod.Post, "api/auth/restore-session")
            {
                Content = JsonContent.Create(new { DeviceId = deviceId })
            };
            await _http.SendAsync(request);
        }
        catch { /* ігноруємо — відновиться при наступному запуску */ }
    }

    public async Task<ActivityResult> LogActivityAsync(
        string action, string details, string? url = null)
    {
        try
        {
            var deviceId = await GetSavedDeviceId();
            var request  = new HttpRequestMessage(HttpMethod.Post, "api/activity/log")
            {
                Content = JsonContent.Create(new { action, details, url })
            };
            request.Headers.Add("X-Device-Id", deviceId);

            var response = await _http.SendAsync(request);

            if ((int)response.StatusCode == 403)
            {
                var data = await response.Content
                    .ReadFromJsonAsync<Dictionary<string, object>>();
                return new ActivityResult
                {
                    IsBlocked = true,
                    Message   = data?.GetValueOrDefault("message")?.ToString()
                                ?? "Дію заблоковано адміністратором"
                };
            }
            return new ActivityResult { IsBlocked = false };
        }
        catch { return new ActivityResult { IsBlocked = false }; }
    }

    // ── Storage helpers ───────────────────────────────────────────────────

    /// Зберігає значення: SecureStorage на мобільних, файл на Linux/Windows.
    private static async Task SaveToStorage(string key, string value)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ||
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var dir  = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "MonitoringSystem");
            Directory.CreateDirectory(dir);
            await File.WriteAllTextAsync(Path.Combine(dir, $"{key}.dat"), value);
            return;
        }
        await SecureStorage.SetAsync(key, value);
    }

    private static async Task<string> GetSavedDeviceId()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ||
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var file = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "MonitoringSystem", "device_id.dat");
            return File.Exists(file) ? (await File.ReadAllTextAsync(file)).Trim() : "";
        }
        return await SecureStorage.GetAsync("device_id") ?? "";
    }
}
