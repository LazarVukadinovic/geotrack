namespace Geotrack.Application.DTOs.Admin;

public class TerritoryUserListResponseDto
{
    public TerritoryAssignedTerritoryDto? Territory { get; set; }
    public List<TerritoryUserListItemDto> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int Skip { get; set; }
    public int Take { get; set; }
    public bool HasMore { get; set; }
}

public class TerritoryUserListItemDto
{
    public int UserId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
    public string Initials { get; set; } = string.Empty;
    public string RoleCode { get; set; } = string.Empty;
    public string RoleDisplayName { get; set; } = string.Empty;
    public List<TerritoryUserAssignedRegionDto> AssignedRegions { get; set; } = new();
    public string Status { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTime? LastLoginAt { get; set; }
    public string LastLoginDisplay { get; set; } = string.Empty;
    public bool CanEdit { get; set; }
    public bool CanAddRegion { get; set; }
    public bool CanRemoveManagerRole { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class TerritoryUserAssignedRegionDto
{
    public int RegionId { get; set; }
    public string RegionName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
}

public class TerritoryUserAvailableRegionsResponseDto
{
    public List<TerritoryUserAvailableRegionDto> Items { get; set; } = new();
}

public class TerritoryUserAvailableRegionDto
{
    public int RegionId { get; set; }
    public string RegionName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
}

public class TerritoryUserUpdateRequestDto
{
    public int? TerritoryId { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? DisplayName { get; set; }
}

public class TerritoryUserAssignRegionRequestDto
{
    public int? TerritoryId { get; set; }
    public int RegionId { get; set; }
}

public class TerritoryUserRemoveManagerRoleRequestDto
{
    public int? TerritoryId { get; set; }
}
