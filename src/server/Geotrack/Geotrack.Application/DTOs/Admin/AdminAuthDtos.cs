using System.Text.Json.Serialization;

namespace Geotrack.Application.DTOs.Admin;

public class AdminAuthUserDto
{
    public int Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public List<string> Roles { get; set; } = new();
    public bool CanAccessRegionalAdmin { get; set; }
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
