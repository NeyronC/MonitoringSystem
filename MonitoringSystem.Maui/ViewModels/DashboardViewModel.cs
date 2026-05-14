using System.Collections.ObjectModel;
using System.Windows.Input;
using MonitoringSystem.Maui.Models;
using MonitoringSystem.Maui.Services;

namespace MonitoringSystem.Maui.ViewModels;

public class DashboardViewModel : BaseViewModel
{
    private readonly IApiService _api;

    public ObservableCollection<UserModel> Users { get; } = new();

    private int    _onlineCount;
    private string _currentUsername = "";
    private string _currentRole     = "";

    public int    OnlineCount       { get => _onlineCount;       set => SetProperty(ref _onlineCount, value); }
    public string CurrentUsername   { get => _currentUsername;   set => SetProperty(ref _currentUsername, value); }
    public string CurrentRole       { get => _currentRole;       set { SetProperty(ref _currentRole, value); OnPropertyChanged(nameof(IsAdmin)); } }
    public bool   IsAdmin           => CurrentRole == "Admin";

    public ICommand RefreshCommand          { get; }
    public ICommand LogoutCommand           { get; }
    public ICommand NavigateToUsersCommand  { get; }
    public ICommand NavigateToRulesCommand  { get; }
    public ICommand NavigateToDevicesCommand { get; }
    public ICommand OpenUrlCommand          { get; }

    public DashboardViewModel(IApiService api)
    {
        _api  = api;
        Title = "Моніторинг активності";

        RefreshCommand           = new Command(async () => await LoadUsers());
        LogoutCommand            = new Command(async () => await Logout());
        NavigateToUsersCommand   = new Command(async () => await Shell.Current.GoToAsync("UsersPage"));
        NavigateToRulesCommand   = new Command(async () => await Shell.Current.GoToAsync("RulesPage"));
        NavigateToDevicesCommand = new Command(async () => await Shell.Current.GoToAsync("DevicesPage"));
        OpenUrlCommand           = new Command<string>(async url => await NavigateToUrl(url));
    }

    public async Task InitializeAsync()
    {
        var token = await GetFromStorage("auth_token");
        if (!string.IsNullOrEmpty(token)) _api.SetToken(token);

        CurrentUsername = await GetFromStorage("username") ?? "";
        CurrentRole     = await GetFromStorage("role")     ?? "";

        await LoadUsers();
        await _api.LogActivityAsync("PageView", "Відкрито Dashboard");
    }

    private async Task LoadUsers()
    {
        IsBusy = true;
        Error  = "";
        try
        {
            var users = await _api.GetUsersAsync();
            Users.Clear();
            foreach (var u in users) Users.Add(u);
            OnlineCount = Users.Count(u => u.IsOnline);
        }
        catch (Exception ex) { Error = $"Помилка: {ex.Message}"; }
        finally { IsBusy = false; }
    }

    private async Task NavigateToUrl(string url)
    {
        var result = await _api.LogActivityAsync(
            "WebNavigation", $"Спроба відкрити: {url}", url);

        if (result.IsBlocked)
        {
            await Shell.Current.DisplayAlert(
                "Доступ заблоковано", result.Message, "OK");
            return;
        }
        await Browser.OpenAsync(url, BrowserLaunchMode.SystemPreferred);
    }

    private async Task Logout()
    {
        await _api.LogActivityAsync("Logout", "Вихід через MAUI");
        await _api.LogoutAsync();

        await ClearStorage("auth_token");
        await ClearStorage("user_id");
        await ClearStorage("username");
        await ClearStorage("role");

        await Shell.Current.GoToAsync("//LoginPage");
    }

    // Helpers: SecureStorage на мобільних, файли на Linux/Windows
    private static async Task<string?> GetFromStorage(string key)
    {
        if (System.Runtime.InteropServices.RuntimeInformation
                .IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Linux) ||
            System.Runtime.InteropServices.RuntimeInformation
                .IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
        {
            var file = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "MonitoringSystem", $"{key}.dat");
            return System.IO.File.Exists(file)
                ? await System.IO.File.ReadAllTextAsync(file)
                : null;
        }
        return await SecureStorage.GetAsync(key);
    }

    private static async Task ClearStorage(string key)
    {
        if (System.Runtime.InteropServices.RuntimeInformation
                .IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Linux) ||
            System.Runtime.InteropServices.RuntimeInformation
                .IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
        {
            var file = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "MonitoringSystem", $"{key}.dat");
            if (System.IO.File.Exists(file))
                System.IO.File.Delete(file);
            return;
        }
        SecureStorage.Remove(key);
        await Task.CompletedTask;
    }
}
