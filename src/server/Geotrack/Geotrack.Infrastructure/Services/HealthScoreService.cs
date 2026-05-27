using Geotrack.Application.Analytics;
using Geotrack.Application.DTOs.Admin;
using Geotrack.Application.Interfaces.Service;
using Geotrack.Domain.Entities.Core;
using Geotrack.Domain.Entities.Reviews;
using Geotrack.Domain.Entities.Users;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;

namespace Geotrack.Infrastructure.Services;

public sealed class HealthScoreService : IHealthScoreService
{
    private const string BoundaryGeometryPropertyName = "BoundaryGeometry";
    private const int FreshContentDays = 180;
    private const int OldPendingDays = 14;

    private static readonly string[] ViewEventTypes =
    {
        AnalyticsEventTypes.PageView,
        AnalyticsEventTypes.DetailView
    };

    private static readonly string[] ExternalActionEventTypes =
    {
        AnalyticsEventTypes.OutboundLinkClicked,
        AnalyticsEventTypes.DirectionsStarted
    };

    private readonly AppDbContext _db;

    public HealthScoreService(AppDbContext db)
    {
        _db = db;
    }

    public HealthScorePeriod ResolvePeriod(string? period, DateTime? nowUtc = null)
    {
        var now = nowUtc ?? DateTime.UtcNow;
        if (now.Kind != DateTimeKind.Utc)
        {
            now = DateTime.SpecifyKind(now, DateTimeKind.Utc);
        }

        var normalized = string.IsNullOrWhiteSpace(period)
            ? "1m"
            : period.Trim().ToLowerInvariant();

        return normalized switch
        {
            "24h" or "day" => new HealthScorePeriod("24h", "Last 24 hours", now.AddDays(-1), now),
            "7d" or "week" => new HealthScorePeriod("7d", "Last 7 days", now.AddDays(-7), now),
            "1m" or "30d" or "month" => new HealthScorePeriod("1m", "Last 30 days", now.AddDays(-30), now),
            "3m" or "90d" or "quarter" => new HealthScorePeriod("3m", "Last 90 days", now.AddDays(-90), now),
            "6m" => new HealthScorePeriod("6m", "Last 6 months", now.AddMonths(-6), now),
            "12m" or "1y" or "year" => new HealthScorePeriod("12m", "Last 12 months", now.AddMonths(-12), now),
            "all" or "all-time" => new HealthScorePeriod(
                "all",
                "All time",
                DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc),
                now),
            _ => throw new InvalidOperationException("Unsupported period.")
        };
    }

    public async Task<IReadOnlyDictionary<int, TerritoryHealthDto>> GetRegionHealthScoresAsync(
        IReadOnlyCollection<int> regionIds,
        HealthScorePeriod period,
        CancellationToken cancellationToken = default)
    {
        var ids = NormalizeIds(regionIds);
        if (ids.Count == 0)
            return new Dictionary<int, TerritoryHealthDto>();

        var contentCounts = await GetRegionContentCountsAsync(ids, cancellationToken);
        var quality = await GetRegionQualityAggregatesAsync(ids, period.End, cancellationToken);
        var engagement = await GetRegionEngagementAggregatesAsync(ids, period, cancellationToken);
        var reviews = await GetRegionReviewAggregatesAsync(ids, period, cancellationToken);
        var moderation = await GetRegionModerationAggregatesAsync(ids, period.End, cancellationToken);

        return ids.ToDictionary(
            regionId => regionId,
            regionId =>
            {
                var regionEngagement = engagement.GetValueOrDefault(regionId, new RegionEngagementAggregate());
                var regionReviews = reviews.GetValueOrDefault(regionId, ReviewAggregate.Empty);
                regionEngagement.Reviews = regionReviews.RecentCount;
                regionEngagement.TotalReviews = regionReviews.Count;
                regionEngagement.AverageRating = regionReviews.AverageRating;

                return CalculateRegionHealth(
                    contentCounts.GetValueOrDefault(regionId, RegionContentCounts.Empty),
                    quality.GetValueOrDefault(regionId, RegionQualityAggregate.Empty),
                    regionEngagement,
                    moderation.GetValueOrDefault(regionId, ModerationAggregate.Empty),
                    period);
            });
    }

    public async Task<IReadOnlyDictionary<int, TerritoryHealthDto>> GetDestinationHealthScoresAsync(
        IReadOnlyCollection<int> destinationIds,
        int? regionId,
        HealthScorePeriod period,
        CancellationToken cancellationToken = default)
    {
        var ids = NormalizeIds(destinationIds);
        if (ids.Count == 0)
            return new Dictionary<int, TerritoryHealthDto>();

        var query = _db.Destinations
            .AsNoTracking()
            .Where(destination => ids.Contains(destination.Id));

        if (regionId.HasValue)
        {
            query = query.Where(destination => destination.RegionId == regionId.Value);
        }

        var rows = await query
            .Select(destination => new DestinationHealthSource
            {
                Id = destination.Id,
                RegionId = destination.RegionId,
                HasSummary = destination.Summary != null && destination.Summary != string.Empty,
                HasDescription = destination.Description != string.Empty,
                HasCardImage = destination.Images.Any(image =>
                    image.TargetType == EntityImageTargetType.Destination &&
                    (image.ImageRole == EntityImageRole.Card || image.IsMain)),
                HasCoverImage = destination.Images.Any(image =>
                    image.TargetType == EntityImageTargetType.Destination &&
                    (image.ImageRole == EntityImageRole.Cover || image.IsMain)),
                HasTags = destination.DestinationTags.Any(),
                HasCoordinates = destination.Latitude != 0 || destination.Longitude != 0,
                HasBoundary =
                    destination.BoundaryShapeType != null ||
                    destination.BoundaryNote != null ||
                    EF.Property<Geometry?>(destination, BoundaryGeometryPropertyName) != null,
                LocationsCount = destination.Locations.Count,
                PlacesCount = destination.Locations
                    .SelectMany(location => location.Places)
                    .Count(place => place.Stay == null),
                StaysCount = destination.Locations
                    .SelectMany(location => location.Places)
                    .Count(place => place.Stay != null),
                EventsCount = destination.Events.Count,
                CreatedAt = destination.CreatedAt,
                UpdatedAt = destination.UpdatedAt
            })
            .ToListAsync(cancellationToken);

        var scopedIds = rows.Select(row => row.Id).ToList();
        var engagement = await GetDestinationEngagementAggregatesAsync(scopedIds, period, cancellationToken);
        var reviews = await GetDestinationReviewAggregatesAsync(scopedIds, period, cancellationToken);
        var moderation = await GetDestinationModerationAggregatesAsync(scopedIds, period.End, cancellationToken);

        return rows.ToDictionary(
            row => row.Id,
            row => CalculateDestinationHealth(
                row,
                engagement.GetValueOrDefault(row.Id, new DestinationEngagementAggregate()),
                reviews.GetValueOrDefault(row.Id, ReviewAggregate.Empty),
                moderation.GetValueOrDefault(row.Id, ModerationAggregate.Empty),
                period));
    }

    private static List<int> NormalizeIds(IReadOnlyCollection<int> ids) =>
        ids
            .Where(id => id > 0)
            .Distinct()
            .ToList();

    private static TerritoryHealthDto CalculateRegionHealth(
        RegionContentCounts counts,
        RegionQualityAggregate quality,
        RegionEngagementAggregate engagement,
        ModerationAggregate moderation,
        HealthScorePeriod period)
    {
        var drivers = new List<TerritoryHealthInsightDto>();
        var issues = new List<TerritoryHealthInsightDto>();
        var recommendations = new List<TerritoryHealthInsightDto>();
        var totalContent = counts.Total;

        var coverageScore = 0;
        if (counts.Destinations > 0) coverageScore += 7;
        if (counts.Locations > 0) coverageScore += 6;
        if (counts.Places + counts.Stays > 0) coverageScore += 7;
        if (counts.Events > 0) coverageScore += 5;

        var completenessScore = quality.PossibleSignals <= 0
            ? 0
            : ClampScore((int)Math.Round(25.0 * quality.CompletedSignals / quality.PossibleSignals));

        var engagementScore =
            ScaleToPoints(engagement.Views, 50, 6) +
            ScaleToPoints(engagement.UniqueVisitors, 25, 5) +
            ScaleToPoints(engagement.Saves, 10, 4) +
            ScaleToPoints(engagement.ExternalClicks, 8, 3) +
            ScaleToPoints(engagement.Reviews, 10, 4) +
            (engagement.TotalReviews > 0 ? ScaleRating(engagement.AverageRating, 3) : 0);

        var moderationPenalty = Math.Min(15, (moderation.PendingRequests * 2) + (moderation.OldPendingRequests * 2));
        var moderationScore = Math.Max(0, 15 - moderationPenalty);

        var freshnessScore =
            RatioToPoints(quality.RecentlyUpdatedItems, quality.TotalItems, 5) +
            ScaleToPoints(engagement.Views, 15, 3) +
            ScaleToPoints(engagement.Reviews, 3, 2);

        var score = ClampScore(coverageScore + completenessScore + engagementScore + moderationScore + freshnessScore);
        if (totalContent == 0)
        {
            score = 0;
        }

        drivers.Add(Driver(
            "content_coverage",
            "Content coverage",
            coverageScore,
            25,
            DriverStatus(coverageScore, 25),
            totalContent == 0
                ? "No content is present in this region."
                : $"{totalContent} content item(s) are present in this region."));
        drivers.Add(Driver(
            "content_completeness",
            "Content completeness",
            completenessScore,
            25,
            DriverStatus(completenessScore, 25),
            $"{Math.Round(quality.QualityRatio * 100)}% of tracked content quality signals are complete."));
        drivers.Add(Driver(
            "engagement",
            "Engagement",
            engagementScore,
            25,
            DriverStatus(engagementScore, 25),
            $"{engagement.Views} view(s), {engagement.UniqueVisitors} visitor(s), {engagement.Saves} save(s), and {engagement.Reviews} recent review(s) in {period.Label.ToLowerInvariant()}."));
        drivers.Add(Driver(
            "moderation",
            "Moderation health",
            moderationScore,
            15,
            DriverStatus(moderationScore, 15),
            moderation.PendingRequests == 0
                ? "No pending requests or reports are blocking this region."
                : $"{moderation.PendingRequests} pending request/report item(s) reduce operational health."));
        drivers.Add(Driver(
            "freshness",
            "Freshness",
            freshnessScore,
            10,
            DriverStatus(freshnessScore, 10),
            $"{quality.RecentlyUpdatedItems} of {quality.TotalItems} content item(s) were updated recently."));

        if (totalContent == 0)
        {
            issues.Add(Issue("no_regional_content", "critical", "No regional content", "This region has no destinations, locations, places, stays, or events yet."));
            recommendations.Add(Recommendation("add_region_inventory", 1, "Add regional inventory", "Add destinations, locations, places, stays, and events before this region can be healthy."));
        }
        else
        {
            if (counts.Destinations == 0)
            {
                issues.Add(Issue("no_destinations", "warning", "No destinations", "The region does not have destination inventory yet."));
                recommendations.Add(Recommendation("add_destinations", 1, "Add destinations", "Add destinations to establish region coverage."));
            }

            if (counts.Locations == 0)
            {
                issues.Add(Issue("no_locations", "warning", "No locations", "The region does not have locations under its destinations."));
                recommendations.Add(Recommendation("add_locations", 2, "Add locations", "Add locations under destinations to improve geographic coverage."));
            }

            if (counts.Places + counts.Stays == 0)
            {
                issues.Add(Issue("low_places", "warning", "Low content depth", "The region does not have places or stays yet."));
                recommendations.Add(Recommendation("add_places", 3, "Add places and stays", "Add more places or stays to improve regional coverage."));
            }

            if (counts.Events == 0)
            {
                issues.Add(Issue("no_events", "info", "No events", "The region does not have event inventory yet."));
                recommendations.Add(Recommendation("add_events", 6, "Add events", "Add events when this region has current local activity."));
            }

            if (quality.QualityRatio < 0.65)
            {
                issues.Add(Issue("low_completeness", "warning", "Low content completeness", "Images, descriptions, tags, coordinates, or boundaries are missing across tracked content."));
                recommendations.Add(Recommendation("improve_completeness", 2, "Improve content completeness", "Fill missing imagery, descriptions, tags, coordinates, and boundaries across low-completeness content."));
            }

            if (engagement.Views == 0)
            {
                issues.Add(Issue("no_recent_views", "warning", "No recent views", $"No region content views were recorded in {period.Label.ToLowerInvariant()}."));
                recommendations.Add(Recommendation("improve_discovery", 4, "Improve discovery", "Improve imagery, tags, and content depth to increase region discovery."));
            }

            if (engagement.TotalReviews == 0)
            {
                issues.Add(Issue("no_reviews", "warning", "No reviews", "No reviews are available for region content yet."));
                recommendations.Add(Recommendation("encourage_reviews", 5, "Encourage reviews", "Encourage reviews on destination, place, location, and event pages."));
            }

            if (moderation.PendingRequests > 0)
            {
                issues.Add(Issue("pending_requests", "warning", "Pending requests", $"{moderation.PendingRequests} pending request/report item(s) need review."));
                recommendations.Add(Recommendation("review_pending_requests", 1, "Review pending requests", "Review pending contributions and reports to reduce moderation backlog."));
            }

            if (quality.TotalItems > 0 && quality.RecentlyUpdatedItems == 0)
            {
                issues.Add(Issue("stale_content", "info", "Stale content", "No tracked content was updated recently."));
                recommendations.Add(Recommendation("refresh_stale_content", 7, "Refresh stale content", "Refresh stale content so visitors see current regional information."));
            }
        }

        if (issues.Count == 0)
        {
            drivers.Add(Driver("healthy_baseline", "Healthy baseline", 0, 0, "positive", "Core content, engagement, and operational signals are present."));
        }

        return BuildHealthDto(score, period, drivers, issues, recommendations);
    }

    private static TerritoryHealthDto CalculateDestinationHealth(
        DestinationHealthSource source,
        DestinationEngagementAggregate engagement,
        ReviewAggregate reviews,
        ModerationAggregate moderation,
        HealthScorePeriod period)
    {
        var drivers = new List<TerritoryHealthInsightDto>();
        var issues = new List<TerritoryHealthInsightDto>();
        var recommendations = new List<TerritoryHealthInsightDto>();

        var completenessScore = 0;
        if (source.HasSummary) completenessScore += 5;
        if (source.HasDescription) completenessScore += 6;
        if (source.HasCardImage) completenessScore += 6;
        if (source.HasCoverImage) completenessScore += 6;
        if (source.HasTags) completenessScore += 5;
        if (source.HasCoordinates) completenessScore += 3;
        if (source.HasBoundary) completenessScore += 4;

        var depthScore =
            ScaleToPoints(source.LocationsCount, 3, 6) +
            ScaleToPoints(source.PlacesCount + source.StaysCount, 6, 8) +
            ScaleToPoints(source.EventsCount, 4, 6);

        var engagementScore =
            ScaleToPoints(engagement.Views, 30, 8) +
            ScaleToPoints(engagement.UniqueVisitors, 15, 5) +
            ScaleToPoints(engagement.Saves, 8, 4) +
            ScaleToPoints(engagement.ExternalClicks, 6, 3);

        var socialProofScore =
            ScaleToPoints(reviews.Count, 10, 8) +
            (reviews.Count > 0 ? ScaleRating(reviews.AverageRating, 7) : 0);

        var moderationPenalty = Math.Min(10, (moderation.PendingRequests * 2) + (moderation.OldPendingRequests * 2));
        var moderationScore = Math.Max(0, 10 - moderationPenalty);

        var score = ClampScore(completenessScore + depthScore + engagementScore + socialProofScore + moderationScore);

        drivers.Add(Driver(
            "content_completeness",
            "Content completeness",
            completenessScore,
            35,
            DriverStatus(completenessScore, 35),
            "Summary, description, imagery, tags, coordinates, and boundary signals are checked."));
        drivers.Add(Driver(
            "content_depth",
            "Content depth",
            depthScore,
            20,
            DriverStatus(depthScore, 20),
            $"{source.LocationsCount} location(s), {source.PlacesCount + source.StaysCount} place/stay item(s), and {source.EventsCount} event(s) are attached."));
        drivers.Add(Driver(
            "engagement",
            "Engagement",
            engagementScore,
            20,
            DriverStatus(engagementScore, 20),
            $"{engagement.Views} destination view(s), {engagement.UniqueVisitors} visitor(s), {engagement.Saves} save(s), and {engagement.ExternalClicks} external action(s) in {period.Label.ToLowerInvariant()}."));
        drivers.Add(Driver(
            "social_proof",
            "Social proof",
            socialProofScore,
            15,
            DriverStatus(socialProofScore, 15),
            reviews.Count == 0
                ? "No destination reviews are available yet."
                : $"{reviews.Count} review(s) with an average rating of {Math.Round(reviews.AverageRating, 1)}."));
        drivers.Add(Driver(
            "moderation",
            "Moderation health",
            moderationScore,
            10,
            DriverStatus(moderationScore, 10),
            moderation.PendingRequests == 0
                ? "No pending destination requests are blocking this item."
                : $"{moderation.PendingRequests} pending request(s) reduce destination health."));

        if (!source.HasBoundary)
        {
            issues.Add(Issue("missing_boundary", "warning", "Missing boundary", "Boundary is not present."));
            recommendations.Add(Recommendation("add_boundary", 1, "Recommendation", "Add or verify boundary geometry for better map filtering."));
        }

        if (engagement.Views == 0)
        {
            issues.Add(Issue("no_recent_views", "warning", "No recent views", "No destination views were recorded in the selected period."));
            recommendations.Add(Recommendation("improve_destination_discovery", 2, "Recommendation", "Improve imagery, tags, and content depth to increase destination discovery."));
        }

        if (reviews.Count == 0)
        {
            issues.Add(Issue("no_reviews", "warning", "No reviews", "No destination reviews are available yet."));
            recommendations.Add(Recommendation("encourage_destination_reviews", 3, "Recommendation", "Encourage reviews to improve confidence score."));
        }
        else if (reviews.AverageRating < 3.5)
        {
            issues.Add(Issue("low_rating", "warning", "Low rating", $"Average destination rating is {Math.Round(reviews.AverageRating, 1)}."));
            recommendations.Add(Recommendation("review_feedback", 3, "Recommendation", "Review recent visitor feedback and prioritize quality improvements."));
        }

        if (moderation.PendingRequests > 0)
        {
            issues.Add(Issue("pending_requests", "warning", "Pending requests", $"{moderation.PendingRequests} pending request(s) need review."));
            recommendations.Add(Recommendation("review_destination_requests", 1, "Recommendation", "Review pending contributions for this destination."));
        }

        if (!source.HasCardImage)
        {
            issues.Add(Issue("missing_card_image", "warning", "Missing card image", "Card image is not present."));
            recommendations.Add(Recommendation("add_card_image", 4, "Recommendation", "Add a card image to improve destination cards and map previews."));
        }

        if (!source.HasCoverImage)
        {
            issues.Add(Issue("missing_cover_image", "warning", "Missing cover image", "Cover image is not present."));
            recommendations.Add(Recommendation("add_cover_image", 5, "Recommendation", "Add a cover image to improve the destination detail page."));
        }

        if (!source.HasTags)
        {
            issues.Add(Issue("missing_tags", "warning", "Missing tags", "Tags are not present."));
            recommendations.Add(Recommendation("add_destination_tags", 4, "Recommendation", "Add tags so content appears in dynamic Explore sections."));
        }

        if (source.LocationsCount == 0 || source.PlacesCount + source.StaysCount == 0)
        {
            issues.Add(Issue("low_content_depth", "warning", "Low content depth", "This destination needs more locations, places, or stays."));
            recommendations.Add(Recommendation("add_destination_content", 4, "Recommendation", "Add more places, locations, or events to improve destination depth."));
        }

        var lastTouched = source.UpdatedAt ?? source.CreatedAt;
        if (lastTouched < period.End.AddDays(-FreshContentDays))
        {
            issues.Add(Issue("stale_content", "info", "Stale content", "Destination content has not been updated recently."));
            recommendations.Add(Recommendation("refresh_destination_content", 7, "Recommendation", "Refresh stale content so visitors see current destination information."));
        }

        if (issues.Count == 0)
        {
            drivers.Add(Driver("healthy_baseline", "Healthy baseline", 0, 0, "positive", "Core destination content and activity signals are present."));
        }

        return BuildHealthDto(score, period, drivers, issues, recommendations);
    }

    private async Task<Dictionary<int, RegionContentCounts>> GetRegionContentCountsAsync(
        List<int> regionIds,
        CancellationToken cancellationToken)
    {
        var destinationCounts = await _db.Destinations
            .AsNoTracking()
            .Where(destination => regionIds.Contains(destination.RegionId))
            .GroupBy(destination => destination.RegionId)
            .Select(group => new { RegionId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(row => row.RegionId, row => row.Count, cancellationToken);

        var locationCounts = await _db.Locations
            .AsNoTracking()
            .Where(location => regionIds.Contains(location.Destination.RegionId))
            .GroupBy(location => location.Destination.RegionId)
            .Select(group => new { RegionId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(row => row.RegionId, row => row.Count, cancellationToken);

        var placeCounts = await _db.Places
            .AsNoTracking()
            .Where(place => regionIds.Contains(place.Location.Destination.RegionId) && place.Stay == null)
            .GroupBy(place => place.Location.Destination.RegionId)
            .Select(group => new { RegionId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(row => row.RegionId, row => row.Count, cancellationToken);

        var stayCounts = await _db.Places
            .AsNoTracking()
            .Where(place => regionIds.Contains(place.Location.Destination.RegionId) && place.Stay != null)
            .GroupBy(place => place.Location.Destination.RegionId)
            .Select(group => new { RegionId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(row => row.RegionId, row => row.Count, cancellationToken);

        var eventCounts = await _db.Events
            .AsNoTracking()
            .Where(evt => regionIds.Contains(evt.Destination.RegionId))
            .GroupBy(evt => evt.Destination.RegionId)
            .Select(group => new { RegionId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(row => row.RegionId, row => row.Count, cancellationToken);

        return regionIds.ToDictionary(
            regionId => regionId,
            regionId => new RegionContentCounts(
                destinationCounts.GetValueOrDefault(regionId),
                locationCounts.GetValueOrDefault(regionId),
                placeCounts.GetValueOrDefault(regionId),
                stayCounts.GetValueOrDefault(regionId),
                eventCounts.GetValueOrDefault(regionId)));
    }

    private async Task<Dictionary<int, RegionQualityAggregate>> GetRegionQualityAggregatesAsync(
        List<int> regionIds,
        DateTime periodEnd,
        CancellationToken cancellationToken)
    {
        var recentCutoff = periodEnd.AddDays(-FreshContentDays);
        var rows = new List<QualitySignalRow>();

        rows.AddRange(await _db.Destinations
            .AsNoTracking()
            .Where(destination => regionIds.Contains(destination.RegionId))
            .Select(destination => new QualitySignalRow
            {
                RegionId = destination.RegionId,
                PossibleSignals = 5,
                CompletedSignals =
                    (destination.Images.Any(image => image.TargetType == EntityImageTargetType.Destination) ? 1 : 0) +
                    ((destination.Description != string.Empty || destination.Summary != null) ? 1 : 0) +
                    (destination.DestinationTags.Any() ? 1 : 0) +
                    ((destination.Latitude != 0 || destination.Longitude != 0) ? 1 : 0) +
                    ((destination.BoundaryShapeType != null ||
                      destination.BoundaryNote != null ||
                      EF.Property<Geometry?>(destination, BoundaryGeometryPropertyName) != null) ? 1 : 0),
                RecentlyUpdated = (destination.UpdatedAt ?? destination.CreatedAt) >= recentCutoff
            })
            .ToListAsync(cancellationToken));

        rows.AddRange(await _db.Locations
            .AsNoTracking()
            .Where(location => regionIds.Contains(location.Destination.RegionId))
            .Select(location => new QualitySignalRow
            {
                RegionId = location.Destination.RegionId,
                PossibleSignals = 5,
                CompletedSignals =
                    (location.Images.Any(image => image.TargetType == EntityImageTargetType.Location) ? 1 : 0) +
                    ((location.Description != string.Empty || location.Summary != null) ? 1 : 0) +
                    (location.LocationTags.Any() ? 1 : 0) +
                    ((location.Latitude != 0 || location.Longitude != 0) ? 1 : 0) +
                    ((location.BoundaryShapeType != null ||
                      location.BoundaryNote != null ||
                      EF.Property<Geometry?>(location, BoundaryGeometryPropertyName) != null) ? 1 : 0),
                RecentlyUpdated = (location.UpdatedAt ?? location.CreatedAt) >= recentCutoff
            })
            .ToListAsync(cancellationToken));

        rows.AddRange(await _db.Places
            .AsNoTracking()
            .Where(place => regionIds.Contains(place.Location.Destination.RegionId))
            .Select(place => new QualitySignalRow
            {
                RegionId = place.Location.Destination.RegionId,
                PossibleSignals = 5,
                CompletedSignals =
                    (place.Images.Any(image => image.TargetType == EntityImageTargetType.Place) ? 1 : 0) +
                    ((place.Description != string.Empty || place.Subtitle != null) ? 1 : 0) +
                    (place.PlaceTags.Any() ? 1 : 0) +
                    ((place.Latitude != 0 || place.Longitude != 0 || place.Address != null) ? 1 : 0) +
                    (place.PlaceTypeId > 0 ? 1 : 0),
                RecentlyUpdated = (place.UpdatedAt ?? place.CreatedAt) >= recentCutoff
            })
            .ToListAsync(cancellationToken));

        rows.AddRange(await _db.Events
            .AsNoTracking()
            .Where(evt => regionIds.Contains(evt.Destination.RegionId))
            .Select(evt => new QualitySignalRow
            {
                RegionId = evt.Destination.RegionId,
                PossibleSignals = 5,
                CompletedSignals =
                    (evt.Images.Any(image => image.TargetType == EntityImageTargetType.Event) ? 1 : 0) +
                    ((evt.Description != string.Empty || evt.Summary != null) ? 1 : 0) +
                    (evt.EventTags.Any() ? 1 : 0) +
                    (evt.StartDate != default ? 1 : 0) +
                    ((evt.LocationId.HasValue || evt.PlaceId.HasValue || evt.DestinationId > 0) ? 1 : 0),
                RecentlyUpdated = (evt.UpdatedAt ?? evt.CreatedAt) >= recentCutoff
            })
            .ToListAsync(cancellationToken));

        var grouped = rows
            .GroupBy(row => row.RegionId)
            .ToDictionary(
                group => group.Key,
                group => new RegionQualityAggregate(
                    group.Count(),
                    group.Sum(row => row.CompletedSignals),
                    group.Sum(row => row.PossibleSignals),
                    group.Count(row => row.RecentlyUpdated)));

        return regionIds.ToDictionary(
            regionId => regionId,
            regionId => grouped.GetValueOrDefault(regionId, RegionQualityAggregate.Empty));
    }

    private async Task<Dictionary<int, RegionEngagementAggregate>> GetRegionEngagementAggregatesAsync(
        List<int> regionIds,
        HealthScorePeriod period,
        CancellationToken cancellationToken)
    {
        var hits = await GetRegionAnalyticsHitsAsync(regionIds, period.Start, period.End, cancellationToken);
        var saveCounts = await GetRegionSaveCountsAsync(regionIds, period, cancellationToken);
        return regionIds.ToDictionary(
            regionId => regionId,
            regionId =>
            {
                var regionHits = hits.Where(hit => hit.RegionId == regionId).ToList();
                return new RegionEngagementAggregate
                {
                    Views = regionHits.Count(hit => ViewEventTypes.Contains(hit.EventType)),
                    UniqueVisitors = CountUniqueVisitors(regionHits),
                    Saves = saveCounts.GetValueOrDefault(regionId),
                    ExternalClicks = regionHits.Count(hit => ExternalActionEventTypes.Contains(hit.EventType))
                };
            });
    }

    private async Task<Dictionary<int, int>> GetRegionSaveCountsAsync(
        List<int> regionIds,
        HealthScorePeriod period,
        CancellationToken cancellationToken)
    {
        var result = regionIds.ToDictionary(regionId => regionId, _ => 0);

        foreach (var row in await (
            from favorite in _db.Favorites.AsNoTracking()
            join destination in _db.Destinations.AsNoTracking()
                on favorite.EntityId equals destination.Id
            where favorite.CreatedAt >= period.Start &&
                  favorite.CreatedAt < period.End &&
                  favorite.EntityType == AnalyticsEntityTypes.Destination &&
                  regionIds.Contains(destination.RegionId)
            group favorite by destination.RegionId into groupRow
            select new { RegionId = groupRow.Key, Count = groupRow.Count() })
            .ToListAsync(cancellationToken))
        {
            result[row.RegionId] += row.Count;
        }

        foreach (var row in await (
            from favorite in _db.Favorites.AsNoTracking()
            join location in _db.Locations.AsNoTracking()
                on favorite.EntityId equals location.Id
            where favorite.CreatedAt >= period.Start &&
                  favorite.CreatedAt < period.End &&
                  favorite.EntityType == AnalyticsEntityTypes.Location &&
                  regionIds.Contains(location.Destination.RegionId)
            group favorite by location.Destination.RegionId into groupRow
            select new { RegionId = groupRow.Key, Count = groupRow.Count() })
            .ToListAsync(cancellationToken))
        {
            result[row.RegionId] += row.Count;
        }

        foreach (var row in await (
            from favorite in _db.Favorites.AsNoTracking()
            join place in _db.Places.AsNoTracking()
                on favorite.EntityId equals place.Id
            where favorite.CreatedAt >= period.Start &&
                  favorite.CreatedAt < period.End &&
                  (favorite.EntityType == AnalyticsEntityTypes.Place ||
                   favorite.EntityType == AnalyticsEntityTypes.Hotel) &&
                  regionIds.Contains(place.Location.Destination.RegionId)
            group favorite by place.Location.Destination.RegionId into groupRow
            select new { RegionId = groupRow.Key, Count = groupRow.Count() })
            .ToListAsync(cancellationToken))
        {
            result[row.RegionId] += row.Count;
        }

        foreach (var row in await (
            from favorite in _db.Favorites.AsNoTracking()
            join eventEntity in _db.Events.AsNoTracking()
                on favorite.EntityId equals eventEntity.Id
            where favorite.CreatedAt >= period.Start &&
                  favorite.CreatedAt < period.End &&
                  favorite.EntityType == AnalyticsEntityTypes.Event &&
                  regionIds.Contains(eventEntity.Destination.RegionId)
            group favorite by eventEntity.Destination.RegionId into groupRow
            select new { RegionId = groupRow.Key, Count = groupRow.Count() })
            .ToListAsync(cancellationToken))
        {
            result[row.RegionId] += row.Count;
        }

        foreach (var row in await (
            from item in _db.CollectionItems.AsNoTracking()
            join destination in _db.Destinations.AsNoTracking()
                on item.DestinationId!.Value equals destination.Id
            where item.CreatedAt >= period.Start &&
                  item.CreatedAt < period.End &&
                  item.DestinationId.HasValue &&
                  regionIds.Contains(destination.RegionId)
            group item by destination.RegionId into groupRow
            select new { RegionId = groupRow.Key, Count = groupRow.Count() })
            .ToListAsync(cancellationToken))
        {
            result[row.RegionId] += row.Count;
        }

        foreach (var row in await (
            from item in _db.CollectionItems.AsNoTracking()
            join location in _db.Locations.AsNoTracking()
                on item.LocationId!.Value equals location.Id
            where item.CreatedAt >= period.Start &&
                  item.CreatedAt < period.End &&
                  item.LocationId.HasValue &&
                  regionIds.Contains(location.Destination.RegionId)
            group item by location.Destination.RegionId into groupRow
            select new { RegionId = groupRow.Key, Count = groupRow.Count() })
            .ToListAsync(cancellationToken))
        {
            result[row.RegionId] += row.Count;
        }

        foreach (var row in await (
            from item in _db.CollectionItems.AsNoTracking()
            join place in _db.Places.AsNoTracking()
                on item.PlaceId!.Value equals place.Id
            where item.CreatedAt >= period.Start &&
                  item.CreatedAt < period.End &&
                  item.PlaceId.HasValue &&
                  regionIds.Contains(place.Location.Destination.RegionId)
            group item by place.Location.Destination.RegionId into groupRow
            select new { RegionId = groupRow.Key, Count = groupRow.Count() })
            .ToListAsync(cancellationToken))
        {
            result[row.RegionId] += row.Count;
        }

        foreach (var row in await (
            from item in _db.CollectionItems.AsNoTracking()
            join eventEntity in _db.Events.AsNoTracking()
                on item.EventId!.Value equals eventEntity.Id
            where item.CreatedAt >= period.Start &&
                  item.CreatedAt < period.End &&
                  item.EventId.HasValue &&
                  regionIds.Contains(eventEntity.Destination.RegionId)
            group item by eventEntity.Destination.RegionId into groupRow
            select new { RegionId = groupRow.Key, Count = groupRow.Count() })
            .ToListAsync(cancellationToken))
        {
            result[row.RegionId] += row.Count;
        }

        return result;
    }

    private async Task<List<AnalyticsRegionHit>> GetRegionAnalyticsHitsAsync(
        List<int> regionIds,
        DateTime periodStart,
        DateTime periodEnd,
        CancellationToken cancellationToken)
    {
        IQueryable<AnalyticsEventBaseRow> baseEvents = _db.AnalyticsEvents
            .AsNoTracking()
            .Where(evt => evt.OccurredAt >= periodStart && evt.OccurredAt < periodEnd)
            .Select(evt => new AnalyticsEventBaseRow
            {
                EventId = evt.Id,
                UserId = evt.UserId,
                AnonymousSessionId = evt.AnonymousSessionId,
                EventType = evt.EventType,
                EntityType = evt.EntityType,
                EntityId = evt.EntityId,
                DestinationId = evt.DestinationId,
                OccurredAt = evt.OccurredAt
            });

        var hits = new List<AnalyticsRegionHit>();

        hits.AddRange(await (
            from evt in baseEvents
            where evt.DestinationId.HasValue
            join destination in _db.Destinations.AsNoTracking()
                on evt.DestinationId!.Value equals destination.Id
            where regionIds.Contains(destination.RegionId)
            select new AnalyticsRegionHit
            {
                EventId = evt.EventId,
                RegionId = destination.RegionId,
                UserId = evt.UserId,
                AnonymousSessionId = evt.AnonymousSessionId,
                EventType = evt.EventType,
                EntityType = evt.EntityType,
                EntityId = evt.EntityId,
                OccurredAt = evt.OccurredAt
            }).ToListAsync(cancellationToken));

        hits.AddRange(await (
            from evt in baseEvents
            where evt.EntityType == AnalyticsEntityTypes.Destination && evt.EntityId.HasValue
            join destination in _db.Destinations.AsNoTracking()
                on evt.EntityId!.Value equals destination.Id
            where regionIds.Contains(destination.RegionId)
            select new AnalyticsRegionHit
            {
                EventId = evt.EventId,
                RegionId = destination.RegionId,
                UserId = evt.UserId,
                AnonymousSessionId = evt.AnonymousSessionId,
                EventType = evt.EventType,
                EntityType = evt.EntityType,
                EntityId = evt.EntityId,
                OccurredAt = evt.OccurredAt
            }).ToListAsync(cancellationToken));

        hits.AddRange(await (
            from evt in baseEvents
            where evt.EntityType == AnalyticsEntityTypes.Location && evt.EntityId.HasValue
            join location in _db.Locations.AsNoTracking()
                on evt.EntityId!.Value equals location.Id
            where regionIds.Contains(location.Destination.RegionId)
            select new AnalyticsRegionHit
            {
                EventId = evt.EventId,
                RegionId = location.Destination.RegionId,
                UserId = evt.UserId,
                AnonymousSessionId = evt.AnonymousSessionId,
                EventType = evt.EventType,
                EntityType = evt.EntityType,
                EntityId = evt.EntityId,
                OccurredAt = evt.OccurredAt
            }).ToListAsync(cancellationToken));

        hits.AddRange(await (
            from evt in baseEvents
            where (evt.EntityType == AnalyticsEntityTypes.Place || evt.EntityType == AnalyticsEntityTypes.Hotel) &&
                  evt.EntityId.HasValue
            join place in _db.Places.AsNoTracking()
                on evt.EntityId!.Value equals place.Id
            where regionIds.Contains(place.Location.Destination.RegionId)
            select new AnalyticsRegionHit
            {
                EventId = evt.EventId,
                RegionId = place.Location.Destination.RegionId,
                UserId = evt.UserId,
                AnonymousSessionId = evt.AnonymousSessionId,
                EventType = evt.EventType,
                EntityType = evt.EntityType,
                EntityId = evt.EntityId,
                OccurredAt = evt.OccurredAt
            }).ToListAsync(cancellationToken));

        hits.AddRange(await (
            from evt in baseEvents
            where evt.EntityType == AnalyticsEntityTypes.Event && evt.EntityId.HasValue
            join eventEntity in _db.Events.AsNoTracking()
                on evt.EntityId!.Value equals eventEntity.Id
            where regionIds.Contains(eventEntity.Destination.RegionId)
            select new AnalyticsRegionHit
            {
                EventId = evt.EventId,
                RegionId = eventEntity.Destination.RegionId,
                UserId = evt.UserId,
                AnonymousSessionId = evt.AnonymousSessionId,
                EventType = evt.EventType,
                EntityType = evt.EntityType,
                EntityId = evt.EntityId,
                OccurredAt = evt.OccurredAt
            }).ToListAsync(cancellationToken));

        hits.AddRange(await (
            from evt in baseEvents
            where evt.EntityType == AnalyticsEntityTypes.Region && evt.EntityId.HasValue
            join region in _db.Regions.AsNoTracking()
                on evt.EntityId!.Value equals region.Id
            where regionIds.Contains(region.Id)
            select new AnalyticsRegionHit
            {
                EventId = evt.EventId,
                RegionId = region.Id,
                UserId = evt.UserId,
                AnonymousSessionId = evt.AnonymousSessionId,
                EventType = evt.EventType,
                EntityType = evt.EntityType,
                EntityId = evt.EntityId,
                OccurredAt = evt.OccurredAt
            }).ToListAsync(cancellationToken));

        return hits
            .GroupBy(hit => hit.EventId)
            .Select(group => group.First())
            .ToList();
    }

    private async Task<Dictionary<int, ReviewAggregate>> GetRegionReviewAggregatesAsync(
        List<int> regionIds,
        HealthScorePeriod period,
        CancellationToken cancellationToken)
    {
        var aggregates = regionIds.ToDictionary(regionId => regionId, _ => new MutableReviewAggregate());

        foreach (var row in await _db.DestinationReviews
            .AsNoTracking()
            .Where(review => regionIds.Contains(review.Destination.RegionId))
            .GroupBy(review => review.Destination.RegionId)
            .Select(group => new
            {
                RegionId = group.Key,
                Count = group.Count(),
                Sum = group.Sum(review => review.Rating),
                RecentCount = group.Count(review => review.CreatedAt >= period.Start && review.CreatedAt < period.End)
            })
            .ToListAsync(cancellationToken))
        {
            aggregates[row.RegionId].Add(row.Count, row.Sum, row.RecentCount);
        }

        foreach (var row in await _db.LocationReviews
            .AsNoTracking()
            .Where(review => regionIds.Contains(review.Location.Destination.RegionId))
            .GroupBy(review => review.Location.Destination.RegionId)
            .Select(group => new
            {
                RegionId = group.Key,
                Count = group.Count(),
                Sum = group.Sum(review => review.Rating),
                RecentCount = group.Count(review => review.CreatedAt >= period.Start && review.CreatedAt < period.End)
            })
            .ToListAsync(cancellationToken))
        {
            aggregates[row.RegionId].Add(row.Count, row.Sum, row.RecentCount);
        }

        foreach (var row in await _db.PlaceReviews
            .AsNoTracking()
            .Where(review => regionIds.Contains(review.Place.Location.Destination.RegionId))
            .GroupBy(review => review.Place.Location.Destination.RegionId)
            .Select(group => new
            {
                RegionId = group.Key,
                Count = group.Count(),
                Sum = group.Sum(review => review.Rating),
                RecentCount = group.Count(review => review.CreatedAt >= period.Start && review.CreatedAt < period.End)
            })
            .ToListAsync(cancellationToken))
        {
            aggregates[row.RegionId].Add(row.Count, row.Sum, row.RecentCount);
        }

        foreach (var row in await _db.EventReviews
            .AsNoTracking()
            .Where(review => regionIds.Contains(review.Event.Destination.RegionId))
            .GroupBy(review => review.Event.Destination.RegionId)
            .Select(group => new
            {
                RegionId = group.Key,
                Count = group.Count(),
                Sum = group.Sum(review => review.Rating),
                RecentCount = group.Count(review => review.CreatedAt >= period.Start && review.CreatedAt < period.End)
            })
            .ToListAsync(cancellationToken))
        {
            aggregates[row.RegionId].Add(row.Count, row.Sum, row.RecentCount);
        }

        return aggregates.ToDictionary(pair => pair.Key, pair => pair.Value.ToReviewAggregate());
    }

    private async Task<Dictionary<int, ModerationAggregate>> GetRegionModerationAggregatesAsync(
        List<int> regionIds,
        DateTime periodEnd,
        CancellationToken cancellationToken)
    {
        var oldCutoff = periodEnd.AddDays(-OldPendingDays);
        var aggregates = regionIds.ToDictionary(regionId => regionId, _ => new MutableModerationAggregate());

        foreach (var row in await _db.PlaceContributions
            .AsNoTracking()
            .Where(contribution =>
                contribution.Status == PlaceContributionStatus.PendingReview &&
                ((contribution.LocationId.HasValue &&
                  contribution.Location != null &&
                  regionIds.Contains(contribution.Location.Destination.RegionId)) ||
                 (contribution.PlaceId.HasValue &&
                  contribution.Place != null &&
                  regionIds.Contains(contribution.Place.Location.Destination.RegionId))))
            .Select(contribution => new
            {
                RegionId = contribution.LocationId.HasValue
                    ? contribution.Location!.Destination.RegionId
                    : contribution.Place!.Location.Destination.RegionId,
                IsOld = contribution.SubmittedAt < oldCutoff
            })
            .GroupBy(row => row.RegionId)
            .Select(group => new
            {
                RegionId = group.Key,
                Pending = group.Count(),
                Old = group.Count(row => row.IsOld)
            })
            .ToListAsync(cancellationToken))
        {
            aggregates[row.RegionId].Add(row.Pending, row.Old);
        }

        foreach (var row in await _db.ReviewReports
            .AsNoTracking()
            .Where(report =>
                report.Status == ReviewReportStatus.Pending &&
                regionIds.Contains(report.RegionId))
            .GroupBy(report => report.RegionId)
            .Select(group => new
            {
                RegionId = group.Key,
                Pending = group.Count(),
                Old = group.Count(report => report.CreatedAt < oldCutoff)
            })
            .ToListAsync(cancellationToken))
        {
            aggregates[row.RegionId].Add(row.Pending, row.Old);
        }

        return aggregates.ToDictionary(pair => pair.Key, pair => pair.Value.ToModerationAggregate());
    }

    private async Task<Dictionary<int, DestinationEngagementAggregate>> GetDestinationEngagementAggregatesAsync(
        List<int> destinationIds,
        HealthScorePeriod period,
        CancellationToken cancellationToken)
    {
        if (destinationIds.Count == 0)
            return new Dictionary<int, DestinationEngagementAggregate>();

        var saveCounts = await GetDestinationSaveCountsAsync(destinationIds, period, cancellationToken);
        var events = await _db.AnalyticsEvents
            .AsNoTracking()
            .Where(evt =>
                evt.OccurredAt >= period.Start &&
                evt.OccurredAt < period.End &&
                evt.EntityType == AnalyticsEntityTypes.Destination &&
                evt.EntityId.HasValue &&
                destinationIds.Contains(evt.EntityId.Value))
            .Select(evt => new DestinationAnalyticsHit
            {
                EventId = evt.Id,
                DestinationId = evt.EntityId!.Value,
                UserId = evt.UserId,
                AnonymousSessionId = evt.AnonymousSessionId,
                EventType = evt.EventType
            })
            .ToListAsync(cancellationToken);

        return destinationIds.ToDictionary(
            destinationId => destinationId,
            destinationId =>
            {
                var destinationEvents = events.Where(evt => evt.DestinationId == destinationId).ToList();
                return new DestinationEngagementAggregate
                {
                    Views = destinationEvents.Count(evt => ViewEventTypes.Contains(evt.EventType)),
                    UniqueVisitors = CountUniqueVisitors(destinationEvents),
                    Saves = saveCounts.GetValueOrDefault(destinationId),
                    ExternalClicks = destinationEvents.Count(evt => ExternalActionEventTypes.Contains(evt.EventType))
                };
            });
    }

    private async Task<Dictionary<int, int>> GetDestinationSaveCountsAsync(
        List<int> destinationIds,
        HealthScorePeriod period,
        CancellationToken cancellationToken)
    {
        var result = destinationIds.ToDictionary(destinationId => destinationId, _ => 0);

        foreach (var row in await _db.Favorites
            .AsNoTracking()
            .Where(favorite =>
                favorite.CreatedAt >= period.Start &&
                favorite.CreatedAt < period.End &&
                favorite.EntityType == AnalyticsEntityTypes.Destination &&
                destinationIds.Contains(favorite.EntityId))
            .GroupBy(favorite => favorite.EntityId)
            .Select(group => new { DestinationId = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken))
        {
            result[row.DestinationId] += row.Count;
        }

        foreach (var row in await _db.CollectionItems
            .AsNoTracking()
            .Where(item =>
                item.CreatedAt >= period.Start &&
                item.CreatedAt < period.End &&
                item.DestinationId.HasValue &&
                destinationIds.Contains(item.DestinationId.Value))
            .GroupBy(item => item.DestinationId!.Value)
            .Select(group => new { DestinationId = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken))
        {
            result[row.DestinationId] += row.Count;
        }

        return result;
    }

    private async Task<Dictionary<int, ReviewAggregate>> GetDestinationReviewAggregatesAsync(
        List<int> destinationIds,
        HealthScorePeriod period,
        CancellationToken cancellationToken)
    {
        var rows = await _db.DestinationReviews
            .AsNoTracking()
            .Where(review => destinationIds.Contains(review.DestinationId))
            .GroupBy(review => review.DestinationId)
            .Select(group => new
            {
                DestinationId = group.Key,
                Count = group.Count(),
                Sum = group.Sum(review => review.Rating),
                RecentCount = group.Count(review => review.CreatedAt >= period.Start && review.CreatedAt < period.End)
            })
            .ToListAsync(cancellationToken);

        var result = destinationIds.ToDictionary(destinationId => destinationId, _ => ReviewAggregate.Empty);
        foreach (var row in rows)
        {
            result[row.DestinationId] = ReviewAggregate.From(row.Count, row.Sum, row.RecentCount);
        }

        return result;
    }

    private async Task<Dictionary<int, ModerationAggregate>> GetDestinationModerationAggregatesAsync(
        List<int> destinationIds,
        DateTime periodEnd,
        CancellationToken cancellationToken)
    {
        if (destinationIds.Count == 0)
            return new Dictionary<int, ModerationAggregate>();

        var oldCutoff = periodEnd.AddDays(-OldPendingDays);
        var rows = await _db.PlaceContributions
            .AsNoTracking()
            .Where(contribution =>
                contribution.Status == PlaceContributionStatus.PendingReview &&
                ((contribution.LocationId.HasValue &&
                  contribution.Location != null &&
                  destinationIds.Contains(contribution.Location.DestinationId)) ||
                 (contribution.PlaceId.HasValue &&
                  contribution.Place != null &&
                  destinationIds.Contains(contribution.Place.Location.DestinationId))))
            .Select(contribution => new
            {
                DestinationId = contribution.LocationId.HasValue
                    ? contribution.Location!.DestinationId
                    : contribution.Place!.Location.DestinationId,
                IsOld = contribution.SubmittedAt < oldCutoff
            })
            .GroupBy(row => row.DestinationId)
            .Select(group => new
            {
                DestinationId = group.Key,
                Pending = group.Count(),
                Old = group.Count(row => row.IsOld)
            })
            .ToListAsync(cancellationToken);

        var result = destinationIds.ToDictionary(destinationId => destinationId, _ => ModerationAggregate.Empty);
        foreach (var row in rows)
        {
            result[row.DestinationId] = new ModerationAggregate(row.Pending, row.Old);
        }

        return result;
    }

    private static TerritoryHealthDto BuildHealthDto(
        int score,
        HealthScorePeriod period,
        List<TerritoryHealthInsightDto> drivers,
        List<TerritoryHealthInsightDto> issues,
        List<TerritoryHealthInsightDto> recommendations)
    {
        var status = BuildHealthStatus(score);
        return new TerritoryHealthDto
        {
            Score = ClampScore(score),
            Status = status.Key,
            StatusLabel = status.Label,
            PeriodLabel = period.Label,
            CalculatedAt = period.End,
            Drivers = drivers,
            Issues = issues,
            Recommendations = recommendations
                .GroupBy(recommendation => recommendation.Key ?? recommendation.Detail)
                .Select(group => group.First())
                .OrderBy(recommendation => recommendation.Priority ?? int.MaxValue)
                .Take(8)
                .ToList()
        };
    }

    private static (string Key, string Label) BuildHealthStatus(int score) =>
        score switch
        {
            >= 85 => ("excellent", "Excellent"),
            >= 70 => ("healthy", "Healthy"),
            >= 50 => ("needs_attention", "Needs Attention"),
            _ => ("critical", "Critical")
        };

    private static TerritoryHealthInsightDto Driver(
        string key,
        string label,
        int scoreContribution,
        int maxPoints,
        string status,
        string message) =>
        new()
        {
            Key = key,
            Label = label,
            Title = label,
            Detail = message,
            Message = message,
            Severity = status == "positive" ? "positive" : status,
            ScoreContribution = scoreContribution,
            MaxPoints = maxPoints,
            Status = status
        };

    private static TerritoryHealthInsightDto Issue(
        string key,
        string severity,
        string title,
        string message) =>
        new()
        {
            Key = key,
            Label = title,
            Title = title,
            Detail = message,
            Message = message,
            Severity = severity,
            Status = severity
        };

    private static TerritoryHealthInsightDto Recommendation(
        string key,
        int priority,
        string title,
        string message) =>
        new()
        {
            Key = key,
            Label = title,
            Title = title,
            Detail = message,
            Message = message,
            Severity = "action",
            Status = "action",
            Priority = priority
        };

    private static string DriverStatus(int scoreContribution, int maxPoints)
    {
        if (maxPoints <= 0)
            return "info";

        var ratio = (double)scoreContribution / maxPoints;
        if (ratio >= 0.8)
            return "positive";
        if (ratio >= 0.5)
            return "info";
        return "warning";
    }

    private static int CountUniqueVisitors<T>(IEnumerable<T> hits)
        where T : IVisitorHit =>
        hits
            .Select(hit => hit.UserId.HasValue
                ? $"u:{hit.UserId.Value}"
                : hit.AnonymousSessionId.HasValue
                    ? $"a:{hit.AnonymousSessionId.Value:N}"
                    : $"e:{hit.EventId}")
            .Distinct(StringComparer.Ordinal)
            .Count();

    private static int ScaleToPoints(int value, int target, int maxPoints)
    {
        if (target <= 0 || maxPoints <= 0 || value <= 0)
            return 0;

        return Math.Clamp((int)Math.Round(maxPoints * Math.Min(value, target) / (double)target), 0, maxPoints);
    }

    private static int RatioToPoints(int value, int total, int maxPoints)
    {
        if (total <= 0 || maxPoints <= 0 || value <= 0)
            return 0;

        return Math.Clamp((int)Math.Round(maxPoints * Math.Min(value, total) / (double)total), 0, maxPoints);
    }

    private static int ScaleRating(double averageRating, int maxPoints)
    {
        if (averageRating <= 0 || maxPoints <= 0)
            return 0;

        return Math.Clamp((int)Math.Round(maxPoints * Math.Min(averageRating, 5d) / 5d), 0, maxPoints);
    }

    private static int ClampScore(int score) => Math.Clamp(score, 0, 100);

    private interface IVisitorHit
    {
        int EventId { get; }
        int? UserId { get; }
        Guid? AnonymousSessionId { get; }
    }

    private sealed record RegionContentCounts(int Destinations, int Locations, int Places, int Stays, int Events)
    {
        public static RegionContentCounts Empty { get; } = new(0, 0, 0, 0, 0);
        public int Total => Destinations + Locations + Places + Stays + Events;
    }

    private sealed record RegionQualityAggregate(
        int TotalItems,
        int CompletedSignals,
        int PossibleSignals,
        int RecentlyUpdatedItems)
    {
        public static RegionQualityAggregate Empty { get; } = new(0, 0, 0, 0);
        public double QualityRatio => PossibleSignals <= 0 ? 0 : (double)CompletedSignals / PossibleSignals;
    }

    private sealed record ModerationAggregate(int PendingRequests, int OldPendingRequests)
    {
        public static ModerationAggregate Empty { get; } = new(0, 0);
    }

    private sealed record ReviewAggregate(int Count, double AverageRating, int RecentCount)
    {
        public static ReviewAggregate Empty { get; } = new(0, 0, 0);

        public static ReviewAggregate From(int count, double ratingSum, int recentCount) =>
            new(count, count == 0 ? 0 : Math.Round(ratingSum / count, 1, MidpointRounding.AwayFromZero), recentCount);
    }

    private sealed class MutableReviewAggregate
    {
        private int Count { get; set; }
        private double RatingSum { get; set; }
        private int RecentCount { get; set; }

        public void Add(int count, double ratingSum, int recentCount)
        {
            Count += count;
            RatingSum += ratingSum;
            RecentCount += recentCount;
        }

        public ReviewAggregate ToReviewAggregate() => ReviewAggregate.From(Count, RatingSum, RecentCount);
    }

    private sealed class MutableModerationAggregate
    {
        private int PendingRequests { get; set; }
        private int OldPendingRequests { get; set; }

        public void Add(int pendingRequests, int oldPendingRequests)
        {
            PendingRequests += pendingRequests;
            OldPendingRequests += oldPendingRequests;
        }

        public ModerationAggregate ToModerationAggregate() => new(PendingRequests, OldPendingRequests);
    }

    private sealed class RegionEngagementAggregate
    {
        public int Views { get; set; }
        public int UniqueVisitors { get; set; }
        public int Saves { get; set; }
        public int ExternalClicks { get; set; }
        public int Reviews { get; set; }
        public int TotalReviews { get; set; }
        public double AverageRating { get; set; }
    }

    private sealed class DestinationEngagementAggregate
    {
        public int Views { get; set; }
        public int UniqueVisitors { get; set; }
        public int Saves { get; set; }
        public int ExternalClicks { get; set; }
    }

    private sealed class QualitySignalRow
    {
        public int RegionId { get; set; }
        public int CompletedSignals { get; set; }
        public int PossibleSignals { get; set; }
        public bool RecentlyUpdated { get; set; }
    }

    private sealed class AnalyticsEventBaseRow
    {
        public int EventId { get; set; }
        public int? UserId { get; set; }
        public Guid? AnonymousSessionId { get; set; }
        public string EventType { get; set; } = string.Empty;
        public string? EntityType { get; set; }
        public int? EntityId { get; set; }
        public int? DestinationId { get; set; }
        public DateTime OccurredAt { get; set; }
    }

    private sealed class AnalyticsRegionHit : IVisitorHit
    {
        public int EventId { get; set; }
        public int RegionId { get; set; }
        public int? UserId { get; set; }
        public Guid? AnonymousSessionId { get; set; }
        public string EventType { get; set; } = string.Empty;
        public string? EntityType { get; set; }
        public int? EntityId { get; set; }
        public DateTime OccurredAt { get; set; }
    }

    private sealed class DestinationAnalyticsHit : IVisitorHit
    {
        public int EventId { get; set; }
        public int DestinationId { get; set; }
        public int? UserId { get; set; }
        public Guid? AnonymousSessionId { get; set; }
        public string EventType { get; set; } = string.Empty;
    }

    private sealed class DestinationHealthSource
    {
        public int Id { get; set; }
        public int RegionId { get; set; }
        public bool HasSummary { get; set; }
        public bool HasDescription { get; set; }
        public bool HasCardImage { get; set; }
        public bool HasCoverImage { get; set; }
        public bool HasTags { get; set; }
        public bool HasCoordinates { get; set; }
        public bool HasBoundary { get; set; }
        public int LocationsCount { get; set; }
        public int PlacesCount { get; set; }
        public int StaysCount { get; set; }
        public int EventsCount { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }
}
