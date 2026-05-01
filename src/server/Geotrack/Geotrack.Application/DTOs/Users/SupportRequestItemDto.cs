namespace Geotrack.Application.DTOs.Users;

public class SupportRequestItemDto
{
    public int Id { get; set; }
    public string ReferenceCode { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
