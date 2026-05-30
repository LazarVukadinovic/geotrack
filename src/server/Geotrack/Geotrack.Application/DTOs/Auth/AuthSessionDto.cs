namespace Geotrack.Application.DTOs.Auth;

public class AuthSessionDto
{
    public int UserId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
    public long? AvatarVersion { get; set; }
    public List<string> Roles { get; set; } = new();
    public bool IsAuthenticated { get; set; } = true;
    public bool CanAccessAdminApp { get; set; }
    public string? AdminRoleType { get; set; }
    public string? AdminLandingRoute { get; set; }
    public string UserAppLandingRoute { get; set; } = "/profile";
    public bool SessionPersistent { get; set; }
    public DateTime? SessionExpiresAt { get; set; }
}
