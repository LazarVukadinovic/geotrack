using System.Net;
using System.Reflection;
using System.Security.Claims;
using Geotrack.Application.DTOs.Contributions;
using Geotrack.Application.Interfaces.Service;
using Geotrack.Application.Options;
using Geotrack.Server.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Xunit;

namespace Geotrack.IntegrationTests;

public sealed class UploadRateLimitHardeningTests
{
    [Fact]
    public async Task Contribution_image_upload_rejects_file_larger_than_configured_max_before_service_call()
    {
        var service = new RecordingContributionService();
        var controller = CreateContributionsController(service);
        var bytes = BytesWithPrefix((int)UploadLimitDefaults.ImageMaxFileSizeBytes + 1, TestPngBytes());

        var result = await controller.CreatePlaceContribution(
            "{}",
            CreateFile(bytes, "card.png", "image/png"),
            null,
            null,
            null);

        AssertObjectResult(result.Result, (HttpStatusCode)413, "Contribution image must be 8 MB or smaller.");
        Assert.Equal(0, service.CreateCalls);
    }

    [Fact]
    public async Task Contribution_image_upload_rejects_too_many_files_before_service_call()
    {
        var service = new RecordingContributionService();
        var controller = CreateContributionsController(service);
        var gallery = Enumerable
            .Range(0, UploadLimitDefaults.ContributionMaxImageFilesPerRequest + 1)
            .Select(index => CreateFile(TestPngBytes(), $"gallery-{index}.png", "image/png"))
            .ToList();

        var result = await controller.CreatePlaceContribution(
            "{}",
            null,
            null,
            gallery,
            null);

        AssertObjectResult(result.Result, HttpStatusCode.BadRequest, "A maximum of 10 contribution images can be uploaded at once.");
        Assert.Equal(0, service.CreateCalls);
    }

    [Fact]
    public async Task Contribution_image_upload_rejects_unsupported_content_type_before_service_call()
    {
        var service = new RecordingContributionService();
        var controller = CreateContributionsController(service);

        var result = await controller.CreatePlaceContribution(
            "{}",
            CreateFile(TestPngBytes(), "card.txt", "text/plain"),
            null,
            null,
            null);

        AssertObjectResult(result.Result, HttpStatusCode.BadRequest, "Only PNG, JPG, GIF, or WEBP images are supported.");
        Assert.Equal(0, service.CreateCalls);
    }

    [Fact]
    public async Task Contribution_image_upload_rejects_mismatched_magic_bytes_before_service_call()
    {
        var service = new RecordingContributionService();
        var controller = CreateContributionsController(service);

        var result = await controller.CreatePlaceContribution(
            "{}",
            CreateFile([1, 2, 3, 4], "card.png", "image/png"),
            null,
            null,
            null);

        AssertObjectResult(result.Result, HttpStatusCode.BadRequest, "Only PNG, JPG, GIF, or WEBP images are supported.");
        Assert.Equal(0, service.CreateCalls);
    }

