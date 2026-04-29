using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Net;
using Geotrack.Application.DTOs.Auth;
using Geotrack.Application.DTOs.Users;
using Geotrack.Domain.Entities.Analytics;
using Geotrack.Infrastructure;
using Geotrack.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Geotrack.IntegrationTests;

public sealed class ExploreRatingsTests
{
    private const string ExploreUrl = "/api/explore?latitude=42.6507&longitude=18.0944";
    private const string RecommendationsUrl = "/api/explore/recommendations?latitude=42.6507&longitude=18.0944";

    [Fact]
    public async Task Explore_feed_uses_real_review_aggregates_and_keeps_unrated_entities_empty()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        using var client = app.CreateClient();

        var response = await client.GetFromJsonAsync<ExploreFeedDto>(ExploreUrl);

        Assert.NotNull(response);

        var ratedStay = Assert.Single(response.TopStays, item => item.Title == "Boutique Hotel Stari Grad");
        Assert.Equal(5.0, ratedStay.Rating);
        Assert.Equal("1 review", ratedStay.ReviewCount);

        var unratedStay = Assert.Single(response.TopStays, item => item.Title == "Hotel Excelsior");
        Assert.Null(unratedStay.Rating);
        Assert.Null(unratedStay.ReviewCount);

        var ratedLocalFavorite = Assert.Single(response.LocalFavorites, item => item.Title == "Nautika Restaurant");
        Assert.Equal(5.0, ratedLocalFavorite.Rating);
        Assert.Equal("1 review", ratedLocalFavorite.ReviewCount);

