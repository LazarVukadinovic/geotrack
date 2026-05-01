namespace Geotrack.Application.DTOs.Users;

public class GetNotificationSettingsResponseDto
{
    public bool GeneralNotifications { get; set; }
    public bool TravelRecommendations { get; set; }
    public bool NearbyEvents { get; set; }
    public bool CollectionUpdates { get; set; }
    public bool EmailNotifications { get; set; }
}
