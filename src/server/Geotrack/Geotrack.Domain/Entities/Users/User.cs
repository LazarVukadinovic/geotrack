using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Geotrack.Domain.Entities.Interests;

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

    public ICollection<UserInterestTopic> UserInterestTopics { get; set; } = new List<UserInterestTopic>(); // NAVIGATIONAL

    public ICollection<UserNotification> Notifications { get; set; } = new List<UserNotification>(); // NAVIGATIONAL

    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;

    public string PasswordHash { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;

    // Profile fields
    public string? AvatarUrl { get; set; }
    public string? DisplayName { get; set; }
    public string? ShortBio { get; set; }
    public string? Location { get; set; }
    public string InterfaceLanguage { get; set; } = "en-US";
    public string Timezone { get; set; } = "UTC";
    public bool NeedsInterestsOnboarding { get; set; }
    public bool GeneralNotificationsEnabled { get; set; } = true;
    public bool TravelRecommendationsEnabled { get; set; } = true;
    public bool NearbyEventsEnabled { get; set; } = true;
    public bool CollectionUpdatesEnabled { get; set; } = true;
    public bool EmailNotificationsEnabled { get; set; }
    public bool EmailRequestsEnabled { get; set; } = true;
    public bool EmailNewReviewEnabled { get; set; } = true;
    public bool AnalyticsEnabled { get; set; } = true;

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public DateTime? LastLoginAt { get; set; }
    public bool IsSuspended { get; set; }
    public DateTime? SuspendedAt { get; set; }
    public int? SuspendedByUserId { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public int? DeletedByUserId { get; set; }

    public ICollection<RegionalManagerRegion> RegionalManagerRegions { get; set; } = new List<RegionalManagerRegion>();

    public ICollection<UserPlace> UserPlaces { get; set; } = new List<UserPlace>();
    public ICollection<PlaceContribution> PlaceContributions { get; set; } = new List<PlaceContribution>();
    public ICollection<UserAiProviderKey> AiProviderKeys { get; set; } = new List<UserAiProviderKey>();
    public TerritoryManager? TerritoryManager { get; set; }
}
