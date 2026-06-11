namespace Geotrack.Application.DTOs.Auth;

public class AuthMessageResponseDto
{
    public string Message { get; set; } = string.Empty;
    public int? RetryAfterSeconds { get; set; }
}
