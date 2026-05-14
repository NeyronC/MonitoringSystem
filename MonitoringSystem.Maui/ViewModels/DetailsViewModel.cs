using System.Collections.ObjectModel;
using System.Windows.Input;

namespace MonitoringSystem.Maui.ViewModels;

/// <summary>
/// ViewModel для сторінки деталей — три вкладки:
/// Активні вкладки браузера / Запущені процеси / Мережеві з'єднання.
/// Дані беруться напряму з AgentViewModel (singleton) — завжди актуальні.
/// </summary>
public class DetailsViewModel : BaseViewModel
{
    private readonly AgentViewModel _agent;

    // ── Видимість вкладок ─────────────────────────────────────────────────
    private bool _showTabs       = true;
    private bool _showProcesses;
    private bool _showNetwork;
    private string _processFilter = "";

    public bool ShowTabs      { get => _showTabs;       set => SetProperty(ref _showTabs, value); }
    public bool ShowProcesses { get => _showProcesses;  set => SetProperty(ref _showProcesses, value); }
    public bool ShowNetwork   { get => _showNetwork;    set => SetProperty(ref _showNetwork, value); }

    // ── Кольори активної вкладки ──────────────────────────────────────────
    public string TabsTabColor  => _showTabs       ? "#6366f1" : "#1e293b";
    public string ProcsTabColor => _showProcesses  ? "#6366f1" : "#1e293b";
    public string NetTabColor   => _showNetwork    ? "#6366f1" : "#1e293b";

    // ── Заголовки з лічильниками ──────────────────────────────────────────
    public string TabsHeader      => $"Активних вкладок: {_agent.ActiveTabs.Count}";
    public string ProcessesHeader => $"Запущено процесів: {_agent.ActiveProcesses.Count}";
    public string NetworkHeader   => $"Активних з'єднань: {_agent.ActiveConnections.Count}";

    // ── Пошук по процесах ────────────────────────────────────────────────
    public string ProcessFilter
    {
        get => _processFilter;
        set
        {
            SetProperty(ref _processFilter, value);
            OnPropertyChanged(nameof(FilteredProcesses));
        }
    }

    public IEnumerable<string> FilteredProcesses =>
        string.IsNullOrEmpty(_processFilter)
            ? _agent.ActiveProcesses
            : _agent.ActiveProcesses
                .Where(p => p.Contains(_processFilter, StringComparison.OrdinalIgnoreCase));

    // ── Прямі посилання на колекції агента ───────────────────────────────
    public ObservableCollection<string> ActiveTabs        => _agent.ActiveTabs;
    public ObservableCollection<string> ActiveConnections => _agent.ActiveConnections;

    // ── Команди перемикання вкладок ───────────────────────────────────────
    public ICommand ShowTabsCommand      { get; }
    public ICommand ShowProcessesCommand { get; }
    public ICommand ShowNetworkCommand   { get; }

    public DetailsViewModel(AgentViewModel agent)
    {
        _agent = agent;
        Title  = "Live дані";

        ShowTabsCommand = new Command(() =>
        {
            ShowTabs = true; ShowProcesses = false; ShowNetwork = false;
            RefreshTabColors();
        });
        ShowProcessesCommand = new Command(() =>
        {
            ShowTabs = false; ShowProcesses = true; ShowNetwork = false;
            RefreshTabColors();
            OnPropertyChanged(nameof(FilteredProcesses));
            OnPropertyChanged(nameof(ProcessesHeader));
        });
        ShowNetworkCommand = new Command(() =>
        {
            ShowTabs = false; ShowProcesses = false; ShowNetwork = true;
            RefreshTabColors();
            OnPropertyChanged(nameof(NetworkHeader));
        });

        // Оновлюємо заголовки при зміні колекцій
        _agent.ActiveTabs.CollectionChanged        += (_, _) => OnPropertyChanged(nameof(TabsHeader));
        _agent.ActiveProcesses.CollectionChanged   += (_, _) =>
        {
            OnPropertyChanged(nameof(ProcessesHeader));
            OnPropertyChanged(nameof(FilteredProcesses));
        };
        _agent.ActiveConnections.CollectionChanged += (_, _) => OnPropertyChanged(nameof(NetworkHeader));
    }

    private void RefreshTabColors()
    {
        OnPropertyChanged(nameof(TabsTabColor));
        OnPropertyChanged(nameof(ProcsTabColor));
        OnPropertyChanged(nameof(NetTabColor));
    }
}
