using MonitoringSystem.Maui.Services;
using MonitoringSystem.Maui.ViewModels;
using MonitoringSystem.Maui.Views;

namespace MonitoringSystem.Maui;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf",  "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        // ── Сервіси ───────────────────────────────────────────────────────
        // Singleton — один HttpClient на весь додаток (правильна практика)
        builder.Services.AddSingleton<IApiService, ApiService>();

        // ── ViewModels ────────────────────────────────────────────────────
        builder.Services.AddTransient<LoginViewModel>();

        // AgentViewModel — Singleton, бо він запускає фоновий цикл
        // і повинен бути один екземпляр на весь час роботи додатку
        builder.Services.AddSingleton<AgentViewModel>();
        builder.Services.AddTransient<DetailsViewModel>();

        // ── Pages ─────────────────────────────────────────────────────────
        builder.Services.AddTransient<LoginPage>();
        builder.Services.AddSingleton<AgentPage>();
        builder.Services.AddTransient<DetailsPage>();

        return builder.Build();
    }
}
