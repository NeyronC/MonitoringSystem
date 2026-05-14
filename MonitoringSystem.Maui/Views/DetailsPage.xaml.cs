using MonitoringSystem.Maui.Services;
using MonitoringSystem.Maui.ViewModels;

namespace MonitoringSystem.Maui.Views;

/// <summary>
/// Сторінка деталей (активні вкладки / процеси / мережа).
/// Захищена — при відсутності токена повертає на Login.
/// </summary>
public partial class DetailsPage : ContentPage
{
    public DetailsPage(DetailsViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        var token = await StorageHelper.GetAsync("auth_token");
        if (string.IsNullOrEmpty(token))
            await Shell.Current.GoToAsync("//LoginPage", animate: false);
    }
}
