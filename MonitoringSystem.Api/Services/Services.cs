using MongoDB.Driver;
using Microsoft.AspNetCore.SignalR;
using MonitoringSystem.Api.Data;
using MonitoringSystem.Api.Hubs;
using MonitoringSystem.Api.Models;
using MonitoringSystem.Api.DTOs;

namespace MonitoringSystem.Api.Services;

// ─── Інтерфейс (контракт) ────────────────────────────────────────────────────

public interface IRulesService
{
    Task<RuleCheckResult> CheckActivityAsync(
        string userId, string deviceId,
        string action, string details, string? url = null);
    Task<List<MonitoringRule>> GetAllRulesAsync();
    Task<MonitoringRule> CreateRuleAsync(CreateRuleDto dto, string createdBy);
    Task<bool> UpdateRuleAsync(string id, CreateRuleDto dto);
    Task<bool> DeleteRuleAsync(string id);
}

// ─── Реалізація ───────────────────────────────────────────────────────────────

public class RulesService : IRulesService
{
    private readonly MongoDbContext          _db;
    private readonly IHubContext<ActivityHub> _hub;

    public RulesService(MongoDbContext db, IHubContext<ActivityHub> hub)
    { _db = db; _hub = hub; }

    /// <summary>
    /// Перевіряє дію юзера проти всіх активних правил.
    ///
    /// Алгоритм:
    /// 1. Завантажуємо всі активні правила з MongoDB одним запитом.
    /// 2. Перебираємо правила в циклі. Для кожного — перевіряємо умову
    ///    через switch expression (pattern matching C# 8+).
    /// 3. При першому спрацьованому правилі:
    ///    - Записуємо порушення в колекцію rule_violations
    ///    - Надсилаємо real-time сповіщення адмінам через SignalR
    ///    - Повертаємо результат (заблоковано чи ні)
    /// 4. Якщо жодне правило не спрацювало — повертаємо IsBlocked=false.
    ///
    /// Важливо: повертаємось на першому порушенні (не перевіряємо всі правила).
    /// Це "fail-fast" підхід — ефективніший і зрозуміліший для адміна.
    /// </summary>
    public async Task<RuleCheckResult> CheckActivityAsync(
        string userId, string deviceId,
        string action, string details, string? url = null)
    {
        // Один запит до MongoDB — отримуємо всі активні правила
        var rules = await _db.Rules.Find(r => r.IsActive).ToListAsync();

        foreach (var rule in rules)
        {
            // Switch expression — компактна перевірка типу правила
            var violated = rule.RuleType switch
            {
                // BlockedDomain: перевіряємо чи URL або details містить заборонений домен.
                // Нормалізуємо: прибираємо www. та перевіряємо і URL і details,
                // бо агент може передати домен як у url так і в details.
                "BlockedDomain" =>
                    ContainsDomain(url ?? "", rule.Value) ||
                    ContainsDomain(details, rule.Value),

                // BlockedKeyword: шукаємо заборонене слово в деталях або URL
                "BlockedKeyword" =>
                    details.Contains(rule.Value, StringComparison.OrdinalIgnoreCase) ||
                    ContainsDomain(url ?? "", rule.Value),

                // BlockedProcess: процес запущений на пристрої юзера
                "BlockedProcess" =>
                    action == "ProcessStarted" &&
                    details.Contains(rule.Value, StringComparison.OrdinalIgnoreCase),

                // CustomEvent: точна відповідність назви події
                "CustomEvent" =>
                    action.Equals(rule.Value, StringComparison.OrdinalIgnoreCase),

                // OffHoursAccess: спрацьовує тільки при реальному ручному логіні.
                // AgentRestart = автовідновлення сесії (не рахується як вхід поза часом).
                // AgentLogin від StartMonitoringAsync теж не рахується — він завжди є.
                // Спрацьовує лише при "Login" = ручний вхід через форму LoginPage.
                "OffHoursAccess" =>
                    IsOffHours() && action == "Login",

                _ => false
            };

            if (!violated) continue;

            // ── Фіксуємо порушення ────────────────────────────────────────
            var violation = new RuleViolation
            {
                UserId    = userId, DeviceId = deviceId,
                RuleId    = rule.Id, RuleName = rule.Name,
                Severity  = rule.Severity,
                Details   = $"Дія: {action} | URL: {url ?? "н/д"} | {details}"
            };
            await _db.Violations.InsertOneAsync(violation);

            // ── Real-time сповіщення адмінам через SignalR ────────────────
            // Відправляємо тільки при Alert/Notify/Block (не при Log)
            // ── Сповіщення юзеру на пристрій (завжди, при будь-якому порушенні) ──
            // SignalR → AgentViewModel.On("AdminAlert") → банер на пристрої юзера
            {
                var userMsg = !string.IsNullOrEmpty(rule.AutoResponseMessage)
                    ? rule.AutoResponseMessage
                    : $"Виявлено порушення правила '{rule.Name}'. Severity: {rule.Severity}.";
                await _hub.Clients.Group($"user:{userId}")
                    .SendAsync("AdminAlert", $"⚠️ {rule.Name}", userMsg);
            }

            // ── Сповіщення адміну (для Alert/Notify/Block) ────────────────
            if (rule.Action is "Alert" or "Notify" or "Block")
            {
                var user = await _db.Users.Find(u => u.Id == userId).FirstOrDefaultAsync();

                // Clients.Group("admins") — надсилає тільки підключеним адмінам,
                // а не всім клієнтам (Clients.All)
                await _hub.Clients.Group("admins").SendAsync("RuleViolation", new
                {
                    ViolationId  = violation.Id,
                    UserId       = userId,
                    Username     = user?.Username ?? "Невідомий",
                    violation.RuleName, violation.Severity,
                    violation.Details,  violation.OccurredAt
                });
            }

            return new RuleCheckResult
            {
                IsBlocked    = rule.Action == "Block",
                ViolatedRule = rule,
                Message      = $"Порушено правило: {rule.Name}"
            };
        }

        // Жодне правило не спрацювало
        return new RuleCheckResult { IsBlocked = false };
    }

