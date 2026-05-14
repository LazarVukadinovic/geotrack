using System.Text.Json;
using Geotrack.Application.DTOs.Destinations;
using Geotrack.Application.DTOs.Explore;
using Geotrack.Application.DTOs.Locations;
using Geotrack.Application.DTOs.Territories;
using Geotrack.Application.Interfaces.Service;
using Geotrack.Server.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Geotrack.IntegrationTests;

public sealed class SafeApiErrorResponseTests
{
    private const string GenericErrorMessage = "An unexpected error occurred.";

    [Fact]
    public async Task Destination_unexpected_exception_returns_generic_500_and_logs_details()
    {
        var exception = new ApplicationException("Npgsql provider failure at D:\\internal\\query.sql");
        var logger = new RecordingLogger<DestinationController>();
        var controller = new DestinationController(
            new ThrowingDestinationService(exception),
            new StubExploreService(),
            logger);
        ConfigureRequest(controller, HttpMethods.Get, "/api/destinations/42");

        var actionResult = await controller.GetDestinationDetails(42);

        var result = Assert.IsType<ObjectResult>(actionResult);
        Assert.Equal(StatusCodes.Status500InternalServerError, result.StatusCode);
        var responseJson = AssertMessage(result, GenericErrorMessage);
        Assert.DoesNotContain("Npgsql", responseJson);
        Assert.DoesNotContain("internal", responseJson);
        var logEntry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Error, logEntry.Level);
        Assert.Same(exception, logEntry.Exception);
    }

    [Fact]
    public async Task Location_unexpected_exception_returns_generic_500_and_logs_details()
    {
        var exception = new ApplicationException("ipwho.is client failure: upstream token abc123");
        var logger = new RecordingLogger<LocationController>();
        var controller = new LocationController(
            new ThrowingLocationService(exception),
            new StubClientLocationResolverService(),
            logger);
        ConfigureRequest(controller, HttpMethods.Get, "/api/locations/7");

        var actionResult = await controller.GetLocationDetails(7);

        var result = Assert.IsType<ObjectResult>(actionResult);
        Assert.Equal(StatusCodes.Status500InternalServerError, result.StatusCode);
        var responseJson = AssertMessage(result, GenericErrorMessage);
        Assert.DoesNotContain("ipwho.is", responseJson);
        Assert.DoesNotContain("abc123", responseJson);
        var logEntry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Error, logEntry.Level);
        Assert.Same(exception, logEntry.Exception);
    }

    [Fact]
    public async Task Territory_unexpected_exception_returns_generic_500_and_logs_details()
    {
        var exception = new ApplicationException("Npgsql territory query failed at D:\\internal\\territories.sql");
        var logger = new RecordingLogger<TerritoryController>();
        var controller = new TerritoryController(new ThrowingTerritoryService(exception), logger);
        ConfigureRequest(controller, HttpMethods.Get, "/api/Territory/4");

        var actionResult = await controller.GetTerritoryDetails(4);

        var result = Assert.IsType<ObjectResult>(actionResult);
        Assert.Equal(StatusCodes.Status500InternalServerError, result.StatusCode);
        var responseJson = AssertMessage(result, GenericErrorMessage);
        Assert.DoesNotContain("Npgsql", responseJson);
        Assert.DoesNotContain("internal", responseJson);
        Assert.DoesNotContain("territories.sql", responseJson);
        var logEntry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Error, logEntry.Level);
        Assert.Same(exception, logEntry.Exception);
    }

    [Fact]
    public async Task Destination_expected_not_found_message_is_preserved()
    {
        var logger = new RecordingLogger<DestinationController>();
        var controller = new DestinationController(
            new ThrowingDestinationService(new KeyNotFoundException("Destination not found.")),
            new StubExploreService(),
            logger);
        ConfigureRequest(controller, HttpMethods.Get, "/api/destinations/999");

        var actionResult = await controller.GetDestinationDetails(999);

        var result = Assert.IsType<NotFoundObjectResult>(actionResult);
        AssertMessage(result, "Destination not found.");
        Assert.Empty(logger.Entries);
    }

    [Fact]
    public async Task Location_expected_validation_message_is_preserved()
    {
        var logger = new RecordingLogger<LocationController>();
        var controller = new LocationController(
            new ThrowingLocationService(new InvalidOperationException("Rating must be between 1 and 5.")),
            new StubClientLocationResolverService(),
            logger);
        ConfigureRequest(controller, HttpMethods.Get, "/api/locations/7/reviews");

        var actionResult = await controller.GetLocationReviews(7);

        var result = Assert.IsType<BadRequestObjectResult>(actionResult);
        AssertMessage(result, "Rating must be between 1 and 5.");
        Assert.Empty(logger.Entries);
    }

    [Fact]
    public async Task Territory_expected_not_found_message_is_preserved()
    {
        var logger = new RecordingLogger<TerritoryController>();
        var controller = new TerritoryController(
            new ThrowingTerritoryService(new KeyNotFoundException("Territory with id '999' was not found.")),
            logger);
        ConfigureRequest(controller, HttpMethods.Get, "/api/Territory/999");

        var actionResult = await controller.GetTerritoryDetails(999);

        var result = Assert.IsType<NotFoundObjectResult>(actionResult);
        AssertMessage(result, "Territory with id '999' was not found.");
        Assert.Empty(logger.Entries);
    }

    [Fact]
    public async Task Territory_expected_validation_message_is_preserved()
    {
        var logger = new RecordingLogger<TerritoryController>();
        var controller = new TerritoryController(
            new ThrowingTerritoryService(new InvalidOperationException("Territory name cannot be empty.")),
            logger);
        ConfigureRequest(controller, HttpMethods.Post, "/api/Territory");

        var actionResult = await controller.CreateTerritory(new CreateTerritoryRequestDto());

        var result = Assert.IsType<BadRequestObjectResult>(actionResult);
        AssertMessage(result, "Territory name cannot be empty.");
        Assert.Empty(logger.Entries);
    }

    private static void ConfigureRequest(ControllerBase controller, string method, string path)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Method = method;
        httpContext.Request.Path = path;
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContext
        };
    }

    private static string AssertMessage(ObjectResult result, string expectedMessage)
    {
        var responseJson = JsonSerializer.Serialize(result.Value);
        using var document = JsonDocument.Parse(responseJson);
        Assert.Equal(expectedMessage, document.RootElement.GetProperty("message").GetString());
        return responseJson;
    }

    private sealed class ThrowingDestinationService : IDestinationService
    {
        private readonly Exception _exception;

        public ThrowingDestinationService(Exception exception)
        {
            _exception = exception;
        }

        public Task<GetDestinationDetailsResponseDto> GetDestinationDetailsAsync(int destinationId, int? userId) => throw _exception;
        public Task<GetDestinationSectionResponseDto> GetDestinationSectionAsync(int destinationId, string sectionKey) => throw _exception;
        public Task<GetDestinationReviewsResponseDto> GetDestinationReviewsAsync(int destinationId, int skip = 0, int take = 10) => throw _exception;
        public Task<CreateDestinationReviewResponseDto> CreateDestinationReviewAsync(int destinationId, int userId, CreateDestinationReviewRequestDto request) => throw _exception;
        public Task<List<DestinationSearchResultDto>> SearchAsync(string query) => throw _exception;
        public Task<List<DestinationSearchItemDto>> SearchWithinDestinationAsync(int destinationId, string query, IEnumerable<string>? types) => throw _exception;
    }

    private sealed class ThrowingLocationService : ILocationService
    {
        private readonly Exception _exception;

        public ThrowingLocationService(Exception exception)
        {
            _exception = exception;
        }

        public Task<GetLocationDetailsResponseDto> GetLocationDetailsAsync(int locationId, int? userId) => throw _exception;
        public Task<GetLocationSectionResponseDto> GetLocationSectionAsync(int locationId, string sectionKey) => throw _exception;
        public Task<GetLocationReviewsResponseDto> GetLocationReviewsAsync(int locationId, int skip = 0, int take = 10) => throw _exception;
        public Task<CreateLocationReviewResponseDto> CreateLocationReviewAsync(int locationId, int userId, CreateLocationReviewRequestDto request) => throw _exception;
    }

    private sealed class StubExploreService : IExploreService
    {
        public Task<ExplorePageDto> GetExplorePageAsync(
            double latitude,
            double longitude,
            int? userId = null) =>
            Task.FromResult(new ExplorePageDto());

        public Task<ExplorePageDto> GetExplorePageForDestinationAsync(
            int destinationId,
            int? userId = null) =>
            Task.FromResult(new ExplorePageDto());

        public Task<ExploreSectionPageDto> GetExploreSectionPageAsync(
            string sectionKey,
            int destinationId,
            int? userId = null,
            int skip = 0,
            int take = 20) =>
            Task.FromResult(new ExploreSectionPageDto
            {
                IsKnownSection = true,
                SelectedDestination = new ExploreDestinationDto { Id = destinationId },
                Key = sectionKey,
                Title = sectionKey
            });
    }

    private sealed class ThrowingTerritoryService : ITerritoryService
    {
        private readonly Exception _exception;

        public ThrowingTerritoryService(Exception exception)
        {
            _exception = exception;
        }

        public Task<GetTerritoriesResponseDto> GetTerritoriesAsync() => throw _exception;
        public Task<GetTerritoryDetailsResponseDto> GetTerritoryDetailsAsync(int territoryId) => throw _exception;
        public Task<CreateTerritoryResponseDto> CreateTerritoryAsync(CreateTerritoryRequestDto request) => throw _exception;
        public Task DeleteTerritoryAsync(int territoryId) => throw _exception;
        public Task<CreateRegionInTerritoryResponseDto> CreateRegionAsync(int territoryId, CreateRegionInTerritoryRequestDto request) => throw _exception;
        public Task DeleteRegionAsync(int territoryId, int regionId) => throw _exception;
    }

    private sealed class StubClientLocationResolverService : IClientLocationResolverService
    {
        public Task<ApproximateLocationDto?> ResolveApproximateLocationAsync(
            string? clientIpAddress,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<ApproximateLocationDto?>(null);
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull =>
            NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add(new LogEntry(logLevel, exception, formatter(state, exception)));
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose()
            {
            }
        }
    }

    private sealed record LogEntry(LogLevel Level, Exception? Exception, string Message);
}


