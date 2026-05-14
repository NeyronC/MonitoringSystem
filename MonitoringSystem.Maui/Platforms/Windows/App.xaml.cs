using Microsoft.Maui;
using Microsoft.Maui.Hosting;
using MonitoringSystem.Maui;

namespace MonitoringSystem.Maui.WinUI;

/// <summary>
/// WinUI3 Application клас для Windows платформи.
/// Успадковує MauiWinUIApplication (не generic в MAUI 10).
/// Перевизначає CreateMauiApp() — саме звідси стартує весь MAUI pipeline.
/// </summary>
public partial class App : MauiWinUIApplication
{
    public App()
    {
        InitializeComponent();
    }

    protected override MauiApp CreateMauiApp()
        => MauiProgram.CreateMauiApp();
}
