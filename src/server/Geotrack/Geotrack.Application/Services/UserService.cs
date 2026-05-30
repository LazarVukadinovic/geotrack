using System.Text.RegularExpressions;
using Geotrack.Application.DTOs.Users;
using Geotrack.Application.DTOs.Interests;
using Geotrack.Application.Interfaces.Repository;
using Geotrack.Application.Interfaces.Service;
using Geotrack.Application.Security;
using Geotrack.Domain.Entities;
using Geotrack.Domain.Entities.Core;
using Geotrack.Domain.Entities.Interests;
using Geotrack.Domain.Entities.Analytics;
using Geotrack.Domain.Entities.Tags;
using Geotrack.Domain.Entities.Users;
using Microsoft.AspNetCore.Identity;

namespace Geotrack.Application.Services;

public class UserService : IUserService
{
    private const double InferredInterestVisibleScore = 5.0;

    private static readonly HashSet<string> AllowedSupportCategories = new(StringComparer.OrdinalIgnoreCase)
    {
        "account-access",
        "saved-places-and-collections",
        "location-and-maps",
        "ask-agent",
        "events-and-stays",
        "privacy-and-data",
    };

    private readonly IUserRepository _userRepository;
    private readonly IDestinationRepository _destinationRepository;
    private readonly ILocationRepository _locationRepository;
    private readonly IPlaceRepository _placeRepository;
    private readonly IEventRepository _eventRepository;
    private readonly IRefreshTokenRepository _refreshTokenRepository;
    private readonly IUserAvatarStorageService _userAvatarStorageService;
    private readonly IFavoriteService _favoriteService;
    private readonly IAdminNotificationService _adminNotificationService;
    private readonly PasswordHasher<User> _passwordHasher = new();

    public UserService(
        IUserRepository userRepository,
        IDestinationRepository destinationRepository,
        ILocationRepository locationRepository,
        IPlaceRepository placeRepository,
        IEventRepository eventRepository,
        IRefreshTokenRepository refreshTokenRepository,
        IUserAvatarStorageService userAvatarStorageService,
        IFavoriteService favoriteService,
        IAdminNotificationService adminNotificationService)
    {
        _userRepository = userRepository;
        _destinationRepository = destinationRepository;
        _locationRepository = locationRepository;
        _placeRepository = placeRepository;
        _eventRepository = eventRepository;
        _refreshTokenRepository = refreshTokenRepository;
        _userAvatarStorageService = userAvatarStorageService;
        _favoriteService = favoriteService;
        _adminNotificationService = adminNotificationService;
    }

    public async Task<GetMeResponseDto> GetMeAsync(int userId)
    {
        var user = await _userRepository.GetByIdWithCollectionsAsync(userId)
            ?? throw new KeyNotFoundException("User not found");

        var collections = new List<CollectionPreviewDto>();

        foreach (var collection in user.Collections.OrderByDescending(x => x.CreatedAt))
        {
            collections.Add(new CollectionPreviewDto
            {
                Id = collection.Id,
                Name = collection.Name,
                Description = collection.Description,
                CreatedAt = collection.CreatedAt,
                UpdatedAt = collection.UpdatedAt,
                ItemsCount = await _userRepository.GetCollectionItemsCountAsync(collection.Id)
            });
        }

        return new GetMeResponseDto
        {
            Id = user.Id,
            Username = user.Username,
            Email = user.Email,
            FirstName = user.FirstName,
            LastName = user.LastName,
            PhoneNumber = user.PhoneNumber,
            AvatarUrl = user.AvatarUrl,
            AvatarVersion = AvatarVersionBuilder.From(user.AvatarUrl, user.UpdatedAt, user.CreatedAt),
            Location = user.Location,
            NeedsInterestsOnboarding = user.NeedsInterestsOnboarding,
            AnalyticsEnabled = user.AnalyticsEnabled,
            CreatedAt = user.CreatedAt,
            UpdatedAt = user.UpdatedAt,
            Collections = collections
        };
    }

    public async Task<UpdateMeResponseDto> UpdateMeAsync(int userId, UpdateMeRequestDto request)
    {
        ValidateProfileFields(request.FirstName, request.LastName, request.PhoneNumber);

        var user = await _userRepository.GetByIdAsync(userId)
            ?? throw new KeyNotFoundException("User not found");

        user.FirstName = request.FirstName?.Trim() ?? string.Empty;
        user.LastName = request.LastName?.Trim() ?? string.Empty;
        user.PhoneNumber = request.PhoneNumber?.Trim() ?? string.Empty;
        user.Location = request.Location?.Trim();
        user.UpdatedAt = DateTime.UtcNow;

        await _userRepository.SaveChangesAsync();

        return new UpdateMeResponseDto
        {
            Id = user.Id,
            Username = user.Username,
            Email = user.Email,
            FirstName = user.FirstName,
            LastName = user.LastName,
            PhoneNumber = user.PhoneNumber,
            AvatarUrl = user.AvatarUrl,
            AvatarVersion = AvatarVersionBuilder.From(user.AvatarUrl, user.UpdatedAt, user.CreatedAt),
            Location = user.Location,
            CreatedAt = user.CreatedAt,
            UpdatedAt = user.UpdatedAt
        };
    }

