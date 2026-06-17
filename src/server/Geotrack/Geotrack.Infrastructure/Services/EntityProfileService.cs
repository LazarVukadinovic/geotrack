using Geotrack.Application.DTOs.Admin;
using Geotrack.Application.Interfaces.Service;
using Geotrack.Domain.Entities.Core;
using Geotrack.Domain.Entities.Tags;
using Microsoft.EntityFrameworkCore;

namespace Geotrack.Infrastructure.Services;

public sealed class EntityProfileService : IEntityProfileService
{
    private readonly AppDbContext _db;
    private readonly IRegionalEntityImageStorageService _storage;

    public EntityProfileService(AppDbContext db, IRegionalEntityImageStorageService storage)
    {
        _db = db;
        _storage = storage;
    }

    // ── Region ──────────────────────────────────────────────────────────────

    public async Task<EntityProfileDto> GetRegionProfileAsync(int userId, int regionId, CancellationToken ct = default)
    {
        await EnsureRegionAssignedAsync(userId, regionId, ct);

        var region = await _db.Regions
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == regionId, ct)
            ?? throw new KeyNotFoundException($"Region {regionId} not found.");

        var images = await _db.EntityImages
            .AsNoTracking()
            .Where(i => i.RegionId == regionId && i.TargetType == EntityImageTargetType.Region)
            .OrderBy(i => i.SortOrder)
            .ToListAsync(ct);

        var tags = await _db.RegionTags
            .AsNoTracking()
            .Where(rt => rt.RegionId == regionId)
            .Include(rt => rt.Tag).ThenInclude(t => t.TagType)
            .Select(rt => rt.Tag)
            .ToListAsync(ct);

