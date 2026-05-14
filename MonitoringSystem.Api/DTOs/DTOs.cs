namespace MonitoringSystem.Api.DTOs;

public record LoginDto(string Username, string Password, string? DeviceId);
public record LogoutDto();
public record ActivityLogDto(string Action, string Details, string? Url = null);
public record CreateRuleDto(
    string Name, string RuleType, string Value,
    string Severity, string Action, bool IsActive,
    string? Description, bool ShowDesktopNotification = true,
    string AutoResponse = "None", string AutoResponseMessage = "");
public record RenameDeviceDto(string Name);
public record RegisterDeviceDto(
    string HardwareId, string SuggestedName,
    string Platform, string OsVersion);

public class LoginResponse
{
    public string Token { get; set; } = "";
    public string UserId { get; set; } = "";
    public string Username { get; set; } = "";
    public string Role { get; set; } = "";
    public string SessionId { get; set; } = "";
}

public class ActivityResult
{
    public bool IsBlocked { get; set; }
    public string Message { get; set; } = "";
}

public class RuleCheckResult
{
    public bool IsBlocked { get; set; }
    public MonitoringSystem.Api.Models.MonitoringRule? ViolatedRule { get; set; }
    public string Message { get; set; } = "";
}

public record NotifyUserDto(string UserId, string Message, string? Title = null);

public record RestoreSessionDto(string? DeviceId = null);
public record ChangePasswordDto(string CurrentPassword, string NewPassword);