    public async Task<UpdateAvatarResponseDto> UpdateAvatarAsync(int userId, byte[] fileBytes)
    {
        var user = await _userRepository.GetByIdAsync(userId)
            ?? throw new KeyNotFoundException("User not found");

        var previousAvatarUrl = user.AvatarUrl;
        var storedAvatar = await _userAvatarStorageService.StoreAsync(userId, fileBytes);

        try
        {
            user.AvatarUrl = storedAvatar.AvatarUrl;
            user.UpdatedAt = DateTime.UtcNow;
            await _userRepository.SaveChangesAsync();
        }
        catch
        {
            await _userAvatarStorageService.DeleteAsync(storedAvatar.AvatarUrl);
            throw;
        }

        if (_userAvatarStorageService.IsManagedAvatarUrl(previousAvatarUrl) &&
            !string.Equals(previousAvatarUrl, storedAvatar.AvatarUrl, StringComparison.OrdinalIgnoreCase))
        {
            await _userAvatarStorageService.DeleteAsync(previousAvatarUrl!);
        }

        return new UpdateAvatarResponseDto
        {
            AvatarUrl = user.AvatarUrl ?? string.Empty,
            AvatarVersion = AvatarVersionBuilder.From(user.AvatarUrl, user.UpdatedAt, user.CreatedAt),
            UpdatedAt = user.UpdatedAt,
        };
    }

    public async Task<UpdateAvatarResponseDto> DeleteAvatarAsync(int userId)
    {
        var user = await _userRepository.GetByIdAsync(userId)
            ?? throw new KeyNotFoundException("User not found");

        var previousAvatarUrl = user.AvatarUrl;

        user.AvatarUrl = null;
        user.UpdatedAt = DateTime.UtcNow;

        await _userRepository.SaveChangesAsync();

        if (_userAvatarStorageService.IsManagedAvatarUrl(previousAvatarUrl))
        {
            await _userAvatarStorageService.DeleteAsync(previousAvatarUrl!);
        }

        return new UpdateAvatarResponseDto
        {
            AvatarUrl = string.Empty,
            AvatarVersion = null,
            UpdatedAt = user.UpdatedAt,
        };
    }

    public async Task<ChangePasswordResponseDto> ChangePasswordAsync(int userId, ChangePasswordRequestDto request)
    {
        ValidatePasswordChangeRequest(request);

        var user = await _userRepository.GetByIdAsync(userId)
            ?? throw new KeyNotFoundException("User not found");

        var currentPasswordResult = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.CurrentPassword);
        if (currentPasswordResult == PasswordVerificationResult.Failed)
            throw new InvalidOperationException("Current password is incorrect");

        if (request.CurrentPassword == request.NewPassword)
            throw new InvalidOperationException("Choose a password you are not already using");

        user.PasswordHash = _passwordHasher.HashPassword(user, request.NewPassword);
        user.UpdatedAt = DateTime.UtcNow;

        await _userRepository.SaveChangesAsync();
        await _refreshTokenRepository.RevokeActiveForUserAsync(userId, DateTime.UtcNow);

