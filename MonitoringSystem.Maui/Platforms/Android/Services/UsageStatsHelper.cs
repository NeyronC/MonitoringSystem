#if ANDROID
using Android.App;
using Android.App.Usage;
using Android.Content;
using Android.Provider;

namespace MonitoringSystem.Maui.Platforms.Android.Services;

/// <summary>
/// Допоміжний клас для читання статистики використання додатків через UsageStats API.
///
/// Як це працює:
/// Android веде внутрішній журнал — який додаток був активний і коли.
/// UsageStats API (доступний з Android 5.0 / API 21) дозволяє читати цей журнал.
///
/// ВАЖЛИВО: Потрібен спеціальний дозвіл PACKAGE_USAGE_STATS який НЕ можна
/// отримати через звичайний запит — юзер повинен вручну дати його в налаштуваннях:
/// Налаштування → Конфіденційність → Додатки з доступом до даних про використання.
/// </summary>
public static class UsageStatsHelper
{
    /// <summary>
    /// Перевіряє чи є дозвіл на читання статистики використання.
    ///
    /// AppOpsManager — система перевірки "небезпечних" операцій Android.
    /// CheckOpNoThrow повертає статус дозволу без виключень (NoThrow).
    /// AppOpsManagerMode.Allowed = 0 означає що дозвіл надано юзером.
    /// </summary>
    public static bool HasUsagePermission(Context context)
    {
        try
        {
            var appOps = (AppOpsManager)context.GetSystemService(Context.AppOpsService)!;
            var mode = appOps.CheckOpNoThrow(
                AppOpsManager.OpstrGetUsageStats,
                global::Android.OS.Process.MyUid(),
                context.PackageName!
            );
            return mode == AppOpsManagerMode.Allowed;
        }
        catch { return false; }
    }

    /// <summary>
    /// Відкриває системний екран де юзер може дати дозвіл на статистику.
    /// Без цього дозволу UsageStats API повертає порожній список.
    /// </summary>
    public static void OpenUsagePermissionSettings(Context context)
    {
        try
        {
            var intent = new Intent(Settings.ActionUsageAccessSettings);
            intent.AddFlags(ActivityFlags.NewTask);
            context.StartActivity(intent);
        }
        catch { }
    }

    /// <summary>
    /// Повертає список додатків які були активні за останні N секунд.
    ///
    /// UsageStatsManager.QueryUsageStats() повертає статистику за часовий інтервал.
    /// Кожен UsageStats об'єкт містить:
    ///   PackageName     — назва пакету (com.google.android.youtube)
    ///   LastTimeUsed    — мілісекунди з epoch коли останній раз використовувався
    ///   TotalTimeInForeground — загальний час на передньому плані (мс)
    ///
    /// Фільтруємо по LastTimeUsed — беремо тільки ті що були активні нещодавно.
    /// </summary>
    public static List<string> GetRecentApps(Context context, int seconds = 60)
    {
        var result = new List<string>();
        if (!HasUsagePermission(context)) return result;

        try
        {
            var usageStatsManager = (UsageStatsManager)
                context.GetSystemService(Context.UsageStatsService)!;

            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var start = now - (seconds * 1000L);

            // QueryUsageStats — повертає статистику за вказаний інтервал.
            // UsageStatsInterval.Best — Android вибирає найкращий рівень деталізації
            var stats = usageStatsManager.QueryUsageStats(
                UsageStatsInterval.Best, start, now);

            if (stats == null) return result;

            result.AddRange(
                stats
                    .Where(s => s.LastTimeUsed > start)
                    .OrderByDescending(s => s.LastTimeUsed)
                    .Select(s => s.PackageName ?? "")
                    .Where(p => !string.IsNullOrEmpty(p)
                        && !p.StartsWith("com.android.")
                        && !p.StartsWith("android.")
                        && p != "com.corporate.monitoring")
                    .Take(10)
            );
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[UsageStats] Error: {ex.Message}");
        }

        return result;
    }

    /// <summary>
    /// Повертає назву додатку який зараз відкритий на передньому плані.
    ///
    /// QueryEvents — деталізований журнал подій: MOVE_TO_FOREGROUND, MOVE_TO_BACKGROUND.
    /// Ми шукаємо останню подію переходу на передній план.
    ///
    /// EventType перевіряємо як int (1 = ACTIVITY_RESUMED / MOVE_TO_FOREGROUND)
    /// оскільки назви констант відрізняються між версіями Android API bindings.
    /// </summary>
    public static string? GetForegroundApp(Context context)
    {
        if (!HasUsagePermission(context)) return null;

        try
        {
            var usageStatsManager = (UsageStatsManager)
                context.GetSystemService(Context.UsageStatsService)!;

            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var start = now - 5000; // останні 5 секунд

            var events = usageStatsManager.QueryEvents(start, now);
            if (events == null) return null;

            var usageEvent = new UsageEvents.Event();
            string? lastForeground = null;

            while (events.HasNextEvent)
            {
                events.GetNextEvent(usageEvent);

                // EventType == 1 відповідає MOVE_TO_FOREGROUND на більшості версій Android.
                // Використовуємо числове значення бо назва константи змінювалась між API рівнями:
                //   API 21-28: EventType.MovedToForeground = 1
                //   API 29+:   EventType.ActivityResumed   = 1
                // Числове значення 1 стабільне в усіх версіях.
                if ((int)usageEvent.EventType == 1)
                    lastForeground = usageEvent.PackageName;
            }

            return lastForeground;
        }
        catch { return null; }
    }
}
#endif
