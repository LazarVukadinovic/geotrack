using Geotrack.Application.DTOs.Admin;
using Geotrack.Application.Interfaces.Service;
using Geotrack.Application.Options;
using Geotrack.Application.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Geotrack.Infrastructure.Services;

public sealed class RegionalMediaService : IRegionalMediaService
{
    private readonly AppDbContext _db;
    private readonly IRegionalEntityImageStorageService _storage;
    private readonly UploadLimitsOptions _uploadLimits;

    public RegionalMediaService(
        AppDbContext db,
        IRegionalEntityImageStorageService storage,
        IOptions<UploadLimitsOptions> uploadLimits)
    {
        _db = db;
        _storage = storage;
        _uploadLimits = uploadLimits.Value;
    }

    public async Task<RegionalImageUploadResponseDto> UploadImagesAsync(
        int userId,
        int regionId,
        IReadOnlyCollection<RegionalImageUploadFileDto> files,
        CancellationToken cancellationToken = default)
    {
        await EnsureAssignedRegionAsync(userId, regionId, cancellationToken);

        if (files.Count == 0)
            throw new InvalidOperationException("Please select at least one image to upload.");

        if (files.Count > _uploadLimits.Images.MaxRegionalFilesPerRequest)
            throw new InvalidOperationException($"A maximum of {_uploadLimits.Images.MaxRegionalFilesPerRequest} images can be uploaded at once.");

        var response = new RegionalImageUploadResponseDto();

        foreach (var file in files)
        {
            var stored = await _storage.StoreImageAsync(
                userId,
                file.Content,
                file.FileName,
                file.ContentType,
                cancellationToken);

            response.Images.Add(new RegionalImageUploadResultDto
            {
                FileName = file.FileName,
                Url = stored.MediaUrl,
                SizeBytes = stored.SizeBytes
            });
        }

        return response;
    }

    private async Task EnsureAssignedRegionAsync(int userId, int regionId, CancellationToken cancellationToken)
    {
        if (regionId <= 0)
            throw new InvalidOperationException("regionId is required.");

        var roles = await _db.UserRoles
            .AsNoTracking()
            .Where(userRole => userRole.UserId == userId)
            .Select(userRole => userRole.Role.Name)
            .ToListAsync(cancellationToken);

        if (!roles.Any(role => RoleNames.Normalize(role) == RoleNames.RegionalManager))
            throw new UnauthorizedAccessException("Regional manager role is required.");

        var assigned = await _db.RegionalManagerRegions
            .AsNoTracking()
            .AnyAsync(assignment =>
                assignment.UserId == userId &&
                assignment.RegionId == regionId &&
                assignment.IsActive,
                cancellationToken);

        if (!assigned)
            throw new UnauthorizedAccessException("Region is not assigned to the current regional manager.");
    }
}
