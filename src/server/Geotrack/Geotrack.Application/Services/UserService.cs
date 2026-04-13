using Geotrack.Application.Interfaces.Service;
using Geotrack.Application.DTOs.User;
using Geotrack.Application.DTOs.Collection;
using Geotrack.Application.DTOs.Interest;
using Geotrack.Application.Interfaces.Repository;
using Geotrack.Domain.Entities.Users;
using Geotrack.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Geotrack.Application.Services;

public class UserService : IUserService
{
    private readonly IUserRepository _userRepository;
    private readonly AppDbContext _context;

    public UserService(IUserRepository userRepository, AppDbContext context)
    {
        _userRepository = userRepository;
        _context = context;
    }

    // ===================== GET ME =====================
    public async Task<GetMeResponseDto> GetMeAsync(int userId)
    {
        var user = await _userRepository.GetByIDAsync(userId);

        if (user == null)
            throw new Exception("User not found");

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

            Collections = user.Collections.Select(c => new CollectionPreviewDto
            {
                Id = c.Id,
                Name = c.Name,
                Description = c.Description,
                CreatedAt = c.CreatedAt,
                UpdatedAt = c.UpdatedAt,
                ItemsCount = c.CollectionItems.Count
            }).ToList()
        };
    }

    // ===================== UPDATE ME =====================
    public async Task<UpdateMeResponseDto> UpdateMeAsync(int userId, UpdateMeRequestDto request)
    {
        var user = await _userRepository.GetByIDAsync(userId);

        if (user == null)
            throw new Exception("User not found");

        user.FirstName = request.FirstName;
        user.LastName = request.LastName;
        user.PhoneNumber = request.PhoneNumber;
        user.UpdatedAt = DateTime.UtcNow;

        await _userRepository.SaveChangesAsync();

        return new UpdateMeResponseDto
        {
            Id = user.Id,
            Username = user.Username,
            Email = user.Email,
            FirstName = user.FirstName ?? "",
            LastName = user.LastName ?? "",
            PhoneNumber = user.PhoneNumber ?? "",
            CreatedAt = user.CreatedAt,
            UpdatedAt = user.UpdatedAt
        };
    }

    // ===================== GET COLLECTIONS =====================
    public async Task<GetCollectionsResponseDto> GetCollectionsAsync(int userId)
    {
        var user = await _userRepository.GetByIDAsync(userId);

        if (user == null)
            throw new Exception("User not found");

        return new GetCollectionsResponseDto
        {
            Collections = user.Collections.Select(c => new CollectionListItemDto
            {
                Id = c.Id,
                Name = c.Name,
                Description = c.Description,
                UserId = c.UserId,
                CreatedAt = c.CreatedAt,
                UpdatedAt = c.UpdatedAt,
                ItemsCount = c.CollectionItems.Count
            }).ToList()
        };
    }

    // ===================== CREATE COLLECTION =====================
    public async Task<CreateCollectionResponseDto> CreateCollectionAsync(int userId, CreateCollectionRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            throw new Exception("Name is required");

        var user = await _userRepository.GetByIDAsync(userId);

        if (user == null)
            throw new Exception("User not found");

        var collection = new Collection
        {
            Name = request.Name,
            Description = request.Description ?? "",
            UserId = userId,
            CreatedAt = DateTime.UtcNow
        };

        user.Collections.Add(collection);
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

    // ===================== GET INTERESTS =====================
    public async Task<GetUserInterestsResponseDto> GetUserInterestsAsync(int userId)
    {
        var user = await _userRepository.GetByIDAsync(userId);

        if (user == null)
            throw new Exception("User not found");

        return new GetUserInterestsResponseDto
        {
            Tags = user.UserTags.Select(ut => new InterestItemDto
            {
                TagId = ut.Tag.Id,
                Name = ut.Tag.Name,
                Slug = ut.Tag.Slug,
                Description = ut.Tag.Description,
                ForDestination = ut.Tag.ForDestination,
                ForLocation = ut.Tag.ForLocation,
                ForPlace = ut.Tag.ForPlace,
                ForEvent = ut.Tag.ForEvent,
                PlaceTypeId = ut.Tag.PlaceTypeId,
                CreatedAt = ut.Tag.CreatedAt,
                UpdatedAt = ut.Tag.UpdatedAt
            }).ToList()
        };
    }

    // ===================== UPDATE INTERESTS =====================
    public async Task<UpdateUserInterestsResponseDto> UpdateUserInterestsAsync(int userId, UpdateUserInterestsRequestDto request)
    {
        var user = await _userRepository.GetByIDAsync(userId);

        if (user == null)
            throw new Exception("User not found");

        var existing = await _context.UserTags
            .Where(ut => ut.UserId == userId)
            .ToListAsync();

        _context.UserTags.RemoveRange(existing);

        foreach (var tagId in request.TagIds.Distinct())
        {
            var exists = await _context.Tags.AnyAsync(t => t.Id == tagId);
            if (!exists)
                throw new Exception($"Tag {tagId} does not exist");

            _context.UserTags.Add(new UserTag
            {
                UserId = userId,
                TagId = tagId,
                CreatedAt = DateTime.UtcNow
            });
        }

        await _context.SaveChangesAsync();

        return await GetUserInterestsAsync(userId);
    }

    // ===================== COLLECTION DETAILS =====================
    public async Task<GetCollectionDetailsResponseDto> GetCollectionDetailsAsync(int userId, int collectionId)
    {
        var collection = await _context.Collections
            .Include(c => c.CollectionItems)
            .FirstOrDefaultAsync(c => c.Id == collectionId);

        if (collection == null)
            throw new Exception("Collection not found");

        if (collection.UserId != userId)
            throw new UnauthorizedAccessException();

        var items = new List<CollectionItemDto>();

        foreach (var item in collection.CollectionItems)
        {
            var dto = new CollectionItemDto
            {
                Id = item.Id,
                CollectionId = item.CollectionId,
                CreatedAt = item.CreatedAt,
                UpdatedAt = item.UpdatedAt
            };

            if (item.DestinationId != null)
            {
                var d = await _context.Destinations.FindAsync(item.DestinationId);
                dto.EntityType = "destination";
                dto.DestinationId = d.Id;
                dto.Name = d.Name;
                dto.Description = d.Description;
                dto.Latitude = d.Latitude;
                dto.Longitude = d.Longitude;
            }
            else if (item.LocationId != null)
            {
                var l = await _context.Locations.FindAsync(item.LocationId);
                dto.EntityType = "location";
                dto.LocationId = l.Id;
                dto.Name = l.Name;
                dto.Latitude = l.Latitude;
                dto.Longitude = l.Longitude;
            }
            else if (item.PlaceId != null)
            {
                var p = await _context.Places.FindAsync(item.PlaceId);
                dto.EntityType = "place";
                dto.PlaceId = p.Id;
                dto.Name = p.Name;
                dto.Description = p.Description;
                dto.Latitude = p.Latitude;
                dto.Longitude = p.Longitude;
                dto.ContactEmail = p.ContactEmail;
                dto.Number = p.Number;
            }
            else if (item.EventId != null)
            {
                var e = await _context.Events.FindAsync(item.EventId);
                dto.EntityType = "event";
                dto.EventId = e.Id;
                dto.Name = e.Name;
                dto.Description = e.Description;
                dto.StartDate = e.StartDate;
                dto.EndDate = e.EndDate;
            }

            items.Add(dto);
        }

        return new GetCollectionDetailsResponseDto
        {
            Id = collection.Id,
            Name = collection.Name,
            Description = collection.Description,
            UserId = collection.UserId,
            CreatedAt = collection.CreatedAt,
            UpdatedAt = collection.UpdatedAt,
            Items = items
        };
    }

    // ===================== UPDATE COLLECTION =====================
    public async Task<UpdateCollectionResponseDto> UpdateCollectionAsync(int userId, int collectionId, UpdateCollectionRequestDto request)
    {
        var collection = await _context.Collections.FindAsync(collectionId);

        if (collection == null)
            throw new Exception("Collection not found");

        if (collection.UserId != userId)
            throw new UnauthorizedAccessException();

        collection.Name = request.Name;
        collection.Description = request.Description ?? "";
        collection.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

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

    // ===================== DELETE COLLECTION =====================
    public async Task DeleteCollectionAsync(int userId, int collectionId)
    {
        var collection = await _context.Collections.FindAsync(collectionId);

        if (collection == null)
            throw new Exception("Collection not found");

        if (collection.UserId != userId)
            throw new UnauthorizedAccessException();

        _context.Collections.Remove(collection);
        await _context.SaveChangesAsync();
    }

    // ===================== ADD ITEM =====================
    public async Task<AddCollectionItemResponseDto> AddCollectionItemAsync(int userId, int collectionId, AddCollectionItemRequestDto request)
    {
        var collection = await _context.Collections.FindAsync(collectionId);

        if (collection == null)
            throw new Exception("Collection not found");

        if (collection.UserId != userId)
            throw new UnauthorizedAccessException();

        var item = new CollectionItem
        {
            CollectionId = collectionId,
            CreatedAt = DateTime.UtcNow
        };

        switch (request.EntityType.ToLower())
        {
            case "destination":
                if (!await _context.Destinations.AnyAsync(x => x.Id == request.EntityId))
                    throw new Exception("Destination not found");
                item.DestinationId = request.EntityId;
                break;

            case "location":
                if (!await _context.Locations.AnyAsync(x => x.Id == request.EntityId))
                    throw new Exception("Location not found");
                item.LocationId = request.EntityId;
                break;

            case "place":
                if (!await _context.Places.AnyAsync(x => x.Id == request.EntityId))
                    throw new Exception("Place not found");
                item.PlaceId = request.EntityId;
                break;

            case "event":
                if (!await _context.Events.AnyAsync(x => x.Id == request.EntityId))
                    throw new Exception("Event not found");
                item.EventId = request.EntityId;
                break;

            default:
                throw new Exception("Invalid entity type");
        }

        _context.CollectionItems.Add(item);
        await _context.SaveChangesAsync();

        return new AddCollectionItemResponseDto
        {
            Id = item.Id,
            CollectionId = item.CollectionId,
            DestinationId = item.DestinationId,
            LocationId = item.LocationId,
            PlaceId = item.PlaceId,
            EventId = item.EventId,
            CreatedAt = item.CreatedAt
        };
    }

    // ===================== DELETE ITEM =====================
    public async Task DeleteCollectionItemAsync(int userId, int collectionId, int itemId)
    {
        var collection = await _context.Collections.FindAsync(collectionId);

        if (collection == null || collection.UserId != userId)
            throw new UnauthorizedAccessException();

        var item = await _context.CollectionItems.FindAsync(itemId);

        if (item == null || item.CollectionId != collectionId)
            throw new Exception("Item not found");

        _context.CollectionItems.Remove(item);
        await _context.SaveChangesAsync();
    }
}