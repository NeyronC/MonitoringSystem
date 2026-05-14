using MonitoringSystem.Maui.Services;
using MonitoringSystem.Maui.ViewModels;

namespace MonitoringSystem.Maui.Views;

public partial class LoginPage : ContentPage
{
    private readonly LoginViewModel _vm;

    public LoginPage(LoginViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = vm;

        // Показуємо панель сервера на всіх платформах
        // (корисно і на Windows для зміни IP без перекомпіляції)
        ServerPanel.IsVisible = true;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _vm.OnPageAppearing();
    }

    private async void OnLoginClicked(object sender, EventArgs e)
    {
        if (_vm.IsBusy) return;
        await _vm.ExecuteLoginPublic();
    }

    /// <summary>
    /// Діалог налаштування IP сервера.
    /// Корисно для реального телефону або іншого ПК в мережі.
    /// Зберігає URL в StorageHelper для використання ApiService і SignalR.
    /// </summary>
    private async void OnServerSettingsClicked(object sender, EventArgs e)
    {
        var current = await StorageHelper.GetAsync("custom_api_url")
                      ?? _vm.ServerUrl;

        var result = await DisplayPromptAsync(
            "Адреса сервера",
            "Введи IP та порт API сервера:",
            initialValue: current,
            placeholder: "http://192.168.1.100:5000",
            keyboard: Keyboard.Url);

        if (result == null) return; // скасовано

        var url = result.Trim().TrimEnd('/');
        if (!url.StartsWith("http"))
            url = "http://" + url;

        await StorageHelper.SetAsync("custom_api_url", url);
        _vm.ServerUrl = url;
    }
}
