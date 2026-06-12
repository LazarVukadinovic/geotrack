namespace Geotrack.Application.DTOs.Auth;

public class AuthActionResponseDto : AuthMessageResponseDto
{
    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public int ExpiresIn { get; set; } = 3600;
    public bool SessionPersistent { get; set; }
    public AuthSessionDto? Session { get; set; }
}
