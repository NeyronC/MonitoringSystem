namespace MonitoringSystem.Maui.Models;

public class UserModel
{
    public string Id { get; set; } = "";
    public string Username { get; set; } = "";
    public string Department { get; set; } = "";
    public string Role { get; set; } = "";
    public bool IsOnline { get; set; }
    public DateTime? LastActivity { get; set; }
    public long TotalActions { get; set; }
}

public class DeviceModel
{
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Platform { get; set; } = "";
    public string OsVersion { get; set; } = "";
    public DateTime LastSeen { get; set; }
    public bool IsActive { get; set; }
    public string HardwareId { get; set; } = "";
}

public class RuleModel
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string RuleType { get; set; } = "";
    public string Value { get; set; } = "";
    public string Severity { get; set; } = "";
    public string Action { get; set; } = "";
    public bool IsActive { get; set; }
    public string? Description { get; set; }
}

public class ActivityModel
{
    public string Id { get; set; } = "";
    public string Action { get; set; } = "";
    public string Details { get; set; } = "";
    public string? Url { get; set; }
    public DateTime Timestamp { get; set; }
    public string Username { get; set; } = "";
    public string IpAddress { get; set; } = "";
}

public class LoginResult
{
    public bool Success { get; set; }
    public string? Token { get; set; }
    public string? UserId { get; set; }
    public string? Username { get; set; }
    public string? Role { get; set; }
    public string? Message { get; set; }
}

public class ActivityResult
{
    public bool IsBlocked { get; set; }
    public string Message { get; set; } = "";
}
