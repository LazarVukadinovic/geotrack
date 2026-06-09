namespace Geotrack.Application.DTOs.Auth;

public class RefreshResponseDto
{
    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public int ExpiresIn { get; set; } = 3600;
    public bool SessionPersistent { get; set; }
    public AuthSessionDto? Session { get; set; }
}
