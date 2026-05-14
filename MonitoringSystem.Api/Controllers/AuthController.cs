using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.IdentityModel.Tokens;
using MongoDB.Driver;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using MonitoringSystem.Api.Data;
using MonitoringSystem.Api.DTOs;
using MonitoringSystem.Api.Hubs;
using MonitoringSystem.Api.Models;

namespace MonitoringSystem.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly MongoDbContext _db;
    private readonly IConfiguration _config;
    private readonly IHubContext<ActivityHub> _hub;

    public AuthController(MongoDbContext db, IConfiguration config,
        IHubContext<ActivityHub> hub)
    { _db = db; _config = config; _hub = hub; }

    /// <summary>
    /// POST /api/auth/login
    ///
    /// Алгоритм входу:
    /// 1. Знаходимо юзера в MongoDB за username (унікальне поле).
    ///    Використовуємо LINQ-лямбду — драйвер MongoDB транслює її в BSON-фільтр:
    ///    { username: "admin" } — замість ручного написання BsonDocument.
    /// 2. Перевіряємо пароль через BCrypt.Verify() — BCrypt зберігає сіль
    ///    всередині хешу, тому не потрібно зберігати сіль окремо.
    /// 3. Позначаємо юзера як IsOnline=true в БД.
    /// 4. Створюємо запис сесії і лог входу.
    /// 5. Генеруємо JWT і повертаємо клієнту.
    /// 6. Сповіщаємо всіх адмінів через SignalR що юзер увійшов.
    /// </summary>
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginDto dto)
    {
        // Пошук юзера: Find() будує MQL-запит, FirstOrDefaultAsync() виконує його.
        // Якщо юзера немає — повертає null (не кидає виняток).
        var user = await _db.Users
            .Find(u => u.Username == dto.Username)
            .FirstOrDefaultAsync();

        // BCrypt.Verify порівнює пароль з хешем константним часом
        // (захист від timing attack — злом через вимір часу відповіді)
        if (user == null || !BCrypt.Net.BCrypt.Verify(dto.Password, user.PasswordHash))
            return Unauthorized(new { message = "Невірний логін або пароль" });

        // UpdateOneAsync — атомарна операція оновлення одного поля
        await _db.Users.UpdateOneAsync(
            u => u.Id == user.Id,
            Builders<User>.Update.Set(u => u.IsOnline, true));

        var session = new UserSession
        {
            UserId   = user.Id,
            DeviceId = dto.DeviceId ?? ""
        };
        await _db.Sessions.InsertOneAsync(session);

        await _db.ActivityLogs.InsertOneAsync(new ActivityLog
        {
            UserId    = user.Id, DeviceId = dto.DeviceId ?? "",
            Action    = "Login", Details  = "Успішний вхід",
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? ""
        });

        // SignalR: надсилаємо подію тільки клієнтам групи "admins"
        // (адміни підписані на групу при підключенні до Hub)
        await _hub.Clients.Group("admins")
            .SendAsync("UserOnline", new { user.Id, user.Username });

        return Ok(new LoginResponse
        {
            Token     = GenerateJwt(user),
            UserId    = user.Id,
            Username  = user.Username,
            Role      = user.Role,
            SessionId = session.Id
        });
    }

    /// <summary>
    /// POST /api/auth/logout
    ///
    /// Алгоритм:
    /// 1. Дістаємо UserId з JWT claims (токен вже перевірено middleware).
    /// 2. Позначаємо юзера offline і закриваємо всі активні сесії.
    /// 3. Сповіщаємо адмінів через SignalR.
    /// </summary>
    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout()
    {
        // ClaimTypes.NameIdentifier — це стандартний claim де ми зберегли UserId при логіні
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)!.Value;

        await _db.Users.UpdateOneAsync(
            u => u.Id == userId,
            Builders<User>.Update.Set(u => u.IsOnline, false));

        // UpdateMany — закриваємо ВСІ незакриті сесії юзера
        // (він міг залогінитись з кількох пристроїв)
        await _db.Sessions.UpdateManyAsync(
            s => s.UserId == userId && s.LogoutTime == null,
            Builders<UserSession>.Update.Set(s => s.LogoutTime, DateTime.UtcNow));

        await _db.ActivityLogs.InsertOneAsync(new ActivityLog
        {
            UserId    = userId, Action = "Logout",
            Details   = "Вихід з системи",
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? ""
        });

        await _hub.Clients.Group("admins").SendAsync("UserOffline", new { userId });
        return Ok();
    }

    /// <summary>
    /// GET /api/auth/me — повертає дані поточного авторизованого юзера.
    /// Використовується клієнтом при відновленні сесії (autologin).
    /// </summary>
    [HttpGet("me")]
    [Authorize]
    public async Task<IActionResult> Me()
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)!.Value;
        var user   = await _db.Users.Find(u => u.Id == userId).FirstOrDefaultAsync();
        if (user == null) return NotFound();
        return Ok(new { user.Id, user.Username, user.Role, user.Department });
    }

    /// <summary>
    /// POST /api/auth/restore-session
    /// 
    /// Відновлює сесію при автологіні агента (є збережений токен, але новий запуск).
    /// Встановлює IsOnline=true і надсилає UserOnline через SignalR —
    /// саме це бракувало при автологіні без повного login-запиту.
    /// </summary>
    [HttpPost("restore-session")]
    [Authorize]
    public async Task<IActionResult> RestoreSession([FromBody] RestoreSessionDto? dto = null)
    {
        var userId   = User.FindFirst(ClaimTypes.NameIdentifier)!.Value;
        var username = User.FindFirst(ClaimTypes.Name)!.Value;

        // Встановлюємо IsOnline = true (як при звичайному логіні)
        await _db.Users.UpdateOneAsync(
            u => u.Id == userId,
            Builders<User>.Update.Set(u => u.IsOnline, true));

        // Створюємо нову сесію
        var session = new UserSession
        {
            UserId   = userId,
            DeviceId = dto?.DeviceId ?? ""
        };
        await _db.Sessions.InsertOneAsync(session);

        await _db.ActivityLogs.InsertOneAsync(new ActivityLog
        {
            UserId    = userId, DeviceId = dto?.DeviceId ?? "",
            Action    = "AgentRestart",
            Details   = "Агент відновив сесію при запуску",
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? ""
        });

        // Сповіщаємо адмінів через SignalR — ключовий крок
        await _hub.Clients.Group("admins")
            .SendAsync("UserOnline", new { Id = userId, Username = username });

        return Ok(new { sessionId = session.Id });
    }

    /// <summary>
    /// POST /api/auth/change-password — юзер змінює власний пароль.
    /// Перевіряє поточний пароль перед зміною (безпека).
    /// </summary>
    [HttpPost("change-password")]
    [Authorize]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.NewPassword) || dto.NewPassword.Length < 6)
            return BadRequest(new { message = "Новий пароль мінімум 6 символів" });

        var userId = User.FindFirst(ClaimTypes.NameIdentifier)!.Value;
        var user   = await _db.Users.Find(u => u.Id == userId).FirstOrDefaultAsync();
        if (user == null) return NotFound();

        if (!BCrypt.Net.BCrypt.Verify(dto.CurrentPassword, user.PasswordHash))
            return BadRequest(new { message = "Поточний пароль невірний" });

        await _db.Users.UpdateOneAsync(
            u => u.Id == userId,
            Builders<User>.Update.Set(u => u.PasswordHash,
                BCrypt.Net.BCrypt.HashPassword(dto.NewPassword)));

        return Ok(new { message = "Пароль успішно змінено" });
    }

    /// <summary>
    /// Генерує підписаний JWT токен для юзера.
    ///
    /// Структура JWT: Header.Payload.Signature (розділені крапками, base64url).
    /// - Header: алгоритм (HS256) і тип (JWT)
    /// - Payload: claims (UserId, Username, Role, exp)
    /// - Signature: HMAC-SHA256(Header + "." + Payload, SecretKey)
    ///
    /// Підпис неможливо підробити без знання секретного ключа.
    /// Термін дії: 8 годин (одна робоча зміна).
    /// </summary>
    private string GenerateJwt(User user)
    {
        var key = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(_config["Jwt:Key"]!));

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id),       // userId
            new Claim(ClaimTypes.Name,           user.Username), // для логів
            new Claim(ClaimTypes.Role,           user.Role)      // для [Authorize(Roles="Admin")]
        };

        var token = new JwtSecurityToken(
            issuer:             _config["Jwt:Issuer"],
            audience:           _config["Jwt:Audience"],
            claims:             claims,
            expires:            DateTime.UtcNow.AddHours(8),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
