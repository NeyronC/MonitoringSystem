using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Runtime.InteropServices;
using MonitoringSystem.Maui.Models;
using MonitoringSystem.Maui.Services;

namespace MonitoringSystem.Maui.ViewModels;

public class UsersViewModel : BaseViewModel
{
    private readonly IApiService _api;
    public ObservableCollection<UserModel> Users { get; } = new();

    private string _newUsername = "", _newPassword = "",
                   _newDepartment = "", _newRole = "User";
    private bool _showForm;

    public string NewUsername   { get => _newUsername;   set => SetProperty(ref _newUsername, value); }
    public string NewPassword   { get => _newPassword;   set => SetProperty(ref _newPassword, value); }
    public string NewDepartment { get => _newDepartment; set => SetProperty(ref _newDepartment, value); }
    public string NewRole       { get => _newRole;       set => SetProperty(ref _newRole, value); }
    public bool ShowForm
    {
        get => _showForm;
        set { SetProperty(ref _showForm, value); OnPropertyChanged(nameof(HideForm)); }
    }
    public bool HideForm => !ShowForm;

    public List<string> Roles { get; } = new() { "User", "Admin" };

    public ICommand RefreshCommand    { get; }
    public ICommand ToggleFormCommand { get; }
    public ICommand CreateUserCommand { get; }
    public ICommand DeleteUserCommand { get; }
    public ICommand MakeAdminCommand  { get; }
    public ICommand MakeUserCommand   { get; }

    public UsersViewModel(IApiService api)
    {
        _api = api;
        Title = "Користувачі";

        RefreshCommand    = new Command(async () => await LoadUsers());
        ToggleFormCommand = new Command(() => { ShowForm = !ShowForm; Error = ""; });

        CreateUserCommand = new Command(
            async () => await CreateUser(),
            () => !IsBusy &&
                  !string.IsNullOrWhiteSpace(NewUsername) &&
                  !string.IsNullOrWhiteSpace(NewPassword));

        DeleteUserCommand = new Command<string>(async id => await DeleteUser(id));
        MakeAdminCommand  = new Command<string>(async id => await ChangeRole(id, "Admin"));
        MakeUserCommand   = new Command<string>(async id => await ChangeRole(id, "User"));
    }

    public async Task InitializeAsync()
    {
        var token = await GetFromStorage("auth_token");
        if (!string.IsNullOrEmpty(token)) _api.SetToken(token);
        await LoadUsers();
    }

    private async Task LoadUsers()
    {
        IsBusy = true;
        Error  = "";
        try
        {
            var users = await _api.GetAllUsersFullAsync();
            Users.Clear();
            foreach (var u in users) Users.Add(u);
        }
        catch (Exception ex) { Error = ex.Message; }
        finally { IsBusy = false; }
    }

    private async Task CreateUser()
    {
        if (NewPassword.Length < 6) { Error = "Пароль мінімум 6 символів"; return; }

        IsBusy = true;
        Error  = "";
        try
        {
            var ok = await _api.CreateUserAsync(
                NewUsername, NewPassword, NewRole, NewDepartment);
            if (ok)
            {
                NewUsername = ""; NewPassword = "";
                NewDepartment = ""; NewRole = "User";
                ShowForm = false;
                await LoadUsers();
            }
            else Error = "Помилка — можливо такий логін вже існує";
        }
        finally { IsBusy = false; }
    }

    private async Task ChangeRole(string id, string role)
    {
        var label   = role == "Admin" ? "адміністратором" : "звичайним юзером";
        var confirm = await Shell.Current.DisplayAlert(
            "Змінити роль", $"Зробити цього юзера {label}?", "Так", "Ні");
        if (!confirm) return;
        await _api.ChangeUserRoleAsync(id, role);
        await LoadUsers();
    }

    private async Task DeleteUser(string id)
    {
        var confirm = await Shell.Current.DisplayAlert(
            "Видалити юзера",
            "Видалити юзера разом з усіма його пристроями та сесіями?",
            "Так, видалити", "Скасувати");
        if (!confirm) return;
        await _api.DeleteUserAsync(id);
        await LoadUsers();
    }

    private static async Task<string?> GetFromStorage(string key)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ||
            RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            var file = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "MonitoringSystem", $"{key}.dat");
            return File.Exists(file) ? await File.ReadAllTextAsync(file) : null;
        }
        return await SecureStorage.GetAsync(key);
    }
}
