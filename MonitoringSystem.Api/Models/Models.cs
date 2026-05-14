using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace MonitoringSystem.Api.Models;

public class User
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = ObjectId.GenerateNewId().ToString();
    public string Username { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string Role { get; set; } = "User";
    public string Department { get; set; } = "";
    public bool IsOnline { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public List<string> DeviceIds { get; set; } = new();
}

public class Device
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = ObjectId.GenerateNewId().ToString();
    public string UserId { get; set; } = "";
    public string HardwareId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Platform { get; set; } = "";
    public string OsVersion { get; set; } = "";
    public DateTime FirstSeen { get; set; } = DateTime.UtcNow;
    public DateTime LastSeen { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; } = true;
}

public class ActivityLog
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = ObjectId.GenerateNewId().ToString();
    public string UserId { get; set; } = "";
    public string DeviceId { get; set; } = "";
    public string Action { get; set; } = "";
    public string Details { get; set; } = "";
    public string? Url { get; set; }
    public string IpAddress { get; set; } = "";
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

public class UserSession
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = ObjectId.GenerateNewId().ToString();
    public string UserId { get; set; } = "";
    public string DeviceId { get; set; } = "";
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime LoginTime { get; set; } = DateTime.UtcNow;
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime? LogoutTime { get; set; }
}

public class MonitoringRule
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = ObjectId.GenerateNewId().ToString();
    public string Name { get; set; } = "";
    public string RuleType { get; set; } = "";
    public string Value { get; set; } = "";
    public string Severity { get; set; } = "Warning";
    public string Action { get; set; } = "Alert";
    public bool IsActive { get; set; } = true;
    public string CreatedBy { get; set; } = "";
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    /// <summary>
    /// Показувати спливаюче сповіщення на пристрої юзера при спрацюванні.
    /// Налаштовується адміном для кожного правила окремо.
    /// </summary>
    public bool ShowDesktopNotification { get; set; } = true;

    /// <summary>
    /// Автоматична реакція при спрацюванні (налаштовується адміном).
    /// "None"        — тільки зафіксувати
    /// "NotifyUser"  — автоматично надіслати повідомлення юзеру
    /// "NotifyAdmin" — надіслати email/сповіщення адміну (через SignalR)
    /// "Both"        — і юзеру і адміну
    /// </summary>
    public string AutoResponse { get; set; } = "None";

    /// <summary>Текст автоповідомлення що надсилається юзеру при спрацюванні.</summary>
    public string AutoResponseMessage { get; set; } = "";
}

public class RuleViolation
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = ObjectId.GenerateNewId().ToString();
    public string UserId { get; set; } = "";
    public string DeviceId { get; set; } = "";
    public string RuleId { get; set; } = "";
    public string RuleName { get; set; } = "";
    public string Severity { get; set; } = "";
    public string Details { get; set; } = "";
    public bool IsAcknowledged { get; set; }
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
}
