using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using MongoDB.Driver;
using System.Security.Claims;
using MonitoringSystem.Api.Data;
using MonitoringSystem.Api.DTOs;
using MonitoringSystem.Api.Hubs;
using MonitoringSystem.Api.Models;

namespace MonitoringSystem.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class DevicesController : ControllerBase
{
    private readonly MongoDbContext           _db;
    private readonly IHubContext<ActivityHub> _hub;

    public DevicesController(MongoDbContext db, IHubContext<ActivityHub> hub)
    { _db = db; _hub = hub; }

    [HttpGet]
    public async Task<IActionResult> GetMyDevices()
    {
        var userId  = User.FindFirst(ClaimTypes.NameIdentifier)!.Value;
        var devices = await _db.Devices.Find(d => d.UserId == userId).ToListAsync();
        return Ok(devices);
    }

    [HttpGet("all")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> GetAll()
    {
        var devices = await _db.Devices.Find(_ => true).ToListAsync();
        var userIds = devices.Select(d => d.UserId).Distinct().ToList();
        var users   = await _db.Users.Find(u => userIds.Contains(u.Id)).ToListAsync();
        var userMap = users.ToDictionary(u => u.Id, u => u.Username);

        return Ok(devices.Select(d => new
        {
            d.Id, d.DisplayName, d.Platform, d.OsVersion,
            d.FirstSeen, d.LastSeen, d.IsActive, d.HardwareId,
            Username = userMap.GetValueOrDefault(d.UserId, "Невідомий")
        }));
    }

    [HttpPatch("{deviceId}/rename")]
    public async Task<IActionResult> Rename(string deviceId, [FromBody] RenameDeviceDto dto)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)!.Value;
        var role   = User.FindFirst(ClaimTypes.Role)?.Value;

        if (string.IsNullOrWhiteSpace(dto.Name))
            return BadRequest(new { message = "Назва не може бути порожньою" });

        var filter = Builders<Device>.Filter.Eq(d => d.Id, deviceId);
        if (role != "Admin")
            filter &= Builders<Device>.Filter.Eq(d => d.UserId, userId);

        var result = await _db.Devices.UpdateOneAsync(
            filter, Builders<Device>.Update.Set(d => d.DisplayName, dto.Name));

        return result.MatchedCount == 0 ? NotFound() : Ok();
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterDeviceDto dto)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)!.Value;

        // Якщо пристрій вже є — просто оновлюємо LastSeen і повертаємо його Id
        var existing = await _db.Devices
            .Find(d => d.HardwareId == dto.HardwareId)
            .FirstOrDefaultAsync();

        if (existing != null)
        {
            await _db.Devices.UpdateOneAsync(
                d => d.Id == existing.Id,
                Builders<Device>.Update
                    .Set(d => d.LastSeen, DateTime.UtcNow)
                    .Set(d => d.IsActive, true));
            return Ok(new { deviceId = existing.Id, existing.DisplayName });
        }

        var device = new Device
        {
            UserId      = userId,
            HardwareId  = dto.HardwareId,
            DisplayName = dto.SuggestedName,
            Platform    = dto.Platform,
            OsVersion   = dto.OsVersion
        };
        await _db.Devices.InsertOneAsync(device);
        await _db.Users.UpdateOneAsync(
            u => u.Id == userId,
            Builders<User>.Update.AddToSet(u => u.DeviceIds, device.Id));

        return Ok(new { deviceId = device.Id, device.DisplayName });
    }

    /// <summary>
    /// Видаляє пристрій і закриває його сесію.
    /// Якщо це єдиний активний пристрій юзера → встановлюємо IsOnline=false
    /// і надсилаємо UserOffline через SignalR → агент отримає розрив і покаже форму логіну.
    /// </summary>
    [HttpDelete("{deviceId}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(string deviceId)
    {
        var device = await _db.Devices.Find(d => d.Id == deviceId).FirstOrDefaultAsync();
        if (device == null) return NotFound();

        var userId = device.UserId;

        // 1. Видаляємо пристрій
        await _db.Devices.DeleteOneAsync(d => d.Id == deviceId);
        await _db.Users.UpdateOneAsync(
            u => u.Id == userId,
            Builders<User>.Update.Pull(u => u.DeviceIds, deviceId));

        // 2. Закриваємо сесії цього пристрою
        await _db.Sessions.UpdateManyAsync(
            s => s.DeviceId == deviceId && s.LogoutTime == null,
            Builders<UserSession>.Update.Set(s => s.LogoutTime, DateTime.UtcNow));

        // 3. Перевіряємо чи є ще активні пристрої у цього юзера
        var otherDevices = await _db.Devices
            .CountDocumentsAsync(d => d.UserId == userId && d.IsActive);

        if (otherDevices == 0)
        {
            // Всі пристрої видалені — юзер офлайн
            await _db.Users.UpdateOneAsync(
                u => u.Id == userId,
                Builders<User>.Update.Set(u => u.IsOnline, false));

            // SignalR: сповіщаємо адмінів + надсилаємо "ForceLogout" агенту
            await _hub.Clients.Group("admins")
                .SendAsync("UserOffline", new { userId });
            await _hub.Clients.Group($"user:{userId}")
                .SendAsync("AdminAlert",
                    "⚠️ Пристрій видалено",
                    "Адміністратор видалив цей пристрій. Необхідно увійти знову.");
        }

        return Ok();
    }
    /// <summary>
    /// Видаляє дублікати пристроїв (залишає один з найновішим LastSeen).
    /// Дублі виникають якщо hwId генерувався по-різному при різних логінах.
    /// </summary>
    [HttpPost("dedup")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Dedup()
    {
        var devices = await _db.Devices.Find(_ => true).ToListAsync();

        // Групуємо по UserId — видаляємо пристрої з однаковим UserId+Platform
        var groups = devices
            .GroupBy(d => $"{d.UserId}|{d.Platform}|{d.OsVersion}")
            .Where(g => g.Count() > 1);

        int deleted = 0;
        foreach (var g in groups)
        {
            // Залишаємо найновіший
            var toDelete = g.OrderByDescending(d => d.LastSeen).Skip(1);
            foreach (var d in toDelete)
            {
                await _db.Devices.DeleteOneAsync(x => x.Id == d.Id);
                deleted++;
            }
        }

        return Ok(new { deleted, message = $"Видалено {deleted} дублікатів" });
    }
}