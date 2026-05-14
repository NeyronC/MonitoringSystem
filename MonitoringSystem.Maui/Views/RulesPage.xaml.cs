using MonitoringSystem.Maui.ViewModels;

namespace MonitoringSystem.Maui.Views;

public partial class RulesPage : ContentPage
{
    private readonly RulesViewModel _vm;

    public RulesPage(RulesViewModel vm)
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
