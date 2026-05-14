using MonitoringSystem.Maui.Services;
using MonitoringSystem.Maui.ViewModels;

namespace MonitoringSystem.Maui.Views;

/// <summary>
/// Головна сторінка агента.
/// При кожному OnAppearing — перевіряє наявність токена.
/// Якщо токена немає (не залогінений) — одразу повертає на LoginPage.
/// Це захищає вкладку моніторингу від неавторизованого доступу.
/// </summary>
public partial class AgentPage : ContentPage
{
    private readonly AgentViewModel _vm;

    public AgentPage(AgentViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        // Перевірка авторизації при кожному переході на сторінку
        var token = await StorageHelper.GetAsync("auth_token");
        if (string.IsNullOrEmpty(token))
        {
            // Токена немає — повертаємо на Login без анімації
            await Shell.Current.GoToAsync("//LoginPage",
                animate: false);
            return;
        }

        await _vm.StartMonitoringAsync();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        // Моніторинг НЕ зупиняємо — працює у фоні
    }
}
