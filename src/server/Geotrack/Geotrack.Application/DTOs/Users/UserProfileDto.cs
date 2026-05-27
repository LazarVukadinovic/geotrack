namespace Geotrack.Application.DTOs.Users;

public class UserProfileDto
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? FullName { get; set; }
    public string? DisplayName { get; set; }
    public string? AvatarUrl { get; set; }
    public long? AvatarVersion { get; set; }
    public string? Location { get; set; }
    public string? PhoneNumber { get; set; }
    public bool IsVerified { get; set; } = true;
}

public class UpdateUserProfileDto
{
    public string? FullName { get; set; }
    public string? DisplayName { get; set; }
    public string? Location { get; set; }
    public string? PhoneNumber { get; set; }
    public string? AvatarUrl { get; set; }
}
