using System.Net.Http.Json;
using Geotrack.IntegrationTests.Infrastructure;
using Xunit;

namespace Geotrack.IntegrationTests;

public sealed class ExploreRatingsTests
{
    [Fact]
    public async Task Explore_feed_uses_real_review_aggregates_and_keeps_unrated_entities_empty()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        using var client = app.CreateClient();

        var response = await client.GetFromJsonAsync<ExploreFeedDto>(
            "/api/explore?latitude=42.6507&longitude=18.0944");

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

    private sealed class ExploreFeedDto
    {
        public List<ExploreCardDto> TopStays { get; set; } = [];
        public List<ExploreCardDto> LocalFavorites { get; set; } = [];
    }

    private sealed class ExploreCardDto
    {
        public string Title { get; set; } = string.Empty;
        public double? Rating { get; set; }
        public string? ReviewCount { get; set; }
    }
}
