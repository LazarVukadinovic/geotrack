using Geotrack.Application.DTOs.Places;
using Geotrack.Application.DTOs.Reviews;

namespace Geotrack.Application.Interfaces.Service;

public interface IPlaceService
{
    Task<List<PlaceListItemDto>> GetPlacesAsync(int destinationId, string? type = null, string? tags = null, string? category = null, int? userId = null);
    Task<PlaceDetailDto?> GetPlaceDetailAsync(int id, int? userId = null);
    Task<HotelDetailDto?> GetHotelDetailAsync(int id, int? userId = null);
    Task<List<PlaceListItemDto>> GetHotelsAsync(int destinationId, string? tag = null, int? userId = null);
}
