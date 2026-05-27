namespace Geotrack.Application.DTOs.Users;

public class UpdateAvatarResponseDto
{
    public string AvatarUrl { get; set; } = string.Empty;
    public long? AvatarVersion { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
