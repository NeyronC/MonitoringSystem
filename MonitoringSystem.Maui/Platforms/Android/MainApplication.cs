using Android.App;
using Android.Runtime;
using Microsoft.Maui;
using Microsoft.Maui.Hosting;

namespace MonitoringSystem.Maui;

/// <summary>
/// Точка входу Android. MAUI автоматично реєструє цей клас
/// через атрибут Application.
/// </summary>
[Application]
public class MainApplication : MauiApplication
{
    public MainApplication(IntPtr handle, JniHandleOwnership ownership)
        : base(handle, ownership) { }

    protected override MauiApp CreateMauiApp()
        => MauiProgram.CreateMauiApp();
}
