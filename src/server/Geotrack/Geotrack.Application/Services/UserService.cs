using System.Text.RegularExpressions;
using Geotrack.Application.DTOs.Users;
using Geotrack.Application.Interfaces.Repository;
using Geotrack.Application.Interfaces.Service;
using Geotrack.Domain.Entities;
using Geotrack.Domain.Entities.Tags;
using Geotrack.Domain.Entities.Users;

namespace Geotrack.Application.Services;

public class UserService : IUserService
{
    private readonly IUserRepository _userRepository;
    private readonly IDestinationRepository _destinationRepository;
    private readonly ILocationRepository _locationRepository;
    private readonly IPlaceRepository _placeRepository;
    private readonly IEventRepository _eventRepository;
    public UserService(
        IUserRepository userRepository,
        IDestinationRepository destinationRepository,
        ILocationRepository locationRepository,
        IPlaceRepository placeRepository,
        IEventRepository eventRepository)
    {
        _userRepository = userRepository;
        _destinationRepository = destinationRepository;
        _locationRepository = locationRepository;
        _placeRepository = placeRepository;
        _eventRepository = eventRepository;
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
            CreatedAt = user.CreatedAt,
            UpdatedAt = user.UpdatedAt
        };
    }

    public async Task<GetUserInterestsResponseDto> GetUserInterestsAsync(int userId)
    {
        var user = await _userRepository.GetByIdWithUserTagsAsync(userId)
            ?? throw new KeyNotFoundException("User not found");

        return new GetUserInterestsResponseDto
        {
            Tags = user.UserTags
                .Where(x => x.Tag != null)
                .Select(x => MapTagToInterestItemDto(x.Tag))
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

        await _userRepository.SaveChangesAsync();

        var updatedUser = await _userRepository.GetByIdWithUserTagsAsync(userId)
            ?? throw new KeyNotFoundException("User not found");

        return new UpdateUserInterestsResponseDto
        {
            Tags = updatedUser.UserTags
                .Where(x => x.Tag != null)
                .Select(x => MapTagToInterestItemDto(x.Tag))
                .OrderBy(x => x.Name)
                .ToList()
        };
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
                ItemsCount = await _userRepository.GetCollectionItemsCountAsync(collection.Id)
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

    public async Task<UpdateUserRoleResponseDtocs> AssignRegionalManagerRoleAsync(int userId)
    {
        var user = await _userRepository.GetByIdWithRolesAsync(userId);

        if (user == null)
            throw new KeyNotFoundException($"User with id '{userId}' was not found.");

        bool alreadyHasRole = user.UserRoles
            .Any(ur => ur.Role.Name == "RegionalManager");

        if (alreadyHasRole)
            throw new InvalidOperationException("User already has the RegionalManager role.");

        var role = await _userRepository.GetRoleByNameAsync("RegionalManager")
            ?? throw new InvalidOperationException("RegionalManager role does not exist.");

        user.UserRoles.Add(new UserRole
        {
            UserId = userId,
            RoleId = role.Id
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
            .FirstOrDefault(ur => ur.Role.Name == "RegionalManager");

        if (userRole == null)
            throw new InvalidOperationException("User does not have the RegionalManager role.");

        await _userRepository.RemoveUserRoleAsync(userRole);

        await _userRepository.SaveChangesAsync();

        return new UpdateUserRoleResponseDtocs
        {
            UserId = userId,
            Roles = user.UserRoles
                .Where(ur => ur.Role.Name != "RegionalManager")
                .Select(ur => ur.Role.Name)
                .ToList()
        };
    }

    public async Task<GetUserRegionsResponseDto> GetUserRegionsAsync(int userId)
    {
        var user = await _userRepository.GetByIdWithRegionsAsync(userId);

        if (user == null)
            throw new KeyNotFoundException($"User with id '{userId}' was not found.");

        var regions = user.RegionalOrganization?
            .RegionalOrganizationRegions
            .Select(ror => new RegionItemDto
            {
                Id = ror.Region.Id,
                Name = ror.Region.Name
            })
            .ToList() ?? new List<RegionItemDto>();

        return new GetUserRegionsResponseDto
        {
            UserId = userId,
            Regions = regions
        };
    }

    public async Task<UpdateUserRegionsResponseDto> UpdateUserRegionsAsync(int userId, UpdateUserRegionsRequestDto request)
    {
        var user = await _userRepository.GetByIdWithRolesAndRegionsAsync(userId);

        if (user == null)
            throw new KeyNotFoundException($"User with id '{userId}' was not found.");

        bool isRegionalManager = user.UserRoles
            .Any(ur => ur.Role.Name == "RegionalManager");

        if (!isRegionalManager)
            throw new InvalidOperationException("User does not have the RegionalManager role.");

        if (user.RegionalOrganization == null)
        {
            user.RegionalOrganization = new RegionalOrganization
            {
                UserId = userId,
                RegionalOrganizationRegions = request.RegionIds.Select(regionId => new RegionalOrganizationRegion
                {
                    UserId = userId,
                    RegionId = regionId,
                    CreatedAt = DateTime.UtcNow
                }).ToList()
            };

            await _userRepository.AddRegionalOrganizationAsync(user.RegionalOrganization);
        }
        else
        {
            user.RegionalOrganization.RegionalOrganizationRegions.Clear();

            foreach (var regionId in request.RegionIds)
            {
                user.RegionalOrganization.RegionalOrganizationRegions.Add(new RegionalOrganizationRegion
                {
                    UserId = userId,
                    RegionId = regionId,
                    CreatedAt = DateTime.UtcNow
                });
            }
        }

        await _userRepository.SaveChangesAsync();

        return new UpdateUserRegionsResponseDto
        {
            UserId = userId,
            RegionIds = request.RegionIds
        };
    }

    public async Task<AddUserRegionResponseDto> AddUserRegionAsync(int userId, int regionId)
    {
        var user = await _userRepository.GetByIdWithRolesAsync(userId);

        if (user == null)
            throw new KeyNotFoundException($"User with id '{userId}' was not found.");

        bool isRegionalManager = user.UserRoles
            .Any(ur => ur.Role.Name == "RegionalManager");

        if (!isRegionalManager)
            throw new InvalidOperationException("User does not have the RegionalManager role.");

        bool regionExists = await _userRepository.RegionExistsAsync(regionId);

        if (!regionExists)
            throw new KeyNotFoundException($"Region with id '{regionId}' was not found.");

        bool alreadyAssigned = await _userRepository.UserRegionExistsAsync(userId, regionId);

        if (alreadyAssigned)
            throw new InvalidOperationException("Region is already assigned to this user.");

        if (user.RegionalOrganization == null)
        {
            var regionalOrganization = new RegionalOrganization
            {
                UserId = userId,
                RegionalOrganizationRegions = new List<RegionalOrganizationRegion>
            {
                new RegionalOrganizationRegion
                {
                    UserId = userId,
                    RegionId = regionId,
                    CreatedAt = DateTime.UtcNow
                }
            }
            };

            await _userRepository.AddRegionalOrganizationAsync(regionalOrganization);
        }
        else
        {
            await _userRepository.AddUserRegionAsync(new RegionalOrganizationRegion
            {
                UserId = userId,
                RegionId = regionId,
                CreatedAt = DateTime.UtcNow
            });
        }

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
}