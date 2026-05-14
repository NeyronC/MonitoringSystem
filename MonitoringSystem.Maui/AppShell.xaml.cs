using MonitoringSystem.Maui.Views;

namespace MonitoringSystem.Maui;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();
        // AgentPage — єдина сторінка після логіну
        Routing.RegisterRoute("AgentPage",   typeof(AgentPage));
        Routing.RegisterRoute("DetailsPage", typeof(DetailsPage));
    }
}
