using Geotrack.Application.DTOs.Agent;
using Geotrack.Application.DTOs.GeoTrackTools;
using Geotrack.Application.DTOs.Events;
using Geotrack.Application.DTOs.Places;
using Geotrack.Application.Interfaces.Service;
using Geotrack.Application.Options;
using Geotrack.Domain.Entities.Core;
using Geotrack.Infrastructure;
using Geotrack.Infrastructure.Services.Agent;
using Geotrack.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Geotrack.IntegrationTests;

public class AgentCatalogGroundingTests
{
    [Fact]
    public async Task Beaches_in_Dubrovnik_returns_beach_context_from_catalog()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        await using var scope = app.Services.CreateAsyncScope();
        var tools = scope.ServiceProvider.GetRequiredService<IGeoTrackToolService>();

        var context = await tools.BuildAgentContextAsync("beaches in Dubrovnik", null, 5);

        Assert.True(context.SearchAttempted);
        Assert.False(context.NoResults);
        Assert.Contains("geotrack_search_places", context.ToolsUsed);
        Assert.Contains(context.Places, place =>
            string.Equals(place.Category, "Beach", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(place.Destination, "Dubrovnik", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(context.FiltersUsed, value => value.Contains("beach", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Beaches_in_Dubrovnik_chat_returns_place_cards_from_catalog()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        await using var scope = app.Services.CreateAsyncScope();
        var tools = scope.ServiceProvider.GetRequiredService<IGeoTrackToolService>();
        var orchestrator = BuildOrchestrator(tools);

        var response = await orchestrator.ChatAsync(ChatRequest("beaches in Dubrovnik"), userId: 1);

        Assert.NotEmpty(response.Cards);
        Assert.All(response.Cards, card =>
        {
            Assert.Equal("place", card.EntityType);
            Assert.True(card.Id > 0);
            Assert.StartsWith("/places/", card.DetailRoute);
        });
        Assert.Contains(response.Cards, card =>
            string.Equals(card.PlaceType, "Beach", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Agent_place_card_image_matches_public_detail_image_ordering()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        using var client = app.CreateClient();
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tools = scope.ServiceProvider.GetRequiredService<IGeoTrackToolService>();
        var orchestrator = BuildOrchestrator(tools);

        var seededBeach = await db.Places
            .AsNoTracking()
            .Include(place => place.Images)
            .SingleAsync(place => place.Name == "Banje Beach");
        var expectedImage = OrderedImageUrls(seededBeach.Images).First();

        var response = await orchestrator.ChatAsync(ChatRequest("beach in Dubrovnik"), userId: 1);
        var card = Assert.Single(response.Cards, card =>
            card.EntityType == "place" &&
            card.Id == seededBeach.Id);
        var detail = await client.GetFromJsonAsync<PlaceDetailDto>($"/api/places/{seededBeach.Id}");

        Assert.NotNull(detail);
        Assert.False(string.IsNullOrWhiteSpace(card.ImageUrl));
        Assert.Equal(expectedImage, card.ImageUrl);
        Assert.Equal(detail!.HeroImage, card.ImageUrl);
    }

    [Fact]
    public async Task Agent_cards_include_public_images_for_places_stays_and_events()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        using var client = app.CreateClient();
        await using var scope = app.Services.CreateAsyncScope();
        var tools = scope.ServiceProvider.GetRequiredService<IGeoTrackToolService>();
        var orchestrator = BuildOrchestrator(tools);

        var placeResponse = await orchestrator.ChatAsync(ChatRequest("beaches in Dubrovnik"), userId: 1);
        var stayResponse = await orchestrator.ChatAsync(ChatRequest("all hotels in Dubrovnik"), userId: 1);
        var eventResponse = await orchestrator.ChatAsync(ChatRequest("events in Dubrovnik"), userId: 1);

        var placeCard = Assert.Single(placeResponse.Cards, card => card.EntityType == "place");
        var stayCard = stayResponse.Cards.FirstOrDefault(card =>
            card.EntityType == "stay" &&
            !string.IsNullOrWhiteSpace(card.ImageUrl));
        var eventCard = eventResponse.Cards.FirstOrDefault(card =>
            card.EntityType == "event" &&
            !string.IsNullOrWhiteSpace(card.ImageUrl));

        Assert.NotNull(stayCard);
        Assert.NotNull(eventCard);

        var placeDetail = await client.GetFromJsonAsync<PlaceDetailDto>($"/api/places/{placeCard.Id}");
        var stayDetail = await client.GetFromJsonAsync<HotelDetailDto>($"/api/places/hotels/{stayCard!.Id}");
        var eventDetail = await client.GetFromJsonAsync<EventDetailDto>($"/api/events/{eventCard!.Id}");

        Assert.NotNull(placeDetail);
        Assert.NotNull(stayDetail);
        Assert.NotNull(eventDetail);
        Assert.Equal(placeDetail!.HeroImage, placeCard.ImageUrl);
        Assert.Equal(stayDetail!.HeroImage, stayCard.ImageUrl);
        Assert.Equal(eventDetail!.HeroImage, eventCard.ImageUrl);
    }

    [Fact]
    public async Task Agent_card_without_image_keeps_null_and_exposes_no_internal_image_data()
    {
        var context = new GeoTrackAgentContextDto
        {
            ToolsUsed = ["geotrack_search_places"],
            LocationUsed = "Testville",
            SearchAttempted = true,
            ResultCount = 1,
            Plan = new GeoTrackAgentQueryPlanDto
            {
                Intent = "catalog_search",
                Language = "en",
                EntityTypes = ["places"],
                LocationUsed = "Testville",
                DestinationName = "Testville"
            },
            Searches =
            [
                new GeoTrackToolSearchTraceDto
                {
                    Tool = "geotrack_search_places",
                    SearchType = "specific",
                    Location = "Testville",
                    ResultCount = 1
                }
            ],
            Places =
            [
                new GeoTrackToolItemDto
                {
                    Id = 501,
                    EntityType = "place",
                    Title = "Image-Free Place",
                    Location = "Test Cove",
                    Destination = "Testville",
                    Category = "Landmark",
                    DetailRoute = "/places/501",
                    ImageUrl = null
                }
            ]
        };
        var orchestrator = BuildOrchestrator(new FakeToolService(context));

        var response = await orchestrator.ChatAsync(ChatRequest("places in Testville"), userId: 1);
        var card = Assert.Single(response.Cards);
        var cardsJson = JsonSerializer.Serialize(response.Cards);

        Assert.Null(card.ImageUrl);
        Assert.DoesNotContain("Images", cardsJson, StringComparison.Ordinal);
        Assert.DoesNotContain("EntityImage", cardsJson, StringComparison.Ordinal);
        Assert.DoesNotContain("IsMain", cardsJson, StringComparison.Ordinal);
        Assert.DoesNotContain("SortOrder", cardsJson, StringComparison.Ordinal);
        Assert.DoesNotContain("CreatedAt", cardsJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Agent_card_normalizes_loopback_media_image_to_public_path()
    {
        var context = new GeoTrackAgentContextDto
        {
            ToolsUsed = ["geotrack_search_places"],
            LocationUsed = "Testville",
            SearchAttempted = true,
            ResultCount = 1,
            Plan = new GeoTrackAgentQueryPlanDto
            {
                Intent = "catalog_search",
                Language = "en",
                EntityTypes = ["places"],
                LocationUsed = "Testville",
                DestinationName = "Testville"
            },
            Searches =
            [
                new GeoTrackToolSearchTraceDto
                {
                    Tool = "geotrack_search_places",
                    SearchType = "specific",
                    Location = "Testville",
                    ResultCount = 1
                }
            ],
            Places =
            [
                new GeoTrackToolItemDto
                {
                    Id = 502,
                    EntityType = "place",
                    Title = "Loopback Image Place",
                    Location = "Test Cove",
                    Destination = "Testville",
                    Category = "Landmark",
                    DetailRoute = "/places/502",
                    ImageUrl = "http://localhost:5003/media/seed/catalog-place.svg"
                }
            ]
        };
        var orchestrator = BuildOrchestrator(new FakeToolService(context));

        var response = await orchestrator.ChatAsync(ChatRequest("places in Testville"), userId: 1);
        var card = Assert.Single(response.Cards);

        Assert.Equal("/media/seed/catalog-place.svg", card.ImageUrl);
    }

    [Fact]
    public async Task Hotels_in_Dubrovnik_chat_returns_stay_cards_from_catalog()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        await using var scope = app.Services.CreateAsyncScope();
        var tools = scope.ServiceProvider.GetRequiredService<IGeoTrackToolService>();
        var orchestrator = BuildOrchestrator(tools);

        var response = await orchestrator.ChatAsync(ChatRequest("all hotels in Dubrovnik"), userId: 1);

        Assert.NotEmpty(response.Cards);
        Assert.All(response.Cards, card =>
        {
            Assert.Equal("stay", card.EntityType);
            Assert.True(card.Id > 0);
            Assert.StartsWith("/stays/", card.DetailRoute);
        });
        Assert.Contains(response.Cards, card => !string.IsNullOrWhiteSpace(card.StayType));
    }

    [Fact]
    public async Task Events_in_Dubrovnik_chat_returns_event_cards_from_catalog()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        await using var scope = app.Services.CreateAsyncScope();
        var tools = scope.ServiceProvider.GetRequiredService<IGeoTrackToolService>();
        var orchestrator = BuildOrchestrator(tools);

        var response = await orchestrator.ChatAsync(ChatRequest("events in Dubrovnik"), userId: 1);

        Assert.NotEmpty(response.Cards);
        Assert.All(response.Cards, card =>
        {
            Assert.Equal("event", card.EntityType);
            Assert.True(card.Id > 0);
            Assert.StartsWith("/events/", card.DetailRoute);
            Assert.False(string.IsNullOrWhiteSpace(card.DateText));
        });
    }

    [Fact]
    public async Task Places_in_Dubrovnik_returns_places_from_catalog()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        await using var scope = app.Services.CreateAsyncScope();
        var tools = scope.ServiceProvider.GetRequiredService<IGeoTrackToolService>();

        var context = await tools.BuildAgentContextAsync("places in Dubrovnik", null, 5);

        Assert.True(context.SearchAttempted);
        Assert.False(context.NoResults);
        Assert.NotEmpty(context.Places);
        Assert.All(context.Places, place => Assert.Equal("Dubrovnik", place.Destination));
    }

    [Fact]
    public async Task Dubrovnik_location_query_returns_mixed_catalog_context()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        await using var scope = app.Services.CreateAsyncScope();
        var tools = scope.ServiceProvider.GetRequiredService<IGeoTrackToolService>();

        var context = await tools.BuildAgentContextAsync("Dubrovnik", null, 5);

        Assert.True(context.SearchAttempted);
        Assert.False(context.NoResults);
        Assert.NotEmpty(context.Places);
        Assert.NotEmpty(context.Stays);
        Assert.NotEmpty(context.Events);
    }

    [Fact]
    public async Task Serbian_beach_query_resolves_to_beach_and_Dubrovnik()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        await using var scope = app.Services.CreateAsyncScope();
        var tools = scope.ServiceProvider.GetRequiredService<IGeoTrackToolService>();

        var context = await tools.BuildAgentContextAsync("Plaže u Dubrovniku", null, 5);

        Assert.Equal("sr", context.Plan?.Language);
        Assert.Equal("Dubrovnik", context.Plan?.DestinationName);
        Assert.Contains(context.Plan?.Categories ?? [], category => category == "beach");
        Assert.Contains(context.Places, place => string.Equals(place.Category, "Beach", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Top_attractions_without_location_asks_for_clarification()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        await using var scope = app.Services.CreateAsyncScope();
        var tools = scope.ServiceProvider.GetRequiredService<IGeoTrackToolService>();

        var context = await tools.BuildAgentContextAsync("Show me top attractions", null, 5);

        Assert.True(context.ClarificationNeeded);
        Assert.False(context.SearchAttempted);
        Assert.Contains("city", context.ClarificationQuestion, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Top_attractions_with_current_context_uses_that_context()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        await using var scope = app.Services.CreateAsyncScope();
        var tools = scope.ServiceProvider.GetRequiredService<IGeoTrackToolService>();

        var context = await tools.BuildAgentContextAsync(
            "Show me top attractions",
            new AgentContextOptionsDto { CurrentDestinationName = "Dubrovnik" },
            5);

        Assert.False(context.ClarificationNeeded);
        Assert.True(context.SearchAttempted);
        Assert.True(context.Plan?.UsesCurrentContext);
        Assert.Equal("Dubrovnik", context.Plan?.DestinationName);
        Assert.NotEmpty(context.Places);
    }

    [Fact]
    public async Task No_results_state_is_only_returned_after_search_attempts()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        await using var scope = app.Services.CreateAsyncScope();
        var tools = scope.ServiceProvider.GetRequiredService<IGeoTrackToolService>();

        var context = await tools.BuildAgentContextAsync("volcano observatories in Dubrovnik", null, 5);

        Assert.True(context.SearchAttempted);
        Assert.True(context.NoResults);
        Assert.Empty(context.Places);
        Assert.Empty(context.Stays);
        Assert.Empty(context.Events);
        Assert.All(context.Searches, search => Assert.Equal(0, search.ResultCount));
    }

    [Fact]
    public async Task No_result_chat_response_returns_empty_cards()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        await using var scope = app.Services.CreateAsyncScope();
        var tools = scope.ServiceProvider.GetRequiredService<IGeoTrackToolService>();
        var orchestrator = BuildOrchestrator(tools);

        var response = await orchestrator.ChatAsync(ChatRequest("volcano observatories in Dubrovnik"), userId: 1);

        Assert.Empty(response.Cards);
        Assert.Contains("did not find", response.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Tool_result_caps_are_enforced()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        await using var scope = app.Services.CreateAsyncScope();
        var tools = scope.ServiceProvider.GetRequiredService<IGeoTrackToolService>();

        var results = await tools.SearchPlacesAsync(new GeoTrackToolSearchRequest
        {
            Query = "Dubrovnik",
            Limit = 100
        });

        Assert.True(results.Count <= 10);
    }

    [Fact]
    public async Task Gemini_prompt_receives_structured_tool_context()
    {
        var provider = new CapturingProvider();
        var orchestrator = new AgentOrchestrator(
            new SingleProviderRegistry(provider),
            new FakeKeyService(),
            new FakeToolService(),
            Options.Create(new AgentOptions { MaxPromptLength = 200, MaxContextItems = 5, RequestTimeoutSeconds = 10 }),
            Options.Create(new McpOptions { Enabled = true }),
            NullLogger<AgentOrchestrator>.Instance);

        var response = await orchestrator.ChatAsync(
            new AgentChatRequestDto
            {
                Message = "beaches in Testville",
                Provider = "gemini",
                Model = "gemini-test",
                KeyMode = AgentKeyModes.Session,
                SessionApiKey = "session-key-123"
            },
            userId: 1);

        Assert.NotNull(provider.LastRequest);
        Assert.Contains("GeoTrack context JSON", provider.LastRequest!.SystemPrompt);
        Assert.Contains("Fixture Beach", provider.LastRequest.SystemPrompt);
        Assert.Contains("searchAttempted", provider.LastRequest.SystemPrompt);
        Assert.Contains("geotrack_search_places", provider.LastRequest.SystemPrompt);
        var card = Assert.Single(response.Cards);
        Assert.Equal("Fixture Beach", card.Title);
        Assert.Equal("place", card.EntityType);
        Assert.Equal(123, card.Id);
        Assert.Equal("/places/123", card.DetailRoute);
    }

    [Fact]
    public async Task Chat_response_cards_are_capped_and_do_not_contain_provider_secrets()
    {
        var context = new GeoTrackAgentContextDto
        {
            ToolsUsed = ["geotrack_search_places"],
            LocationUsed = "Testville",
            SearchAttempted = true,
            ResultCount = 12,
            Plan = new GeoTrackAgentQueryPlanDto
            {
                Intent = "catalog_search",
                Language = "en",
                EntityTypes = ["places"],
                LocationUsed = "Testville",
                DestinationName = "Testville"
            },
            Searches =
            [
                new GeoTrackToolSearchTraceDto
                {
                    Tool = "geotrack_search_places",
                    SearchType = "specific",
                    Location = "Testville",
                    ResultCount = 12
                }
            ],
            Places = Enumerable.Range(1, 12)
                .Select(id => new GeoTrackToolItemDto
                {
                    Id = id,
                    EntityType = "place",
                    Title = $"Fixture Place {id}",
                    Location = "Test Cove",
                    Destination = "Testville",
                    Category = "Landmark",
                    DetailRoute = $"/places/{id}"
                })
                .ToList()
        };
        var orchestrator = BuildOrchestrator(new FakeToolService(context), maxToolResults: 10, maxContextItems: 10);

        var response = await orchestrator.ChatAsync(
            ChatRequest("places in Testville", sessionApiKey: "session-key-secret-123"),
            userId: 1);

        Assert.True(response.Cards.Count <= 5);
        Assert.All(response.Cards, card =>
        {
            Assert.True(card.Id > 0);
            Assert.StartsWith("/places/", card.DetailRoute);
        });

        var cardsJson = JsonSerializer.Serialize(response.Cards);
        Assert.DoesNotContain("session-key-secret-123", cardsJson, StringComparison.Ordinal);
        Assert.DoesNotContain("saved-key-123", cardsJson, StringComparison.Ordinal);
        Assert.DoesNotContain("gemini-test", cardsJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Overlong_chat_prompt_is_rejected_safely()
    {
        var orchestrator = new AgentOrchestrator(
            new SingleProviderRegistry(new CapturingProvider()),
            new FakeKeyService(),
            new FakeToolService(),
            Options.Create(new AgentOptions { MaxPromptLength = 20, MaxContextItems = 5, RequestTimeoutSeconds = 10 }),
            Options.Create(new McpOptions()),
            NullLogger<AgentOrchestrator>.Instance);

        var ex = await Assert.ThrowsAsync<Geotrack.Application.Services.AgentException>(() =>
            orchestrator.ChatAsync(
                new AgentChatRequestDto
                {
                    Message = new string('x', 250),
                    Provider = "gemini",
                    Model = "gemini-test",
                    KeyMode = AgentKeyModes.Session,
                    SessionApiKey = "session-key-123"
                },
                userId: 1));

        Assert.Equal("prompt_too_long", ex.Code);
    }

    private static AgentChatRequestDto ChatRequest(
        string message,
        string sessionApiKey = "session-key-123") => new()
    {
        Message = message,
        Provider = "gemini",
        Model = "gemini-test",
        KeyMode = AgentKeyModes.Session,
        SessionApiKey = sessionApiKey
    };

    private static AgentOrchestrator BuildOrchestrator(
        IGeoTrackToolService tools,
        int maxToolResults = 10,
        int maxContextItems = 6)
    {
        return new AgentOrchestrator(
            new SingleProviderRegistry(new CapturingProvider()),
            new FakeKeyService(),
            tools,
            Options.Create(new AgentOptions
            {
                MaxPromptLength = 4000,
                MaxContextItems = maxContextItems,
                MaxToolResults = maxToolResults,
                RequestTimeoutSeconds = 10
            }),
            Options.Create(new McpOptions { Enabled = true }),
            NullLogger<AgentOrchestrator>.Instance);
    }

    private static List<string> OrderedImageUrls(IEnumerable<EntityImage> images) =>
        images
            .OrderByDescending(image => image.IsMain)
            .ThenBy(image => image.SortOrder)
            .ThenBy(image => image.CreatedAt)
            .ThenBy(image => image.Id)
            .Select(image => image.Url)
            .ToList();

    private sealed class FakeToolService : IGeoTrackToolService
    {
        private readonly GeoTrackAgentContextDto _context;

        public FakeToolService()
            : this(DefaultContext())
        {
        }

        public FakeToolService(GeoTrackAgentContextDto context)
        {
            _context = context;
        }

        public Task<GeoTrackAgentContextDto> BuildAgentContextAsync(
            string message,
            AgentContextOptionsDto? contextOptions,
            int maxItems,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_context);

        private static GeoTrackAgentContextDto DefaultContext() => new()
        {
            ToolsUsed = ["geotrack_search_places"],
            LocationUsed = "Testville",
            SearchAttempted = true,
            ResultCount = 1,
            Plan = new GeoTrackAgentQueryPlanDto
            {
                Intent = "catalog_search",
                Language = "en",
                EntityTypes = ["places"],
                LocationUsed = "Testville",
                DestinationName = "Testville",
                Categories = ["beach"]
            },
            Searches =
            [
                new GeoTrackToolSearchTraceDto
                {
                    Tool = "geotrack_search_places",
                    SearchType = "specific",
                    Location = "Testville",
                    Filters = ["beach"],
                    ResultCount = 1
                }
            ],
            Places =
            [
                new GeoTrackToolItemDto
                {
                    Id = 123,
                    EntityType = "place",
                    Title = "Fixture Beach",
                    Location = "Test Cove",
                    Destination = "Testville",
                    Category = "Beach",
                    DetailRoute = "/places/123",
                    Tags = ["Beaches"]
                }
            ]
        };

        public Task<IReadOnlyList<GeoTrackToolItemDto>> SearchPlacesAsync(GeoTrackToolSearchRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<GeoTrackToolItemDto>>([]);
        public Task<GeoTrackToolItemDto?> GetPlaceDetailsAsync(int id, CancellationToken cancellationToken = default) => Task.FromResult<GeoTrackToolItemDto?>(null);
        public Task<IReadOnlyList<GeoTrackToolItemDto>> SearchStaysAsync(GeoTrackToolSearchRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<GeoTrackToolItemDto>>([]);
        public Task<GeoTrackToolItemDto?> GetStayDetailsAsync(int id, CancellationToken cancellationToken = default) => Task.FromResult<GeoTrackToolItemDto?>(null);
        public Task<IReadOnlyList<GeoTrackToolItemDto>> SearchEventsAsync(GeoTrackToolSearchRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<GeoTrackToolItemDto>>([]);
        public Task<GeoTrackToolItemDto?> GetEventDetailsAsync(int id, CancellationToken cancellationToken = default) => Task.FromResult<GeoTrackToolItemDto?>(null);
        public Task<GeoTrackMapFiltersDto> GetMapFiltersAsync(GeoTrackMapFiltersRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new GeoTrackMapFiltersDto());
    }

    private sealed class CapturingProvider : IAgentProvider
    {
        public AgentProviderChatRequest? LastRequest { get; private set; }
        public string Id => "gemini";

        public AgentProviderMetadataDto GetMetadata(int? userId, AgentApiKeyMetadataDto? savedKey) => new()
        {
            Id = Id,
            DisplayName = "Gemini",
            Enabled = true,
            DefaultModel = "gemini-test",
            SupportsSavedKey = true,
            SupportsSessionKey = true,
            Models = [new AgentProviderModelDto { Id = "gemini-test", DisplayName = "Gemini Test", IsDefault = true }]
        };

        public Task<AgentProviderChatResult> GenerateAsync(AgentProviderChatRequest request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult(new AgentProviderChatResult { Text = "Grounded answer" });
        }

        public Task TestAsync(string apiKey, string model, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class SingleProviderRegistry : IAgentProviderRegistry
    {
        private readonly IAgentProvider _provider;
        public SingleProviderRegistry(IAgentProvider provider) => _provider = provider;
        public IReadOnlyCollection<IAgentProvider> GetAll() => [_provider];
        public IAgentProvider? Get(string providerId) =>
            string.Equals(providerId, _provider.Id, StringComparison.OrdinalIgnoreCase) ? _provider : null;
    }

    private sealed class FakeKeyService : IAiProviderKeyService
    {
        public Task<IReadOnlyList<AgentApiKeyMetadataDto>> GetKeysAsync(int userId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AgentApiKeyMetadataDto>>([]);
        public Task<AgentApiKeyMetadataDto?> GetKeyMetadataAsync(int userId, string provider, CancellationToken cancellationToken = default) =>
            Task.FromResult<AgentApiKeyMetadataDto?>(null);
        public Task<string?> GetDecryptedKeyAsync(int userId, string provider, CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>("saved-key-123");
        public Task<AgentApiKeyMetadataDto> SaveOrReplaceKeyAsync(int userId, string provider, string apiKey, string? displayName, CancellationToken cancellationToken = default) =>
            Task.FromResult(new AgentApiKeyMetadataDto());
        public Task DeleteKeyAsync(int userId, string provider, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task MarkKeyUsedAsync(int userId, string provider, bool success, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
