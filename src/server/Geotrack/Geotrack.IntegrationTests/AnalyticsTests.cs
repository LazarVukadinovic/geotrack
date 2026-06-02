using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Geotrack.Application.Analytics;
using Geotrack.Application.DTOs.Auth;
using Geotrack.Application.DTOs.Users;
using Geotrack.Application.Interfaces.Service;
using Geotrack.Domain.Entities.Analytics;
using Geotrack.Domain.Entities.Interests;
using Geotrack.Infrastructure;
using Geotrack.IntegrationTests.Infrastructure;
using Geotrack.Server.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Geotrack.IntegrationTests;

/// <summary>
/// Each test registers a fresh user (unique email) and uses a unique
/// AnonymousSessionId so assertions can filter on rows owned by this test
/// alone — the existing test factory does not isolate the database, so
/// global counts on AnalyticsEvents would be polluted by sibling tests.
/// </summary>
public sealed class AnalyticsTests
{
    [Fact]
    public void Interest_events_endpoint_uses_interests_route_and_rate_limit_metadata()
    {
        var route = Assert.Single(typeof(AnalyticsController).GetCustomAttributes<RouteAttribute>());
        Assert.Equal("api/interests", route.Template);

        var rateLimit = Assert.Single(typeof(AnalyticsController).GetCustomAttributes<EnableRateLimitingAttribute>());
        Assert.Equal("analytics", rateLimit.PolicyName);

        var ingest = typeof(AnalyticsController).GetMethod(
            nameof(AnalyticsController.Ingest),
            BindingFlags.Instance | BindingFlags.Public);

        Assert.NotNull(ingest);
        var post = Assert.Single(ingest!.GetCustomAttributes<HttpPostAttribute>());
        Assert.Equal("events", post.Template);
    }

