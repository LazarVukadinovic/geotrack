using System.Text.RegularExpressions;
using Geotrack.Application.DTOs.Users;
using Geotrack.Application.Interfaces.Repository;
using Geotrack.Application.Interfaces.Service;
using Geotrack.Domain.Entities;
using Geotrack.Domain.Entities.Tags;
using Geotrack.Domain.Entities.Users;
using Microsoft.AspNetCore.Identity;

namespace Geotrack.Application.Services;

public class UserService : IUserService
{
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
    private readonly IUserAvatarStorageService _userAvatarStorageService;
    private readonly PasswordHasher<User> _passwordHasher = new();

    public UserService(
        IUserRepository userRepository,
        IDestinationRepository destinationRepository,
        ILocationRepository locationRepository,
        IPlaceRepository placeRepository,
        IEventRepository eventRepository,
        IUserAvatarStorageService userAvatarStorageService)
    {
        _userRepository = userRepository;
        _destinationRepository = destinationRepository;
        _locationRepository = locationRepository;
        _placeRepository = placeRepository;
        _eventRepository = eventRepository;
        _userAvatarStorageService = userAvatarStorageService;
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
            Location = user.Location,
            NeedsInterestsOnboarding = user.NeedsInterestsOnboarding,
            CreatedAt = user.CreatedAt,
            UpdatedAt = user.UpdatedAt,
            Collections = collections
        };
    }

    public async Task<UpdateMeResponseDto> UpdateMeAsync(int userId, UpdateMeRequestDto request)
    {
        ValidateProfileFields(request.FirstName, request.LastName, request.PhoneNumber);

        var user = await _userRepository.GetByIDAsync(userId)
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
            Location = user.Location,
            CreatedAt = user.CreatedAt,
            UpdatedAt = user.UpdatedAt
        };
    }

    public async Task<UpdateAvatarResponseDto> UpdateAvatarAsync(int userId, byte[] fileBytes)
    {
        var user = await _userRepository.GetByIDAsync(userId)
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
            UpdatedAt = user.UpdatedAt,
        };
    }

    public async Task<ChangePasswordResponseDto> ChangePasswordAsync(int userId, ChangePasswordRequestDto request)
    {
        ValidatePasswordChangeRequest(request);

        var user = await _userRepository.GetByIDAsync(userId)
            ?? throw new KeyNotFoundException("User not found");

        var currentPasswordResult = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.CurrentPassword);
        if (currentPasswordResult == PasswordVerificationResult.Failed)
            throw new InvalidOperationException("Current password is incorrect");

        if (request.CurrentPassword == request.NewPassword)
            throw new InvalidOperationException("Choose a password you are not already using");

        user.PasswordHash = _passwordHasher.HashPassword(user, request.NewPassword);
        user.UpdatedAt = DateTime.UtcNow;

        await _userRepository.SaveChangesAsync();

        return new ChangePasswordResponseDto
        {
            Message = "Password updated successfully."
        };
    }

    public async Task<GetNotificationSettingsResponseDto> GetNotificationSettingsAsync(int userId)
    {
        var user = await _userRepository.GetByIDAsync(userId)
            ?? throw new KeyNotFoundException("User not found");

        return MapNotificationSettings(user);
    }

    public async Task<UpdateNotificationSettingsResponseDto> UpdateNotificationSettingsAsync(int userId, UpdateNotificationSettingsRequestDto request)
    {
        var user = await _userRepository.GetByIDAsync(userId)
            ?? throw new KeyNotFoundException("User not found");

        user.GeneralNotificationsEnabled = request.GeneralNotifications;
        user.TravelRecommendationsEnabled = request.TravelRecommendations;
        user.NearbyEventsEnabled = request.NearbyEvents;
        user.CollectionUpdatesEnabled = request.CollectionUpdates;
        user.EmailNotificationsEnabled = request.EmailNotifications;
        user.UpdatedAt = DateTime.UtcNow;

        await _userRepository.SaveChangesAsync();

        return MapUpdatedNotificationSettings(user);
    }

    public async Task<GetUserInterestsResponseDto> GetUserInterestsAsync(int userId)
    {
        var user = await _userRepository.GetByIdWithUserTagsAsync(userId)
            ?? throw new KeyNotFoundException("User not found");

        return new GetUserInterestsResponseDto
        {
            Tags = user.UserTags
                .Where(x => x.Tag != null)
                .Select(x => MapTagToInterestItemDto(x.Tag!))
                .OrderBy(x => x.Name)
                .ToList()
        };
    }

    public async Task<UpdateUserInterestsResponseDto> UpdateUserInterestsAsync(int userId, UpdateUserInterestsRequestDto request)
    {
        request.TagIds ??= new List<int>();

        var distinctTagIds = request.TagIds.Distinct().ToList();

        var user = await _userRepository.GetByIdWithUserTagsAsync(userId)
            ?? throw new KeyNotFoundException("User not found");

        var tags = await _userRepository.GetTagsByIdsAsync(distinctTagIds);

        if (tags.Count != distinctTagIds.Count)
            throw new InvalidOperationException("One or more tags do not exist");

        var existingTagIds = user.UserTags.Select(x => x.TagId).ToHashSet();

        var userTagsToRemove = user.UserTags
            .Where(x => !distinctTagIds.Contains(x.TagId))
            .ToList();

        if (userTagsToRemove.Any())
        {
            await _userRepository.DeleteUserTagsAsync(userTagsToRemove);
        }

        var tagIdsToAdd = distinctTagIds
            .Where(tagId => !existingTagIds.Contains(tagId))
            .ToList();

        foreach (var tagId in tagIdsToAdd)
        {
            await _userRepository.AddUserTagAsync(new UserTag
            {
                UserId = userId,
                TagId = tagId,
                CreatedAt = DateTime.UtcNow
            });
        }

        user.NeedsInterestsOnboarding = false;
        user.UpdatedAt = DateTime.UtcNow;
        await _userRepository.SaveChangesAsync();

        var updatedUser = await _userRepository.GetByIdWithUserTagsAsync(userId)
            ?? throw new KeyNotFoundException("User not found");

        return new UpdateUserInterestsResponseDto
        {
            Tags = updatedUser.UserTags
                .Where(x => x.Tag != null)
                .Select(x => MapTagToInterestItemDto(x.Tag!))
                .OrderBy(x => x.Name)
                .ToList()
        };
    }

    public async Task<GetSupportRequestsResponseDto> GetSupportRequestsAsync(int userId)
    {
        var user = await _userRepository.GetByIDAsync(userId)
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
        var user = await _userRepository.GetByIDAsync(userId)
            ?? throw new KeyNotFoundException("User not found");

        _ = user;

        ValidateSupportRequest(request);

        var supportRequest = new SupportRequest
        {
            UserId = userId,
            Category = NormalizeSupportCategory(request.Category),
            Subject = request.Subject.Trim(),
            Message = request.Message.Trim(),
            Status = "Received",
            CreatedAt = DateTime.UtcNow
        };

        await _userRepository.AddSupportRequestAsync(supportRequest);
        await _userRepository.SaveChangesAsync();

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
        var collection = await _userRepository.GetCollectionByIdForUserAsync(userId, collectionId)
            ?? throw new KeyNotFoundException("Collection not found");

        await _userRepository.DeleteCollectionAsync(collection);
        await _userRepository.SaveChangesAsync();
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
                if (await _destinationRepository.GetByIDAsync(request.EntityId) == null)
                    throw new KeyNotFoundException("Destinations not found");
                item.DestinationId = request.EntityId;
                break;

            case "location":
                if (await _locationRepository.GetByIDAsync(request.EntityId) == null)
                    throw new KeyNotFoundException("Locations not found");
                item.LocationId = request.EntityId;
                break;

            case "place":
                if (await _placeRepository.GetByIDAsync(request.EntityId) == null)
                    throw new KeyNotFoundException("Places not found");
                item.PlaceId = request.EntityId;
                break;

            case "event":
                if (await _eventRepository.GetByIDAsync(request.EntityId) == null)
                    throw new KeyNotFoundException("Events not found");
                item.EventId = request.EntityId;
                break;
        }

        await _userRepository.AddCollectionItemAsync(item);
        await _userRepository.SaveChangesAsync();

        return new AddCollectionItemResponseDto
        {
            Id = item.Id,
            CollectionId = item.CollectionId,
            DestinationId = item.DestinationId,
            LocationId = item.LocationId,
            PlaceId = item.PlaceId,
            EventId = item.EventId,
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

        await _userRepository.DeleteCollectionItemAsync(item);
        await _userRepository.SaveChangesAsync();
    }

    private static InterestItemDto MapTagToInterestItemDto(Tag tag)
    {
        return new InterestItemDto
        {
            TagId = tag.Id,
            Name = tag.Name,
            Slug = tag.Slug,
            Description = tag.Description,
            ForDestination = tag.ForDestination,
            ForLocation = tag.ForLocation,
            ForPlace = tag.ForPlace,
            ForEvent = tag.ForEvent,
            PlaceTypeId = tag.PlaceTypeId,
            CreatedAt = tag.CreatedAt,
            UpdatedAt = tag.UpdatedAt
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
            EmailNotifications = user.EmailNotificationsEnabled
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
            EmailNotifications = user.EmailNotificationsEnabled
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
                ImageUrl = item.Destination.ImageUrl,
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
                ImageUrl = item.Location.ImageUrl,
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
                ImageUrl = item.Place.ImageUrl,
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
                ImageUrl = item.Event.ImageUrl,
                StartDate = item.Event.StartDate,
                EndDate = item.Event.EndDate,
                CreatedAt = item.Event.CreatedAt,
                UpdatedAt = item.Event.UpdatedAt
            };
        }

        throw new InvalidOperationException("Invalid collection item state");
    }

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

    private static void ValidatePasswordChangeRequest(ChangePasswordRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.CurrentPassword))
            throw new InvalidOperationException("Current password is required");

        if (string.IsNullOrWhiteSpace(request.NewPassword))
            throw new InvalidOperationException("New password is required");

        if (request.NewPassword.Length < 8)
            throw new InvalidOperationException("New password must be at least 8 characters long");
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
}
