using System.Text.Json;

namespace Geotrack.Application.DTOs.Admin;

public class AdminTerritoryPageRequest
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 100;
    public string? Search { get; set; }
    public string? SortField { get; set; }
    public string? SortDir { get; set; }
    public List<string> CountryFilter { get; set; } = new();
}

public class AdminTerritoryPageResponseDto
{
    public List<AdminTerritoryDto> Items { get; set; } = new();
    public int Total { get; set; }
}

public class AdminTerritoryDto
{
    public int TerritoryId { get; set; }
    public int CountryId { get; set; }
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string InternalReference { get; set; } = string.Empty;
    public string? CountryCode { get; set; }
    public List<string> Countries { get; set; } = new();
    public int? AssignedManagerUserId { get; set; }
    public string? AssignedManagerId { get; set; }
    public string? AssignedManagerName { get; set; }
    public string? AssignedManagerEmail { get; set; }
    public string Status { get; set; } = "Unassigned";
    public string? BoundarySource { get; set; }
    public string? BoundaryShapeType { get; set; }
    public bool HasBoundary { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class AdminTerritoryDetailDto : AdminTerritoryDto
{
    public AdminTerritoryAssignedManagerDto? AssignedManager { get; set; }
    public AdminTerritoryGeometryDto? BoundaryGeoJson { get; set; }
    public AdminTerritoryMapViewportDto Viewport { get; set; } = new();
    public string? BoundaryRequiredMessage { get; set; }
    public bool CanAssign { get; set; }
}

public class AdminTerritoryAssignedManagerDto
{
    public int UserId { get; set; }
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
    public string Initials { get; set; } = string.Empty;
    public List<string> Roles { get; set; } = new();
    public DateTime AssignedAt { get; set; }
}

public class AdminTerritoryGeometryDto
{
    public string Type { get; set; } = string.Empty;
    public object Coordinates { get; set; } = Array.Empty<double>();
}

public class AdminTerritoryMapBoundsDto
{
    public double North { get; set; }
    public double South { get; set; }
    public double East { get; set; }
    public double West { get; set; }
}

public class AdminTerritoryMapViewportDto
{
    public double? CenterLatitude { get; set; }
    public double? CenterLongitude { get; set; }
    public double? Zoom { get; set; }
    public AdminTerritoryMapBoundsDto? Bounds { get; set; }
    public bool IsEmpty { get; set; }
}

public class AdminTerritoryManagerSearchResponseDto
{
    public List<AdminTerritoryManagerCandidateDto> Items { get; set; } = new();
}

public class AdminTerritoryManagerCandidateDto
{
    public int UserId { get; set; }
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
    public string Initials { get; set; } = string.Empty;
    public List<string> Roles { get; set; } = new();
    public List<AdminTerritoryManagerAssignedTerritoryDto> AssignedTerritories { get; set; } = new();
    public string Status { get; set; } = "Active";
    public bool IsActive { get; set; }
    public bool CanAssign { get; set; }
    public string? IneligibilityReason { get; set; }
}

public class AdminTerritoryManagerAssignedTerritoryDto
{
    public int TerritoryId { get; set; }
    public string TerritoryName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? CountryCode { get; set; }
}

public class AdminCreateTerritoryRequestDto
{
    public string? Name { get; set; }
    public string? CountryCode { get; set; }
    public int? AssignedManagerUserId { get; set; }
    public AdminCountryBoundaryDto? Boundary { get; set; }
}

public class AdminAssignTerritoryManagerRequestDto
{
    public int? AssignedManagerUserId { get; set; }
    public int? ManagerUserId { get; set; }
}

public class AdminCountryBoundaryDto
{
    public string? Type { get; set; }
    public JsonElement Coordinates { get; set; }
    public string? Source { get; set; }
}