    [Fact]
    public async Task Legacy_analytics_events_endpoint_is_not_exposed()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        using var client = app.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/analytics/events",
            new
            {
                events = Array.Empty<object>(),
            });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Authenticated_batch_attaches_user_id_from_jwt_and_ignores_client_supplied_user_id()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var (client, userId) = await RegisterFreshUserAsync(app);

        var sessionId = Guid.NewGuid();
        var batch = new
        {
            anonymousSessionId = sessionId,
            events = new object[]
            {
                new
                {
                    eventType = AnalyticsEventTypes.PageView,
                    route = "/explore",
                    metadata = new Dictionary<string, object?> { ["region"] = "Dubrovnik" },
                    userId = 999_999, // attempted spoof — must be ignored.
                },
                new
                {
                    eventType = AnalyticsEventTypes.DetailView,
                    entityType = AnalyticsEntityTypes.Place,
                    entityId = 1,
                },
            },
        };

        var response = await client.PostAsJsonAsync("/api/interests/events", batch);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var rows = await db.AnalyticsEvents
            .Where(e => e.AnonymousSessionId == sessionId)
            .OrderBy(e => e.Id)
            .ToListAsync();

        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.Equal(userId, r.UserId));
        Assert.NotEqual(999_999, rows[0].UserId);
        Assert.Equal(AnalyticsEventTypes.PageView, rows[0].EventType);
        Assert.Equal(AnalyticsEventTypes.DetailView, rows[1].EventType);
    }

    [Fact]
    public async Task Authenticated_analytics_preference_disables_raw_events_and_inferred_signals()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var (client, userId) = await RegisterFreshUserAsync(app);

        int placeId;
        await using (var setup = app.Services.CreateAsyncScope())
        {
            var db = setup.ServiceProvider.GetRequiredService<AppDbContext>();
            placeId = await db.Places.Where(p => p.Name == "Banje Beach").Select(p => p.Id).FirstAsync();
        }

        var disableResponse = await client.PutAsJsonAsync(
            "/api/user/me/analytics",
            new UpdateAnalyticsSettingsRequestDto
            {
                AnalyticsEnabled = false,
            });
        disableResponse.EnsureSuccessStatusCode();

        var disabledSessionId = Guid.NewGuid();
        var disabledBatch = new
        {
            anonymousSessionId = disabledSessionId,
            events = new object[]
            {
                new
                {
                    eventType = AnalyticsEventTypes.PageView,
                    route = "/explore",
                },
                new
                {
                    eventType = AnalyticsEventTypes.FavoriteAdded,
                    entityType = AnalyticsEntityTypes.Place,
                    entityId = placeId,
                },
            },
        };

        var disabledIngest = await client.PostAsJsonAsync("/api/interests/events", disabledBatch);
        Assert.Equal(HttpStatusCode.NoContent, disabledIngest.StatusCode);

        await using (var verifyDisabled = app.Services.CreateAsyncScope())
        {
            var db = verifyDisabled.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.False(await db.AnonymousSessions.AnyAsync(session => session.Id == disabledSessionId));
            Assert.False(await db.AnalyticsEvents.AnyAsync(e => e.UserId == userId || e.AnonymousSessionId == disabledSessionId));
            Assert.False(await db.UserInterestSignals.AnyAsync(signal => signal.UserId == userId));
        }

        var enableResponse = await client.PutAsJsonAsync(
            "/api/user/me/analytics",
            new UpdateAnalyticsSettingsRequestDto
            {
                AnalyticsEnabled = true,
            });
        enableResponse.EnsureSuccessStatusCode();

        var enabledSessionId = Guid.NewGuid();
        var enabledBatch = new
        {
            anonymousSessionId = enabledSessionId,
            events = new object[]
            {
                new
                {
                    eventType = AnalyticsEventTypes.PageView,
                    route = "/explore",
                },
            },
        };

        var enabledIngest = await client.PostAsJsonAsync("/api/interests/events", enabledBatch);
        Assert.Equal(HttpStatusCode.NoContent, enabledIngest.StatusCode);

        await using var verifyEnabled = app.Services.CreateAsyncScope();
        var verifyDb = verifyEnabled.ServiceProvider.GetRequiredService<AppDbContext>();
        var storedEvent = await verifyDb.AnalyticsEvents.SingleAsync(e => e.AnonymousSessionId == enabledSessionId);
        Assert.Equal(userId, storedEvent.UserId);
        Assert.Equal(AnalyticsEventTypes.PageView, storedEvent.EventType);
    }

    [Fact]
    public async Task Anonymous_batch_creates_session_row_and_links_events()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        using var client = app.CreateClient();

        var sessionId = Guid.NewGuid();
        var batch = new
        {
            anonymousSessionId = sessionId,
            events = new object[]
            {
                new
                {
                    eventType = AnalyticsEventTypes.PageView,
                    route = "/explore",
                },
            },
        };

        var response = await client.PostAsJsonAsync("/api/interests/events", batch);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var session = await db.AnonymousSessions.FindAsync(sessionId);
        Assert.NotNull(session);
        Assert.Equal("/explore", session.EntryRoute);

        var events = await db.AnalyticsEvents
            .Where(e => e.AnonymousSessionId == sessionId)
            .ToListAsync();
        Assert.Single(events);
        Assert.Null(events[0].UserId);
    }

    [Fact]
    public async Task Anonymous_batch_uses_x_anonymous_session_header_when_body_session_is_absent()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        using var client = app.CreateClient();

        var sessionId = Guid.NewGuid();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/interests/events")
        {
            Content = JsonContent.Create(new
            {
                events = new object[]
                {
                    new
                    {
                        eventType = AnalyticsEventTypes.PageView,
                        route = "/explore",
                    },
                },
            }),
        };
        request.Headers.TryAddWithoutValidation("X-Anonymous-Session", sessionId.ToString());

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        Assert.NotNull(await db.AnonymousSessions.FindAsync(sessionId));
        var row = await db.AnalyticsEvents.SingleAsync(e => e.AnonymousSessionId == sessionId);
        Assert.Equal(AnalyticsEventTypes.PageView, row.EventType);
        Assert.Equal("/explore", row.Route);
        Assert.Null(row.UserId);
    }

    [Fact]
    public async Task Malformed_json_returns_bad_request_not_server_error()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        using var client = app.CreateClient();
        using var content = new StringContent("{", Encoding.UTF8, "application/json");

        var response = await client.PostAsync("/api/interests/events", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Invalid_event_type_in_mixed_batch_is_dropped_and_response_is_still_204()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        using var client = app.CreateClient();

        var sessionId = Guid.NewGuid();
        var batch = new
        {
            anonymousSessionId = sessionId,
            events = new object[]
            {
                new { eventType = "definitely_not_a_real_event" },
                new { eventType = AnalyticsEventTypes.PageView, route = "/explore" },
            },
        };

        var response = await client.PostAsJsonAsync("/api/interests/events", batch);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var rows = await db.AnalyticsEvents
            .Where(e => e.AnonymousSessionId == sessionId)
            .ToListAsync();
        Assert.Single(rows);
        Assert.Equal(AnalyticsEventTypes.PageView, rows[0].EventType);
    }

    [Fact]
    public async Task Noisy_duplicate_events_are_dropped_across_recent_ingestion_window()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        using var client = app.CreateClient();

        var sessionId = Guid.NewGuid();
        var batch = new
        {
            anonymousSessionId = sessionId,
            events = new object[]
            {
                new { eventType = AnalyticsEventTypes.PageView, route = "/explore" },
            },
        };

        var first = await client.PostAsJsonAsync("/api/interests/events", batch);
        var second = await client.PostAsJsonAsync("/api/interests/events", batch);

        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, second.StatusCode);

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rows = await db.AnalyticsEvents
            .Where(e => e.AnonymousSessionId == sessionId)
            .ToListAsync();

        Assert.Single(rows);
        Assert.Equal(AnalyticsEventTypes.PageView, rows[0].EventType);
    }

    [Fact]
    public async Task High_value_outbound_clicks_are_not_deduped_as_noisy_events()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        using var client = app.CreateClient();

        var sessionId = Guid.NewGuid();
        var batch = new
        {
            anonymousSessionId = sessionId,
            events = new object[]
            {
                new
                {
                    eventType = AnalyticsEventTypes.OutboundLinkClicked,
                    entityType = AnalyticsEntityTypes.Hotel,
                    entityId = 1,
                    metadata = new Dictionary<string, object?>
                    {
                        ["provider"] = "booking",
                        ["host"] = "booking.com",
                    },
                },
                new
                {
                    eventType = AnalyticsEventTypes.OutboundLinkClicked,
                    entityType = AnalyticsEntityTypes.Hotel,
                    entityId = 1,
                    metadata = new Dictionary<string, object?>
                    {
                        ["provider"] = "booking",
                        ["host"] = "booking.com",
                    },
                },
            },
        };

        var response = await client.PostAsJsonAsync("/api/interests/events", batch);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rows = await db.AnalyticsEvents
            .Where(e => e.AnonymousSessionId == sessionId)
            .ToListAsync();

        Assert.Equal(2, rows.Count);
        Assert.All(rows, row => Assert.Equal(AnalyticsEventTypes.OutboundLinkClicked, row.EventType));
    }

    [Fact]
    public async Task Favorite_added_event_writes_inferred_signals_for_each_place_tag()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var (client, userId) = await RegisterFreshUserAsync(app);

        // Pick a place that has tags in the seed data.
        int placeId;
        int expectedTagCount;
        await using (var setup = app.Services.CreateAsyncScope())
        {
            var db = setup.ServiceProvider.GetRequiredService<AppDbContext>();
            var place = await db.Places.FirstOrDefaultAsync(p => p.Name == "Banje Beach");
            Assert.NotNull(place);
            placeId = place.Id;
            expectedTagCount = await db.PlaceTags.CountAsync(pt => pt.PlaceId == placeId);
            Assert.True(expectedTagCount > 0, "Seed data should attach tags to Banje Beach.");
        }

        var batch = new
        {
            events = new object[]
            {
                new
                {
                    eventType = AnalyticsEventTypes.FavoriteAdded,
                    entityType = AnalyticsEntityTypes.Place,
                    entityId = placeId,
                },
            },
        };

        var response = await client.PostAsJsonAsync("/api/interests/events", batch);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        await WaitForAsync(
            async () =>
            {
                await using var waitScope = app.Services.CreateAsyncScope();
                var waitDb = waitScope.ServiceProvider.GetRequiredService<AppDbContext>();
                return await waitDb.UserInterestSignals.CountAsync(s => s.UserId == userId) == expectedTagCount;
            },
            "Timed out waiting for async inferred-interest processing.");

        await using var verify = app.Services.CreateAsyncScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<AppDbContext>();

        var signals = await verifyDb.UserInterestSignals
            .Where(s => s.UserId == userId)
            .ToListAsync();

        Assert.Equal(expectedTagCount, signals.Count);
        Assert.All(signals, s =>
        {
            Assert.Equal(5.0, s.Score);
            Assert.Equal("inferred", s.Source);
            Assert.False(string.IsNullOrWhiteSpace(s.LastReason));
        });
    }

    [Fact]
    public async Task Score_is_clamped_to_cap_after_repeated_favorite_events()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var (client, userId) = await RegisterFreshUserAsync(app);

        int placeId;
        await using (var setup = app.Services.CreateAsyncScope())
        {
            var db = setup.ServiceProvider.GetRequiredService<AppDbContext>();
            placeId = await db.Places.Where(p => p.Name == "Banje Beach").Select(p => p.Id).FirstAsync();
        }

        // 5 batches × 50 events = 250 favorite_added events × +5.0 = 1250 raw,
        // which exceeds the +1000 cap. Stays well under the analytics rate limit
        // (60/min per authenticated user partition).
        for (var batchIndex = 0; batchIndex < 5; batchIndex++)
        {
            var batch = new
            {
                events = Enumerable.Range(0, 50).Select(_ => (object)new
                {
                    eventType = AnalyticsEventTypes.FavoriteAdded,
                    entityType = AnalyticsEntityTypes.Place,
                    entityId = placeId,
                }).ToArray(),
            };
            var response = await client.PostAsJsonAsync("/api/interests/events", batch);
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }

        await WaitForAsync(
            async () =>
            {
                await using var waitScope = app.Services.CreateAsyncScope();
                var waitDb = waitScope.ServiceProvider.GetRequiredService<AppDbContext>();
                return await waitDb.UserInterestSignals
                    .Where(s => s.UserId == userId)
                    .AnyAsync(s => s.Score >= 1000.0);
            },
            "Timed out waiting for repeated async inferred-interest processing.");

        await using var verify = app.Services.CreateAsyncScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<AppDbContext>();

        var scores = await verifyDb.UserInterestSignals
            .Where(s => s.UserId == userId)
            .Select(s => s.Score)
            .ToListAsync();

        Assert.NotEmpty(scores);
        Assert.All(scores, s => Assert.True(s <= 1000.0, $"Score should be clamped to 1000 but was {s}."));
        Assert.Contains(scores, s => s > 0);
    }

    [Fact]
    public async Task Oversized_metadata_event_in_mixed_batch_is_dropped_while_sibling_persists()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        using var client = app.CreateClient();

        var sessionId = Guid.NewGuid();
        var oversizeMetadata = new Dictionary<string, object?> { ["payload"] = new string('x', 10_000) };
        var batch = new
        {
            anonymousSessionId = sessionId,
            events = new object[]
            {
                new
                {
                    eventType = AnalyticsEventTypes.SearchPerformed,
                    metadata = oversizeMetadata,
                },
                new
                {
                    eventType = AnalyticsEventTypes.PageView,
                    route = "/explore",
                },
            },
        };

        var response = await client.PostAsJsonAsync("/api/interests/events", batch);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var rows = await db.AnalyticsEvents
            .Where(e => e.AnonymousSessionId == sessionId)
            .ToListAsync();
        Assert.Single(rows);
        Assert.Equal(AnalyticsEventTypes.PageView, rows[0].EventType);
    }

    [Fact]
    public async Task Inferred_interests_are_visible_dismissible_and_resurface_after_stronger_evidence()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var (client, userId) = await RegisterFreshUserAsync(app);

        int placeId;
        int tagId;
        int interestTopicId;
        double topicWeight;
        await using (var setup = app.Services.CreateAsyncScope())
        {
            var db = setup.ServiceProvider.GetRequiredService<AppDbContext>();
            var topicLink = await db.PlaceTags
                .Join(
                    db.InterestTopicTags.Where(link => link.InterestTopic.IsActive),
                    placeTag => placeTag.TagId,
                    topicTag => topicTag.TagId,
                    (placeTag, topicTag) => new
                    {
                        placeTag.PlaceId,
                        placeTag.TagId,
                        topicTag.InterestTopicId,
                        topicTag.Weight
                    })
                .GroupBy(link => new { link.PlaceId, link.InterestTopicId })
                .Select(group => new
                {
                    group.Key.PlaceId,
                    group.Key.InterestTopicId,
                    TagId = group.Min(link => link.TagId),
                    TopicWeight = group.Sum(link => link.Weight)
                })
                .Where(link => link.TopicWeight >= 1.0 && link.TopicWeight < 10.0 / 7.0)
                .OrderBy(link => link.PlaceId)
                .ThenBy(link => link.InterestTopicId)
                .FirstAsync();
            placeId = topicLink.PlaceId;
            tagId = topicLink.TagId;
            interestTopicId = topicLink.InterestTopicId;
            topicWeight = topicLink.TopicWeight;
        }

        await PostFavoriteAddedAsync(client, placeId);
        await WaitForSignalAsync(app, userId, tagId, signal => signal.Score >= 5.0);
        await WaitForTopicSignalAsync(app, userId, interestTopicId, signal => signal.Score >= 5.0);

        var interests = await client.GetFromJsonAsync<GetUserInterestsResponseDto>("/api/user/me/interests");
        Assert.NotNull(interests);
        Assert.Contains(interests!.InferredTags, tag => tag.InterestTopicId == interestTopicId);
        Assert.Empty(interests.Tags);

        var dismiss = await client.DeleteAsync($"/api/user/me/interests/inferred/{interestTopicId}");
        Assert.Equal(HttpStatusCode.NoContent, dismiss.StatusCode);

        interests = await client.GetFromJsonAsync<GetUserInterestsResponseDto>("/api/user/me/interests");
        Assert.NotNull(interests);
        Assert.DoesNotContain(interests!.InferredTags, tag => tag.InterestTopicId == interestTopicId);

        await PostInterestEventAsync(client, placeId, AnalyticsEventTypes.ReviewCreated);
        await WaitForTopicSignalAsync(
            app,
            userId,
            interestTopicId,
            signal => signal.IsDismissed && signal.EvidenceSinceDismissal >= 7.0 * topicWeight);
        interests = await client.GetFromJsonAsync<GetUserInterestsResponseDto>("/api/user/me/interests");
        Assert.NotNull(interests);
        Assert.DoesNotContain(interests!.InferredTags, tag => tag.InterestTopicId == interestTopicId);

        await PostInterestEventAsync(client, placeId, AnalyticsEventTypes.ReviewCreated);
        await WaitForTopicSignalAsync(app, userId, interestTopicId, signal => !signal.IsDismissed);
        interests = await client.GetFromJsonAsync<GetUserInterestsResponseDto>("/api/user/me/interests");
        Assert.NotNull(interests);
        Assert.Contains(interests!.InferredTags, tag => tag.InterestTopicId == interestTopicId);

        await using var verify = app.Services.CreateAsyncScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<AppDbContext>();
        var signal = await verifyDb.UserInterestTopicSignals.SingleAsync(s =>
            s.UserId == userId &&
            s.InterestTopicId == interestTopicId);
        Assert.False(signal.IsDismissed);
        Assert.Equal(0.0, signal.EvidenceSinceDismissal);
    }

    [Fact]
    public async Task Outbound_partner_link_click_is_accepted_for_anonymous_sessions()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        using var client = app.CreateClient();

        var sessionId = Guid.NewGuid();
        var batch = new
        {
            anonymousSessionId = sessionId,
            events = new object[]
            {
                new
                {
                    eventType = AnalyticsEventTypes.OutboundLinkClicked,
                    entityType = AnalyticsEntityTypes.Hotel,
                    entityId = 1,
                    route = "/stays/1?ignored=true",
                    metadata = new Dictionary<string, object?>
                    {
                        ["provider"] = "booking",
                        ["host"] = "booking.com",
                        ["context"] = "hotel_detail",
                    },
                },
            },
        };

        var response = await client.PostAsJsonAsync("/api/interests/events", batch);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.AnalyticsEvents.SingleAsync(e => e.AnonymousSessionId == sessionId);

        Assert.Equal(AnalyticsEventTypes.OutboundLinkClicked, row.EventType);
        Assert.Equal(AnalyticsEntityTypes.Hotel, row.EntityType);
        Assert.Equal("/stays/:id", row.Route);

        using var metadata = JsonDocument.Parse(row.MetadataJson);
        Assert.Equal("booking", metadata.RootElement.GetProperty("provider").GetString());
        Assert.Equal("booking.com", metadata.RootElement.GetProperty("host").GetString());
    }

    [Fact]
    public async Task Page_duration_events_validate_duration_without_breaking_ingest()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        using var client = app.CreateClient();

        var sessionId = Guid.NewGuid();
        var batch = new
        {
            anonymousSessionId = sessionId,
            events = new object[]
            {
                new
                {
                    eventType = AnalyticsEventTypes.PageDuration,
                    entityType = AnalyticsEntityTypes.Place,
                    entityId = 1,
                    route = "/places/1",
                    durationMs = 500,
                },
                new
                {
                    eventType = AnalyticsEventTypes.PageDuration,
                    entityType = AnalyticsEntityTypes.Place,
                    entityId = 1,
                    route = "/places/1",
                    durationMs = 3_600_000,
                },
            },
        };

        var response = await client.PostAsJsonAsync("/api/interests/events", batch);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.AnalyticsEvents.SingleAsync(e => e.AnonymousSessionId == sessionId);

        Assert.Equal(AnalyticsEventTypes.PageDuration, row.EventType);
        Assert.Equal(30 * 60 * 1000, row.DurationMs);
        Assert.Equal("/places/:id", row.Route);
    }

    [Fact]
    public async Task Outbound_partner_link_click_updates_inferred_signals_for_authenticated_users()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var (client, userId) = await RegisterFreshUserAsync(app);

        int hotelId;
        int expectedTagCount;
        await using (var setup = app.Services.CreateAsyncScope())
        {
            var db = setup.ServiceProvider.GetRequiredService<AppDbContext>();
            var hotel = await db.Places
                .Where(place => place.Stay != null && place.Stay.PricePerNight != null && place.PlaceTags.Any())
                .Select(place => new { place.Id, TagCount = place.PlaceTags.Count })
                .FirstAsync();

            hotelId = hotel.Id;
            expectedTagCount = hotel.TagCount;
        }

        var response = await client.PostAsJsonAsync(
            "/api/interests/events",
            new
            {
                events = new object[]
                {
                    new
                    {
                        eventType = AnalyticsEventTypes.OutboundLinkClicked,
                        entityType = AnalyticsEntityTypes.Hotel,
                        entityId = hotelId,
                        metadata = new Dictionary<string, object?>
                        {
                            ["provider"] = "booking",
                            ["host"] = "booking.com",
                        },
                    },
                },
            });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        await WaitForAsync(
            async () =>
            {
                await using var waitScope = app.Services.CreateAsyncScope();
                var waitDb = waitScope.ServiceProvider.GetRequiredService<AppDbContext>();
                return await waitDb.UserInterestSignals.CountAsync(signal => signal.UserId == userId) == expectedTagCount;
            },
            "Timed out waiting for outbound-click inferred-interest processing.");

        await using var verify = app.Services.CreateAsyncScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<AppDbContext>();
        var signals = await verifyDb.UserInterestSignals
            .Where(signal => signal.UserId == userId)
            .ToListAsync();

        Assert.Equal(expectedTagCount, signals.Count);
        Assert.All(signals, signal =>
        {
            Assert.Equal(5.0, signal.Score);
            Assert.StartsWith($"{AnalyticsEventTypes.OutboundLinkClicked}:{AnalyticsEntityTypes.Hotel}#", signal.LastReason);
        });
    }

    [Fact]
    public async Task Event_metadata_scrubs_secret_and_pii_keys_before_storage()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        using var client = app.CreateClient();

        var sessionId = Guid.NewGuid();
        var response = await client.PostAsJsonAsync(
            "/api/interests/events",
            new
            {
                anonymousSessionId = sessionId,
                events = new object[]
                {
                    new
                    {
                        eventType = AnalyticsEventTypes.PageView,
                        route = "/agent",
                        metadata = new Dictionary<string, object?>
                        {
                            ["region"] = "Dubrovnik",
                            ["email"] = "person@example.com",
                            ["apiKey"] = "secret-key",
                            ["refreshToken"] = "refresh-secret",
                            ["prompt"] = "Find trips for my phone number",
                            ["phoneNumber"] = "+381 00 000"
                        },
                    },
                },
            });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.AnalyticsEvents.SingleAsync(evt => evt.AnonymousSessionId == sessionId);

        Assert.Contains("Dubrovnik", row.MetadataJson, StringComparison.Ordinal);
        Assert.DoesNotContain("person@example.com", row.MetadataJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret-key", row.MetadataJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("refresh-secret", row.MetadataJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Find trips", row.MetadataJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("+381", row.MetadataJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Retention_cleanup_is_manual_configured_and_deletes_only_expired_rows()
    {
        await using var app = await GeotrackApiFactory.CreateAsync(
            configurationOverrides: new Dictionary<string, string?>
            {
                ["AnalyticsRetention:RetentionDays"] = "30",
                ["AnalyticsRetention:BatchSize"] = "10"
            });

        var now = DateTime.UtcNow;
        var oldSessionId = Guid.NewGuid();
        var emptyOldSessionId = Guid.NewGuid();
        var newSessionId = Guid.NewGuid();

        await using (var setup = app.Services.CreateAsyncScope())
        {
            var setupDb = setup.ServiceProvider.GetRequiredService<AppDbContext>();
            setupDb.AnonymousSessions.AddRange(
                new AnonymousSession
                {
                    Id = oldSessionId,
                    CreatedAt = now.AddDays(-60),
                    LastSeenAt = now.AddDays(-60)
                },
                new AnonymousSession
                {
                    Id = emptyOldSessionId,
                    CreatedAt = now.AddDays(-60),
                    LastSeenAt = now.AddDays(-60)
                },
                new AnonymousSession
                {
                    Id = newSessionId,
                    CreatedAt = now.AddDays(-2),
                    LastSeenAt = now.AddDays(-2)
                });
            setupDb.AnalyticsEvents.AddRange(
                new AnalyticsEvent
                {
                    AnonymousSessionId = oldSessionId,
                    EventType = AnalyticsEventTypes.PageView,
                    Route = "/old",
                    MetadataJson = "{}",
                    OccurredAt = now.AddDays(-60)
                },
                new AnalyticsEvent
                {
                    AnonymousSessionId = newSessionId,
                    EventType = AnalyticsEventTypes.PageView,
                    Route = "/new",
                    MetadataJson = "{}",
                    OccurredAt = now.AddDays(-2)
                });
            await setupDb.SaveChangesAsync();
        }

        await using var scope = app.Services.CreateAsyncScope();
        var retention = scope.ServiceProvider.GetRequiredService<IAnalyticsRetentionService>();
        var dryRun = await retention.CleanupExpiredAsync(now, execute: false);

        Assert.False(dryRun.Execute);
        Assert.Equal(30, dryRun.RetentionDays);
        Assert.Equal(1, dryRun.DeletedAnalyticsEvents);
        Assert.Equal(1, dryRun.DeletedAnonymousSessions);

        var executed = await retention.CleanupExpiredAsync(now, execute: true);

        Assert.True(executed.Execute);
        Assert.Equal(1, executed.DeletedAnalyticsEvents);
        Assert.Equal(1, executed.DeletedAnonymousSessions);

        var verifyDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await verifyDb.AnalyticsEvents.AnyAsync(evt => evt.Route == "/old"));
        Assert.True(await verifyDb.AnalyticsEvents.AnyAsync(evt => evt.Route == "/new"));
        Assert.False(await verifyDb.AnonymousSessions.AnyAsync(session => session.Id == emptyOldSessionId));
        Assert.True(await verifyDb.AnonymousSessions.AnyAsync(session => session.Id == oldSessionId));
        Assert.True(await verifyDb.AnonymousSessions.AnyAsync(session => session.Id == newSessionId));
    }

    private static async Task<(HttpClient client, int userId)> RegisterFreshUserAsync(GeotrackApiFactory app)
    {
        var client = app.CreateClient();
        var email = $"analytics-{Guid.NewGuid():N}@example.com";
        var register = await client.PostAsJsonAsync(
            "/api/auth/register",
            new RegisterRequestDto
            {
                FullName = "Analytics Tester",
                Email = email,
                Password = "Password123!",
            });
        register.EnsureSuccessStatusCode();

        var session = await register.Content.ReadFromJsonAsync<LoginResponseDto>();
        Assert.NotNull(session);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", session!.AccessToken);

        // Resolve the just-registered user's id from the DB so assertions can
        // filter on it without depending on serial seed-data ids.
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userId = await db.Users.Where(u => u.Email == email).Select(u => u.Id).FirstAsync();

        return (client, userId);
    }

    private static async Task PostFavoriteAddedAsync(HttpClient client, int placeId)
    {
        await PostInterestEventAsync(client, placeId, AnalyticsEventTypes.FavoriteAdded);
    }

    private static async Task PostInterestEventAsync(HttpClient client, int placeId, string eventType)
    {
        var response = await client.PostAsJsonAsync(
            "/api/interests/events",
            new
            {
                events = new object[]
                {
                    new
                    {
                        eventType,
                        entityType = AnalyticsEntityTypes.Place,
                        entityId = placeId,
                    },
                },
            });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private static Task WaitForSignalAsync(
        GeotrackApiFactory app,
        int userId,
        int tagId,
        Func<UserInterestSignal, bool> predicate) =>
        WaitForAsync(
            async () =>
            {
                await using var scope = app.Services.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var signal = await db.UserInterestSignals
                    .AsNoTracking()
                    .SingleOrDefaultAsync(s => s.UserId == userId && s.TagId == tagId);
                return signal is not null && predicate(signal);
            },
            "Timed out waiting for inferred-interest signal processing.");

    private static Task WaitForTopicSignalAsync(
        GeotrackApiFactory app,
        int userId,
        int interestTopicId,
        Func<UserInterestTopicSignal, bool> predicate) =>
        WaitForAsync(
            async () =>
            {
                await using var scope = app.Services.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var signal = await db.UserInterestTopicSignals
                    .AsNoTracking()
                    .SingleOrDefaultAsync(s => s.UserId == userId && s.InterestTopicId == interestTopicId);
                return signal is not null && predicate(signal);
            },
            "Timed out waiting for inferred-interest topic signal processing.");

    private static async Task WaitForAsync(Func<Task<bool>> predicate, string timeoutMessage)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (await predicate())
            {
                return;
            }

            await Task.Delay(50);
        }

        throw new TimeoutException(timeoutMessage);
    }
}
