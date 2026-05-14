using System.Windows.Input;
using MonitoringSystem.Maui.Services;

namespace MonitoringSystem.Maui.ViewModels;

public class LoginViewModel : BaseViewModel
{
    private readonly IApiService _api;
    private string _username = "";
    private string _password = "";

    public string Username { get => _username; set => SetProperty(ref _username, value); }
    public string Password { get => _password; set => SetProperty(ref _password, value); }

    private string _serverUrl = "";
    public string ServerUrl
    {
        get => _serverUrl;
        set => SetProperty(ref _serverUrl, value);
    }
    public ICommand LoginCommand { get; }

    // Результат успішного логіну — передається в AgentViewModel напряму
    public static string? LastToken   { get; private set; }
    public static string? LastUsername { get; private set; }
    public static string? LastRole    { get; private set; }

    public LoginViewModel(IApiService api)
    {
        _api = api;
        Title = "Вхід у систему";
        LoginCommand = new Command(async () => await ExecuteLogin());
    }

    public void OnPageAppearing()
    {
        // Завантажуємо збережений URL сервера
        _ = Task.Run(async () =>
        {
            var saved = await StorageHelper.GetAsync("custom_api_url");
            var url = saved ?? GetDefaultApiUrl();
            MainThread.BeginInvokeOnMainThread(() => ServerUrl = url);
        });

        // Очищуємо статичний кеш і форму
        LastToken = null;
        LastUsername = null;
        LastRole = null;
        Username = "";
        Password = "";
        Error = "";
    }

    public async Task ExecuteLoginPublic() => await ExecuteLogin();

    private static string GetDefaultApiUrl()
    {
#if ANDROID
        return "http://10.0.2.2:5000";
#else
        return "http://localhost:5000";
#endif
    }

    private async Task ExecuteLogin()
    {
        if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(Password))
        { Error = "Введіть логін та пароль"; return; }

        if (IsBusy) return;
        IsBusy = true;
        Error  = "";

        try
        {
            var result = await _api.LoginAsync(Username, Password);
            if (!result.Success)
            { Error = result.Message ?? "Невірний логін або пароль"; return; }

            // Зберігаємо статично — без файлової системи, без race condition
            LastToken    = result.Token;
            LastUsername = result.Username;
            LastRole     = result.Role;

            // Також зберігаємо на диск для інших частин додатку
            await StorageHelper.SetAsync("auth_token", result.Token ?? "");
            await StorageHelper.SetAsync("username",   result.Username ?? "");
            await StorageHelper.SetAsync("role",       result.Role ?? "");

            try
            {
                var deviceId = await _api.RegisterDeviceAsync();
                if (!string.IsNullOrEmpty(deviceId))
                    await StorageHelper.SetAsync("device_id", deviceId);
            }
            catch { }

            try { await _api.RestoreSessionAsync(); } catch { }

            await MainThread.InvokeOnMainThreadAsync(async () =>
                await Shell.Current.GoToAsync("//AgentPage"));
        }
        catch (Exception ex)
        { Error = $"Немає з'єднання з сервером: {ex.Message}"; }
        finally { IsBusy = false; }
    }
}
