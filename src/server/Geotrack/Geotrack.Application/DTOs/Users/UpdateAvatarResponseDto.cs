namespace Geotrack.Application.DTOs.Users;

public class UpdateAvatarResponseDto
{
    public string AvatarUrl { get; set; } = string.Empty;
    public DateTime? UpdatedAt { get; set; }
}
