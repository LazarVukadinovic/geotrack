namespace Geotrack.Application.DTOs.Admin;

public class AdminProfileUserDto
{
    public int Id { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string Email { get; set; } = string.Empty;
    public string? ShortBio { get; set; }
    public string? AvatarUrl { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class AdminProfileRegionDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? TerritoryName { get; set; }
    public DateTime AssignedAt { get; set; }
}

public class AdminProfileRegionalManagerDto
{
    public List<AdminProfileRegionDto> AssignedRegions { get; set; } = new();
    public DateTime? MemberSince { get; set; }
}

public class AdminProfilePreferencesDto
{
    public string InterfaceLanguage { get; set; } = "en-US";
    public string Timezone { get; set; } = "UTC";
    public bool EmailRequestsEnabled { get; set; }
    public bool EmailNewReviewEnabled { get; set; }
}

public class AdminProfileResponseDto
{
    public AdminProfileUserDto User { get; set; } = new();
    public List<string> Roles { get; set; } = new();
    public AdminProfileRegionalManagerDto RegionalManager { get; set; } = new();
    public AdminProfilePreferencesDto Preferences { get; set; } = new();
}

public class UpdateAdminProfileRequestDto
{
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? ShortBio { get; set; }
    public string? InterfaceLanguage { get; set; }
    public string? Timezone { get; set; }
    public bool EmailRequestsEnabled { get; set; }
    public bool EmailNewReviewEnabled { get; set; }
}
