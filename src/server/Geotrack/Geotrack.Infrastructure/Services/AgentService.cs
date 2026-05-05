using Geotrack.Application.DTOs.Agent;
using Geotrack.Application.Interfaces.Service;
using Geotrack.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Geotrack.Infrastructure.Services;

public class AgentService : IAgentService
{
    private readonly AppDbContext _db;
    private readonly IFavoriteService _favoriteService;

    public AgentService(AppDbContext db, IFavoriteService favoriteService)
    {
        _db = db;
        _favoriteService = favoriteService;
    }

    public async Task<AgentResponseDto> ProcessMessageAsync(string text, int? userId = null)
    {
        var lower = (text ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(lower))
        {
            return new AgentResponseDto
            {
                Id = Guid.NewGuid().ToString(),
                Role = "assistant",
                Type = "text",
                Text = "Ask Agent needs a search question before it can look through the GeoTrack catalog.",
                SuggestionPills = GetDefaultPills(),
                Timestamp = DateTime.UtcNow
            };
        }

        // Catalog search shell only; no fabricated AI planning response.
        if (ContainsAny(lower, "beach", "swim", "coast", "sea"))
            return BuildCardsResponse("Beach Recommendations", await GetPlaceCards("beach", 3, userId));

        if (ContainsAny(lower, "restaurant", "food", "eat", "dinner", "lunch", "cuisine"))
            return BuildCardsResponse("Restaurant Picks", await GetPlaceCards("restaurant", 3, userId));

        if (ContainsAny(lower, "hotel", "stay", "accommodation", "sleep"))
            return BuildCardsResponse("Top Stays", await GetHotelCards(3, userId));

        if (ContainsAny(lower, "event", "festival", "concert", "show"))
            return BuildCardsResponse("Upcoming Events", await GetEventCards(3, userId));

        if (ContainsAny(lower, "nature", "hike", "park", "island", "outdoor"))
            return BuildCardsResponse("Nature Escapes", await GetPlaceCards("nature", 3, userId));

        if (ContainsAny(lower, "culture", "museum", "history", "landmark"))
            return BuildCardsResponse("Cultural Highlights", await GetPlaceCards("museum", 3, userId));

        if (ContainsAny(lower, "fun", "weekend", "things to do", "activities", "recommend"))
            return BuildCardsResponse("Suggested Places", await GetMixedCards(3, userId));

        // Default text response
        return new AgentResponseDto
        {
            Id = Guid.NewGuid().ToString(),
            Role = "assistant",
            Type = "text",
            Text = "Ask Agent is currently a limited GeoTrack catalog search, not a live AI planner. Try asking for beaches, restaurants, stays, events, culture, or nature and I can search the current database.",
            SuggestionPills = GetDefaultPills(),
            Timestamp = DateTime.UtcNow
        };
    }

    private AgentResponseDto BuildCardsResponse(string title, List<AgentRecommendationCardDto> cards)
    {
        if (!cards.Any())
        {
            return new AgentResponseDto
            {
                Id = Guid.NewGuid().ToString(),
                Role = "assistant",
                Type = "text",
            Text = "I couldn't find matching GeoTrack catalog results for that request.",
                SuggestionPills = GetDefaultPills(),
                Timestamp = DateTime.UtcNow
            };
        }

        return new AgentResponseDto
        {
            Id = Guid.NewGuid().ToString(),
            Role = "assistant",
            Type = "suggestion_cards",
            Text = "I searched the current GeoTrack catalog and found these options:",
            CardsTitle = title,
            Cards = cards,
            SuggestionPills = GetDefaultPills(),
            Timestamp = DateTime.UtcNow
        };
    }

    private async Task<List<AgentRecommendationCardDto>> GetPlaceCards(string category, int count, int? userId)
    {
        var places = await _db.Places
            .AsNoTracking()
            .Where(p => p.PlaceTags.Any(pt => pt.Tag.Slug.ToLower().Contains(category) || pt.Tag.Name.ToLower().Contains(category))
                        || p.PlaceType.Name.ToLower().Contains(category))
            .Select(p => new
            {
                p.Id,
                p.Name,
                p.Description,
                Image = p.Images
                    .OrderByDescending(image => image.IsMain)
                    .ThenBy(image => image.SortOrder)
                    .ThenBy(image => image.CreatedAt)
                    .ThenBy(image => image.Id)
                    .Select(image => image.Url)
                    .FirstOrDefault(),
                p.AverageRating
            })
            .Take(count)
            .ToListAsync();
        var favoriteIds = userId.HasValue
            ? await _favoriteService.GetFavoriteIdsAsync(userId.Value, "place")
            : new HashSet<int>();

        return places.Select(p => new AgentRecommendationCardDto
        {
            Id = p.Id,
            EntityType = "place",
            DetailUrl = $"/places/{p.Id}",
            Title = p.Name,
            Description = TruncateDescription(p.Description),
            Image = p.Image,
            Rating = p.AverageRating,
            DurationHours = null,
            DistanceKm = null,
            IsFavorite = favoriteIds.Contains(p.Id)
        }).ToList();
    }

