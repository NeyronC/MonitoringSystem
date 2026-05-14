using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using System.Security.Claims;
using MonitoringSystem.Api.Data;
using MonitoringSystem.Api.Models;

namespace MonitoringSystem.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin")]
public class UsersController : ControllerBase
{
    private readonly MongoDbContext _db;
    public UsersController(MongoDbContext db) => _db = db;

    // Отримати всіх юзерів (повний список для адмін-панелі)
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var users = await _db.Users.Find(_ => true)
            .Project(u => new
            {
                u.Id, u.Username, u.Role, u.Department,
                u.IsOnline, u.CreatedAt
            })
            .ToListAsync();
        return Ok(users);
    }

    // Створити нового юзера (Admin або User)
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateUserDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Username) ||
            string.IsNullOrWhiteSpace(dto.Password))
            return BadRequest(new { message = "Логін та пароль обов'язкові" });

        var exists = await _db.Users
            .Find(u => u.Username == dto.Username)
            .AnyAsync();

        if (exists)
            return BadRequest(new { message = "Такий логін вже існує" });

        var validRoles = new[] { "Admin", "User" };
        var role = validRoles.Contains(dto.Role) ? dto.Role : "User";

        var user = new User
        {
            Username     = dto.Username,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password),
            Role         = role,
            Department   = dto.Department ?? ""
        };
        await _db.Users.InsertOneAsync(user);

        return Ok(new { user.Id, user.Username, user.Role, user.Department });
    }

    // Змінити роль юзера
    [HttpPatch("{id}/role")]
    public async Task<IActionResult> ChangeRole(
        string id, [FromBody] ChangeRoleDto dto)
    {
        var myId = User.FindFirst(ClaimTypes.NameIdentifier)!.Value;
        if (id == myId)
            return BadRequest(new { message = "Не можна змінити власну роль" });

        var valid = new[] { "Admin", "User" };
        if (!valid.Contains(dto.Role))
            return BadRequest(new { message = "Невалідна роль" });

        var result = await _db.Users.UpdateOneAsync(
            u => u.Id == id,
            Builders<User>.Update.Set(u => u.Role, dto.Role));

        return result.MatchedCount == 0 ? NotFound() : Ok();
    }

    // Скинути пароль юзера
    [HttpPatch("{id}/password")]
    public async Task<IActionResult> ResetPassword(
        string id, [FromBody] ResetPasswordDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.NewPassword) ||
            dto.NewPassword.Length < 6)
            return BadRequest(new { message = "Пароль мінімум 6 символів" });

        var hash = BCrypt.Net.BCrypt.HashPassword(dto.NewPassword);
        var result = await _db.Users.UpdateOneAsync(
            u => u.Id == id,
            Builders<User>.Update.Set(u => u.PasswordHash, hash));

        return result.MatchedCount == 0 ? NotFound() : Ok();
    }

    // Видалити юзера і всі його дані
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var myId = User.FindFirst(ClaimTypes.NameIdentifier)!.Value;
        if (id == myId)
            return BadRequest(new { message = "Не можна видалити самого себе" });

        await _db.Users.DeleteOneAsync(u => u.Id == id);
        await _db.Devices.DeleteManyAsync(d => d.UserId == id);
        await _db.Sessions.DeleteManyAsync(s => s.UserId == id);
        // Логи залишаємо для аудиту (або видаляємо — на вибір)
        // await _db.ActivityLogs.DeleteManyAsync(l => l.UserId == id);

        return Ok();
    }
}

public record CreateUserDto(
    string Username, string Password,
    string Role, string? Department);

public record ChangeRoleDto(string Role);
public record ResetPasswordDto(string NewPassword);