    [Fact]
    public async Task Contribution_image_upload_accepts_valid_image_after_preflight()
    {
        var service = new RecordingContributionService();
        var controller = CreateContributionsController(service);
        var bytes = TestPngBytes();

        var result = await controller.CreatePlaceContribution(
            "{}",
            CreateFile(bytes, "card.png", "image/png"),
            null,
            null,
            null);

        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        Assert.Equal((int)HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(1, service.CreateCalls);
        Assert.Equal(bytes, service.LastCardImageBytes);
    }

    [Fact]
    public async Task Ownership_proof_upload_rejects_oversized_file_before_service_call()
    {
        var service = new RecordingContributionService();
        var controller = CreateContributionsController(service);
        var bytes = BytesWithPrefix((int)UploadLimitDefaults.ProofDocumentMaxFileSizeBytes + 1, "%PDF-"u8.ToArray());

        var result = await controller.CreatePlaceContribution(
            "{}",
            null,
            null,
            null,
            CreateFile(bytes, "proof.pdf", "application/pdf"));

        AssertObjectResult(result.Result, (HttpStatusCode)413, "Ownership proof document must be 10 MB or smaller.");
        Assert.Equal(0, service.CreateCalls);
    }

    [Fact]
    public async Task Ownership_proof_upload_rejects_unsupported_type_before_service_call()
    {
        var service = new RecordingContributionService();
        var controller = CreateContributionsController(service);

        var result = await controller.CreatePlaceContribution(
            "{}",
            null,
            null,
            null,
            CreateFile([1, 2, 3, 4], "proof.txt", "text/plain"));

        AssertObjectResult(result.Result, HttpStatusCode.BadRequest, "Ownership proof document must be a PDF, PNG, JPG/JPEG, or WEBP file.");
        Assert.Equal(0, service.CreateCalls);
    }

    [Fact]
    public async Task Ownership_proof_upload_accepts_valid_image_proof_after_preflight()
    {
        var service = new RecordingContributionService();
        var controller = CreateContributionsController(service);
        var bytes = TestPngBytes();

        var result = await controller.CreatePlaceContribution(
            "{}",
            null,
            null,
            null,
            CreateFile(bytes, "proof.png", "image/png"));

        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        Assert.Equal((int)HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(1, service.CreateCalls);
        Assert.Equal(bytes, service.LastOwnershipProofBytes);
        Assert.Equal("proof.png", service.LastOwnershipProofOriginalFileName);
        Assert.Equal("image/png", service.LastOwnershipProofContentType);
    }

    [Fact]
    public void Upload_endpoints_have_request_form_and_rate_limit_metadata()
    {
        AssertUploadEndpoint<ContributionsController>(
            nameof(ContributionsController.CreatePlaceContribution),
            "contributions",
            UploadLimitDefaults.ContributionMaxTotalRequestBytes,
            UploadLimitDefaults.ProofDocumentMaxFileSizeBytes);
        AssertUploadEndpoint<ContributionsController>(
            nameof(ContributionsController.UpdatePlaceContribution),
            "contributions",
            UploadLimitDefaults.ContributionMaxTotalRequestBytes,
            UploadLimitDefaults.ProofDocumentMaxFileSizeBytes);
        AssertUploadEndpoint<AdminRegionalController>(
            nameof(AdminRegionalController.UploadImages),
            "uploads",
            UploadLimitDefaults.RegionalMaxTotalRequestBytes,
            UploadLimitDefaults.ImageMaxFileSizeBytes);
        AssertUploadEndpoint<UserController>(
            nameof(UserController.UpdateAvatar),
            "uploads",
            UploadLimitDefaults.UserAvatarMaxFileSizeBytes,
            UploadLimitDefaults.UserAvatarMaxFileSizeBytes);
        AssertUploadEndpoint<AdminProfileController>(
            nameof(AdminProfileController.UploadAvatar),
            "uploads",
            UploadLimitDefaults.AdminAvatarMaxFileSizeBytes,
            UploadLimitDefaults.AdminAvatarMaxFileSizeBytes);
    }

    [Fact]
    public void Spam_prone_write_endpoints_have_explicit_rate_limit_metadata()
    {
        AssertRateLimitPolicy<ReviewsController>(nameof(ReviewsController.CreatePlaceReview), "reviews");
        AssertRateLimitPolicy<ReviewsController>(nameof(ReviewsController.CreateEventReview), "reviews");
        AssertRateLimitPolicy<ReviewsController>(nameof(ReviewsController.ReportReview), "reports");
        AssertRateLimitPolicy<PlacesController>(nameof(PlacesController.CreatePlaceReview), "reviews");
        AssertRateLimitPolicy<PlacesController>(nameof(PlacesController.OwnerUpdatePlace), "contributions");
        AssertRateLimitPolicy<DestinationController>(nameof(DestinationController.CreateDestinationReview), "reviews");
        AssertRateLimitPolicy<EventsController>(nameof(EventsController.CreateEventReview), "reviews");
        AssertRateLimitPolicy<LocationController>(nameof(LocationController.CreateLocationReview), "reviews");
        AssertRateLimitPolicy<UserController>(nameof(UserController.CreateSupportRequest), "support");
        AssertRateLimitPolicy<UserController>(nameof(UserController.CreateCollection), "userActions");
        AssertRateLimitPolicy<UserController>(nameof(UserController.AddCollectionItem), "userActions");
        AssertRateLimitPolicy<FavoritesController>(nameof(FavoritesController.Toggle), "userActions");
    }

    private static ContributionsController CreateContributionsController(RecordingContributionService service)
    {
        var controller = new ContributionsController(
            service,
            Options.Create(new UploadLimitsOptions()));

        var httpContext = new DefaultHttpContext();
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, "42"),
            new Claim("userId", "42")
        ], "Test"));

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContext
        };

        return controller;
    }

    private static IFormFile CreateFile(byte[] bytes, string fileName, string contentType)
    {
        var stream = new MemoryStream(bytes);
        return new FormFile(stream, 0, bytes.Length, "file", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType
        };
    }

    private static byte[] BytesWithPrefix(int length, byte[] prefix)
    {
        var bytes = new byte[length];
        prefix.CopyTo(bytes, 0);
        return bytes;
    }

    private static byte[] TestPngBytes() =>
    [
        0x89, 0x50, 0x4E, 0x47,
        0x0D, 0x0A, 0x1A, 0x0A,
        0x00, 0x00, 0x00, 0x0D
    ];

    private static void AssertObjectResult(
        IActionResult? result,
        HttpStatusCode expectedStatus,
        string expectedMessage)
    {
        var objectResult = Assert.IsAssignableFrom<ObjectResult>(result);
        Assert.Equal((int)expectedStatus, objectResult.StatusCode);
        Assert.Equal(expectedMessage, ReadMessage(objectResult.Value));
    }

    private static string? ReadMessage(object? value)
    {
        var property = value?.GetType().GetProperty("message");
        return property?.GetValue(value)?.ToString();
    }

    private static void AssertUploadEndpoint<TController>(
        string actionName,
        string expectedPolicy,
        long expectedRequestSizeLimit,
        long expectedMultipartBodyLengthLimit)
    {
        var method = GetAction<TController>(actionName);
        var rateLimit = Assert.Single(method.GetCustomAttributes<EnableRateLimitingAttribute>());
        Assert.Equal(expectedPolicy, rateLimit.PolicyName);

        var requestSizeLimit = Assert.Single(method.GetCustomAttributes<RequestSizeLimitAttribute>());
        var requestSizeMetadata = Assert.IsAssignableFrom<IRequestSizeLimitMetadata>(requestSizeLimit);
        Assert.Equal(expectedRequestSizeLimit, requestSizeMetadata.MaxRequestBodySize);

        var formLimits = Assert.Single(method.GetCustomAttributes<RequestFormLimitsAttribute>());
        Assert.Equal(expectedMultipartBodyLengthLimit, formLimits.MultipartBodyLengthLimit);
    }

    private static void AssertRateLimitPolicy<TController>(string actionName, string expectedPolicy)
    {
        var method = GetAction<TController>(actionName);
        var attribute = Assert.Single(method.GetCustomAttributes<EnableRateLimitingAttribute>());
        Assert.Equal(expectedPolicy, attribute.PolicyName);
    }

    private static MethodInfo GetAction<TController>(string actionName)
    {
        var method = typeof(TController).GetMethod(
            actionName,
            BindingFlags.Instance | BindingFlags.Public);

        Assert.NotNull(method);
        return method!;
    }

    private sealed class RecordingContributionService : IPlaceContributionService
    {
        public int CreateCalls { get; private set; }
        public byte[]? LastCardImageBytes { get; private set; }
        public byte[]? LastOwnershipProofBytes { get; private set; }
        public string? LastOwnershipProofOriginalFileName { get; private set; }
        public string? LastOwnershipProofContentType { get; private set; }

        public Task<GetPlaceContributionsResponseDto> GetForUserAsync(int userId) =>
            Task.FromResult(new GetPlaceContributionsResponseDto());

        public Task<PlaceContributionDetailDto?> GetByIdForUserAsync(int userId, int contributionId) =>
            Task.FromResult<PlaceContributionDetailDto?>(new PlaceContributionDetailDto { Id = contributionId });

        public Task<GetPlaceContributionsResponseDto> GetPendingReviewAsync(int? reviewerUserId = null) =>
            Task.FromResult(new GetPlaceContributionsResponseDto());

        public Task<PlaceContributionDetailDto?> GetPendingReviewByIdAsync(int contributionId, int? reviewerUserId = null) =>
            Task.FromResult<PlaceContributionDetailDto?>(new PlaceContributionDetailDto { Id = contributionId });

        public Task<GetPlaceContributionsResponseDto> GetPendingForContentManagerAsync() =>
            Task.FromResult(new GetPlaceContributionsResponseDto());

        public Task<PlaceContributionDetailDto?> GetPendingForContentManagerByIdAsync(int contributionId) =>
            Task.FromResult<PlaceContributionDetailDto?>(new PlaceContributionDetailDto { Id = contributionId });

        public Task<List<ContributionPlaceTypeDto>> GetPlaceTypesAsync() =>
            Task.FromResult(new List<ContributionPlaceTypeDto>());

        public Task<List<ContributionStayTypeDto>> GetStayTypesAsync() =>
            Task.FromResult(new List<ContributionStayTypeDto>());

        public Task<ContributionFeatureOptionsDto> GetFeatureOptionsAsync() =>
            Task.FromResult(new ContributionFeatureOptionsDto());

        public Task<PlaceContributionDetailDto> CreateAsync(
            int userId,
            UpsertPlaceContributionDto request,
            byte[]? cardImageBytes,
            byte[]? coverImageBytes,
            IReadOnlyList<byte[]>? galleryImageBytes,
            byte[]? ownershipProofBytes,
            string? ownershipProofOriginalFileName,
            string? ownershipProofContentType)
        {
            CreateCalls++;
            LastCardImageBytes = cardImageBytes;
            LastOwnershipProofBytes = ownershipProofBytes;
            LastOwnershipProofOriginalFileName = ownershipProofOriginalFileName;
            LastOwnershipProofContentType = ownershipProofContentType;
            return Task.FromResult(new PlaceContributionDetailDto { Id = 123 });
        }

        public Task<PlaceContributionDetailDto> UpdateAsync(
            int userId,
            int contributionId,
            UpsertPlaceContributionDto request,
            byte[]? cardImageBytes,
            byte[]? coverImageBytes,
            IReadOnlyList<byte[]>? galleryImageBytes,
            byte[]? ownershipProofBytes,
            string? ownershipProofOriginalFileName,
            string? ownershipProofContentType) =>
            Task.FromResult(new PlaceContributionDetailDto { Id = contributionId });

        public Task<ResolveContributionLocationDto?> ResolveNearestLocationAsync(double latitude, double longitude) =>
            Task.FromResult<ResolveContributionLocationDto?>(null);

        public Task DeleteAsync(int userId, int contributionId) => Task.CompletedTask;

        public Task<PlaceContributionDetailDto> ApproveAsync(
            int contributionId,
            int reviewerUserId,
            string? note,
            string? internalNote = null,
            string? sourceArea = null) =>
            Task.FromResult(new PlaceContributionDetailDto { Id = contributionId });

        public Task<PlaceContributionDetailDto> RejectAsync(
            int contributionId,
            int reviewerUserId,
            string? note,
            string? internalNote = null,
            string? sourceArea = null) =>
            Task.FromResult(new PlaceContributionDetailDto { Id = contributionId });

        public Task<PlaceContributionDetailDto> ApproveForContentManagerAsync(
            int contributionId,
            int reviewerUserId,
            int regionId,
            int? existingDestinationId,
            string? newDestinationName,
            string? newLocationName,
            string? note) =>
            Task.FromResult(new PlaceContributionDetailDto { Id = contributionId });

        public Task<ContributionRegionOptionsDto> GetRegionOptionsAsync() =>
            Task.FromResult(new ContributionRegionOptionsDto());

        public Task<ContributionLocationOptionsDto> GetDestinationLocationOptionsAsync(int destinationId) =>
            Task.FromResult(new ContributionLocationOptionsDto());

        public Task AddDestinationCardImageAsync(int destinationId, byte[] imageBytes) => Task.CompletedTask;

        public Task AddLocationCardImageAsync(int locationId, byte[] imageBytes) => Task.CompletedTask;
    }
}