        var unratedLocalFavorite = Assert.Single(response.LocalFavorites, item => item.Title == "Cogito Coffee");
        Assert.Null(unratedLocalFavorite.Rating);
        Assert.Null(unratedLocalFavorite.ReviewCount);
    }

    [Fact]
    public async Task Explore_curated_for_you_is_empty_for_guests_and_users_without_interest_signals()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();

        using var guestClient = app.CreateClient();
        var guestResponse = await guestClient.GetFromJsonAsync<ExploreFeedDto>(ExploreUrl);
        Assert.NotNull(guestResponse);
        Assert.Empty(guestResponse!.CuratedForYou);

        var (authorizedClient, _) = await RegisterFreshUserAsync(app);
        var authorizedResponse = await authorizedClient.GetFromJsonAsync<ExploreFeedDto>(ExploreUrl);
        Assert.NotNull(authorizedResponse);
        Assert.Empty(authorizedResponse!.CuratedForYou);
    }

    [Fact]
    public async Task Explore_curated_for_you_prioritizes_explicit_interests()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var (client, _) = await RegisterFreshUserAsync(app);

        int historyTagId;
        await using (var setup = app.Services.CreateAsyncScope())
        {
            var db = setup.ServiceProvider.GetRequiredService<AppDbContext>();
            historyTagId = await db.Tags
                .Where(tag => tag.Slug == "history")
                .Select(tag => tag.Id)
                .SingleAsync();
        }

        var update = await client.PutAsJsonAsync(
            "/api/user/me/interests",
            new UpdateUserInterestsRequestDto
            {
                TagIds = new List<int> { historyTagId }
            });
        update.EnsureSuccessStatusCode();

        var response = await client.GetFromJsonAsync<ExploreFeedDto>(ExploreUrl);
        Assert.NotNull(response);
        Assert.NotEmpty(response!.CuratedForYou);
        Assert.Contains(response.CuratedForYou, item => item.Title == "Dubrovnik Old Town Walls");
        Assert.DoesNotContain(response.CuratedForYou, item => item.Title == "Nautika Restaurant");
        Assert.All(response.CuratedForYou, item =>
        {
            Assert.Equal("History", item.CategoryLabel);
            Assert.Equal("Matches your saved interest: History", item.Subtitle);
        });
    }

    [Fact]
    public async Task Explore_curated_for_you_uses_visible_inferred_signals_and_ignores_dismissed_ones()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var (client, userId) = await RegisterFreshUserAsync(app);

        int beachesTagId;
        await using (var setup = app.Services.CreateAsyncScope())
        {
            var db = setup.ServiceProvider.GetRequiredService<AppDbContext>();
            beachesTagId = await db.Tags
                .Where(tag => tag.Slug == "beaches")
                .Select(tag => tag.Id)
                .SingleAsync();

            db.UserInterestSignals.Add(new UserInterestSignal
            {
                UserId = userId,
                TagId = beachesTagId,
                Source = "inferred",
                Score = 8.0,
                LastInteractionAt = DateTime.UtcNow,
                LastReason = "test:place#1"
            });
            await db.SaveChangesAsync();
        }

        var response = await client.GetFromJsonAsync<ExploreFeedDto>(ExploreUrl);
        Assert.NotNull(response);
        Assert.NotEmpty(response!.CuratedForYou);
        Assert.Contains(response.CuratedForYou, item => item.Title == "Banje Beach");
        Assert.All(response.CuratedForYou, item =>
        {
            Assert.Equal("Beaches", item.CategoryLabel);
            Assert.Equal("Based on recent activity: Beaches", item.Subtitle);
        });

        var dismiss = await client.DeleteAsync($"/api/user/me/interests/inferred/{beachesTagId}");
        dismiss.EnsureSuccessStatusCode();

        var afterDismissal = await client.GetFromJsonAsync<ExploreFeedDto>(ExploreUrl);
        Assert.NotNull(afterDismissal);
        Assert.Empty(afterDismissal!.CuratedForYou);
    }

    [Fact]
    public async Task Explore_recommendations_requires_authenticated_user()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        using var client = app.CreateClient();

        var response = await client.GetAsync(RecommendationsUrl);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Explore_recommendations_are_empty_when_user_has_no_interest_signals()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var (client, _) = await RegisterFreshUserAsync(app);

        var response = await client.GetFromJsonAsync<RecommendationsPageDto>(RecommendationsUrl);

        Assert.NotNull(response);
        Assert.False(response!.HasSignals);
        Assert.Empty(response.Items);
        Assert.False(response.HasMore);
        Assert.NotNull(response.EmptyReason);
    }

    [Fact]
    public async Task Explore_recommendations_use_real_signals_filters_and_backend_pagination()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var (client, userId) = await RegisterFreshUserAsync(app);

        int historyTagId;
        int beachesTagId;
        await using (var setup = app.Services.CreateAsyncScope())
        {
            var db = setup.ServiceProvider.GetRequiredService<AppDbContext>();
            var tags = await db.Tags
                .Where(tag => tag.Slug == "history" || tag.Slug == "beaches")
                .ToDictionaryAsync(tag => tag.Slug, tag => tag.Id);

            historyTagId = tags["history"];
            beachesTagId = tags["beaches"];

            db.UserInterestSignals.Add(new UserInterestSignal
            {
                UserId = userId,
                TagId = beachesTagId,
                Source = "inferred",
                Score = 8.0,
                LastInteractionAt = DateTime.UtcNow,
                LastReason = "test:place#beach"
            });
            await db.SaveChangesAsync();
        }

        var update = await client.PutAsJsonAsync(
            "/api/user/me/interests",
            new UpdateUserInterestsRequestDto
            {
                TagIds = new List<int> { historyTagId }
            });
        update.EnsureSuccessStatusCode();

        var firstPage = await client.GetFromJsonAsync<RecommendationsPageDto>($"{RecommendationsUrl}&take=1");
        Assert.NotNull(firstPage);
        Assert.True(firstPage!.HasSignals);
        var firstItem = Assert.Single(firstPage.Items);
        Assert.Equal("History", firstItem.CategoryLabel);
        Assert.Equal("Matches your saved interest: History", firstItem.Reason);
        Assert.True(firstPage.HasMore);
        Assert.Equal(1, firstPage.NextSkip);
        Assert.Contains(firstPage.Filters, filter => filter.TagId == historyTagId && filter.Label == "History");
        Assert.Contains(firstPage.Filters, filter => filter.TagId == beachesTagId && filter.Label == "Beaches");

        var secondPage = await client.GetFromJsonAsync<RecommendationsPageDto>($"{RecommendationsUrl}&skip={firstPage.NextSkip}&take=1");
        Assert.NotNull(secondPage);
        var secondItem = Assert.Single(secondPage!.Items);
        Assert.NotEqual(firstItem.Id, secondItem.Id);

        var beachesPage = await client.GetFromJsonAsync<RecommendationsPageDto>($"{RecommendationsUrl}&tagId={beachesTagId}&take=8");
        Assert.NotNull(beachesPage);
        Assert.NotEmpty(beachesPage!.Items);
        Assert.All(beachesPage.Items, item =>
        {
            Assert.Equal("Beaches", item.CategoryLabel);
            Assert.Equal("Based on recent activity: Beaches", item.Reason);
        });
    }

    private sealed class ExploreFeedDto
    {
        public List<ExploreCardDto> TopStays { get; set; } = [];
        public List<ExploreCardDto> LocalFavorites { get; set; } = [];
        public List<ExploreCuratedCardDto> CuratedForYou { get; set; } = [];
    }

    private sealed class ExploreCardDto
    {
        public string Title { get; set; } = string.Empty;
        public double? Rating { get; set; }
        public string? ReviewCount { get; set; }
    }

    private sealed class ExploreCuratedCardDto
    {
        public string Title { get; set; } = string.Empty;
        public string? Subtitle { get; set; }
        public string? CategoryLabel { get; set; }
    }

    private sealed class RecommendationsPageDto
    {
        public List<RecommendationFilterDto> Filters { get; set; } = [];
        public List<RecommendationItemDto> Items { get; set; } = [];
        public bool HasSignals { get; set; }
        public string? EmptyReason { get; set; }
        public int NextSkip { get; set; }
        public bool HasMore { get; set; }
    }

    private sealed class RecommendationFilterDto
    {
        public int TagId { get; set; }
        public string Label { get; set; } = string.Empty;
    }

    private sealed class RecommendationItemDto
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string? Reason { get; set; }
        public string? CategoryLabel { get; set; }
    }

    private static async Task<(HttpClient client, int userId)> RegisterFreshUserAsync(GeotrackApiFactory app)
    {
        var client = app.CreateClient();
        var email = $"explore-{Guid.NewGuid():N}@example.com";

        var response = await client.PostAsJsonAsync(
            "/api/auth/register",
            new RegisterRequestDto
            {
                FullName = "Explore Tester",
                Email = email,
                Password = "Password123!",
            });
        response.EnsureSuccessStatusCode();

        var login = await response.Content.ReadFromJsonAsync<LoginResponseDto>();
        Assert.NotNull(login);

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", login.AccessToken);

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userId = await db.Users
            .Where(user => user.Email == email)
            .Select(user => user.Id)
            .SingleAsync();

        return (client, userId);
    }
}
