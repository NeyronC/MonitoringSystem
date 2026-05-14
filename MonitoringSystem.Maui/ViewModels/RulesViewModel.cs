using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Runtime.InteropServices;
using MonitoringSystem.Maui.Models;
using MonitoringSystem.Maui.Services;

namespace MonitoringSystem.Maui.ViewModels;

public class RulesViewModel : BaseViewModel
{
    private readonly IApiService _api;
    public ObservableCollection<RuleModel> Rules { get; } = new();

    private string _newName = "";
    private string _newRuleType = "BlockedDomain";
    private string _newValue = "";
    private string _newSeverity = "Warning";
    private string _newAction = "Alert";
    private bool _showForm;

    public string NewName     { get => _newName;     set => SetProperty(ref _newName, value); }
    public string NewRuleType { get => _newRuleType; set => SetProperty(ref _newRuleType, value); }
    public string NewValue    { get => _newValue;    set => SetProperty(ref _newValue, value); }
    public string NewSeverity { get => _newSeverity; set => SetProperty(ref _newSeverity, value); }
    public string NewAction   { get => _newAction;   set => SetProperty(ref _newAction, value); }
    public bool ShowForm      { get => _showForm;    set { SetProperty(ref _showForm, value); OnPropertyChanged(nameof(HideForm)); } }
    public bool HideForm => !ShowForm;

    public List<string> RuleTypes { get; } = new()
    {
        "BlockedDomain", "BlockedKeyword", "CustomEvent",
        "OffHoursAccess", "SessionTimeLimit"
    };
    public List<string> Severities { get; } = new() { "Info", "Warning", "Critical" };
    public List<string> Actions    { get; } = new() { "Log", "Alert", "Block", "Notify" };

    public ICommand RefreshCommand    { get; }
    public ICommand ToggleFormCommand { get; }
    public ICommand SaveRuleCommand   { get; }
    public ICommand DeleteRuleCommand { get; }

    public RulesViewModel(IApiService api)
    {
        _api = api;
        Title = "Правила моніторингу";
        RefreshCommand    = new Command(async () => await LoadRules());
        ToggleFormCommand = new Command(() => ShowForm = !ShowForm);
        SaveRuleCommand   = new Command(async () => await SaveRule(),
            () => !string.IsNullOrWhiteSpace(NewName) && !string.IsNullOrWhiteSpace(NewValue));
        DeleteRuleCommand = new Command<string>(async id => await DeleteRule(id));
    }

    public async Task InitializeAsync()
    {
        var token = await GetFromStorage("auth_token");
        if (!string.IsNullOrEmpty(token)) _api.SetToken(token);
        await LoadRules();
    }

    private async Task LoadRules()
    {
        IsBusy = true;
        Error = "";
        try
        {
            var rules = await _api.GetRulesAsync();
            Rules.Clear();
            foreach (var r in rules) Rules.Add(r);
        }
        catch (Exception ex) { Error = ex.Message; }
        finally { IsBusy = false; }
    }

    private async Task SaveRule()
    {
        IsBusy = true;
        try
        {
            var rule = new RuleModel
            {
                Name = NewName, RuleType = NewRuleType, Value = NewValue,
                Severity = NewSeverity, Action = NewAction, IsActive = true
            };
            var ok = await _api.CreateRuleAsync(rule);
            if (ok)
            {
                NewName = ""; NewValue = "";
                ShowForm = false;
                await LoadRules();
            }
            else Error = "Не вдалося зберегти правило";
        }
        finally { IsBusy = false; }
    }

    private async Task DeleteRule(string id)
    {
        var confirm = await Shell.Current.DisplayAlert(
            "Видалити правило", "Ви впевнені?", "Так", "Ні");
        if (!confirm) return;
        await _api.DeleteRuleAsync(id);
        await LoadRules();
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