    private async Task<List<AgentRecommendationCardDto>> GetHotelCards(int count, int? userId)
    {
        var hotels = await _db.Places
            .AsNoTracking()
            .Where(p => p.Stay != null)
            .Select(p => new
            {
                p.Id,
                p.Name,
                p.Description,
                Image = p.Images
                    .OrderByDescending(image => image.IsMain)
                    .ThenBy(image => image.SortOrder)
                    .ThenBy(image => image.CreatedAt)
                    .ThenBy(image => image.Id)
                    .Select(image => image.Url)
                    .FirstOrDefault(),
                p.AverageRating
            })
            .Take(count)
            .ToListAsync();
        var favoriteIds = userId.HasValue
            ? await _favoriteService.GetFavoriteIdsAsync(userId.Value, "place")
            : new HashSet<int>();

        return hotels.Select(p => new AgentRecommendationCardDto
        {
            Id = p.Id,
            EntityType = "place",
            DetailUrl = $"/stays/{p.Id}",
            Title = p.Name,
            Description = TruncateDescription(p.Description),
            Image = p.Image,
            Rating = p.AverageRating,
            DurationHours = null,
            DistanceKm = null,
            IsFavorite = favoriteIds.Contains(p.Id)
        }).ToList();
    }

    private async Task<List<AgentRecommendationCardDto>> GetEventCards(int count, int? userId)
    {
        var events = await _db.Events
            .AsNoTracking()
            .Where(e => e.EndDate >= DateTime.UtcNow)
            .Select(e => new
            {
                e.Id,
                e.Name,
                e.Description,
                Image = e.Images
                    .OrderByDescending(image => image.IsMain)
                    .ThenBy(image => image.SortOrder)
                    .ThenBy(image => image.CreatedAt)
                    .ThenBy(image => image.Id)
                    .Select(image => image.Url)
                    .FirstOrDefault(),
                e.StartDate,
                e.EndDate
            })
            .Take(count)
            .ToListAsync();
        var favoriteIds = userId.HasValue
            ? await _favoriteService.GetFavoriteIdsAsync(userId.Value, "event")
            : new HashSet<int>();

        return events.Select(e => new AgentRecommendationCardDto
        {
            Id = e.Id,
            EntityType = "event",
            DetailUrl = $"/events/{e.Id}",
            Title = e.Name,
            Description = TruncateDescription(e.Description),
            Image = e.Image,
            Rating = null,
            DurationHours = (e.EndDate - e.StartDate).TotalHours,
            DistanceKm = null,
            IsFavorite = favoriteIds.Contains(e.Id)
        }).ToList();
    }

    private async Task<List<AgentRecommendationCardDto>> GetMixedCards(int count, int? userId)
    {
        var places = await _db.Places
            .AsNoTracking()
            .OrderByDescending(p => p.AverageRating)
            .Select(p => new
            {
                p.Id,
                p.Name,
                p.Description,
                IsStay = p.Stay != null,
                Image = p.Images
                    .OrderByDescending(image => image.IsMain)
                    .ThenBy(image => image.SortOrder)
                    .ThenBy(image => image.CreatedAt)
                    .ThenBy(image => image.Id)
                    .Select(image => image.Url)
                    .FirstOrDefault(),
                p.AverageRating
            })
            .Take(count)
            .ToListAsync();
        var favoriteIds = userId.HasValue
            ? await _favoriteService.GetFavoriteIdsAsync(userId.Value, "place")
            : new HashSet<int>();

        return places.Select(p => new AgentRecommendationCardDto
        {
            Id = p.Id,
            EntityType = "place",
            DetailUrl = p.IsStay ? $"/stays/{p.Id}" : $"/places/{p.Id}",
            Title = p.Name,
            Description = TruncateDescription(p.Description),
            Image = p.Image,
            Rating = p.AverageRating,
            DurationHours = null,
            DistanceKm = null,
            IsFavorite = favoriteIds.Contains(p.Id)
        }).ToList();
    }

    private static bool ContainsAny(string text, params string[] keywords) =>
        keywords.Any(k => text.Contains(k));

    private static string? TruncateDescription(string? description)
    {
        return description is { Length: > 100 }
            ? description[..100] + "..."
            : description;
    }

    private static List<SuggestionPillDto> GetDefaultPills() => new()
    {
        new() { Label = "Top Attractions", Prompt = "What are the top attractions?" },
        new() { Label = "Best Restaurants", Prompt = "Recommend the best restaurants" },
        new() { Label = "Beaches", Prompt = "Show me the best beaches" },
        new() { Label = "Events", Prompt = "What events are happening?" }
    };
}
