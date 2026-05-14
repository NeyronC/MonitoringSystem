using MonitoringSystem.Maui.ViewModels;

namespace MonitoringSystem.Maui.Views;

public partial class DevicesPage : ContentPage
{
    private readonly DevicesViewModel _vm;

    public DevicesPage(DevicesViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _vm.InitializeAsync();
    }
}