        return BuildProfileDto(region.Description, images, tags);
    }

    public async Task<EntityProfileDto> UpdateRegionProfileAsync(int userId, int regionId, EntityProfileUpdateRequestDto request, CancellationToken ct = default)
    {
        await EnsureRegionAssignedAsync(userId, regionId, ct);

        var region = await _db.Regions.FirstOrDefaultAsync(r => r.Id == regionId, ct)
            ?? throw new KeyNotFoundException($"Region {regionId} not found.");

        region.Description = request.Description?.Trim().NullIfEmpty();
        region.UpdatedAt = DateTime.UtcNow;

        await SyncImagesAsync(
            targetType: EntityImageTargetType.Region,
            existingFilter: i => i.RegionId == regionId && i.TargetType == EntityImageTargetType.Region,
            setForeignKey: img => img.RegionId = regionId,
            request,
            ct);

        await SyncRegionTagsAsync(regionId, request.TagIds, ct);

        await _db.SaveChangesAsync(ct);

        return await GetRegionProfileAsync(userId, regionId, ct);
    }

    public async Task<List<EntityProfileTagDto>> GetRegionTagsAsync(int userId, int regionId, CancellationToken ct = default)
    {
        await EnsureRegionAssignedAsync(userId, regionId, ct);

        return await _db.Tags
            .AsNoTracking()
            .Where(t => t.ForRegion)
            .Include(t => t.TagType)
            .OrderBy(t => t.TagType != null ? t.TagType.Name : string.Empty)
            .ThenBy(t => t.Name)
            .Select(t => new EntityProfileTagDto
            {
                Id = t.Id,
                Name = t.Name,
                Slug = t.Slug,
                TagTypeId = t.TagTypeId,
                TagTypeName = t.TagType != null ? t.TagType.Name : null,
                TagTypeSlug = t.TagType != null ? t.TagType.Slug : null,
            })
            .ToListAsync(ct);
    }

    public async Task<RegionalImageUploadResponseDto> UploadRegionImagesAsync(
        int userId,
        int regionId,
        IReadOnlyCollection<RegionalImageUploadFileDto> files,
        CancellationToken ct = default)
    {
        await EnsureRegionAssignedAsync(userId, regionId, ct);

        if (files.Count == 0)
            throw new InvalidOperationException("Please select at least one image to upload.");

        var response = new RegionalImageUploadResponseDto();

        foreach (var file in files)
        {
            var stored = await _storage.StoreImageAsync(userId, file.Content, file.FileName, file.ContentType, ct);
            response.Images.Add(new RegionalImageUploadResultDto
            {
                FileName = file.FileName,
                Url = stored.MediaUrl,
                SizeBytes = stored.SizeBytes
            });
        }

        return response;
    }

    // ── Territory ────────────────────────────────────────────────────────────

    public async Task<EntityProfileDto> GetTerritoryProfileAsync(int userId, int territoryId, CancellationToken ct = default)
    {
        await EnsureTerritoryAssignedAsync(userId, territoryId, ct);

        var territory = await _db.Territories
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == territoryId, ct)
            ?? throw new KeyNotFoundException($"Territory {territoryId} not found.");

        var images = await _db.EntityImages
            .AsNoTracking()
            .Where(i => i.TerritoryId == territoryId && i.TargetType == EntityImageTargetType.Territory)
            .OrderBy(i => i.SortOrder)
            .ToListAsync(ct);

        var tags = await _db.TerritoryTags
            .AsNoTracking()
            .Where(tt => tt.TerritoryId == territoryId)
            .Include(tt => tt.Tag).ThenInclude(t => t.TagType)
            .Select(tt => tt.Tag)
            .ToListAsync(ct);

        return BuildProfileDto(territory.Description, images, tags);
    }

    public async Task<EntityProfileDto> UpdateTerritoryProfileAsync(int userId, int territoryId, EntityProfileUpdateRequestDto request, CancellationToken ct = default)
    {
        await EnsureTerritoryAssignedAsync(userId, territoryId, ct);

        var territory = await _db.Territories.FirstOrDefaultAsync(t => t.Id == territoryId, ct)
            ?? throw new KeyNotFoundException($"Territory {territoryId} not found.");

        territory.Description = request.Description?.Trim().NullIfEmpty();
        territory.UpdatedAt = DateTime.UtcNow;

        await SyncImagesAsync(
            targetType: EntityImageTargetType.Territory,
            existingFilter: i => i.TerritoryId == territoryId && i.TargetType == EntityImageTargetType.Territory,
            setForeignKey: img => img.TerritoryId = territoryId,
            request,
            ct);

        await SyncTerritoryTagsAsync(territoryId, request.TagIds, ct);

        await _db.SaveChangesAsync(ct);

        return await GetTerritoryProfileAsync(userId, territoryId, ct);
    }

    public async Task<List<EntityProfileTagDto>> GetTerritoryTagsAsync(int userId, int territoryId, CancellationToken ct = default)
    {
        await EnsureTerritoryAssignedAsync(userId, territoryId, ct);

        return await _db.Tags
            .AsNoTracking()
            .Where(t => t.ForTerritory)
            .Include(t => t.TagType)
            .OrderBy(t => t.TagType != null ? t.TagType.Name : string.Empty)
            .ThenBy(t => t.Name)
            .Select(t => new EntityProfileTagDto
            {
                Id = t.Id,
                Name = t.Name,
                Slug = t.Slug,
                TagTypeId = t.TagTypeId,
                TagTypeName = t.TagType != null ? t.TagType.Name : null,
                TagTypeSlug = t.TagType != null ? t.TagType.Slug : null,
            })
            .ToListAsync(ct);
    }

    public async Task<RegionalImageUploadResponseDto> UploadTerritoryImagesAsync(
        int userId,
        int territoryId,
        IReadOnlyCollection<RegionalImageUploadFileDto> files,
        CancellationToken ct = default)
    {
        await EnsureTerritoryAssignedAsync(userId, territoryId, ct);

        if (files.Count == 0)
            throw new InvalidOperationException("Please select at least one image to upload.");

        var response = new RegionalImageUploadResponseDto();

        foreach (var file in files)
        {
            var stored = await _storage.StoreImageAsync(userId, file.Content, file.FileName, file.ContentType, ct);
            response.Images.Add(new RegionalImageUploadResultDto
            {
                FileName = file.FileName,
                Url = stored.MediaUrl,
                SizeBytes = stored.SizeBytes
            });
        }

        return response;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static EntityProfileDto BuildProfileDto(string? description, IEnumerable<EntityImage> images, IEnumerable<Geotrack.Domain.Entities.Tags.Tag> tags)
    {
        var imageList = images.ToList();
        var card = imageList.FirstOrDefault(i => i.ImageRole == EntityImageRole.Card);
        var cover = imageList.FirstOrDefault(i => i.ImageRole == EntityImageRole.Cover);
        var gallery = imageList.Where(i => i.ImageRole == EntityImageRole.Gallery).Select(i => i.Url).ToList();

        return new EntityProfileDto
        {
            Description = description,
            CardImageUrl = card?.Url,
            CoverImageUrl = cover?.Url,
            GalleryImageUrls = gallery,
            Tags = tags.Select(t => new EntityProfileTagDto
            {
                Id = t.Id,
                Name = t.Name,
                Slug = t.Slug,
                TagTypeId = t.TagTypeId,
                TagTypeName = t.TagType?.Name,
                TagTypeSlug = t.TagType?.Slug,
            }).ToList()
        };
    }

    private async Task SyncImagesAsync(
        EntityImageTargetType targetType,
        System.Linq.Expressions.Expression<Func<EntityImage, bool>> existingFilter,
        Action<EntityImage> setForeignKey,
        EntityProfileUpdateRequestDto request,
        CancellationToken ct)
    {
        var existing = await _db.EntityImages.Where(existingFilter).ToListAsync(ct);
        _db.EntityImages.RemoveRange(existing);

        var now = DateTime.UtcNow;
        int sortOrder = 0;

        if (!string.IsNullOrWhiteSpace(request.CardImageUrl))
        {
            var img = new EntityImage { TargetType = targetType, Url = request.CardImageUrl, ImageRole = EntityImageRole.Card, SortOrder = sortOrder++, IsMain = true, CreatedAt = now };
            setForeignKey(img);
            _db.EntityImages.Add(img);
        }

        if (!string.IsNullOrWhiteSpace(request.CoverImageUrl))
        {
            var img = new EntityImage { TargetType = targetType, Url = request.CoverImageUrl, ImageRole = EntityImageRole.Cover, SortOrder = sortOrder++, CreatedAt = now };
            setForeignKey(img);
            _db.EntityImages.Add(img);
        }

        foreach (var galleryUrl in request.GalleryImageUrls.Where(u => !string.IsNullOrWhiteSpace(u)))
        {
            var img = new EntityImage { TargetType = targetType, Url = galleryUrl, ImageRole = EntityImageRole.Gallery, SortOrder = sortOrder++, CreatedAt = now };
            setForeignKey(img);
            _db.EntityImages.Add(img);
        }
    }

    private async Task SyncRegionTagsAsync(int regionId, List<int> tagIds, CancellationToken ct)
    {
        var existing = await _db.RegionTags.Where(rt => rt.RegionId == regionId).ToListAsync(ct);
        _db.RegionTags.RemoveRange(existing);

        foreach (var tagId in tagIds.Distinct())
        {
            _db.RegionTags.Add(new RegionTag { RegionId = regionId, TagId = tagId });
        }
    }

    private async Task SyncTerritoryTagsAsync(int territoryId, List<int> tagIds, CancellationToken ct)
    {
        var existing = await _db.TerritoryTags.Where(tt => tt.TerritoryId == territoryId).ToListAsync(ct);
        _db.TerritoryTags.RemoveRange(existing);

        foreach (var tagId in tagIds.Distinct())
        {
            _db.TerritoryTags.Add(new TerritoryTag { TerritoryId = territoryId, TagId = tagId });
        }
    }

    private async Task EnsureRegionAssignedAsync(int userId, int regionId, CancellationToken ct)
    {
        if (regionId <= 0)
            throw new InvalidOperationException("regionId is required.");

        var assigned = await _db.RegionalManagerRegions
            .AsNoTracking()
            .AnyAsync(r => r.UserId == userId && r.RegionId == regionId && r.IsActive, ct);

        if (!assigned)
            throw new UnauthorizedAccessException("Region is not assigned to the current regional manager.");
    }

    private async Task EnsureTerritoryAssignedAsync(int userId, int territoryId, CancellationToken ct)
    {
        if (territoryId <= 0)
            throw new InvalidOperationException("territoryId is required.");

        var assigned = await _db.TerritoryManagerTerritories
            .AsNoTracking()
            .AnyAsync(t => t.UserId == userId && t.TerritoryId == territoryId, ct);

        if (!assigned)
            throw new UnauthorizedAccessException("Territory is not assigned to the current territory manager.");
    }
}

internal static class StringExtensions
{
    internal static string? NullIfEmpty(this string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
