using System.Collections.ObjectModel;
using System.Windows.Input;
using MonitoringSystem.Maui.Models;
using MonitoringSystem.Maui.Services;

namespace MonitoringSystem.Maui.ViewModels;

// ViewModel для окремого пристрою (inline rename)
public class DeviceItemViewModel : BaseViewModel
{
    private readonly IApiService _api;

    public string Id { get; }
    public string Platform { get; }
    public string OsVersion { get; }
    public DateTime LastSeen { get; }
    public string HardwareId { get; }

    private string _displayName;
    private string _editName = "";
    private bool _isEditing;

    public string DisplayName
    {
        get => _displayName;
        set => SetProperty(ref _displayName, value);
    }
    public string EditName
    {
        get => _editName;
        set => SetProperty(ref _editName, value);
    }
    public bool IsEditing
    {
        get => _isEditing;
        set
        {
            SetProperty(ref _isEditing, value);
            OnPropertyChanged(nameof(IsNotEditing));
        }
    }
    public bool IsNotEditing => !IsEditing;

    public string PlatformIcon => Platform switch
    {
        "Windows" => "🖥",
        "Android" => "📱",
        "iOS"     => "🍎",
        _         => "💻"
    };

    public ICommand StartEditCommand { get; }
    public ICommand SaveNameCommand { get; }
    public ICommand CancelEditCommand { get; }

    public DeviceItemViewModel(DeviceModel device, IApiService api)
    {
        _api = api;
        Id = device.Id;
        _displayName = device.DisplayName;
        Platform = device.Platform;
        OsVersion = device.OsVersion;
        LastSeen = device.LastSeen;
        HardwareId = device.HardwareId;

        StartEditCommand = new Command(() =>
        {
            EditName = DisplayName;
            IsEditing = true;
        });

        SaveNameCommand = new Command(async () =>
        {
            if (string.IsNullOrWhiteSpace(EditName)) return;
            IsBusy = true;
            var success = await _api.RenameDeviceAsync(Id, EditName);
            if (success) DisplayName = EditName;
            IsEditing = false;
            IsBusy = false;
        });

        CancelEditCommand = new Command(() =>
        {
            EditName = DisplayName;
            IsEditing = false;
        });
    }
}

// ViewModel для сторінки пристроїв
public class DevicesViewModel : BaseViewModel
{
    private readonly IApiService _api;
    public ObservableCollection<DeviceItemViewModel> Devices { get; } = new();

    private bool _isAdmin;
    public bool IsAdmin { get => _isAdmin; set => SetProperty(ref _isAdmin, value); }

    public ICommand RefreshCommand { get; }

    public DevicesViewModel(IApiService api)
    {
        _api = api;
        Title = "Пристрої";
        RefreshCommand = new Command(async () => await LoadDevices());
    }

    public async Task InitializeAsync()
    {
        var token = await SecureStorage.GetAsync("auth_token");
        if (!string.IsNullOrEmpty(token)) _api.SetToken(token);
        IsAdmin = (await SecureStorage.GetAsync("role")) == "Admin";
        await LoadDevices();
    }

    private async Task LoadDevices()
    {
        IsBusy = true;
        Error = "";
        try
        {
            var devices = IsAdmin
                ? await _api.GetAllDevicesAsync()
                : await _api.GetMyDevicesAsync();

            Devices.Clear();
            foreach (var d in devices)
                Devices.Add(new DeviceItemViewModel(d, _api));
        }
        catch (Exception ex) { Error = ex.Message; }
        finally { IsBusy = false; }
    }
}
