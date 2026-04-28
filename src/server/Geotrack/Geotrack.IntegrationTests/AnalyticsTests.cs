using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Geotrack.Application.Analytics;
using Geotrack.Application.DTOs.Auth;
using Geotrack.Application.DTOs.Users;
using Geotrack.Domain.Entities.Analytics;
using Geotrack.Infrastructure;
using Geotrack.IntegrationTests.Infrastructure;
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

        var response = await client.PostAsJsonAsync("/api/analytics/events", batch);
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

        var response = await client.PostAsJsonAsync("/api/analytics/events", batch);
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

        var response = await client.PostAsJsonAsync("/api/analytics/events", batch);
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

        var first = await client.PostAsJsonAsync("/api/analytics/events", batch);
        var second = await client.PostAsJsonAsync("/api/analytics/events", batch);

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

        var response = await client.PostAsJsonAsync("/api/analytics/events", batch);
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

        var response = await client.PostAsJsonAsync("/api/analytics/events", batch);
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
            var response = await client.PostAsJsonAsync("/api/analytics/events", batch);
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

        var response = await client.PostAsJsonAsync("/api/analytics/events", batch);
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
        await using (var setup = app.Services.CreateAsyncScope())
        {
            var db = setup.ServiceProvider.GetRequiredService<AppDbContext>();
            var place = await db.Places.FirstOrDefaultAsync(p => p.Name == "Banje Beach");
            Assert.NotNull(place);
            placeId = place.Id;
            tagId = await db.PlaceTags
                .Where(pt => pt.PlaceId == placeId)
                .Select(pt => pt.TagId)
                .FirstAsync();
        }

        await PostFavoriteAddedAsync(client, placeId);
        await WaitForSignalAsync(app, userId, tagId, signal => signal.Score >= 5.0);

        var interests = await client.GetFromJsonAsync<GetUserInterestsResponseDto>("/api/user/me/interests");
        Assert.NotNull(interests);
        Assert.Contains(interests!.InferredTags, tag => tag.TagId == tagId);
        Assert.Empty(interests.Tags);

        var dismiss = await client.DeleteAsync($"/api/user/me/interests/inferred/{tagId}");
        Assert.Equal(HttpStatusCode.NoContent, dismiss.StatusCode);

        interests = await client.GetFromJsonAsync<GetUserInterestsResponseDto>("/api/user/me/interests");
        Assert.NotNull(interests);
        Assert.DoesNotContain(interests!.InferredTags, tag => tag.TagId == tagId);

        await PostFavoriteAddedAsync(client, placeId);
        await WaitForSignalAsync(app, userId, tagId, signal => signal.EvidenceSinceDismissal >= 5.0);
        interests = await client.GetFromJsonAsync<GetUserInterestsResponseDto>("/api/user/me/interests");
        Assert.NotNull(interests);
        Assert.DoesNotContain(interests!.InferredTags, tag => tag.TagId == tagId);

        await PostFavoriteAddedAsync(client, placeId);
        await WaitForSignalAsync(app, userId, tagId, signal => !signal.IsDismissed);
        interests = await client.GetFromJsonAsync<GetUserInterestsResponseDto>("/api/user/me/interests");
        Assert.NotNull(interests);
        Assert.Contains(interests!.InferredTags, tag => tag.TagId == tagId);

        await using var verify = app.Services.CreateAsyncScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<AppDbContext>();
        var signal = await verifyDb.UserInterestSignals.SingleAsync(s => s.UserId == userId && s.TagId == tagId);
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

        var response = await client.PostAsJsonAsync("/api/analytics/events", batch);
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
                .Where(place => place.PricePerNight != null && place.PlaceTags.Any())
                .Select(place => new { place.Id, TagCount = place.PlaceTags.Count })
                .FirstAsync();

            hotelId = hotel.Id;
            expectedTagCount = hotel.TagCount;
        }

        var response = await client.PostAsJsonAsync(
            "/api/analytics/events",
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
        var response = await client.PostAsJsonAsync(
            "/api/analytics/events",
            new
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
