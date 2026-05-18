#if ANDROID
using Android.App;
using Android.Content;
using AndroidColor = Android.Graphics.Color;
using Android.OS;
using AndroidX.Core.App;

namespace MonitoringSystem.Maui.Platforms.Android.Services;

/// <summary>
/// Допоміжний клас для відображення системних сповіщень Android.
///
/// Як працюють сповіщення Android:
/// 1. NotificationChannel — обов'язковий канал (з Android 8.0 / API 26).
///    Канал визначає загальні налаштування: гучність, вібрація, важливість.
///    Юзер може вимкнути канал в налаштуваннях телефону.
///
/// 2. NotificationCompat.Builder — будує саме сповіщення.
///    Використовуємо AndroidX (Compat) версію щоб підтримувати старі Android.
///
/// 3. NotificationManagerCompat.Notify() — показує сповіщення в шторці.
///    Кожне сповіщення має унікальний ID — якщо ID збігається, нове замінить старе.
/// </summary>
public static class NotificationHelper
{
    // ID каналу — унікальний рядок що ідентифікує наш канал сповіщень
    private const string ChannelId = "monitoring_violations";
    private const string ChannelName = "Порушення правил";

    // Лічильник ID сповіщень — кожне нове сповіщення має свій ID
    // щоб вони накопичувались в шторці а не замінювали одне одного
    private static int _notificationId = 1000;

    /// <summary>
    /// Ініціалізує канал сповіщень.
    ///
    /// NotificationChannel обов'язковий починаючи з Android 8.0 (API 26 — Oreo).
    /// На старіших версіях Android (API < 26) канали ігноруються.
    ///
    /// Важливість каналу (Importance):
    ///   High    — з'являється у верхній частині екрану (heads-up) і звучить
    ///   Default — показується в шторці зі звуком
    ///   Low     — тихо, без звуку
    ///   Min     — тільки в шторці, без іконки в статусбарі
    /// </summary>
    public static void CreateNotificationChannel(Context context)
    {
        // Перевіряємо версію Android — канали потрібні тільки з API 26
        if (Build.VERSION.SdkInt < BuildVersionCodes.O) return;

        var channel = new NotificationChannel(
            ChannelId,
            ChannelName,
            // High — сповіщення про порушення важливі, показуємо heads-up
            NotificationImportance.High)
        {
            Description = "Сповіщення про порушення правил моніторингу"
        };

        // Вібрація при сповіщенні
        channel.EnableVibration(true);
        // Світлодіодна підсвітка (якщо є на пристрої)
        channel.EnableLights(true);
        channel.LightColor = AndroidColor.Red;

        var manager = (NotificationManager)context
            .GetSystemService(Context.NotificationService)!;
        manager.CreateNotificationChannel(channel);
    }

    /// <summary>
    /// Показує системне сповіщення про порушення правила.
    ///
    /// Параметри сповіщення:
    ///   title   — заголовок (наприклад "⚠️ Порушення правила")
    ///   message — текст (наприклад "Запущено заборонений додаток: telegram")
    ///   urgent  — true = heads-up (спливає на весь екран), false = тільки в шторці
    /// </summary>
    public static void ShowViolationNotification(
        Context context,
        string title,
        string message,
        bool urgent = true)
    {
        try
        {
            // Переконуємось що канал існує (безпечно викликати повторно)
            CreateNotificationChannel(context);

            // Intent — що відбудеться коли юзер натисне на сповіщення.
            // Тут відкриваємо наш додаток (MainActivity).
            var intent = new Intent(context, typeof(MainActivity));
            intent.SetFlags(ActivityFlags.SingleTop | ActivityFlags.ClearTop);

            // PendingIntent — "відкладений" Intent що виконається пізніше
            // (коли юзер натисне сповіщення). FLAG_IMMUTABLE — обов'язковий з API 31.
            var pendingIntent = PendingIntent.GetActivity(
                context,
                0,
                intent,
                PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);

            // Будуємо сповіщення через AndroidX Compat builder
            var builder = new NotificationCompat.Builder(context, ChannelId)
                // Іконка в статусбарі (використовуємо системну іконку попередження)
                .SetSmallIcon(global::Android.Resource.Drawable.IcDialogAlert)
                // Заголовок сповіщення
                .SetContentTitle(title)
                // Текст сповіщення
                .SetContentText(message)
                // Автоматично прибирає сповіщення коли юзер на нього натискає
                .SetAutoCancel(true)
                // Що відбудеться при натисканні
                .SetContentIntent(pendingIntent)
                // Пріоритет (для API < 26 де немає каналів)
                .SetPriority(urgent
                    ? NotificationCompat.PriorityHigh   // спливає на екран
                    : NotificationCompat.PriorityDefault)
                // Стиль BigText — дозволяє розгорнути довгий текст
                .SetStyle(new NotificationCompat.BigTextStyle()
                    .BigText(message)
                    .SetBigContentTitle(title))
                // Колір іконки в статусбарі (червоний для порушень)
                .SetColor(AndroidColor.Red)
                .SetColorized(true)
                // Вібрація: 0мс чекаємо, 300мс вібрація, 200мс пауза, 300мс вібрація
                .SetVibrate(new long[] { 0, 300, 200, 300 });

            // Показуємо сповіщення
            // Кожне сповіщення має унікальний ID — вони накопичуються в шторці
            var notificationManager = NotificationManagerCompat.From(context);

            // Перевіряємо чи є дозвіл (обов'язково з Android 13 / API 33)
            if (OperatingSystem.IsAndroidVersionAtLeast(33))
            {
                // На Android 13+ потрібен явний дозвіл POST_NOTIFICATIONS
                // Якщо дозволу немає — сповіщення не з'явиться (тихо ігнорується)
                notificationManager.Notify(_notificationId++, builder.Build());
            }
            else
            {
                // До Android 13 дозвіл не потрібен
                notificationManager.Notify(_notificationId++, builder.Build());
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Notification] Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Показує сповіщення про заблокований додаток.
    /// Зручний метод щоб не писати title/message вручну.
    /// </summary>
    public static void ShowBlockedAppNotification(Context context, string appPackage)
    {
        ShowViolationNotification(
            context,
            title: "⛔ Заборонений додаток",
            message: $"Виявлено запуск забороненого додатку:\n{appPackage}",
            urgent: true);
    }

    /// <summary>
    /// Показує сповіщення про адміністративне повідомлення.
    /// </summary>
    public static void ShowAdminAlert(Context context, string title, string message)
    {
        ShowViolationNotification(
            context,
            title: $"📢 {title}",
            message: message,
            urgent: true);
    }
}
#endif