    /// <summary>
    /// Повертає всі правила, відсортовані за датою створення (старіші — перші).
    /// SortBy компілюється в MongoDB sort: { createdAt: 1 }
    /// </summary>
    public async Task<List<MonitoringRule>> GetAllRulesAsync()
        => await _db.Rules.Find(_ => true).SortBy(r => r.CreatedAt).ToListAsync();

    /// <summary>
    /// Створює нове правило. CreatedBy — ім'я адміна для аудиту.
    /// InsertOneAsync автоматично заповнює Id (ObjectId від MongoDB).
    /// </summary>
    public async Task<MonitoringRule> CreateRuleAsync(CreateRuleDto dto, string createdBy)
    {
        var rule = new MonitoringRule
        {
            Name        = dto.Name,     RuleType    = dto.RuleType,
            Value       = dto.Value,    Severity    = dto.Severity,
            Action      = dto.Action,   IsActive    = dto.IsActive,
            Description = dto.Description, CreatedBy = createdBy,
            ShowDesktopNotification = dto.ShowDesktopNotification,
            AutoResponse = dto.AutoResponse,
            AutoResponseMessage = dto.AutoResponseMessage
        };
        await _db.Rules.InsertOneAsync(rule);
        return rule;
    }

    /// <summary>
    /// Оновлює правило атомарно через UpdateOne + Builders.Update.
    /// Builders.Update.Set() генерує BSON: { $set: { name: "...", ... } }
    /// Повертає true якщо документ знайдено (MatchedCount > 0).
    /// </summary>
    public async Task<bool> UpdateRuleAsync(string id, CreateRuleDto dto)
    {
        var result = await _db.Rules.UpdateOneAsync(
            r => r.Id == id,
            Builders<MonitoringRule>.Update
                .Set(r => r.Name,        dto.Name)
                .Set(r => r.RuleType,    dto.RuleType)
                .Set(r => r.Value,       dto.Value)
                .Set(r => r.Severity,    dto.Severity)
                .Set(r => r.Action,      dto.Action)
                .Set(r => r.IsActive,    dto.IsActive)
                .Set(r => r.Description, dto.Description)
                .Set(r => r.AutoResponse, dto.AutoResponse)
                .Set(r => r.AutoResponseMessage, dto.AutoResponseMessage));
        return result.MatchedCount > 0;
    }

    /// <summary>
    /// Видаляє правило за Id. DeletedCount = 0 якщо правило не знайдено.
    /// </summary>
    public async Task<bool> DeleteRuleAsync(string id)
    {
        var result = await _db.Rules.DeleteOneAsync(r => r.Id == id);
        return result.DeletedCount > 0;
    }

    /// <summary>
    /// Перевірка позаробочого часу.
    /// Робочий час: 08:00–18:00, Понеділок–П'ятниця (за локальним часом сервера).
    /// </summary>
    /// <summary>
    /// Перевіряє чи рядок містить домен. Ігнорує www. префікс і регістр.
    /// Приклад: "https://www.facebook.com/foo" містить "facebook.com" → true
    /// </summary>
    private static bool ContainsDomain(string text, string domain)
    {
        if (string.IsNullOrEmpty(text)) return false;
        var normalized = text.Replace("www.", "").ToLowerInvariant();
        return normalized.Contains(domain.ToLowerInvariant());
    }

    private static bool IsOffHours() =>
        DateTime.Now.Hour < 8 || DateTime.Now.Hour >= 18 ||
        DateTime.Now.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
}
