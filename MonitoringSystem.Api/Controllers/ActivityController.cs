using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using MongoDB.Driver;
using System.Security.Claims;
using MonitoringSystem.Api.Data;
using MonitoringSystem.Api.DTOs;
using MonitoringSystem.Api.Hubs;
using MonitoringSystem.Api.Models;
using MonitoringSystem.Api.Services;

namespace MonitoringSystem.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ActivityController : ControllerBase
{
    private readonly MongoDbContext          _db;
    private readonly IRulesService           _rules;
    private readonly IHubContext<ActivityHub> _hub;

    public ActivityController(MongoDbContext db, IRulesService rules,
        IHubContext<ActivityHub> hub)
    { _db = db; _rules = rules; _hub = hub; }

    /// <summary>POST /api/activity/log — агент надсилає подію.</summary>
    [HttpPost("log")]
    public async Task<IActionResult> Log([FromBody] ActivityLogDto dto)
    {
        var userId   = User.FindFirst(ClaimTypes.NameIdentifier)!.Value;
        var deviceId = Request.Headers["X-Device-Id"].FirstOrDefault() ?? "";

        var check = await _rules.CheckActivityAsync(
            userId, deviceId, dto.Action, dto.Details, dto.Url);

        var log = new ActivityLog
        {
            UserId    = userId, DeviceId  = deviceId,
            Action    = dto.Action, Details = dto.Details, Url = dto.Url,
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? ""
        };
        await _db.ActivityLogs.InsertOneAsync(log);

        await _hub.Clients.Group("admins").SendAsync("NewActivity", new
        {
            log.UserId, log.Action, log.Details, log.Timestamp
        });

        if (check.IsBlocked)
            return StatusCode(403, new
            {
                blocked  = true, check.Message,
                ruleName = check.ViolatedRule?.Name
            });

        return Ok(new { blocked = false });
    }

    /// <summary>GET /api/activity/users — список юзерів зі статистикою.</summary>
    [HttpGet("users")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> GetUsers()
    {
        var users  = await _db.Users.Find(_ => true).ToListAsync();
        var result = new List<object>();

        foreach (var u in users)
        {
            var lastLog = await _db.ActivityLogs
                .Find(l => l.UserId == u.Id)
                .SortByDescending(l => l.Timestamp)
                .Limit(1).FirstOrDefaultAsync();

            var totalActions = await _db.ActivityLogs
                .CountDocumentsAsync(l => l.UserId == u.Id);

            result.Add(new
            {
                u.Id, u.Username, u.Department, u.IsOnline, u.Role,
                LastActivity = lastLog?.Timestamp,
                TotalActions = totalActions
            });
        }
        return Ok(result);
    }

    /// <summary>
    /// GET /api/activity/stats — агрегована статистика для графіків.
    /// Повертає: активність по годинах, топ юзерів, типи дій, динаміку порушень.
    /// </summary>
    [HttpGet("stats")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> GetStats([FromQuery] int days = 7)
    {
        var since = DateTime.UtcNow.AddDays(-days);

        // Загальні лічильники
        var totalLogs       = await _db.ActivityLogs.CountDocumentsAsync(_ => true);
        var totalUsers      = await _db.Users.CountDocumentsAsync(_ => true);
        var totalViolations = await _db.Violations.CountDocumentsAsync(_ => true);
        var newViolations   = await _db.Violations
            .CountDocumentsAsync(v => !v.IsAcknowledged);
        var onlineUsers     = await _db.Users
            .CountDocumentsAsync(u => u.IsOnline);

        // Активність по днях за останні N днів
        var recentLogs = await _db.ActivityLogs
            .Find(l => l.Timestamp >= since)
            .ToListAsync();

        var activityByDay = recentLogs
            .GroupBy(l => l.Timestamp.Date)
            .Select(g => new { Date = g.Key.ToString("dd.MM"), Count = g.Count() })
            .OrderBy(x => x.Date)
            .ToList();

        // Топ 5 юзерів по активності
        var allUsers = await _db.Users.Find(_ => true).ToListAsync();
        var userMap  = allUsers.ToDictionary(u => u.Id, u => u.Username);

        var topUsers = recentLogs
            .GroupBy(l => l.UserId)
            .Select(g => new
            {
                Username = userMap.GetValueOrDefault(g.Key, "Невідомий"),
                Count    = g.Count()
            })
            .OrderByDescending(x => x.Count)
            .Take(5)
            .ToList();

        // Розподіл по типах дій
        var actionTypes = recentLogs
            .GroupBy(l => l.Action)
            .Select(g => new { Action = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .Take(8)
            .ToList();

        // Порушення по днях
        var recentViolations = await _db.Violations
            .Find(v => v.OccurredAt >= since)
            .ToListAsync();

        var violationsByDay = recentViolations
            .GroupBy(v => v.OccurredAt.Date)
            .Select(g => new { Date = g.Key.ToString("dd.MM"), Count = g.Count() })
            .OrderBy(x => x.Date)
            .ToList();

        // Розподіл порушень по severity
        var violationsBySeverity = recentViolations
            .GroupBy(v => v.Severity)
            .Select(g => new { Severity = g.Key, Count = g.Count() })
            .ToList();

        return Ok(new
        {
            summary = new
            {
                TotalLogs       = totalLogs,
                TotalUsers      = totalUsers,
                TotalViolations = totalViolations,
                NewViolations   = newViolations,
                OnlineUsers     = onlineUsers
            },
            activityByDay,
            topUsers,
            actionTypes,
            violationsByDay,
            violationsBySeverity
        });
    }

    /// <summary>GET /api/activity/logs — пагінований журнал.</summary>
    [HttpGet("logs")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> GetLogs(int page = 1, int pageSize = 50,
        string? userId = null)
    {
        var filter = userId != null
            ? Builders<ActivityLog>.Filter.Eq(l => l.UserId, userId)
            : Builders<ActivityLog>.Filter.Empty;

        var logs = await _db.ActivityLogs
            .Find(filter)
            .SortByDescending(l => l.Timestamp)
            .Skip((page - 1) * pageSize)
            .Limit(pageSize)
            .ToListAsync();

        var userIds = logs.Select(l => l.UserId).Distinct().ToList();
        var users   = await _db.Users.Find(u => userIds.Contains(u.Id)).ToListAsync();
        var userMap = users.ToDictionary(u => u.Id, u => u.Username);

        return Ok(logs.Select(l => new
        {
            l.Id, l.Action, l.Details, l.Url, l.Timestamp, l.IpAddress,
            Username = userMap.GetValueOrDefault(l.UserId, "Невідомий"),
            l.DeviceId
        }));
    }

    [HttpGet("sessions")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> GetActiveSessions()
    {
        var sessions = await _db.Sessions.Find(s => s.LogoutTime == null).ToListAsync();
        var userIds  = sessions.Select(s => s.UserId).Distinct().ToList();
        var users    = await _db.Users.Find(u => userIds.Contains(u.Id)).ToListAsync();
        var userMap  = users.ToDictionary(u => u.Id, u => u.Username);

        return Ok(sessions.Select(s => new
        {
            s.Id, s.UserId, s.DeviceId, s.LoginTime,
            Username = userMap.GetValueOrDefault(s.UserId, "Невідомий"),
            Duration = (DateTime.UtcNow - s.LoginTime).TotalMinutes
        }));
    }
}
