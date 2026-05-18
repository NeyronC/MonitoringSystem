using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Net;
using Android.OS;
using Microsoft.Maui;
using MonitoringSystem.Maui.Platforms.Android.Services;

namespace MonitoringSystem.Maui;

[Activity(
    Theme = "@style/AppTheme",
    MainLauncher = true,
    LaunchMode = LaunchMode.SingleTask,
    ConfigurationChanges =
        ConfigChanges.ScreenSize | ConfigChanges.Orientation |
        ConfigChanges.UiMode | ConfigChanges.ScreenLayout |
        ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    // Request codes для ідентифікації результатів
    private const int VpnRequestCode         = 2001;
    private const int NotificationRequestCode = 1001;

    // Статичний Action — AgentViewModel підписується щоб дізнатись
    // коли юзер дав дозвіл на VPN і можна запускати сервіс
    public static Action? OnVpnPermissionGranted;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        // Ініціалізуємо канал сповіщень
        NotificationHelper.CreateNotificationChannel(this);

        // Запитуємо дозвіл на сповіщення (Android 13+)
        if (Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu)
        {
            RequestPermissions(
                new[] { Android.Manifest.Permission.PostNotifications },
                NotificationRequestCode);
        }
    }

    /// <summary>
    /// Запускає діалог дозволу VPN.
    ///
    /// VpnService.Prepare() перевіряє чи є вже дозвіл:
    ///   - null   → дозвіл вже є, можна запускати сервіс відразу
    ///   - Intent → потрібно показати системний діалог "Довіряєте цьому VPN?"
    ///
    /// StartActivityForResult запускає системний діалог і чекає результату.
    /// Результат приходить в OnActivityResult (нижче).
    /// </summary>
    public static void RequestVpnPermission(Activity activity)
    {
        try
        {
            // Prepare повертає Intent якщо потрібен дозвіл, або null якщо вже є
            var intent = VpnService.Prepare(activity);
            if (intent == null)
            {
                // Дозвіл вже є — запускаємо VPN одразу
                OnVpnPermissionGranted?.Invoke();
            }
            else
            {
                // Показуємо системний діалог "Встановити VPN-підключення?"
                // Результат прийде в OnActivityResult з кодом VpnRequestCode
                activity.StartActivityForResult(intent, VpnRequestCode);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[VPN] Permission request error: {ex.Message}");
        }
    }

    /// <summary>
    /// Викликається після того як юзер відповів на будь-який системний діалог:
    /// VPN або інший. Визначаємо по requestCode який саме діалог завершився.
    /// </summary>
    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);

        if (requestCode == VpnRequestCode)
        {
            if (resultCode == Result.Ok)
            {
                // Юзер натиснув "Гаразд" у діалозі VPN → запускаємо сервіс
                System.Diagnostics.Debug.WriteLine("[VPN] Permission granted by user");
                OnVpnPermissionGranted?.Invoke();
            }
            else
            {
                // Юзер відмовив від VPN → моніторинг трафіку недоступний
                System.Diagnostics.Debug.WriteLine("[VPN] Permission denied by user");
            }
        }
    }

    public override void OnRequestPermissionsResult(
        int requestCode, string[] permissions, Permission[] grantResults)
    {
        base.OnRequestPermissionsResult(requestCode, permissions, grantResults);

        if (requestCode == NotificationRequestCode)
        {
            var granted = grantResults.Length > 0 && grantResults[0] == Permission.Granted;
            System.Diagnostics.Debug.WriteLine($"[Notifications] Permission: {(granted ? "granted" : "denied")}");
        }
    }
}
