using Android.App;
using Android.Content.PM;
using Microsoft.Maui;

namespace MonitoringSystem.Maui;

/// <summary>
/// Головна Activity Android.
/// Наслідує MauiAppCompatActivity — MAUI автоматично реєструє
/// цей клас як точку входу через атрибут Activity.
/// </summary>
[Activity(
    Theme = "@style/AppTheme",
    MainLauncher = true,
    LaunchMode = LaunchMode.SingleTask,
    ConfigurationChanges =
        ConfigChanges.ScreenSize | ConfigChanges.Orientation |
        ConfigChanges.UiMode | ConfigChanges.ScreenLayout |
        ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity { }
