using System.Text.Json.Serialization;

namespace Geotrack.Application.DTOs.Admin;

public class AdminAuthUserDto
{
    public int Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
    public long? AvatarVersion { get; set; }
    public List<string> Roles { get; set; } = new();
    public bool CanAccessAdminApp { get; set; }
    public bool CanAccessRegionalAdmin { get; set; }
    public bool CanAccessTerritoryAdmin { get; set; }
    public bool CanAccessContentModeration { get; set; }
}

public class AdminLoginResponseDto
{
    public string AccessToken { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public int ExpiresIn { get; set; } = 900;
    public bool SessionPersistent { get; set; }
    public AdminAuthUserDto User { get; set; } = new();

    [JsonIgnore]
    public string RefreshToken { get; set; } = string.Empty;
}