        return new ChangePasswordResponseDto
        {
            Message = "Password updated successfully. Please sign in again."
        };
    }

    public async Task<GetNotificationSettingsResponseDto> GetNotificationSettingsAsync(int userId)
    {
        var user = await _userRepository.GetByIdAsync(userId)
            ?? throw new KeyNotFoundException("User not found");

        return MapNotificationSettings(user);
    }

    public async Task<UpdateNotificationSettingsResponseDto> UpdateNotificationSettingsAsync(int userId, UpdateNotificationSettingsRequestDto request)
    {
        var user = await _userRepository.GetByIdAsync(userId)
            ?? throw new KeyNotFoundException("User not found");

        user.GeneralNotificationsEnabled = request.GeneralNotifications;
        user.TravelRecommendationsEnabled = request.TravelRecommendations;
        user.NearbyEventsEnabled = request.NearbyEvents;
        user.CollectionUpdatesEnabled = request.CollectionUpdates;
        user.EmailNotificationsEnabled = false;
        user.UpdatedAt = DateTime.UtcNow;

        await _userRepository.SaveChangesAsync();

        return MapUpdatedNotificationSettings(user);
    }

    public async Task<GetAnalyticsSettingsResponseDto> GetAnalyticsSettingsAsync(int userId)
    {
        var user = await _userRepository.GetByIdAsync(userId)
            ?? throw new KeyNotFoundException("User not found");

        return MapAnalyticsSettings(user);
    }

    public async Task<UpdateAnalyticsSettingsResponseDto> UpdateAnalyticsSettingsAsync(int userId, UpdateAnalyticsSettingsRequestDto request)
    {
        var user = await _userRepository.GetByIdAsync(userId)
            ?? throw new KeyNotFoundException("User not found");

        user.AnalyticsEnabled = request.AnalyticsEnabled;
        user.UpdatedAt = DateTime.UtcNow;

        await _userRepository.SaveChangesAsync();

        return MapUpdatedAnalyticsSettings(user);
    }

    public async Task<GetUserInterestsResponseDto> GetUserInterestsAsync(int userId)
    {
        var user = await _userRepository.GetByIdWithUserInterestTopicsAsync(userId)
            ?? throw new KeyNotFoundException("User not found");

        var explicitTopics = user.UserInterestTopics
            .Where(x => x.InterestTopic is { IsActive: true })
            .Select(x => MapInterestTopicToInterestItemDto(x.InterestTopic))
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Name)
            .ToList();

        var inferredSignals = await _userRepository.GetVisibleInferredInterestTopicSignalsAsync(userId, InferredInterestVisibleScore);

        return new GetUserInterestsResponseDto
        {
            Tags = explicitTopics,
            ExplicitTags = explicitTopics,
            InferredTags = inferredSignals
                .Where(x => x.InterestTopic is { IsActive: true })
                .Select(MapTopicSignalToInferredInterestItemDto)
                .ToList()
        };
    }

    public async Task<UpdateUserInterestsResponseDto> UpdateUserInterestsAsync(int userId, UpdateUserInterestsRequestDto request)
    {
        var requestedIds = request.InterestTopicIds is { Count: > 0 }
            ? request.InterestTopicIds
            : request.TagIds ?? new List<int>();

        var distinctTopicIds = requestedIds.Distinct().ToList();

        var user = await _userRepository.GetByIdWithUserInterestTopicsAsync(userId)
            ?? throw new KeyNotFoundException("User not found");

        var topics = await _userRepository.GetInterestTopicsByIdsAsync(distinctTopicIds);

        if (topics.Count != distinctTopicIds.Count)
            throw new InvalidOperationException("One or more interest topics do not exist");

        var existingTopicIds = user.UserInterestTopics.Select(x => x.InterestTopicId).ToHashSet();

        var userTopicsToRemove = user.UserInterestTopics
            .Where(x => !distinctTopicIds.Contains(x.InterestTopicId))
            .ToList();

        if (userTopicsToRemove.Any())
        {
            await _userRepository.DeleteUserInterestTopicsAsync(userTopicsToRemove);
        }

        var topicIdsToAdd = distinctTopicIds
            .Where(topicId => !existingTopicIds.Contains(topicId))
            .ToList();

        foreach (var topicId in topicIdsToAdd)
        {
            await _userRepository.AddUserInterestTopicAsync(new UserInterestTopic
            {
                UserId = userId,
                InterestTopicId = topicId,
                CreatedAt = DateTime.UtcNow
            });
        }

        user.NeedsInterestsOnboarding = false;
        user.UpdatedAt = DateTime.UtcNow;
        await _userRepository.SaveChangesAsync();

        var updatedUser = await _userRepository.GetByIdWithUserInterestTopicsAsync(userId)
            ?? throw new KeyNotFoundException("User not found");

        var explicitTopics = updatedUser.UserInterestTopics
            .Where(x => x.InterestTopic is { IsActive: true })
            .Select(x => MapInterestTopicToInterestItemDto(x.InterestTopic))
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Name)
            .ToList();

        return new UpdateUserInterestsResponseDto
        {
            Tags = explicitTopics,
            ExplicitTags = explicitTopics
        };
    }

    public async Task DismissInferredInterestAsync(int userId, int interestTopicId)
    {
        var signal = await _userRepository.GetUserInterestTopicSignalAsync(userId, interestTopicId);
        if (signal is null)
        {
            return;
        }

        signal.IsDismissed = true;
        signal.DismissedAt = DateTime.UtcNow;
        signal.DismissedAtScore = signal.Score;
        signal.EvidenceSinceDismissal = 0.0;

        await _userRepository.SaveChangesAsync();
    }

    public async Task<GetSupportRequestsResponseDto> GetSupportRequestsAsync(int userId)
    {
        var user = await _userRepository.GetByIdAsync(userId)
            ?? throw new KeyNotFoundException("User not found");

        _ = user;

        var requests = await _userRepository.GetSupportRequestsByUserIdAsync(userId, 5);

        return new GetSupportRequestsResponseDto
        {
            Requests = requests
                .Select(MapSupportRequest)
                .ToList()
        };
    }

    public async Task<CreateSupportRequestResponseDto> CreateSupportRequestAsync(int userId, CreateSupportRequestRequestDto request)
    {
        var user = await _userRepository.GetByIdAsync(userId)
            ?? throw new KeyNotFoundException("User not found");

        _ = user;

        ValidateSupportRequest(request);

        var supportRequest = new SupportRequest
        {
            UserId = userId,
            Category = NormalizeSupportCategory(request.Category),
            Subject = request.Subject.Trim(),
            Message = request.Message.Trim(),
            Status = SupportRequestStatuses.Open,
            Priority = SupportRequestPriorities.Normal,
            PageUrl = NormalizeOptionalSupportField(request.PageUrl, 500, "Support request page URL is too long"),
            UserAgent = NormalizeOptionalSupportField(request.UserAgent, 500, "Support request user agent is too long"),
            DeviceInfo = NormalizeOptionalSupportField(request.DeviceInfo, 300, "Support request device info is too long"),
            Browser = NormalizeOptionalSupportField(request.Browser, 120, "Support request browser is too long"),
            Platform = NormalizeOptionalSupportField(request.Platform, 120, "Support request platform is too long"),
            AttachmentUrl = NormalizeOptionalSupportField(request.AttachmentUrl, 1000, "Support request attachment URL is too long"),
            CreatedAt = DateTime.UtcNow
        };

        await _userRepository.AddSupportRequestAsync(supportRequest);
        await _userRepository.SaveChangesAsync();
        await _adminNotificationService.CreateSupportTicketCreatedNotificationAsync(supportRequest);

        return MapCreatedSupportRequest(supportRequest);
    }

    public async Task<GetCollectionsResponseDto> GetCollectionsAsync(int userId)
    {
        var collections = await _userRepository.GetCollectionsByUserIdAsync(userId);

        var result = new List<CollectionListItemDto>();

        foreach (var collection in collections.OrderByDescending(x => x.CreatedAt))
        {
            result.Add(new CollectionListItemDto
            {
                Id = collection.Id,
                Name = collection.Name,
                Description = collection.Description,
                UserId = collection.UserId,
                CreatedAt = collection.CreatedAt,
                UpdatedAt = collection.UpdatedAt,
                ItemsCount = await _userRepository.GetCollectionItemsCountAsync(collection.Id),
                CoverImages = await _userRepository.GetCollectionCoverImagesAsync(collection.Id, 3)
            });
        }

        return new GetCollectionsResponseDto
        {
            Collections = result
        };
    }

    public async Task<CreateCollectionResponseDto> CreateCollectionAsync(int userId, CreateCollectionRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            throw new InvalidOperationException("Collection name is required");

        if (request.Name.Length > 200)
            throw new InvalidOperationException("Collection name is too long");

        if ((request.Description ?? string.Empty).Length > 1000)
            throw new InvalidOperationException("Collection description is too long");

        var collection = new Collection
        {
            Name = request.Name.Trim(),
            Description = request.Description?.Trim() ?? string.Empty,
            UserId = userId,
            CreatedAt = DateTime.UtcNow
        };

        await _userRepository.AddCollectionAsync(collection);
        await _userRepository.SaveChangesAsync();

        return new CreateCollectionResponseDto
        {
            Id = collection.Id,
            Name = collection.Name,
            Description = collection.Description,
            UserId = collection.UserId,
            CreatedAt = collection.CreatedAt,
            UpdatedAt = collection.UpdatedAt
        };
    }

    public async Task<GetCollectionDetailsResponseDto> GetCollectionDetailsAsync(int userId, int collectionId)
    {
        var collection = await _userRepository.GetCollectionByIdWithItemsAsync(userId, collectionId)
            ?? throw new KeyNotFoundException("Collection not found");

        return new GetCollectionDetailsResponseDto
        {
            Id = collection.Id,
            Name = collection.Name,
            Description = collection.Description,
            UserId = collection.UserId,
            CreatedAt = collection.CreatedAt,
            UpdatedAt = collection.UpdatedAt,
            Items = collection.CollectionItems
                .OrderByDescending(x => x.CreatedAt)
                .Select(MapCollectionItem)
                .ToList()
        };
    }

    public async Task<UpdateCollectionResponseDto> UpdateCollectionAsync(int userId, int collectionId, UpdateCollectionRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            throw new InvalidOperationException("Collection name is required");

        if (request.Name.Length > 200)
            throw new InvalidOperationException("Collection name is too long");

        if ((request.Description ?? string.Empty).Length > 1000)
            throw new InvalidOperationException("Collection description is too long");

        var collection = await _userRepository.GetCollectionByIdForUserAsync(userId, collectionId)
            ?? throw new KeyNotFoundException("Collection not found");

        collection.Name = request.Name.Trim();
        collection.Description = request.Description?.Trim() ?? string.Empty;
        collection.UpdatedAt = DateTime.UtcNow;

        await _userRepository.SaveChangesAsync();

        return new UpdateCollectionResponseDto
        {
            Id = collection.Id,
            Name = collection.Name,
            Description = collection.Description,
            UserId = collection.UserId,
            CreatedAt = collection.CreatedAt,
            UpdatedAt = collection.UpdatedAt
        };
    }

    public async Task DeleteCollectionAsync(int userId, int collectionId)
    {
        var collection = await _userRepository.GetCollectionByIdWithItemsAsync(userId, collectionId)
            ?? throw new KeyNotFoundException("Collection not found");

        var affectedFavorites = collection.CollectionItems
            .Select(ResolveFavoriteEntity)
            .Where(x => x.HasValue)
            .Select(x => x!.Value)
            .Distinct()
            .ToList();

        await _userRepository.DeleteCollectionAsync(collection);
        await _userRepository.SaveChangesAsync();

        foreach (var affected in affectedFavorites)
        {
            await _favoriteService.RemoveIfUnsavedAsync(userId, affected.EntityType, affected.EntityId);
        }

        if (affectedFavorites.Count > 0)
        {
            await _userRepository.SaveChangesAsync();
        }
    }

    public async Task<AddCollectionItemResponseDto> AddCollectionItemAsync(int userId, int collectionId, AddCollectionItemRequestDto request)
    {
        var collection = await _userRepository.GetCollectionByIdForUserAsync(userId, collectionId)
            ?? throw new KeyNotFoundException("Collection not found");

        var entityType = (request.EntityType ?? string.Empty).Trim().ToLowerInvariant();

        if (entityType is not ("destination" or "location" or "place" or "event"))
            throw new InvalidOperationException("Invalid entity type");

        var exists = await _userRepository.CollectionItemExistsAsync(collectionId, entityType, request.EntityId);
        if (exists)
            throw new InvalidOperationException("Entity already exists in collection");

        var item = new CollectionItem
        {
            CollectionId = collection.Id,
            CreatedAt = DateTime.UtcNow
        };

        switch (entityType)
        {
            case "destination":
                if (await _destinationRepository.GetByIdAsync(request.EntityId) == null)
                    throw new KeyNotFoundException("Destinations not found");
                item.DestinationId = request.EntityId;
                break;

            case "location":
                if (await _locationRepository.GetByIdAsync(request.EntityId) == null)
                    throw new KeyNotFoundException("Locations not found");
                item.LocationId = request.EntityId;
                break;

            case "place":
                if (await _placeRepository.GetByIdAsync(request.EntityId) == null)
                    throw new KeyNotFoundException("Places not found");
                item.PlaceId = request.EntityId;
                break;

            case "event":
                if (await _eventRepository.GetByIdAsync(request.EntityId) == null)
                    throw new KeyNotFoundException("Events not found");
                item.EventId = request.EntityId;
                break;
        }

        await _userRepository.AddCollectionItemAsync(item);
        if (entityType is "place" or "event")
        {
            await _favoriteService.EnsureSavedAsync(userId, entityType, request.EntityId);
        }
        await _userRepository.SaveChangesAsync();

        return new AddCollectionItemResponseDto
        {
            Id = item.Id,
            CollectionId = item.CollectionId,
            EntityType = entityType,
            DestinationId = item.DestinationId,
            LocationId = item.LocationId,
            PlaceId = item.PlaceId,
            EventId = item.EventId,
            DetailRoute = await ResolveCollectionItemDetailRouteAsync(entityType, request.EntityId),
            CreatedAt = item.CreatedAt,
            UpdatedAt = item.UpdatedAt
        };
    }

    public async Task DeleteCollectionItemAsync(int userId, int collectionId, int itemId)
    {
        var collection = await _userRepository.GetCollectionByIdForUserAsync(userId, collectionId)
            ?? throw new KeyNotFoundException("Collection not found");

        var item = await _userRepository.GetCollectionItemByIdAsync(collection.Id, itemId)
            ?? throw new KeyNotFoundException("Collection item not found");

        var affectedFavorite = ResolveFavoriteEntity(item);

        await _userRepository.DeleteCollectionItemAsync(item);
        await _userRepository.SaveChangesAsync();

        if (affectedFavorite.HasValue)
        {
            await _favoriteService.RemoveIfUnsavedAsync(
                userId,
                affectedFavorite.Value.EntityType,
                affectedFavorite.Value.EntityId);
            await _userRepository.SaveChangesAsync();
        }
    }

    private static InterestItemDto MapInterestTopicToInterestItemDto(InterestTopic topic) => new()
    {
        InterestTopicId = topic.Id,
        Name = topic.Name,
        Slug = topic.Slug,
        Description = topic.Description,
        IconKey = topic.IconKey,
        Group = topic.Group,
        SortOrder = topic.SortOrder,
        Tags = topic.TopicTags
            .OrderByDescending(x => x.Weight)
            .ThenBy(x => x.Tag.Name)
            .Select(x => new InterestTopicTagDto
            {
                TagId = x.TagId,
                Name = x.Tag.Name,
                Slug = x.Tag.Slug,
                Weight = x.Weight
            })
            .ToList(),
        CreatedAt = topic.CreatedAt,
        UpdatedAt = topic.UpdatedAt
    };

    private static InferredInterestItemDto MapTopicSignalToInferredInterestItemDto(UserInterestTopicSignal signal)
    {
        var topic = signal.InterestTopic;

        return new InferredInterestItemDto
        {
            InterestTopicId = topic.Id,
            Name = topic.Name,
            Slug = topic.Slug,
            Description = topic.Description,
            IconKey = topic.IconKey,
            Group = topic.Group,
            SortOrder = topic.SortOrder,
            Tags = topic.TopicTags
                .OrderByDescending(x => x.Weight)
                .ThenBy(x => x.Tag.Name)
                .Select(x => new InterestTopicTagDto
                {
                    TagId = x.TagId,
                    Name = x.Tag.Name,
                    Slug = x.Tag.Slug,
                    Weight = x.Weight
                })
                .ToList(),
            CreatedAt = topic.CreatedAt,
            UpdatedAt = topic.UpdatedAt,
            Score = signal.Score,
            LastInteractionAt = signal.LastInteractionAt,
            LastReason = signal.LastReason
        };
    }

    private static GetNotificationSettingsResponseDto MapNotificationSettings(User user)
    {
        return new GetNotificationSettingsResponseDto
        {
            GeneralNotifications = user.GeneralNotificationsEnabled,
            TravelRecommendations = user.TravelRecommendationsEnabled,
            NearbyEvents = user.NearbyEventsEnabled,
            CollectionUpdates = user.CollectionUpdatesEnabled,
            EmailNotifications = false
        };
    }

    private static UpdateNotificationSettingsResponseDto MapUpdatedNotificationSettings(User user)
    {
        return new UpdateNotificationSettingsResponseDto
        {
            GeneralNotifications = user.GeneralNotificationsEnabled,
            TravelRecommendations = user.TravelRecommendationsEnabled,
            NearbyEvents = user.NearbyEventsEnabled,
            CollectionUpdates = user.CollectionUpdatesEnabled,
            EmailNotifications = false
        };
    }

    private static GetAnalyticsSettingsResponseDto MapAnalyticsSettings(User user)
    {
        return new GetAnalyticsSettingsResponseDto
        {
            AnalyticsEnabled = user.AnalyticsEnabled
        };
    }

    private static UpdateAnalyticsSettingsResponseDto MapUpdatedAnalyticsSettings(User user)
    {
        return new UpdateAnalyticsSettingsResponseDto
        {
            AnalyticsEnabled = user.AnalyticsEnabled
        };
    }

    private static CreateSupportRequestResponseDto MapCreatedSupportRequest(SupportRequest supportRequest)
    {
        return new CreateSupportRequestResponseDto
        {
            Id = supportRequest.Id,
            ReferenceCode = BuildSupportReferenceCode(supportRequest.Id),
            Category = supportRequest.Category,
            Subject = supportRequest.Subject,
            Message = supportRequest.Message,
            Status = supportRequest.Status,
            CreatedAt = supportRequest.CreatedAt
        };
    }

    private static SupportRequestItemDto MapSupportRequest(SupportRequest supportRequest)
    {
        return new SupportRequestItemDto
        {
            Id = supportRequest.Id,
            ReferenceCode = BuildSupportReferenceCode(supportRequest.Id),
            Category = supportRequest.Category,
            Subject = supportRequest.Subject,
            Message = supportRequest.Message,
            Status = supportRequest.Status,
            CreatedAt = supportRequest.CreatedAt
        };
    }

    private static string BuildSupportReferenceCode(int id) => $"GT-{id:D6}";

    private static CollectionItemDto MapCollectionItem(CollectionItem item)
    {
        if (item.DestinationId.HasValue && item.Destination != null)
        {
            return new CollectionItemDto
            {
                Id = item.Id,
                CollectionId = item.CollectionId,
                EntityType = "destination",
                DestinationId = item.DestinationId,
                Name = item.Destination.Name,
                Description = item.Destination.Description,
                ImageUrl = MainImageUrl(item.Destination.Images),
                DetailRoute = null,
                Latitude = item.Destination.Latitude,
                Longitude = item.Destination.Longitude,
                CreatedAt = item.Destination.CreatedAt,
                UpdatedAt = item.Destination.UpdatedAt
            };
        }

        if (item.LocationId.HasValue && item.Location != null)
        {
            return new CollectionItemDto
            {
                Id = item.Id,
                CollectionId = item.CollectionId,
                EntityType = "location",
                LocationId = item.LocationId,
                Name = item.Location.Name,
                ImageUrl = MainImageUrl(item.Location.Images),
                DetailRoute = null,
                Latitude = item.Location.Latitude,
                Longitude = item.Location.Longitude,
                CreatedAt = item.Location.CreatedAt,
                UpdatedAt = item.Location.UpdatedAt
            };
        }

        if (item.PlaceId.HasValue && item.Place != null)
        {
            return new CollectionItemDto
            {
                Id = item.Id,
                CollectionId = item.CollectionId,
                EntityType = "place",
                PlaceId = item.PlaceId,
                Name = item.Place.Name,
                Description = item.Place.Description,
                ImageUrl = MainImageUrl(item.Place.Images),
                DetailRoute = BuildPlaceDetailRoute(item.Place),
                Latitude = item.Place.Latitude,
                Longitude = item.Place.Longitude,
                ContactEmail = item.Place.ContactEmail,
                Number = item.Place.Number,
                CreatedAt = item.Place.CreatedAt,
                UpdatedAt = item.Place.UpdatedAt
            };
        }

        if (item.EventId.HasValue && item.Event != null)
        {
            return new CollectionItemDto
            {
                Id = item.Id,
                CollectionId = item.CollectionId,
                EntityType = "event",
                EventId = item.EventId,
                Name = item.Event.Name,
                Description = item.Event.Description,
                ImageUrl = MainImageUrl(item.Event.Images),
                DetailRoute = $"/events/{item.Event.Id}",
                StartDate = item.Event.StartDate,
                EndDate = item.Event.EndDate,
                CreatedAt = item.Event.CreatedAt,
                UpdatedAt = item.Event.UpdatedAt
            };
        }

        throw new InvalidOperationException("Invalid collection item state");
    }

    private static (string EntityType, int EntityId)? ResolveFavoriteEntity(CollectionItem item)
    {
        if (item.PlaceId.HasValue)
        {
            return ("place", item.PlaceId.Value);
        }

        if (item.EventId.HasValue)
        {
            return ("event", item.EventId.Value);
        }

        return null;
    }

    private async Task<string?> ResolveCollectionItemDetailRouteAsync(string entityType, int entityId)
    {
        if (entityType == "event")
        {
            return $"/events/{entityId}";
        }

        if (entityType == "place")
        {
            var place = await _placeRepository.GetByIdAsync(entityId);
            return place is null ? null : BuildPlaceDetailRoute(place);
        }

        return null;
    }

    private static string BuildPlaceDetailRoute(Place place) =>
        place.Stay != null ? $"/stays/{place.Id}" : $"/places/{place.Id}";


    private static string? MainImageUrl(IEnumerable<EntityImage> images) =>
        images
            .OrderBy(image => image.ImageRole == EntityImageRole.Card ? 0 : image.ImageRole == EntityImageRole.Cover ? 1 : 2)
            .ThenBy(image => image.SortOrder)
            .ThenBy(image => image.CreatedAt)
            .ThenBy(image => image.Id)
            .Select(image => image.Url)
            .FirstOrDefault();

    private static void ValidateProfileFields(string firstName, string lastName, string phoneNumber)
    {
        if ((firstName ?? string.Empty).Length > 100)
            throw new InvalidOperationException("First name is too long");

        if ((lastName ?? string.Empty).Length > 100)
            throw new InvalidOperationException("Last name is too long");

        if ((phoneNumber ?? string.Empty).Length > 50)
            throw new InvalidOperationException("Phone number is too long");

        if (!string.IsNullOrWhiteSpace(phoneNumber) &&
            !Regex.IsMatch(phoneNumber, @"^[0-9+\-\s()/]{6,50}$"))
        {
            throw new InvalidOperationException("Invalid phone number format");
        }
    }

    public async Task<UpdateUserRoleResponseDtocs> AssignRegionalManagerRoleAsync(int userId)
    {
        var user = await _userRepository.GetByIdWithRolesAsync(userId);

        if (user == null)
            throw new KeyNotFoundException($"User with id '{userId}' was not found.");

        bool alreadyHasRole = user.UserRoles
            .Any(ur => RoleNames.Normalize(ur.Role.Name) == RoleNames.RegionalManager);

        if (alreadyHasRole)
            throw new InvalidOperationException("User already has the RegionalManager role.");

        var role = await _userRepository.GetRoleByNameAsync(RoleNames.RegionalManager)
            ?? throw new InvalidOperationException("RegionalManager role does not exist.");

        user.UserRoles.Add(new UserRole
        {
            UserId = userId,
            RoleId = role.Id,
            Role = role
        });

        await _userRepository.SaveChangesAsync();

        return new UpdateUserRoleResponseDtocs
        {
            UserId = userId,
            Roles = user.UserRoles.Select(ur => ur.Role.Name).ToList()
        };
    }

    public async Task<UpdateUserRoleResponseDtocs> RemoveRegionalManagerRoleAsync(int userId)
    {
        var user = await _userRepository.GetByIdWithRolesAsync(userId);

        if (user == null)
            throw new KeyNotFoundException($"User with id '{userId}' was not found.");

        var userRole = user.UserRoles
            .FirstOrDefault(ur => RoleNames.Normalize(ur.Role.Name) == RoleNames.RegionalManager);

        if (userRole == null)
            throw new InvalidOperationException("User does not have the RegionalManager role.");

        await _userRepository.RemoveUserRoleAsync(userRole);

        await _userRepository.SaveChangesAsync();

        return new UpdateUserRoleResponseDtocs
        {
            UserId = userId,
            Roles = user.UserRoles
                .Where(ur => RoleNames.Normalize(ur.Role.Name) != RoleNames.RegionalManager)
                .Select(ur => ur.Role.Name)
                .ToList()
        };
    }

    public async Task<GetUserRegionsResponseDto> GetUserRegionsAsync(int userId)
    {
        var user = await _userRepository.GetByIdWithRegionsAsync(userId);

        if (user == null)
            throw new KeyNotFoundException($"User with id '{userId}' was not found.");

        var regions = user.RegionalManagerRegions
            .Where(ror => ror.IsActive)
            .Select(ror => new RegionItemDto
            {
                Id = ror.Region.Id,
                Name = ror.Region.Name
            })
            .ToList();

        return new GetUserRegionsResponseDto
        {
            UserId = userId,
            Regions = regions
        };
    }

    public async Task<UpdateUserRegionsResponseDto> UpdateUserRegionsAsync(int userId, UpdateUserRegionsRequestDto request)
    {
        var regionIds = request.RegionIds.Distinct().ToList();
        var user = await _userRepository.GetByIdWithRolesAndRegionsAsync(userId);

        if (user == null)
            throw new KeyNotFoundException($"User with id '{userId}' was not found.");

        bool isRegionalManager = user.UserRoles
            .Any(ur => RoleNames.Normalize(ur.Role.Name) == RoleNames.RegionalManager);

        if (!isRegionalManager)
            throw new InvalidOperationException("User does not have the RegionalManager role.");

        foreach (var regionId in regionIds)
        {
            if (!await _userRepository.RegionExistsAsync(regionId))
                throw new KeyNotFoundException($"Region with id '{regionId}' was not found.");
        }

        var requestedRegionIds = regionIds.ToHashSet();
        var existingAssignments = user.RegionalManagerRegions.ToList();
        var now = DateTime.UtcNow;

        foreach (var existingAssignment in existingAssignments.Where(assignment => !requestedRegionIds.Contains(assignment.RegionId)))
        {
            await _userRepository.DeleteUserRegionAsync(existingAssignment);
        }

        foreach (var existingAssignment in existingAssignments.Where(assignment => requestedRegionIds.Contains(assignment.RegionId)))
        {
            existingAssignment.IsActive = true;
            existingAssignment.UpdatedAt = now;
        }

        var existingRegionIds = existingAssignments.Select(assignment => assignment.RegionId).ToHashSet();
        foreach (var regionId in regionIds.Where(regionId => !existingRegionIds.Contains(regionId)))
        {
            await _userRepository.AddUserRegionAsync(new RegionalManagerRegion
            {
                UserId = userId,
                RegionId = regionId,
                IsActive = true,
                CreatedAt = now
            });
        }

        await _userRepository.SaveChangesAsync();

        return new UpdateUserRegionsResponseDto
        {
            UserId = userId,
            RegionIds = regionIds
        };
    }

    public async Task<AddUserRegionResponseDto> AddUserRegionAsync(int userId, int regionId)
    {
        var user = await _userRepository.GetByIdWithRolesAndRegionsAsync(userId);

        if (user == null)
            throw new KeyNotFoundException($"User with id '{userId}' was not found.");

        bool isRegionalManager = user.UserRoles
            .Any(ur => RoleNames.Normalize(ur.Role.Name) == RoleNames.RegionalManager);

        if (!isRegionalManager)
            throw new InvalidOperationException("User does not have the RegionalManager role.");

        bool regionExists = await _userRepository.RegionExistsAsync(regionId);

        if (!regionExists)
            throw new KeyNotFoundException($"Region with id '{regionId}' was not found.");

        bool alreadyAssigned = await _userRepository.UserRegionExistsAsync(userId, regionId);

        if (alreadyAssigned)
            throw new InvalidOperationException("Region is already assigned to this user.");

        await _userRepository.AddUserRegionAsync(new RegionalManagerRegion
        {
            UserId = userId,
            RegionId = regionId,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });

        await _userRepository.SaveChangesAsync();

        return new AddUserRegionResponseDto
        {
            UserId = userId,
            RegionId = regionId
        };
    }

    public async Task<DeleteUserRegionResponseDto> DeleteUserRegionAsync(int userId, int regionId)
    {
        var userRegion = await _userRepository.GetUserRegionAsync(userId, regionId);

        if (userRegion == null)
            throw new KeyNotFoundException($"Region '{regionId}' is not assigned to user '{userId}'.");

        await _userRepository.DeleteUserRegionAsync(userRegion);

        await _userRepository.SaveChangesAsync();

        return new DeleteUserRegionResponseDto
        {
            UserId = userId,
            RegionId = regionId
        };
    }

    private static void ValidatePasswordChangeRequest(ChangePasswordRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.CurrentPassword))
            throw new InvalidOperationException("Current password is required");

        if (string.IsNullOrWhiteSpace(request.NewPassword))
            throw new InvalidOperationException("New password is required");

        if (request.NewPassword.Length < 8)
            throw new InvalidOperationException("New password must be at least 8 characters long");

        if (!request.NewPassword.Any(char.IsDigit))
            throw new InvalidOperationException("New password must include a number");

        if (!Regex.IsMatch(request.NewPassword, @"[!@#$%^&*(),.?""{}|<>]"))
            throw new InvalidOperationException("New password must include a special character");
    }

    private static void ValidateSupportRequest(CreateSupportRequestRequestDto request)
    {
        var normalizedCategory = NormalizeSupportCategory(request.Category);
        _ = normalizedCategory;

        if (string.IsNullOrWhiteSpace(request.Subject))
            throw new InvalidOperationException("Support request subject is required");

        if (request.Subject.Trim().Length < 6)
            throw new InvalidOperationException("Support request subject must be at least 6 characters long");

        if (request.Subject.Trim().Length > 160)
            throw new InvalidOperationException("Support request subject is too long");

        if (string.IsNullOrWhiteSpace(request.Message))
            throw new InvalidOperationException("Support request message is required");

        if (request.Message.Trim().Length < 20)
            throw new InvalidOperationException("Support request message must be at least 20 characters long");

        if (request.Message.Trim().Length > 2000)
            throw new InvalidOperationException("Support request message is too long");
    }

    private static string NormalizeSupportCategory(string? category)
    {
        var normalized = (category ?? string.Empty).Trim().ToLowerInvariant();

        if (!AllowedSupportCategories.Contains(normalized))
            throw new InvalidOperationException("Invalid support request category");

        return normalized;
    }

    private static string? NormalizeOptionalSupportField(string? value, int maxLength, string tooLongMessage)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
            throw new InvalidOperationException(tooLongMessage);

        return trimmed;
    }
}
