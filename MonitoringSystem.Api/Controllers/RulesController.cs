using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using MongoDB.Driver;
using System.Security.Claims;
using MonitoringSystem.Api.Data;
using MonitoringSystem.Api.DTOs;
using MonitoringSystem.Api.Hubs;
using MonitoringSystem.Api.Services;

namespace MonitoringSystem.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin")]
public class RulesController : ControllerBase
{
    private readonly IRulesService           _rules;
    private readonly MongoDbContext          _db;
    private readonly IHubContext<ActivityHub> _hub;

    public RulesController(IRulesService rules, MongoDbContext db,
        IHubContext<ActivityHub> hub)
    { _rules = rules; _db = db; _hub = hub; }

    [HttpGet]
    public async Task<IActionResult> GetAll()
        => Ok(await _rules.GetAllRulesAsync());

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateRuleDto dto)
    {
        var username = User.FindFirst(ClaimTypes.Name)!.Value;
        var rule = await _rules.CreateRuleAsync(dto, username);
        return CreatedAtAction(nameof(GetAll), new { id = rule.Id }, rule);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, [FromBody] CreateRuleDto dto)
        => await _rules.UpdateRuleAsync(id, dto) ? Ok() : NotFound();

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
        => await _rules.DeleteRuleAsync(id) ? Ok() : NotFound();

    // ── Порушення ────────────────────────────────────────────────────────

    /// <summary>
    /// Список порушень з фільтром по юзеру і можливістю сортування.
    /// </summary>
    [HttpGet("violations")]
    public async Task<IActionResult> GetViolations(
        int page = 1, int pageSize = 100, string? username = null)
    {
        var filter = username != null
            ? Builders<MonitoringSystem.Api.Models.RuleViolation>.Filter.Empty
            : Builders<MonitoringSystem.Api.Models.RuleViolation>.Filter.Empty;

        var violations = await _db.Violations
            .Find(_ => true)
            .SortByDescending(v => v.OccurredAt)
            .Skip((page - 1) * pageSize)
            .Limit(pageSize)
            .ToListAsync();

        var userIds = violations.Select(v => v.UserId).Distinct().ToList();
        var users   = await _db.Users.Find(u => userIds.Contains(u.Id)).ToListAsync();
        var userMap = users.ToDictionary(u => u.Id, u => u.Username);

        var result = violations.Select(v => new
        {
            v.Id, v.RuleName, v.Severity, v.Details,
            v.OccurredAt, v.IsAcknowledged, v.DeviceId,
            v.UserId,
            Username = userMap.GetValueOrDefault(v.UserId, "Невідомий")
        });

        // Фільтр по юзеру якщо передано
        if (!string.IsNullOrEmpty(username))
            result = result.Where(v => v.Username.Contains(
                username, StringComparison.OrdinalIgnoreCase));

        return Ok(result);
    }

    /// <summary>
    /// Підтвердити одне порушення.
    /// </summary>
    [HttpPatch("violations/{id}/acknowledge")]
    public async Task<IActionResult> Acknowledge(string id)
    {
        var result = await _db.Violations.UpdateOneAsync(
            v => v.Id == id,
            Builders<MonitoringSystem.Api.Models.RuleViolation>.Update
                .Set(v => v.IsAcknowledged, true));
        return result.MatchedCount == 0 ? NotFound() : Ok();
    }

    /// <summary>
    /// Підтвердити ВСІ непрочитані порушення одразу.
    /// Фронтенд може передати username для фільтрації по юзеру.
    /// </summary>
    [HttpPatch("violations/acknowledge-all")]
    public async Task<IActionResult> AcknowledgeAll([FromQuery] string? username = null)
    {
        FilterDefinition<MonitoringSystem.Api.Models.RuleViolation> filter;

        if (!string.IsNullOrEmpty(username))
        {
            // Знаходимо UserId по username
            var user = await _db.Users
                .Find(u => u.Username == username)
                .FirstOrDefaultAsync();
            if (user == null) return Ok(new { updated = 0 });

            filter = Builders<MonitoringSystem.Api.Models.RuleViolation>.Filter.And(
                Builders<MonitoringSystem.Api.Models.RuleViolation>.Filter.Eq(v => v.UserId, user.Id),
                Builders<MonitoringSystem.Api.Models.RuleViolation>.Filter.Eq(v => v.IsAcknowledged, false));
        }
        else
        {
            filter = Builders<MonitoringSystem.Api.Models.RuleViolation>.Filter
                .Eq(v => v.IsAcknowledged, false);
        }

        var result = await _db.Violations.UpdateManyAsync(
            filter,
            Builders<MonitoringSystem.Api.Models.RuleViolation>.Update
                .Set(v => v.IsAcknowledged, true));

        return Ok(new { updated = result.ModifiedCount });
    }

    // ── Повідомлення адміна → юзер ───────────────────────────────────────

    /// <summary>
    /// Адмін надсилає повідомлення конкретному юзеру через SignalR.
    /// Агент на пристрої отримає "AdminAlert" і покаже toast-сповіщення.
    /// </summary>
    [HttpPost("notify-user")]
    public async Task<IActionResult> NotifyUser([FromBody] NotifyUserDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.UserId) || string.IsNullOrWhiteSpace(dto.Message))
            return BadRequest(new { message = "UserId і Message обов'язкові" });

        var adminName = User.FindFirst(ClaimTypes.Name)?.Value ?? "Адмін";

        // Надсилаємо через SignalR до групи конкретного юзера
        await _hub.Clients.Group($"user:{dto.UserId}")
            .SendAsync("AdminAlert", dto.Title ?? "Повідомлення від адміна", dto.Message);

        return Ok(new { sent = true, to = dto.UserId });
    }
}
