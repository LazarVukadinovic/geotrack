using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Geotrack.Domain.Entities.Users;

public class User
{
    public int Id { get; set; } // PRIMARY KEY
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;

    public ICollection<Collection> Collections { get; set; } = new List<Collection>(); // NAVIGATIONAL
    public ICollection<SupportRequest> SupportRequests { get; set; } = new List<SupportRequest>(); // NAVIGATIONAL

    public ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>(); // NAVIGATIONAL

    public ICollection<UserTag> UserTags { get; set; } = new List<UserTag>(); // NAVIGATIONAL

    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;

    public string PasswordHash { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;

    // Profile fields
    public string? AvatarUrl { get; set; }
    public string? DisplayName { get; set; }
    public string? Location { get; set; }
    public bool GeneralNotificationsEnabled { get; set; } = true;
    public bool TravelRecommendationsEnabled { get; set; } = true;
    public bool NearbyEventsEnabled { get; set; } = true;
    public bool CollectionUpdatesEnabled { get; set; } = true;
    public bool EmailNotificationsEnabled { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    public RegionalOrganization? RegionalOrganization { get; set; }
}
