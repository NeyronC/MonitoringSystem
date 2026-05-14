using System.Runtime.InteropServices;

namespace MonitoringSystem.Maui.Services;

/// <summary>
/// Хелпер для зберігання даних між сесіями.
/// Windows/Linux: файли у AppData\MonitoringSystem\.
/// Підхід через файли надійніший ніж SecureStorage на desktop платформах.
/// </summary>
public static class StorageHelper
{
    private static string Dir
    {
        get
        {
#if ANDROID
            // Android: використовуємо внутрішнє сховище додатку
            return Path.Combine(
                Android.App.Application.Context.FilesDir!.AbsolutePath,
                "MonitoringSystem");
#else
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "MonitoringSystem");
#endif
        }
    }

    public static async Task<string?> GetAsync(string key)
    {
        try
        {
            var file = Path.Combine(Dir, $"{key}.dat");
            return File.Exists(file)
                ? (await File.ReadAllTextAsync(file)).Trim()
                : null;
        }
        catch { return null; }
    }

    /// <summary>
    /// Синхронне читання для використання в не-async контексті.
    /// </summary>
    public static string? GetSync(string key)
    {
        try
        {
            var file = Path.Combine(Dir, $"{key}.dat");
            return File.Exists(file) ? File.ReadAllText(file).Trim() : null;
        }
        catch { return null; }
    }

    public static async Task SetAsync(string key, string value)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            await File.WriteAllTextAsync(Path.Combine(Dir, $"{key}.dat"), value);
        }
        catch { }
    }

    public static Task ClearAsync(string key)
    {
        try
        {
            var file = Path.Combine(Dir, $"{key}.dat");
            if (File.Exists(file)) File.Delete(file);
        }
        catch { }
        return Task.CompletedTask;
    }
}
