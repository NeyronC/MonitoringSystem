using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using System.Security.Claims;

namespace MonitoringSystem.Api.Hubs;

/// <summary>
/// SignalR Hub — real-time комунікація між агентами і веб-порталом.
///
/// Групи:
/// - "admins"  — всі підключені адміни (Blazor Web)
/// - "agents"  — всі підключені MAUI-агенти (клієнти на пристроях юзерів)
/// - "user:{userId}" — конкретний юзер (для персональних сповіщень)
/// </summary>
[Authorize]
public class ActivityHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        var role   = Context.User?.FindFirst(ClaimTypes.Role)?.Value;
        var userId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (role == "Admin")
            await Groups.AddToGroupAsync(Context.ConnectionId, "admins");

        // Кожен юзер (включно з агентами) потрапляє у власну групу
        // Це дозволяє адміну надіслати сповіщення конкретному юзеру
        if (userId != null)
            await Groups.AddToGroupAsync(Context.ConnectionId, $"user:{userId}");

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var userId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (userId != null)
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"user:{userId}");

        await Groups.RemoveFromGroupAsync(Context.ConnectionId, "admins");
        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// Адмін надсилає сповіщення конкретному юзеру (виклик з Blazor).
    /// Агент на пристрої юзера отримує це через "AdminAlert" подію.
    /// </summary>
    public async Task SendAlertToUser(string userId, string title, string message)
    {
        var callerRole = Context.User?.FindFirst(ClaimTypes.Role)?.Value;
        if (callerRole != "Admin") return; // тільки адміни можуть надсилати

        await Clients.Group($"user:{userId}")
            .SendAsync("AdminAlert", title, message);
    }
}
