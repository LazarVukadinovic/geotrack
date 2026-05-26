namespace Geotrack.Application.DTOs.Users;

public class CreateSupportRequestRequestDto
{
    public string Category { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? PageUrl { get; set; }
    public string? UserAgent { get; set; }
    public string? DeviceInfo { get; set; }
    public string? Browser { get; set; }
    public string? Platform { get; set; }
    public string? AttachmentUrl { get; set; }
}
