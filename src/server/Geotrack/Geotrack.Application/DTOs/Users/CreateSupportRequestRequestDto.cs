namespace Geotrack.Application.DTOs.Users;

public class CreateSupportRequestRequestDto
{
    public string Category { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}
