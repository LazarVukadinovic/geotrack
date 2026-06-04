using Geotrack.Application.DTOs.Agent;
using Geotrack.Application.DTOs.GeoTrackTools;
using Geotrack.Application.DTOs.Events;
using Geotrack.Application.DTOs.Places;
using Geotrack.Application.Interfaces.Service;
using Geotrack.Application.Options;
using Geotrack.Application.Services;
using Geotrack.Domain.Entities.Core;
using Geotrack.Domain.Entities.Tags;
using Geotrack.Infrastructure;
using Geotrack.Infrastructure.Services.Agent;
using Geotrack.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Geotrack.IntegrationTests;

public class AgentCatalogGroundingTests
{
    private static readonly string[] FoodPlaceTypes = ["Restaurant", "Cafe", "Bar"];

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

        var placeCard = placeResponse.Cards.FirstOrDefault(card =>
            card.EntityType == "place" &&
            !string.IsNullOrWhiteSpace(card.ImageUrl));
        var stayCard = stayResponse.Cards.FirstOrDefault(card =>
            card.EntityType == "stay" &&
            !string.IsNullOrWhiteSpace(card.ImageUrl));
        var eventCard = eventResponse.Cards.FirstOrDefault(card =>
            card.EntityType == "event" &&
            !string.IsNullOrWhiteSpace(card.ImageUrl));

        Assert.NotNull(placeCard);
        Assert.NotNull(stayCard);
        Assert.NotNull(eventCard);

