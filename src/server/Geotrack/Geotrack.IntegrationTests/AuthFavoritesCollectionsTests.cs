using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Geotrack.Application.DTOs.Auth;
using Geotrack.Application.DTOs.Favorites;
using Geotrack.Application.DTOs.Tags;
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
        Assert.Equal(string.Empty, tokenPair.RefreshToken);
        Assert.Equal(900, tokenPair.ExpiresIn);
        Assert.True(login.Headers.TryGetValues("Set-Cookie", out var authCookies));
        Assert.Contains(authCookies, value => value.Contains("geotrack_refresh=", StringComparison.Ordinal));

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
    public async Task Refresh_flow_uses_cookie_backed_session_and_logout_revokes_it()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        using var client = app.CreateClient();

        var login = await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequestDto
            {
                Email = "marko.p@example.com",
                Password = "password123",
                RememberMe = false,
            });
        login.EnsureSuccessStatusCode();

        var loggedInSession = await login.Content.ReadFromJsonAsync<LoginResponseDto>();
        Assert.NotNull(loggedInSession);
        Assert.False(string.IsNullOrWhiteSpace(loggedInSession.AccessToken));
        Assert.Equal(string.Empty, loggedInSession.RefreshToken);

        var refresh = await client.PostAsJsonAsync("/api/auth/refresh", new { });
        refresh.EnsureSuccessStatusCode();

        var refreshedSession = await refresh.Content.ReadFromJsonAsync<RefreshResponseDto>();
        Assert.NotNull(refreshedSession);
        Assert.False(string.IsNullOrWhiteSpace(refreshedSession.AccessToken));
        Assert.Equal(string.Empty, refreshedSession.RefreshToken);

        var logout = await client.PostAsJsonAsync("/api/auth/logout", new { });
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

        var refreshAfterLogout = await client.PostAsJsonAsync("/api/auth/refresh", new { });
        Assert.Equal(HttpStatusCode.Unauthorized, refreshAfterLogout.StatusCode);
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

    [Fact]
    public async Task Collection_item_membership_drives_favorite_state_and_rejects_duplicates()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        using var client = await LoginAsync(app);

        var createResponse = await client.PostAsJsonAsync(
            "/api/user/me/collections",
            new CreateCollectionRequestDto
            {
                Name = "Saved from cards",
                Description = "Collection item coverage",
            });
        createResponse.EnsureSuccessStatusCode();

        var createdCollection = await createResponse.Content.ReadFromJsonAsync<CreateCollectionResponseDto>();
        Assert.NotNull(createdCollection);

        var addResponse = await client.PostAsJsonAsync(
            $"/api/user/me/collections/{createdCollection.Id}/items",
            new AddCollectionItemRequestDto { EntityType = "place", EntityId = 1 });
        addResponse.EnsureSuccessStatusCode();

        var addedItem = await addResponse.Content.ReadFromJsonAsync<AddCollectionItemResponseDto>();
        Assert.NotNull(addedItem);

        var favoritesAfterCollectionSave = await client.GetFromJsonAsync<HashSet<int>>("/api/favorites?entityType=place");
        Assert.NotNull(favoritesAfterCollectionSave);
        Assert.Contains(1, favoritesAfterCollectionSave);

        var duplicateResponse = await client.PostAsJsonAsync(
            $"/api/user/me/collections/{createdCollection.Id}/items",
            new AddCollectionItemRequestDto { EntityType = "place", EntityId = 1 });
        Assert.Equal(HttpStatusCode.BadRequest, duplicateResponse.StatusCode);

        var details = await client.GetFromJsonAsync<GetCollectionDetailsResponseDto>(
            $"/api/user/me/collections/{createdCollection.Id}");
        Assert.NotNull(details);
        Assert.Single(details.Items);
        Assert.Equal(1, details.Items[0].PlaceId);
        Assert.Equal("place", details.Items[0].EntityType);
        Assert.Equal("/places/1", details.Items[0].DetailRoute);

        var removeResponse = await client.DeleteAsync(
            $"/api/user/me/collections/{createdCollection.Id}/items/{addedItem.Id}");
        Assert.Equal(HttpStatusCode.NoContent, removeResponse.StatusCode);

        var favoritesAfterRemove = await client.GetFromJsonAsync<HashSet<int>>("/api/favorites?entityType=place");
        Assert.NotNull(favoritesAfterRemove);
        Assert.DoesNotContain(1, favoritesAfterRemove);

        var addAgainResponse = await client.PostAsJsonAsync(
            $"/api/user/me/collections/{createdCollection.Id}/items",
            new AddCollectionItemRequestDto { EntityType = "place", EntityId = 1 });
        addAgainResponse.EnsureSuccessStatusCode();

        var favoritesAfterAddAgain = await client.GetFromJsonAsync<HashSet<int>>("/api/favorites?entityType=place");
        Assert.NotNull(favoritesAfterAddAgain);
        Assert.Contains(1, favoritesAfterAddAgain);

        var deleteCollectionResponse = await client.DeleteAsync($"/api/user/me/collections/{createdCollection.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteCollectionResponse.StatusCode);

        var favoritesAfterCollectionDelete = await client.GetFromJsonAsync<HashSet<int>>("/api/favorites?entityType=place");
        Assert.NotNull(favoritesAfterCollectionDelete);
        Assert.DoesNotContain(1, favoritesAfterCollectionDelete);
    }

    [Fact]
    public async Task Deleting_one_of_multiple_collections_keeps_remaining_membership_favorited()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        using var client = await LoginAsync(app);

        var firstCollection = await CreateCollectionAsync(client, "First saved set");
        var secondCollection = await CreateCollectionAsync(client, "Second saved set");

        var firstAdd = await client.PostAsJsonAsync(
            $"/api/user/me/collections/{firstCollection.Id}/items",
            new AddCollectionItemRequestDto { EntityType = "place", EntityId = 1 });
        firstAdd.EnsureSuccessStatusCode();

        var secondAdd = await client.PostAsJsonAsync(
            $"/api/user/me/collections/{secondCollection.Id}/items",
            new AddCollectionItemRequestDto { EntityType = "place", EntityId = 1 });
        secondAdd.EnsureSuccessStatusCode();

        var favoritesAfterBothAdds = await client.GetFromJsonAsync<HashSet<int>>("/api/favorites?entityType=place");
        Assert.NotNull(favoritesAfterBothAdds);
        Assert.Contains(1, favoritesAfterBothAdds);

        var deleteFirst = await client.DeleteAsync($"/api/user/me/collections/{firstCollection.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteFirst.StatusCode);

        var favoritesAfterDeletingOne = await client.GetFromJsonAsync<HashSet<int>>("/api/favorites?entityType=place");
        Assert.NotNull(favoritesAfterDeletingOne);
        Assert.Contains(1, favoritesAfterDeletingOne);

        var remainingDetails = await client.GetFromJsonAsync<GetCollectionDetailsResponseDto>(
            $"/api/user/me/collections/{secondCollection.Id}");
        Assert.NotNull(remainingDetails);
        var remainingItem = Assert.Single(remainingDetails.Items);
        Assert.Equal(1, remainingItem.PlaceId);
        Assert.Equal("/places/1", remainingItem.DetailRoute);

        var duplicateInRemaining = await client.PostAsJsonAsync(
            $"/api/user/me/collections/{secondCollection.Id}/items",
            new AddCollectionItemRequestDto { EntityType = "place", EntityId = 1 });
        Assert.Equal(HttpStatusCode.BadRequest, duplicateInRemaining.StatusCode);

        var deleteSecond = await client.DeleteAsync($"/api/user/me/collections/{secondCollection.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteSecond.StatusCode);

        var favoritesAfterDeletingLast = await client.GetFromJsonAsync<HashSet<int>>("/api/favorites?entityType=place");
        Assert.NotNull(favoritesAfterDeletingLast);
        Assert.DoesNotContain(1, favoritesAfterDeletingLast);
    }

    [Fact]
    public async Task Profile_avatar_flow_uploads_serves_replaces_and_persists_current_user_avatar()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        using var client = await RegisterAndLoginAsync(app);

        using var firstUpload = CreateAvatarUploadContent(AvatarPngBytes, "avatar-one.png", "image/png");
        var firstResponse = await client.PutAsync("/api/user/me/avatar", firstUpload);
        firstResponse.EnsureSuccessStatusCode();

        var firstAvatar = await firstResponse.Content.ReadFromJsonAsync<UpdateAvatarResponseDto>();
        Assert.NotNull(firstAvatar);
        Assert.StartsWith("/media/uploads/avatars/", firstAvatar.AvatarUrl);

        var meAfterFirstUpload = await client.GetFromJsonAsync<GetMeResponseDto>("/api/user/me");
        Assert.NotNull(meAfterFirstUpload);
        Assert.Equal(firstAvatar.AvatarUrl, meAfterFirstUpload.AvatarUrl);

        var firstAvatarFile = await client.GetAsync(firstAvatar.AvatarUrl);
        firstAvatarFile.EnsureSuccessStatusCode();
        Assert.Equal("image/png", firstAvatarFile.Content.Headers.ContentType?.MediaType);

        using var secondUpload = CreateAvatarUploadContent(AvatarPngBytes, "avatar-two.png", "image/png");
        var secondResponse = await client.PutAsync("/api/user/me/avatar", secondUpload);
        secondResponse.EnsureSuccessStatusCode();

        var secondAvatar = await secondResponse.Content.ReadFromJsonAsync<UpdateAvatarResponseDto>();
        Assert.NotNull(secondAvatar);
        Assert.StartsWith("/media/uploads/avatars/", secondAvatar.AvatarUrl);
        Assert.NotEqual(firstAvatar.AvatarUrl, secondAvatar.AvatarUrl);

        var meAfterSecondUpload = await client.GetFromJsonAsync<GetMeResponseDto>("/api/user/me");
        Assert.NotNull(meAfterSecondUpload);
        Assert.Equal(secondAvatar.AvatarUrl, meAfterSecondUpload.AvatarUrl);

        var oldAvatarRequest = await client.GetAsync(firstAvatar.AvatarUrl);
        Assert.Equal(HttpStatusCode.NotFound, oldAvatarRequest.StatusCode);

        var secondAvatarFile = await client.GetAsync(secondAvatar.AvatarUrl);
        secondAvatarFile.EnsureSuccessStatusCode();
        Assert.Equal("image/png", secondAvatarFile.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Profile_avatar_flow_rejects_invalid_uploads()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        using var client = await RegisterAndLoginAsync(app);

        using var invalidUpload = CreateAvatarUploadContent(new byte[] { 1, 2, 3, 4 }, "avatar.txt", "text/plain");
        var response = await client.PutAsync("/api/user/me/avatar", invalidUpload);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var errorBody = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        Assert.NotNull(errorBody);
        Assert.Equal("Only PNG, JPG, GIF, or WEBP images are supported.", errorBody["message"]);
    }

    [Fact]
    public async Task Swagger_openapi_includes_avatar_upload_endpoint_as_multipart_form_data()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        using var client = app.CreateClient();

        var response = await client.GetAsync("/swagger/v1/swagger.json");
        response.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        var operation = root
            .GetProperty("paths")
            .GetProperty("/api/user/me/avatar")
            .GetProperty("put");

        var requestBodySchema = operation
            .GetProperty("requestBody")
            .GetProperty("content")
            .GetProperty("multipart/form-data")
            .GetProperty("schema");

        var resolvedSchema = ResolveSchema(root, requestBodySchema);
        var avatarProperty = resolvedSchema
            .GetProperty("properties")
            .GetProperty("avatar");

        Assert.Equal("string", avatarProperty.GetProperty("type").GetString());
        Assert.Equal("binary", avatarProperty.GetProperty("format").GetString());
    }

    [Fact]
    public async Task Register_flow_sets_and_clears_interests_onboarding_state()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        using var client = app.CreateClient();

        var email = $"onboarding-{Guid.NewGuid():N}@example.com";

        var registerResponse = await client.PostAsJsonAsync(
            "/api/auth/register",
            new RegisterRequestDto
            {
                FullName = "Onboarding Traveler",
                Email = email,
                Password = "Password123!",
            });
        registerResponse.EnsureSuccessStatusCode();

        var registeredSession = await registerResponse.Content.ReadFromJsonAsync<LoginResponseDto>();
        Assert.NotNull(registeredSession);
        Assert.False(string.IsNullOrWhiteSpace(registeredSession.AccessToken));

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", registeredSession.AccessToken);

        var meAfterRegister = await client.GetFromJsonAsync<GetMeResponseDto>("/api/user/me");
        Assert.NotNull(meAfterRegister);
        Assert.True(meAfterRegister.NeedsInterestsOnboarding);
        Assert.Equal(email, meAfterRegister.Email);

        var availableTags = await client.GetFromJsonAsync<List<TagDto>>("/api/tags");
        Assert.NotNull(availableTags);
        Assert.NotEmpty(availableTags);

        var onboardingTags = await client.GetFromJsonAsync<List<TagDto>>("/api/tags?forUser=true");
        Assert.NotNull(onboardingTags);
        Assert.NotEmpty(onboardingTags);
        Assert.Contains(onboardingTags, tag => tag.Slug == "food-drink");
        Assert.DoesNotContain(onboardingTags, tag => tag.Slug == "budget");

        var updateInterestsResponse = await client.PutAsJsonAsync(
            "/api/user/me/interests",
            new UpdateUserInterestsRequestDto
            {
                TagIds = new List<int> { availableTags[0].Id }
            });
        updateInterestsResponse.EnsureSuccessStatusCode();

        var meAfterInterests = await client.GetFromJsonAsync<GetMeResponseDto>("/api/user/me");
        Assert.NotNull(meAfterInterests);
        Assert.False(meAfterInterests.NeedsInterestsOnboarding);
    }

    [Fact]
    public async Task Register_flow_returns_a_clear_message_when_the_email_is_already_in_use()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        using var client = app.CreateClient();

        var duplicateResponse = await client.PostAsJsonAsync(
            "/api/auth/register",
            new RegisterRequestDto
            {
                FullName = "Duplicate Traveler",
                Email = "marko.p@example.com",
                Password = "Password123!",
            });

        Assert.Equal(HttpStatusCode.BadRequest, duplicateResponse.StatusCode);

        var errorBody = await duplicateResponse.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        Assert.NotNull(errorBody);
        Assert.Equal("An account with this email already exists.", errorBody["message"]);
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

    private static async Task<CreateCollectionResponseDto> CreateCollectionAsync(HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync(
            "/api/user/me/collections",
            new CreateCollectionRequestDto
            {
                Name = name,
                Description = "Integration test collection",
            });
        response.EnsureSuccessStatusCode();

        var created = await response.Content.ReadFromJsonAsync<CreateCollectionResponseDto>();
        Assert.NotNull(created);
        return created;
    }

    private static MultipartFormDataContent CreateAvatarUploadContent(byte[] bytes, string fileName, string contentType)
    {
        var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        content.Add(fileContent, "avatar", fileName);
        return content;
    }

    private static async Task<HttpClient> RegisterAndLoginAsync(GeotrackApiFactory app)
    {
        var client = app.CreateClient();
        var email = $"avatar-{Guid.NewGuid():N}@example.com";

        var registerResponse = await client.PostAsJsonAsync(
            "/api/auth/register",
            new RegisterRequestDto
            {
                FullName = "Avatar Tester",
                Email = email,
                Password = "Password123!",
            });
        registerResponse.EnsureSuccessStatusCode();

        var login = await registerResponse.Content.ReadFromJsonAsync<LoginResponseDto>();
        Assert.NotNull(login);

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", login.AccessToken);

        return client;
    }

    private static JsonElement ResolveSchema(JsonElement root, JsonElement schema)
    {
        if (!schema.TryGetProperty("$ref", out var schemaRef))
            return schema;

        var reference = schemaRef.GetString()
            ?? throw new InvalidOperationException("Swagger schema reference was missing.");
        var referenceSegments = reference.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var schemaName = referenceSegments[^1];

        return root
            .GetProperty("components")
            .GetProperty("schemas")
            .GetProperty(schemaName);
    }

    private static readonly byte[] AvatarPngBytes = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jx4kAAAAASUVORK5CYII=");
}
