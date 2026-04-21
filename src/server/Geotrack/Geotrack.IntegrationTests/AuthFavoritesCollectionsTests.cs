using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Geotrack.Application.DTOs.Auth;
using Geotrack.Application.DTOs.Favorites;
using Geotrack.Application.DTOs.Users;
using Geotrack.IntegrationTests.Infrastructure;
using Xunit;

namespace Geotrack.IntegrationTests;

public sealed class AuthFavoritesCollectionsTests
{
    [Fact]
    public async Task Auth_flow_supports_login_invalid_credentials_and_current_user_access()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();

        using var anonymousClient = app.CreateClient();

        var unauthorizedMe = await anonymousClient.GetAsync("/api/user/me");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorizedMe.StatusCode);

        var invalidLogin = await anonymousClient.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequestDto
            {
                Email = "marko.p@example.com",
                Password = "wrong-password",
                RememberMe = false,
            });
        Assert.Equal(HttpStatusCode.Unauthorized, invalidLogin.StatusCode);

        using var authorizedClient = app.CreateClient();
        var login = await authorizedClient.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequestDto
            {
                Email = "marko.p@example.com",
                Password = "password123",
                RememberMe = true,
            });
        login.EnsureSuccessStatusCode();

        var tokenPair = await login.Content.ReadFromJsonAsync<LoginResponseDto>();
        Assert.NotNull(tokenPair);
        Assert.False(string.IsNullOrWhiteSpace(tokenPair.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(tokenPair.RefreshToken));
        Assert.Equal(900, tokenPair.ExpiresIn);

        authorizedClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", tokenPair.AccessToken);

        var meResponse = await authorizedClient.GetAsync("/api/user/me");
        meResponse.EnsureSuccessStatusCode();

        var me = await meResponse.Content.ReadFromJsonAsync<GetMeResponseDto>();
        Assert.NotNull(me);
        Assert.Equal("marko.p@example.com", me.Email);
        Assert.Equal("Marko", me.FirstName);
    }

    [Fact]
    public async Task Favorites_flow_toggles_saved_state_for_an_authenticated_user()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        using var client = await LoginAsync(app);

        var firstToggle = await client.PostAsJsonAsync(
            "/api/favorites/toggle",
            new FavoriteToggleDto { EntityType = "place", EntityId = 1 });
        firstToggle.EnsureSuccessStatusCode();

        var firstResult = await firstToggle.Content.ReadFromJsonAsync<FavoriteToggleResponseDto>();
        Assert.NotNull(firstResult);
        Assert.True(firstResult.IsSaved);

        var favoritesAfterSave = await client.GetFromJsonAsync<HashSet<int>>("/api/favorites?entityType=place");
        Assert.NotNull(favoritesAfterSave);
        Assert.Contains(1, favoritesAfterSave);

        var secondToggle = await client.PostAsJsonAsync(
            "/api/favorites/toggle",
            new FavoriteToggleDto { EntityType = "place", EntityId = 1 });
        secondToggle.EnsureSuccessStatusCode();

        var secondResult = await secondToggle.Content.ReadFromJsonAsync<FavoriteToggleResponseDto>();
        Assert.NotNull(secondResult);
        Assert.False(secondResult.IsSaved);
    }

    [Fact]
    public async Task Favorites_flow_rejects_invalid_entity_type_and_missing_targets()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        using var client = await LoginAsync(app);

        var invalidType = await client.PostAsJsonAsync(
            "/api/favorites/toggle",
            new { entityType = "bogus", entityId = 1 });
        Assert.Equal(HttpStatusCode.BadRequest, invalidType.StatusCode);

        var missingTarget = await client.PostAsJsonAsync(
            "/api/favorites/toggle",
            new FavoriteToggleDto { EntityType = "place", EntityId = 999999 });
        Assert.Equal(HttpStatusCode.NotFound, missingTarget.StatusCode);
    }

    [Fact]
    public async Task Collections_flow_can_read_create_rename_and_delete_current_user_collections()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        using var client = await LoginAsync(app);

        var initialCollections = await client.GetFromJsonAsync<GetCollectionsResponseDto>("/api/user/me/collections");
        Assert.NotNull(initialCollections);
        Assert.Empty(initialCollections.Collections);

        var createResponse = await client.PostAsJsonAsync(
            "/api/user/me/collections",
            new CreateCollectionRequestDto
            {
                Name = "Weekend trip",
                Description = "Batch 11 integration coverage",
            });
        createResponse.EnsureSuccessStatusCode();

        var createdCollection = await createResponse.Content.ReadFromJsonAsync<CreateCollectionResponseDto>();
        Assert.NotNull(createdCollection);
        Assert.True(createdCollection.Id > 0);
        Assert.Equal("Weekend trip", createdCollection.Name);

        var afterCreate = await client.GetFromJsonAsync<GetCollectionsResponseDto>("/api/user/me/collections");
        Assert.NotNull(afterCreate);
        Assert.Contains(afterCreate.Collections, collection => collection.Id == createdCollection.Id);

        var renameResponse = await client.PutAsJsonAsync(
            $"/api/user/me/collections/{createdCollection.Id}",
            new UpdateCollectionRequestDto
            {
                Name = "Renamed weekend trip",
                Description = "Updated during Batch 11 coverage",
            });
        renameResponse.EnsureSuccessStatusCode();

        var renamed = await renameResponse.Content.ReadFromJsonAsync<UpdateCollectionResponseDto>();
        Assert.NotNull(renamed);
        Assert.Equal("Renamed weekend trip", renamed.Name);

        var details = await client.GetFromJsonAsync<GetCollectionDetailsResponseDto>(
            $"/api/user/me/collections/{createdCollection.Id}");
        Assert.NotNull(details);
        Assert.Equal("Renamed weekend trip", details.Name);
        Assert.Empty(details.Items);

        var deleteResponse = await client.DeleteAsync($"/api/user/me/collections/{createdCollection.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var afterDelete = await client.GetFromJsonAsync<GetCollectionsResponseDto>("/api/user/me/collections");
        Assert.NotNull(afterDelete);
        Assert.DoesNotContain(afterDelete.Collections, collection => collection.Id == createdCollection.Id);
    }

    private static async Task<HttpClient> LoginAsync(GeotrackApiFactory app)
    {
        var client = app.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequestDto
            {
                Email = "marko.p@example.com",
                Password = "password123",
                RememberMe = false,
            });
        response.EnsureSuccessStatusCode();

        var login = await response.Content.ReadFromJsonAsync<LoginResponseDto>();
        Assert.NotNull(login);

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", login.AccessToken);

        return client;
    }
}