        var placeDetail = await client.GetFromJsonAsync<PlaceDetailDto>($"/api/places/{placeCard!.Id}");
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
        Assert.Null(context.Plan?.QueryText);
        Assert.Contains(context.Places, place => string.Equals(place.Category, "Beach", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Serbian_restaurant_query_returns_food_cards_before_beaches()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        await using var scope = app.Services.CreateAsyncScope();
        var tools = scope.ServiceProvider.GetRequiredService<IGeoTrackToolService>();
        var orchestrator = BuildOrchestrator(tools);

        var response = await orchestrator.ChatAsync(ChatRequest("restorani u Dubrovniku"), userId: 1);

        Assert.NotEmpty(response.Cards);
        Assert.All(response.Cards, card => Assert.Equal("place", card.EntityType));
        Assert.Contains(response.Cards, card => FoodPlaceTypes.Contains(card.PlaceType ?? string.Empty, StringComparer.OrdinalIgnoreCase));
        Assert.DoesNotContain(response.Cards, card => string.Equals(card.PlaceType, "Beach", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("da li ima nesto zanimljivo da se vidi u dubrovniku", "attractions")]
    [InlineData("sta ima u dubrovniku da se vidi", "attractions")]
    [InlineData("aktivnosti u dubrovniku", "general")]
    public async Task Serbian_discovery_queries_resolve_Dubrovnik_without_literal_query_filter(
        string message,
        string expectedCategory)
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        await using var scope = app.Services.CreateAsyncScope();
        var tools = scope.ServiceProvider.GetRequiredService<IGeoTrackToolService>();

        var context = await tools.BuildAgentContextAsync(message, null, 5);

        Assert.Equal("Dubrovnik", context.Plan?.AreaName);
        Assert.Equal(expectedCategory, context.Plan?.Category);
        Assert.Null(context.Plan?.QueryText);
        Assert.True(context.SearchAttempted);
        Assert.False(context.NoResults);
        Assert.NotEmpty(context.Places.Concat(context.Events));
    }

    [Fact]
    public async Task Serbian_cafe_query_returns_food_places_and_no_beaches()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        await using var scope = app.Services.CreateAsyncScope();
        var tools = scope.ServiceProvider.GetRequiredService<IGeoTrackToolService>();
        var orchestrator = BuildOrchestrator(tools);

        var response = await orchestrator.ChatAsync(ChatRequest("koji kafici imaju u dubrovniku"), userId: 1);

        Assert.NotEmpty(response.Cards);
        Assert.All(response.Cards, card => Assert.Equal("place", card.EntityType));
        Assert.Contains(response.Cards, card => FoodPlaceTypes.Contains(card.PlaceType ?? string.Empty, StringComparer.OrdinalIgnoreCase));
        Assert.DoesNotContain(response.Cards, card => string.Equals(card.PlaceType, "Beach", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Agent_taxonomy_loads_food_beach_nearby_and_follow_up_concepts()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        await using var scope = app.Services.CreateAsyncScope();
        var taxonomyProvider = scope.ServiceProvider.GetRequiredService<IAgentTaxonomyProvider>();

        var taxonomy = await taxonomyProvider.GetTaxonomyAsync();

        Assert.False(string.IsNullOrWhiteSpace(taxonomy.Version));
        Assert.Contains(taxonomy.Concepts, concept => concept.Id == "FOOD_DRINK" && concept.Category == "restaurants");
        Assert.Contains(taxonomy.Concepts, concept => concept.Id == "BEACHES" && concept.Category == "beaches");
        Assert.Contains(taxonomy.Concepts, concept => concept.Id == "FISHING_WATER_ACTIVITY" && concept.Category == "fishing_water_activity");
        Assert.Contains(taxonomy.Concepts, concept => concept.Id == "NEARBY");
        Assert.Contains(taxonomy.Concepts, concept => concept.Id == "FOLLOW_UP_ACCEPTANCE");
    }

    [Fact]
    public async Task Agent_database_capability_service_detects_enabled_extensions_and_disabled_vector()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        await using var scope = app.Services.CreateAsyncScope();
        var capabilities = scope.ServiceProvider.GetRequiredService<IAgentDatabaseCapabilityService>();

        var detected = await capabilities.GetCapabilitiesAsync();

        Assert.True(detected.SupportsPostgis);
        Assert.True(detected.SupportsPgTrgm);
        Assert.True(detected.SupportsUnaccent);
        Assert.False(detected.SupportsVector);
    }

    [Fact]
    public async Task Serbian_restaurant_follow_up_reuses_Dubrovnik_and_does_not_show_beaches()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        await using var scope = app.Services.CreateAsyncScope();
        var tools = scope.ServiceProvider.GetRequiredService<IGeoTrackToolService>();
        var orchestrator = BuildOrchestrator(tools);

        var first = await orchestrator.ChatAsync(ChatRequest("sta ima u dubrovniku"), userId: 87);
        var followUp = await orchestrator.ChatAsync(
            new AgentChatRequestDto
            {
                Message = "a od restorana sta imaju",
                ConversationId = first.ConversationId
            },
            userId: 87);

        Assert.Equal(first.ConversationId, followUp.ConversationId);
        Assert.NotEmpty(followUp.Cards);
        Assert.All(followUp.Cards, card =>
        {
            Assert.Equal("place", card.EntityType);
            Assert.Contains("Dubrovnik", card.LocationName ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(FoodPlaceTypes, type => string.Equals(type, card.PlaceType, StringComparison.OrdinalIgnoreCase));
        });
        Assert.DoesNotContain(followUp.Cards, card => string.Equals(card.PlaceType, "Beach", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Ajde_after_no_result_executes_previous_broader_Dubrovnik_fallback()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        await using var scope = app.Services.CreateAsyncScope();
        var tools = scope.ServiceProvider.GetRequiredService<IGeoTrackToolService>();
        var orchestrator = BuildOrchestrator(tools);

        var first = await orchestrator.ChatAsync(ChatRequest("vulkanske opservatorije u Dubrovniku"), userId: 88);
        var followUp = await orchestrator.ChatAsync(
            new AgentChatRequestDto
            {
                Message = "ajde",
                ConversationId = first.ConversationId
            },
            userId: 88);

        Assert.Empty(first.Cards);
        Assert.Equal(first.ConversationId, followUp.ConversationId);
        Assert.NotEmpty(followUp.Cards);
        Assert.All(followUp.Cards, card =>
            Assert.Contains("Dubrovnik", card.LocationName ?? string.Empty, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Provider_failure_after_retrieval_returns_local_fallback_with_cards()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        await using var scope = app.Services.CreateAsyncScope();
        var tools = scope.ServiceProvider.GetRequiredService<IGeoTrackToolService>();
        var orchestrator = BuildOrchestrator(
            tools,
            gemini: new FailingGeminiAskAgentService("provider_bad_request"));

        var response = await orchestrator.ChatAsync(ChatRequest("najbolje plaze u dubrovniku"), userId: 89);

        Assert.NotEmpty(response.Cards);
        Assert.All(response.Cards, card =>
        {
            Assert.Equal("place", card.EntityType);
            Assert.Equal("Beach", card.PlaceType);
            Assert.Contains("Dubrovnik", card.LocationName ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        });
        Assert.Contains(response.Cards[0].Title, response.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Kartice", response.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("provider", response.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("FAILED_PRECONDITION", response.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("matching", response.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("food-drink", response.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Provider_failure_after_attractions_retrieval_returns_local_fallback_with_cards()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        await using var scope = app.Services.CreateAsyncScope();
        var tools = scope.ServiceProvider.GetRequiredService<IGeoTrackToolService>();
        var orchestrator = BuildOrchestrator(
            tools,
            gemini: new FailingGeminiAskAgentService("provider_bad_request"));

        var response = await orchestrator.ChatAsync(ChatRequest("najbolje atrakcije u dubrovniku"), userId: 94);

        Assert.NotEmpty(response.Cards);
        Assert.Contains(response.Cards[0].Title, response.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Kartice", response.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("provider", response.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("FAILED_PRECONDITION", response.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("matching", response.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("food-drink", response.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Provider_failure_after_empty_retrieval_returns_local_no_result_without_cards()
    {
        var context = new GeoTrackAgentContextDto
        {
            ToolsUsed = ["geotrack_search_places"],
            LocationUsed = "Testville",
            SearchAttempted = true,
            ResultCount = 0,
            NoResults = false,
            Plan = new GeoTrackAgentQueryPlanDto
            {
                Intent = "catalog_search",
                Language = "en",
                EntityTypes = ["places"],
                LocationUsed = "Testville",
                DestinationName = "Testville",
                Categories = ["observatory"]
            }
        };
        var orchestrator = BuildOrchestrator(
            new FakeToolService(context),
            gemini: new FailingGeminiAskAgentService("provider_config_error"));

        var response = await orchestrator.ChatAsync(ChatRequest("observatories in Testville"), userId: 90);

        Assert.Empty(response.Cards);
        Assert.Contains("did not find", response.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("provider", response.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("FAILED_PRECONDITION", response.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Pending_beach_clarification_then_Dubrovnik_returns_beach_cards()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        await using var scope = app.Services.CreateAsyncScope();
        var tools = scope.ServiceProvider.GetRequiredService<IGeoTrackToolService>();
        var orchestrator = BuildOrchestrator(tools);

        var first = await orchestrator.ChatAsync(ChatRequest("plaze"), userId: 91);
        var followUp = await orchestrator.ChatAsync(
            new AgentChatRequestDto
            {
                Message = "dubrovnik",
                ConversationId = first.ConversationId
            },
            userId: 91);

        Assert.Empty(first.Cards);
        Assert.Contains("grad", first.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(first.ConversationId, followUp.ConversationId);
        Assert.NotEmpty(followUp.Cards);
        Assert.All(followUp.Cards, card =>
        {
            Assert.Equal("place", card.EntityType);
            Assert.Equal("Beach", card.PlaceType);
            Assert.Contains("Dubrovnik", card.LocationName ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public async Task Pending_clarification_is_isolated_by_conversation_id()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        await using var scope = app.Services.CreateAsyncScope();
        var tools = scope.ServiceProvider.GetRequiredService<IGeoTrackToolService>();
        var orchestrator = BuildOrchestrator(tools);

        await orchestrator.ChatAsync(
            new AgentChatRequestDto
            {
                Message = "plaze",
                ConversationId = "pending-beaches-conversation"
            },
            userId: 92);
        var isolated = await orchestrator.ChatAsync(
            new AgentChatRequestDto
            {
                Message = "dubrovnik",
                ConversationId = "separate-conversation"
            },
            userId: 92);

        Assert.NotEmpty(isolated.Cards);
        Assert.Contains(isolated.Cards, card =>
            !string.Equals(card.PlaceType, "Beach", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Explicit_new_intent_clears_pending_clarification()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        await using var scope = app.Services.CreateAsyncScope();
        var tools = scope.ServiceProvider.GetRequiredService<IGeoTrackToolService>();
        var orchestrator = BuildOrchestrator(tools);

        var first = await orchestrator.ChatAsync(ChatRequest("plaze"), userId: 93);
        var followUp = await orchestrator.ChatAsync(
            new AgentChatRequestDto
            {
                Message = "restorani u Dubrovniku",
                ConversationId = first.ConversationId
            },
            userId: 93);

        Assert.NotEmpty(followUp.Cards);
        Assert.All(followUp.Cards, card =>
            Assert.Contains(FoodPlaceTypes, type => string.Equals(type, card.PlaceType, StringComparison.OrdinalIgnoreCase)));
        Assert.DoesNotContain(followUp.Cards, card => string.Equals(card.PlaceType, "Beach", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Serbian_no_result_response_does_not_expose_internal_matching_or_raw_tags()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        await using var scope = app.Services.CreateAsyncScope();
        var tools = scope.ServiceProvider.GetRequiredService<IGeoTrackToolService>();
        var orchestrator = BuildOrchestrator(tools);

        var response = await orchestrator.ChatAsync(ChatRequest("vulkanske opservatorije u Dubrovniku"), userId: 1);

        Assert.Empty(response.Cards);
        Assert.DoesNotContain("matching", response.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("dest-", response.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("food-drink", response.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Serbian_skiing_definition_is_general_knowledge_without_catalog_cards()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        await using var scope = app.Services.CreateAsyncScope();
        var tools = scope.ServiceProvider.GetRequiredService<IGeoTrackToolService>();
        var orchestrator = BuildOrchestrator(
            tools,
            gemini: new FailingGeminiAskAgentService("provider_bad_request"));

        var context = await tools.BuildAgentContextAsync(
            "sta je skijanje",
            new AgentContextOptionsDto { CurrentDestinationName = "Kopaonik" },
            5);
        var response = await orchestrator.ChatAsync(
            new AgentChatRequestDto { Message = "sta je skijanje" },
            userId: 95);

        Assert.Equal("GENERAL_KNOWLEDGE", context.Plan?.Intent);
        Assert.False(context.SearchAttempted);
        Assert.False(context.NoResults);
        Assert.Empty(response.Cards);
        Assert.Contains("Skijanje je zimski sport", response.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("GeoTrack rezultate", response.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Kopaonik", response.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Serbian_swimming_definition_is_general_knowledge_without_catalog_cards()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        await using var scope = app.Services.CreateAsyncScope();
        var tools = scope.ServiceProvider.GetRequiredService<IGeoTrackToolService>();
        var orchestrator = BuildOrchestrator(
            tools,
            gemini: new FailingGeminiAskAgentService("provider_config_error"));

        var context = await tools.BuildAgentContextAsync(
            "sta je plivanje",
            new AgentContextOptionsDto { CurrentDestinationName = "Dubrovnik" },
            5);
        var response = await orchestrator.ChatAsync(
            new AgentChatRequestDto
            {
                Message = "sta je plivanje",
                ContextOptions = new AgentContextOptionsDto { CurrentDestinationName = "Dubrovnik" }
            },
            userId: 97);

        Assert.Equal("GENERAL_KNOWLEDGE", context.Plan?.Intent);
        Assert.Equal("plivanje", context.Plan?.KnowledgeTopic);
        Assert.False(context.SearchAttempted);
        Assert.Empty(response.Cards);
        Assert.Contains("Plivanje je fizička aktivnost", response.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Dubrovnik", response.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("provider", response.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Unknown_general_knowledge_with_provider_unavailable_returns_clean_local_message()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        await using var scope = app.Services.CreateAsyncScope();
        var tools = scope.ServiceProvider.GetRequiredService<IGeoTrackToolService>();
        var orchestrator = BuildOrchestrator(
            tools,
            gemini: new FailingGeminiAskAgentService("provider_config_error"));

        var response = await orchestrator.ChatAsync(ChatRequest("sta je astroturizam"), userId: 98);

        Assert.Empty(response.Cards);
        Assert.Contains("Ne mogu trenutno pouzdano da objasnim taj pojam", response.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("GeoTrack katalog", response.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("provider", response.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("FAILED_PRECONDITION", response.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("nisam pronašao", response.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Serbian_swimming_recommendation_uses_beach_water_intent_and_returns_public_beach_cards()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        await using var scope = app.Services.CreateAsyncScope();
        var tools = scope.ServiceProvider.GetRequiredService<IGeoTrackToolService>();
        var orchestrator = BuildOrchestrator(
            tools,
            gemini: new FailingGeminiAskAgentService("provider_config_error"));

        var context = await tools.BuildAgentContextAsync("gde moze da ide da se kupa", null, 5);
        var response = await orchestrator.ChatAsync(ChatRequest("gde moze da ide da se kupa"), userId: 99);

        Assert.Equal("BEACH_WATER_RECOMMENDATION", context.Plan?.Intent);
        Assert.True(context.SearchAttempted);
        Assert.NotEmpty(response.Cards);
        Assert.All(response.Cards, card => Assert.Equal("place", card.EntityType));
        Assert.Contains(response.Cards, card => string.Equals(card.PlaceType, "Beach", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain("provider", response.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("FAILED_PRECONDITION", response.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Broad_beach_question_after_Tara_context_does_not_reuse_stale_non_coastal_area()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        await using var scope = app.Services.CreateAsyncScope();
        var tools = scope.ServiceProvider.GetRequiredService<IGeoTrackToolService>();

        var context = await tools.BuildAgentContextAsync(
            "gde imaju plaze",
            new AgentContextOptionsDto { CurrentDestinationName = "Mitrovac, Tara" },
            5);

        Assert.Equal("BEACH_WATER_RECOMMENDATION", context.Plan?.Intent);
        Assert.False(context.Plan?.NearbyIntent);
        Assert.False(context.Plan?.UsesUserLocation);
        Assert.False(context.Plan?.UsesCurrentContext);
        Assert.Null(context.Plan?.AreaName);
        Assert.True(context.SearchAttempted);
        Assert.NotEmpty(context.Places);
        Assert.Contains(context.Places, place => string.Equals(place.Category, "Beach", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(context.Places, place =>
            (place.Destination?.Contains("Tara", StringComparison.OrdinalIgnoreCase) ?? false) ||
            (place.Location?.Contains("Mitrovac", StringComparison.OrdinalIgnoreCase) ?? false));
    }

    [Fact]
    public async Task Broad_sea_recommendation_after_Dubrovnik_context_is_not_forced_to_previous_area()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        await using var scope = app.Services.CreateAsyncScope();
        var tools = scope.ServiceProvider.GetRequiredService<IGeoTrackToolService>();

        var context = await tools.BuildAgentContextAsync(
            "gde bi mi savetovao da idem na more",
            new AgentContextOptionsDto { CurrentDestinationName = "Dubrovnik" },
            5);

        Assert.Equal("BEACH_WATER_RECOMMENDATION", context.Plan?.Intent);
        Assert.False(context.Plan?.NearbyIntent);
        Assert.False(context.Plan?.UsesCurrentContext);
        Assert.Null(context.Plan?.AreaName);
        Assert.True(context.SearchAttempted);
        Assert.NotEmpty(context.Places);
        Assert.Contains(context.Places, place => string.Equals(place.Category, "Beach", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Broad_beach_question_with_coordinates_is_not_nearby_without_nearby_phrase()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        await using var scope = app.Services.CreateAsyncScope();
        var tools = scope.ServiceProvider.GetRequiredService<IGeoTrackToolService>();

        var context = await tools.BuildAgentContextAsync(
            "gde imaju plaze",
            new AgentContextOptionsDto
            {
                Latitude = 43.2856,
                Longitude = 20.8095,
                LocationSource = "gps"
            },
            5);

        Assert.Equal("BEACH_WATER_RECOMMENDATION", context.Plan?.Intent);
        Assert.False(context.Plan?.NearbyIntent);
        Assert.False(context.Plan?.UsesUserLocation);
        Assert.DoesNotContain("geotrack_find_nearby_public_content", context.ToolsUsed);
        Assert.Contains("geotrack_search_places", context.ToolsUsed);
        Assert.Contains(context.Places, place => string.Equals(place.Category, "Beach", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Plaze_u_Dubrovniku_keeps_explicit_area_and_beach_category()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        await using var scope = app.Services.CreateAsyncScope();
        var tools = scope.ServiceProvider.GetRequiredService<IGeoTrackToolService>();

        var context = await tools.BuildAgentContextAsync("plaze u dubrovniku", null, 5);

        Assert.Equal("Dubrovnik", context.Plan?.AreaName);
        Assert.Equal("Dubrovnik", context.Plan?.DestinationName);
        Assert.False(context.Plan?.NearbyIntent);
        Assert.True(context.SearchAttempted);
        Assert.Contains(context.Places, place =>
            string.Equals(place.Category, "Beach", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(place.Destination, "Dubrovnik", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Skiing_recommendation_does_not_reuse_previous_Dubrovnik_context_and_finds_Kopaonik()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        await using var scope = app.Services.CreateAsyncScope();
        var tools = scope.ServiceProvider.GetRequiredService<IGeoTrackToolService>();

        var context = await tools.BuildAgentContextAsync(
            "koju bi mi destinaciju preporucio ako volim da skijam",
            new AgentContextOptionsDto
            {
                ConversationAreaType = "destination",
                ConversationAreaName = "Dubrovnik",
                ConversationDestinationName = "Dubrovnik",
                CurrentDestinationName = "Dubrovnik"
            },
            5);

        var results = context.Places.Concat(context.Stays).Concat(context.Events).ToList();
        Assert.Equal("SKIING_WINTER_RECOMMENDATION", context.Plan?.Intent);
        Assert.False(context.Plan?.UsesCurrentContext);
        Assert.Null(context.Plan?.AreaName);
        Assert.False(context.NoResults);
        Assert.Contains(results, item =>
            item.Title.Contains("Kopaonik", StringComparison.OrdinalIgnoreCase) ||
            (item.Destination?.Contains("Kopaonik", StringComparison.OrdinalIgnoreCase) ?? false));
        Assert.DoesNotContain(results, item =>
            string.Equals(item.Destination, "Dubrovnik", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(results, item =>
            item.Title.Contains("Banjska Stena", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Serbian_skiing_recommendation_returns_ski_specific_content_before_generic_viewpoints()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        await using var scope = app.Services.CreateAsyncScope();
        var tools = scope.ServiceProvider.GetRequiredService<IGeoTrackToolService>();

        var context = await tools.BuildAgentContextAsync("gde mogu na skijanje da idem", null, 5);
        var results = context.Places.Concat(context.Stays).Concat(context.Events).ToList();

        Assert.Equal("SKIING_WINTER_RECOMMENDATION", context.Plan?.Intent);
        Assert.Equal("SKIING_WINTER", context.Plan?.CategoryGroup);
        Assert.True(context.Plan?.StrictCategoryMatch);
        Assert.False(context.Plan?.NearbyIntent);
        Assert.False(context.Plan?.UsesCurrentContext);
        Assert.False(context.NoResults);
        Assert.NotEmpty(context.Places);
        Assert.Contains("Kopaonik Ski Center", context.Places[0].Title, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(results, item =>
            item.Title.Contains("Kopaonik Ski Center", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(item.MatchQualification, GeoTrackMatchQualification.ExactCategoryMatch, StringComparison.OrdinalIgnoreCase));
        Assert.All(results, item =>
            Assert.Contains(item.MatchQualification, new[]
            {
                GeoTrackMatchQualification.ExactCategoryMatch,
                GeoTrackMatchQualification.RelatedCategoryMatch
            }));
        Assert.DoesNotContain(results, item => item.IsFallback);
        Assert.DoesNotContain(results, item =>
            item.Title.Contains("Banjska Stena", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(results, item =>
            item.Title.Contains("Knjazevsko", StringComparison.OrdinalIgnoreCase) ||
            item.Title.Contains("Knjaževsko", StringComparison.OrdinalIgnoreCase) ||
            item.Title.Contains("teatar", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(results, item =>
            item.Title.Contains("Novosadski", StringComparison.OrdinalIgnoreCase) ||
            item.Title.Contains("Noćni Bazar", StringComparison.OrdinalIgnoreCase) ||
            item.Title.Contains("Nocni Bazar", StringComparison.OrdinalIgnoreCase));

        var hotelGrand = results.FirstOrDefault(item =>
            item.Title.Contains("Hotel Grand Kopaonik", StringComparison.OrdinalIgnoreCase));
        if (hotelGrand is not null)
        {
            Assert.Equal(GeoTrackMatchQualification.RelatedCategoryMatch, hotelGrand.MatchQualification);
            Assert.True(
                (hotelGrand.Description?.Contains("ski", StringComparison.OrdinalIgnoreCase) ?? false) ||
                (hotelGrand.Location?.Contains("Ski Center", StringComparison.OrdinalIgnoreCase) ?? false));
        }
    }

    [Fact]
    public async Task Skiing_in_area_without_ski_content_returns_no_cards_until_fallback_accepted()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        await using var scope = app.Services.CreateAsyncScope();
        var tools = scope.ServiceProvider.GetRequiredService<IGeoTrackToolService>();
        var orchestrator = BuildOrchestrator(
            tools,
            gemini: new FailingGeminiAskAgentService("provider_config_error"));

        var first = await orchestrator.ChatAsync(ChatRequest("gde mogu na skijanje u Dubrovniku"), userId: 103);
        var followUp = await orchestrator.ChatAsync(
            new AgentChatRequestDto
            {
                Message = "ajde",
                ConversationId = first.ConversationId
            },
            userId: 103);

        Assert.Empty(first.Cards);
        Assert.Contains("potvrđene ski", first.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Knjazevsko", first.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Novosadski", first.Text, StringComparison.OrdinalIgnoreCase);
        Assert.NotEmpty(followUp.Cards);
        Assert.All(followUp.Cards, card =>
        {
            Assert.True(card.IsFallback);
            Assert.Equal("Broader alternative", card.MatchLabel);
            Assert.Contains("Dubrovnik", card.LocationName ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        });
        Assert.Contains("širu alternativu", followUp.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Provider_failure_for_strict_skiing_mentions_only_exact_primary_cards()
    {
        var context = new GeoTrackAgentContextDto
        {
            ToolsUsed = ["geotrack_search_places", "geotrack_search_stays", "geotrack_search_events"],
            SearchAttempted = true,
            ResultCount = 3,
            Plan = new GeoTrackAgentQueryPlanDto
            {
                Intent = "SKIING_WINTER_RECOMMENDATION",
                Language = "sr",
                CategoryGroup = "SKIING_WINTER",
                Category = "skiing_winter",
                StrictCategoryMatch = true,
                EntityTypes = ["places", "stays", "events"]
            },
            Places =
            [
                new GeoTrackToolItemDto
                {
                    Id = 501,
                    EntityType = "place",
                    Title = "Kopaonik Ski Center",
                    Description = "Main ski and winter activity area.",
                    Location = "Kopaonik Ski Center",
                    Destination = "Kopaonik",
                    Category = "Nature",
                    DetailRoute = "/places/501",
                    MatchQualification = GeoTrackMatchQualification.ExactCategoryMatch,
                    CategoryScore = 140d,
                    Tags = ["Skiing"]
                }
            ],
            Stays =
            [
                new GeoTrackToolItemDto
                {
                    Id = 502,
                    EntityType = "stay",
                    Title = "Generic Mountain Hotel",
                    Description = "Mountain stay without ski evidence.",
                    Location = "Mountain Center",
                    Destination = "Test Mountain",
                    Category = "Hotel",
                    DetailRoute = "/stays/502",
                    MatchQualification = GeoTrackMatchQualification.FallbackMatch,
                    IsFallback = true,
                    Tags = ["Nature"]
                }
            ],
            Events =
            [
                new GeoTrackToolItemDto
                {
                    Id = 503,
                    EntityType = "event",
                    Title = "Novosadski Noćni Bazar",
                    Description = "Evening bazaar and market.",
                    Location = "Novi Sad",
                    Destination = "Novi Sad",
                    Category = "Festival",
                    DetailRoute = "/events/503",
                    MatchQualification = GeoTrackMatchQualification.Rejected,
                    Tags = ["Culture"]
                }
            ]
        };
        var orchestrator = BuildOrchestrator(
            new FakeToolService(context),
            gemini: new FailingGeminiAskAgentService("provider_config_error"));

        var response = await orchestrator.ChatAsync(ChatRequest("gde mogu na skijanje da idem"), userId: 104);

        var card = Assert.Single(response.Cards);
        Assert.Equal("Kopaonik Ski Center", card.Title);
        Assert.False(card.IsFallback);
        Assert.Contains("skijanje", response.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Kopaonik Ski Center", response.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Generic Mountain Hotel", response.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Novosadski", response.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ExactCategoryMatch", response.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("FallbackMatch", response.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SKIING_WINTER", response.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Fishing_query_detects_water_activity_and_returns_public_fishing_content_when_available()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tools = scope.ServiceProvider.GetRequiredService<IGeoTrackToolService>();
        await EnsureFishingPlaceAsync(db);

        var context = await tools.BuildAgentContextAsync("gde mogu na pecanje", null, 5);
        var results = context.Places.Concat(context.Events).ToList();

        Assert.Equal("FISHING_WATER_ACTIVITY", context.Plan?.Intent);
        Assert.True(context.SearchAttempted);
        Assert.False(context.NoResults);
        Assert.Contains(results, item =>
            item.Title.Contains("Fishing", StringComparison.OrdinalIgnoreCase) ||
            item.Tags.Any(tag => tag.Contains("Fishing", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public async Task Fishing_on_these_destinations_uses_previous_result_destinations_when_provider_fails()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tools = scope.ServiceProvider.GetRequiredService<IGeoTrackToolService>();
        await EnsureFishingPlaceAsync(db);
        var orchestrator = BuildOrchestrator(
            tools,
            gemini: new FailingGeminiAskAgentService("provider_config_error"));

        var first = await orchestrator.ChatAsync(ChatRequest("gde mogu na more da idem"), userId: 101);
        var followUp = await orchestrator.ChatAsync(
            new AgentChatRequestDto
            {
                Message = "da li mogu negde na pecanje na ovim destinacijama da odem",
                ConversationId = first.ConversationId
            },
            userId: 101);

        Assert.Equal(first.ConversationId, followUp.ConversationId);
        Assert.NotEmpty(followUp.Cards);
        Assert.Contains(followUp.Cards, card =>
            card.Title.Contains("Fishing", StringComparison.OrdinalIgnoreCase) ||
            (card.Tags?.Any(tag => tag.Contains("Fishing", StringComparison.OrdinalIgnoreCase)) ?? false));
        Assert.DoesNotContain("provider", followUp.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("FAILED_PRECONDITION", followUp.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Fishing_on_context_destinations_without_exact_data_returns_clean_no_result()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tools = scope.ServiceProvider.GetRequiredService<IGeoTrackToolService>();
        var orchestrator = BuildOrchestrator(
            tools,
            gemini: new FailingGeminiAskAgentService("provider_config_error"));

        var destinations = await db.Destinations
            .AsNoTracking()
            .Where(destination => destination.Name == "Kopaonik" || destination.Name == "Tara")
            .Select(destination => new { destination.Id, destination.Name })
            .ToListAsync();

        var response = await orchestrator.ChatAsync(
            new AgentChatRequestDto
            {
                Message = "da li mogu negde na pecanje na ovim destinacijama da odem",
                ContextOptions = new AgentContextOptionsDto
                {
                    ConversationDestinationIds = destinations.Select(destination => destination.Id).ToList(),
                    ConversationDestinationNames = destinations.Select(destination => destination.Name).ToList()
                }
            },
            userId: 102);

        Assert.Empty(response.Cards);
        Assert.Contains("Nemam potvrđene GeoTrack informacije o pecanju za te destinacije", response.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("provider", response.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("FAILED_PRECONDITION", response.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Nearby_query_with_coordinates_uses_nearby_public_tool()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        await using var scope = app.Services.CreateAsyncScope();
        var tools = scope.ServiceProvider.GetRequiredService<IGeoTrackToolService>();

        var context = await tools.BuildAgentContextAsync(
            "sta ima u mojoj blizini",
            new AgentContextOptionsDto
            {
                Latitude = 42.6413,
                Longitude = 18.1089,
                LocationSource = "gps"
            },
            5);

        Assert.True(context.Plan?.UsesUserLocation);
        Assert.Contains("geotrack_find_nearby_public_content", context.ToolsUsed);
        Assert.True(context.SearchAttempted);
        Assert.False(context.NoResults);
        Assert.NotEmpty(context.Places.Concat(context.Stays).Concat(context.Events));
        Assert.Contains(context.Places.Concat(context.Stays).Concat(context.Events), item => item.DistanceKm.HasValue);
    }

    [Fact]
    public async Task Nearby_query_expands_radius_and_returns_Kopaonik_when_about_40km_away()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        await using var scope = app.Services.CreateAsyncScope();
        var tools = scope.ServiceProvider.GetRequiredService<IGeoTrackToolService>();

        var context = await tools.BuildAgentContextAsync(
            "sta ima u mojoj blizini",
            new AgentContextOptionsDto
            {
                Latitude = 43.6456,
                Longitude = 20.8095,
                LocationSource = "gps"
            },
            5);

        var results = context.Places.Concat(context.Stays).Concat(context.Events).ToList();
        Assert.Equal("NEARBY_DISCOVERY", context.Plan?.Intent);
        Assert.Contains(context.Searches, search => search.RadiusKm == 10 && search.ResultCount == 0);
        Assert.Contains(context.Searches, search => search.RadiusKm == 25 && search.ResultCount == 0);
        Assert.Contains(context.Searches, search => search.RadiusKm == 50 && search.ResultCount > 0);
        Assert.False(context.NoResults);
        Assert.Contains(results, item =>
            item.DistanceKm is >= 35 and <= 50 &&
            (item.Title.Contains("Kopaonik", StringComparison.OrdinalIgnoreCase) ||
             (item.Destination?.Contains("Kopaonik", StringComparison.OrdinalIgnoreCase) ?? false)));
    }

    [Fact]
    public async Task Nearby_visitable_query_expands_radius_and_returns_Kopaonik_public_content()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        await using var scope = app.Services.CreateAsyncScope();
        var tools = scope.ServiceProvider.GetRequiredService<IGeoTrackToolService>();

        var context = await tools.BuildAgentContextAsync(
            "sta ima u mojoj blizini da se poseti",
            new AgentContextOptionsDto
            {
                Latitude = 43.6456,
                Longitude = 20.8095,
                LocationSource = "gps"
            },
            5);

        var results = context.Places.Concat(context.Stays).Concat(context.Events).ToList();
        Assert.Equal("NEARBY_DISCOVERY", context.Plan?.Intent);
        Assert.False(context.NoResults);
        Assert.Contains(results, item =>
            item.Title.Contains("Kopaonik", StringComparison.OrdinalIgnoreCase) ||
            (item.Destination?.Contains("Kopaonik", StringComparison.OrdinalIgnoreCase) ?? false));
    }

    [Fact]
    public async Task Strict_nearby_no_result_uses_display_labels_not_raw_internal_categories()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        await using var scope = app.Services.CreateAsyncScope();
        var tools = scope.ServiceProvider.GetRequiredService<IGeoTrackToolService>();
        var orchestrator = BuildOrchestrator(tools);

        var response = await orchestrator.ChatAsync(
            new AgentChatRequestDto
            {
                Message = "plaze u mojoj blizini",
                ContextOptions = new AgentContextOptionsDto
                {
                    Latitude = 43.2856,
                    Longitude = 20.8095,
                    LocationSource = "gps"
                }
            },
            userId: 96);

        Assert.Empty(response.Cards);
        Assert.Contains("plaže", response.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("activity", response.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Bar, Beach, Cafe", response.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("food-drink", response.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("matching", response.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Nearby_query_without_coordinates_asks_for_location_or_destination()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        await using var scope = app.Services.CreateAsyncScope();
        var tools = scope.ServiceProvider.GetRequiredService<IGeoTrackToolService>();

        var context = await tools.BuildAgentContextAsync("sta ima u mojoj blizini", null, 5);

        Assert.True(context.ClarificationNeeded);
        Assert.False(context.SearchAttempted);
        Assert.Contains("lokaciju", context.ClarificationQuestion ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Follow_up_beach_query_reuses_previous_Dubrovnik_context()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        await using var scope = app.Services.CreateAsyncScope();
        var tools = scope.ServiceProvider.GetRequiredService<IGeoTrackToolService>();
        var orchestrator = BuildOrchestrator(tools);

        var first = await orchestrator.ChatAsync(ChatRequest("koji su dobri restorani u Dubrovniku"), userId: 77);
        var followUp = await orchestrator.ChatAsync(
            new AgentChatRequestDto
            {
                Message = "a plaže",
                ConversationId = first.ConversationId
            },
            userId: 77);

        Assert.Equal(first.ConversationId, followUp.ConversationId);
        Assert.NotEmpty(followUp.Cards);
        Assert.All(followUp.Cards, card =>
        {
            Assert.Equal("place", card.EntityType);
            Assert.Equal("Beach", card.PlaceType);
            Assert.Contains("Dubrovnik", card.LocationName ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public async Task Follow_up_beach_query_without_conversation_context_asks_for_area()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        await using var scope = app.Services.CreateAsyncScope();
        var tools = scope.ServiceProvider.GetRequiredService<IGeoTrackToolService>();

        var context = await tools.BuildAgentContextAsync("a plaže", null, 5);

        Assert.True(context.ClarificationNeeded);
        Assert.False(context.SearchAttempted);
    }

    [Fact]
    public async Task Explicit_new_area_overrides_current_context()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        await using var scope = app.Services.CreateAsyncScope();
        var tools = scope.ServiceProvider.GetRequiredService<IGeoTrackToolService>();

        var context = await tools.BuildAgentContextAsync(
            "šta ima u Kragujevcu",
            new AgentContextOptionsDto { CurrentDestinationName = "Dubrovnik" },
            5);

        Assert.False(context.ClarificationNeeded);
        Assert.Equal("Kragujevac", context.Plan?.AreaName);
        Assert.Equal("Kragujevac", context.Plan?.DestinationName);
        Assert.NotEmpty(context.Places.Concat(context.Stays).Concat(context.Events));
    }

    [Fact]
    public async Task Serbian_general_queries_for_Kragujevac_and_Kopaonik_return_public_content()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        await using var scope = app.Services.CreateAsyncScope();
        var tools = scope.ServiceProvider.GetRequiredService<IGeoTrackToolService>();

        var kragujevac = await tools.BuildAgentContextAsync("šta ima u Kragujevcu", null, 5);
        var kopaonik = await tools.BuildAgentContextAsync("šta ima na Kopaoniku", null, 5);

        Assert.Equal("Kragujevac", kragujevac.Plan?.AreaName);
        Assert.Null(kragujevac.Plan?.QueryText);
        Assert.False(kragujevac.NoResults);
        Assert.NotEmpty(kragujevac.Places.Concat(kragujevac.Stays).Concat(kragujevac.Events));

        Assert.Equal("Kopaonik", kopaonik.Plan?.AreaName);
        Assert.Null(kopaonik.Plan?.QueryText);
        Assert.False(kopaonik.NoResults);
        Assert.NotEmpty(kopaonik.Places.Concat(kopaonik.Stays).Concat(kopaonik.Events));
    }

    [Fact]
    public async Task Area_resolver_handles_Serbian_locative_suffixes()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        await using var scope = app.Services.CreateAsyncScope();
        var tools = scope.ServiceProvider.GetRequiredService<IGeoTrackToolService>();

        var dubrovnik = await tools.ResolveAreaAsync(new GeoTrackAreaResolveRequest { Query = "Dubrovniku" });
        var kragujevac = await tools.ResolveAreaAsync(new GeoTrackAreaResolveRequest { Query = "Kragujevcu" });
        var kopaonik = await tools.ResolveAreaAsync(new GeoTrackAreaResolveRequest { Query = "Kopaoniku" });

        Assert.Contains(dubrovnik.Matches, match => match.Type == "destination" && match.Name == "Dubrovnik");
        Assert.Contains(kragujevac.Matches, match => match.Type == "destination" && match.Name == "Kragujevac");
        Assert.Contains(kopaonik.Matches, match => match.Type == "destination" && match.Name == "Kopaonik");
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
        var gemini = new FakeGeminiAskAgentService();
        var orchestrator = new AgentOrchestrator(
            gemini,
            new FakeToolService(),
            Options.Create(new AgentOptions { MaxPromptLength = 200, MaxContextItems = 5, RequestTimeoutSeconds = 10 }),
            Options.Create(new McpOptions { Enabled = true }),
            NullLogger<AgentOrchestrator>.Instance);

        var response = await orchestrator.ChatAsync(
            new AgentChatRequestDto
            {
                Message = "beaches in Testville"
            },
            userId: 1);

        Assert.NotNull(gemini.LastRequest);
        Assert.Contains("GeoTrack context JSON", gemini.LastRequest!.SystemPrompt);
        Assert.Contains("Fixture Beach", gemini.LastRequest.SystemPrompt);
        Assert.Contains("searchAttempted", gemini.LastRequest.SystemPrompt);
        Assert.Contains("geotrack_search_places", gemini.LastRequest.SystemPrompt);
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

        var response = await orchestrator.ChatAsync(ChatRequest("places in Testville"), userId: 1);

        Assert.True(response.Cards.Count <= 5);
        Assert.All(response.Cards, card =>
        {
            Assert.True(card.Id > 0);
            Assert.StartsWith("/places/", card.DetailRoute);
        });

        var cardsJson = JsonSerializer.Serialize(response.Cards);
        Assert.DoesNotContain("gemini-test", cardsJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Overlong_chat_prompt_is_rejected_safely()
    {
        var orchestrator = new AgentOrchestrator(
            new FakeGeminiAskAgentService(),
            new FakeToolService(),
            Options.Create(new AgentOptions { MaxPromptLength = 20, MaxContextItems = 5, RequestTimeoutSeconds = 10 }),
            Options.Create(new McpOptions()),
            NullLogger<AgentOrchestrator>.Instance);

        var ex = await Assert.ThrowsAsync<Geotrack.Application.Services.AgentException>(() =>
            orchestrator.ChatAsync(
                new AgentChatRequestDto
                {
                    Message = new string('x', 250)
                },
                userId: 1));

        Assert.Equal("prompt_too_long", ex.Code);
    }

    [Fact]
    public async Task Ask_agent_logs_metadata_without_full_prompt_or_pii()
    {
        var logger = new RecordingLogger<AgentOrchestrator>();
        var prompt = "Find places near secret@example.com and phone +381000000.";
        var orchestrator = new AgentOrchestrator(
            new FakeGeminiAskAgentService(),
            new FakeToolService(),
            Options.Create(new AgentOptions { MaxPromptLength = 4000, MaxContextItems = 5, RequestTimeoutSeconds = 10 }),
            Options.Create(new McpOptions()),
            logger);

        await orchestrator.ChatAsync(
            new AgentChatRequestDto
            {
                Message = prompt
            },
            userId: 42);

        var logText = string.Join('\n', logger.Messages);
        Assert.Contains("prompt length", logText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(prompt, logText, StringComparison.Ordinal);
        Assert.DoesNotContain("secret@example.com", logText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("+381000000", logText, StringComparison.OrdinalIgnoreCase);
    }

    private static AgentChatRequestDto ChatRequest(string message) => new()
    {
        Message = message
    };

    private static AgentOrchestrator BuildOrchestrator(
        IGeoTrackToolService tools,
        int maxToolResults = 10,
        int maxContextItems = 6,
        IGeminiAskAgentService? gemini = null,
        ILogger<AgentOrchestrator>? logger = null)
    {
        return new AgentOrchestrator(
            gemini ?? new FakeGeminiAskAgentService(),
            tools,
            Options.Create(new AgentOptions
            {
                MaxPromptLength = 4000,
                MaxContextItems = maxContextItems,
                MaxToolResults = maxToolResults,
                RequestTimeoutSeconds = 10
            }),
            Options.Create(new McpOptions { Enabled = true }),
            logger ?? NullLogger<AgentOrchestrator>.Instance);
    }

    private static List<string> OrderedImageUrls(IEnumerable<EntityImage> images) =>
        images
            .OrderByDescending(image => image.IsMain)
            .ThenBy(image => image.SortOrder)
            .ThenBy(image => image.CreatedAt)
            .ThenBy(image => image.Id)
            .Select(image => image.Url)
            .ToList();

    private static async Task EnsureFishingPlaceAsync(AppDbContext db)
    {
        const string fixtureName = "Ask Agent Fishing Pier Fixture";

        if (await db.Places.AnyAsync(place => place.Name == fixtureName))
        {
            return;
        }

        var now = DateTime.UtcNow;
        var location = await db.Locations
            .Include(location => location.Destination)
            .Where(location => location.Status == "Published" && location.Destination.Status == "Published")
            .OrderBy(location => location.Destination.Name == "Dubrovnik" ? 0 : 1)
            .ThenBy(location => location.Id)
            .FirstAsync();
        var placeType = await db.PlaceTypes.FirstOrDefaultAsync(type => type.Name == "Landmark")
            ?? await db.PlaceTypes.OrderBy(type => type.Id).FirstAsync();
        var fishingTag = await db.Tags.FirstOrDefaultAsync(tag => tag.Slug == "fishing");
        if (fishingTag is null)
        {
            fishingTag = new Tag
            {
                Name = "Fishing",
                Slug = "fishing",
                Description = "Fishing activity",
                ForPlace = true,
                ForEvent = true,
                CanGenerateExploreSection = true,
                CreatedAt = now
            };
        }

        var place = new Place
        {
            Name = fixtureName,
            SearchText = "ask agent fishing pier fixture fishing pecanje ribolov",
            Status = "Published",
            Subtitle = "Fishing and ribolov test fixture",
            Description = "A quiet fishing pier fixture for pecanje and ribolov recommendations.",
            PlaceTypeId = placeType.Id,
            LocationId = location.Id,
            Latitude = location.Latitude,
            Longitude = location.Longitude,
            AverageRating = 4.8,
            ReviewCount = 3,
            CreatedAt = now,
            PublishedAt = now
        };

        place.PlaceTags.Add(new PlaceTag { Place = place, Tag = fishingTag });
        db.Places.Add(place);
        await db.SaveChangesAsync();
    }

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
        public Task<GeoTrackAreaResolveResponseDto> ResolveAreaAsync(GeoTrackAreaResolveRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new GeoTrackAreaResolveResponseDto());
        public Task<GeoTrackCatalogSearchResponseDto> SearchCatalogAsync(GeoTrackCatalogSearchRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new GeoTrackCatalogSearchResponseDto());
        public Task<GeoTrackAreaContentResponseDto> SearchByAreaAsync(GeoTrackAreaContentRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new GeoTrackAreaContentResponseDto());
        public Task<GeoTrackAreaOverviewDto> GetAreaOverviewAsync(GeoTrackAreaOverviewRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new GeoTrackAreaOverviewDto());
        public Task<GeoTrackCategoryOptionsResponseDto> GetCategoryOptionsAsync(GeoTrackCategoryOptionsRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new GeoTrackCategoryOptionsResponseDto());
        public Task<GeoTrackNearbySearchResponseDto> FindNearbyPublicContentAsync(GeoTrackNearbySearchRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new GeoTrackNearbySearchResponseDto());
    }

    private sealed class FakeGeminiAskAgentService : IGeminiAskAgentService
    {
        public GeminiTextGenerationRequest? LastRequest { get; private set; }
        public string ProviderId => "gemini";
        public string Model => "gemini-test";
        public bool HasAvailableKey => true;

        public Task<GeminiTextGenerationResult> GenerateAsync(
            GeminiTextGenerationRequest request,
            CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult(new GeminiTextGenerationResult { Text = "Grounded answer" });
        }
    }

    private sealed class FailingGeminiAskAgentService : IGeminiAskAgentService
    {
        private readonly string _code;

        public FailingGeminiAskAgentService(string code)
        {
            _code = code;
        }

        public string ProviderId => "gemini";
        public string Model => "gemini-test";
        public bool HasAvailableKey => true;

        public Task<GeminiTextGenerationResult> GenerateAsync(
            GeminiTextGenerationRequest request,
            CancellationToken cancellationToken = default)
        {
            throw new AgentException(_code, "Provider failed safely.", 400);
        }
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = new();

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull =>
            NullLogger.Instance.BeginScope(state);

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
        }
    }
}
